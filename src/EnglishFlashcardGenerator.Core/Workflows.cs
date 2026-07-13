using EnglishFlashcardGenerator.Core.Agents;
using EnglishFlashcardGenerator.Core.Markdown;
using EnglishFlashcardGenerator.Core.Output;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace EnglishFlashcardGenerator.Core;

public sealed record MergedWorkerAccumulator(DayChunk Day, NoteProcessingRequest Options, int ExpectedWorkers, IReadOnlyList<WorkerBatchResult> WorkerResults, IReadOnlyList<string> Warnings);

public static class CSharpMafWorkflowFactory
{
    public static Workflow BuildNoteWorkflow(IStructuredAgentPort agents, ILogger logger)
    {
        var read = Bind<NoteProcessingRequest, MarkdownSource>("read-source", request =>
            new MarkdownSource(request.SourcePath, File.ReadAllText(request.SourcePath), request));
        var split = Bind<MarkdownSource, DayChunks>("split-days", source =>
            new DayChunks(source, MarkdownDaySplitter.Split(source.Markdown), source.Options));
        var select = Bind<DayChunks, SelectedDays>("select-days", chunks =>
            new SelectedDays(chunks.Source, chunks.Options.MaxDays is { } max ? chunks.Days.Take(max).ToArray() : chunks.Days, chunks.Options));
        var processDays = BindAsync<SelectedDays, ProcessedDays>("process-days-sequentially", async (selected, ct) =>
        {
            var results = new List<DayResult>();
            foreach (var day in selected.Days)
            {
                logger.LogInformation("Starting processing for day: {Heading}", day.Heading);
                var dayWorkflow = BuildDayWorkflow(agents, selected.Options.MaxParallelGroupWorkers, logger);
                results.Add(await WorkflowRunner.RunAsync<DayResult>(dayWorkflow, new DayProcessingRequest(day, selected.Options), ct).ConfigureAwait(false));
            }

            return new ProcessedDays(results, selected.Options);
        });
        var summary = Bind<ProcessedDays, RunSummary>("build-run-summary", processed =>
            new RunSummary(
                processed.Results.Count,
                processed.Results.Count(r => r.Succeeded),
                processed.Results.Count(r => !r.Succeeded),
                processed.Results.Sum(r => r.CardsCount),
                processed.Results.SelectMany(r => r.OutputFiles).ToArray(),
                processed.Results.SelectMany(r => r.Warnings).ToArray()));

        return new WorkflowBuilder(read)
            .WithName("EnglishFlashcards.NoteWorkflow.CSharpV2")
            .WithDescription("Read source note, split dated day chunks, run DayWorkflow sequentially, and build a run summary.")
            .AddEdge(read, split)
            .AddEdge(split, select)
            .AddEdge(select, processDays)
            .AddEdge(processDays, summary)
            .WithOutputFrom(summary)
            .Build();
    }

    public static Workflow BuildDayWorkflow(IStructuredAgentPort agents, int workerCount, ILogger logger)
    {
        if (workerCount < 1) throw new ArgumentOutOfRangeException(nameof(workerCount));

        var planGroups = BindAsync<DayProcessingRequest, GroupPlan>("plan-groups-agent", async (request, ct) =>
        {
            var dto = await agents.PlanGroupsAsync(request.Day, request.Options, ct).ConfigureAwait(false);
            var groups = dto.Groups.Take(request.Options.MaxGroupsPerDay).Select((g, i) => new TopicGroup(
                i,
                request.Day.DayIndex,
                ParseKind(g.Kind),
                string.IsNullOrWhiteSpace(g.Title) ? $"Group {i + 1}" : g.Title.Trim(),
                string.IsNullOrWhiteSpace(g.SourceExcerpt) ? request.Day.Markdown : g.SourceExcerpt.Trim(),
                g.SourceOrder)).ToArray();
            return new GroupPlan(request.Day, groups, request.Options);
        });
        var validatePlan = Bind<GroupPlan, ValidatedGroupPlan>("validate-group-plan", plan =>
        {
            var warnings = new List<string>();
            var groups = plan.Groups.Where(g => !string.IsNullOrWhiteSpace(g.SourceExcerpt)).OrderBy(g => g.SourceOrder).ToArray();
            if (groups.Length == 0) warnings.Add("Planner produced no usable groups.");
            return new ValidatedGroupPlan(plan.Day, groups, plan.Options, warnings);
        });
        var partition = Bind<ValidatedGroupPlan, WorkerPartitionPlan>("partition-groups", plan =>
        {
            logger.LogInformation("Day '{Heading}' planner identified {Count} valid groups.", plan.Day.Heading, plan.Groups.Count);
            var batches = Enumerable.Range(0, workerCount).Select(i => new WorkerBatch(i, [])).ToArray();
            var mutable = batches.Select(b => new List<TopicGroup>()).ToArray();
            for (var i = 0; i < plan.Groups.Count; i++) mutable[i % workerCount].Add(plan.Groups[i]);
            return new WorkerPartitionPlan(plan.Day, mutable.Select((items, i) => new WorkerBatch(i, items)).ToArray(), plan.Options, plan.Warnings);
        });
        var workers = Enumerable.Range(0, workerCount)
            .Select(i => BuildGroupWorkerWorkflow(i, agents, logger).BindAsExecutor($"group-worker-{i}"))
            .ToArray();
        var merge = ExecutorBindingExtensions.BindAsExecutor<WorkerBatchResult, MergedWorkerAccumulator>(
            (current, result) =>
            {
                var prior = current?.WorkerResults ?? [];
                var merged = prior.Where(r => r.WorkerIndex != result.WorkerIndex).Append(result).OrderBy(r => r.WorkerIndex).ToArray();
                return new MergedWorkerAccumulator(
                    result.Day,
                    result.Options,
                    result.ExpectedWorkers,
                    merged,
                    merged.SelectMany(r => r.Warnings).Distinct().ToArray());
            },
            "merge-worker-results",
            null,
            true);
        var dedupe = Bind<MergedWorkerAccumulator, MergedGroupResults>("dedupe-cards", merged =>
        {
            var groupResults = merged.WorkerResults.SelectMany(r => r.Groups).OrderBy(g => g.Group.SourceOrder).ToArray();
            var cards = groupResults.SelectMany(g => g.Cards)
                .Where(c => !string.IsNullOrWhiteSpace(c.Front) && !string.IsNullOrWhiteSpace(c.Back))
                .DistinctBy(c => c.Front.Trim().ToUpperInvariant())
                .ToArray();
            return new MergedGroupResults(merged.Day, groupResults, cards, merged.Options, merged.Warnings);
        });
        var extract = Bind<MergedGroupResults, DayOutputDraft>("extract-daily-source-excerpt", merged =>
            new DayOutputDraft(merged.Day, merged.Cards, merged.Day.Markdown, merged.Options, merged.Warnings));
        var format = Bind<DayOutputDraft, DayWritePlan>("format-day-outputs", OutputPathBuilder.Build);
        var write = Bind<DayWritePlan, DayResult>("write-day-outputs", AtomicMarkdownWriter.Execute);

        return new WorkflowBuilder(planGroups)
            .WithName("EnglishFlashcards.DayWorkflow.CSharpV2")
            .WithDescription("Plan groups, fixed worker-pool fan-out/fan-in, copy source excerpt, format and write outputs.")
            .AddEdge(planGroups, validatePlan)
            .AddEdge(validatePlan, partition)
            .AddFanOutEdge(partition, workers)
            .AddFanInBarrierEdge(workers, merge)
            .AddEdge(merge, dedupe)
            .AddEdge(dedupe, extract)
            .AddEdge(extract, format)
            .AddEdge(format, write)
            .WithOutputFrom(write)
            .Build();
    }

    public static Workflow BuildGroupWorkerWorkflow(int workerIndex, IStructuredAgentPort agents, ILogger logger)
    {
        var select = Bind<WorkerPartitionPlan, WorkerBatchRequest>($"select-worker-batch-{workerIndex}", plan =>
        {
            var batch = plan.Batches.SingleOrDefault(b => b.WorkerIndex == workerIndex) ?? new WorkerBatch(workerIndex, []);
            return new WorkerBatchRequest(plan.Day, batch, plan.Options, plan.Warnings);
        });
        var process = BindAsync<WorkerBatchRequest, WorkerBatchResult>($"process-worker-batch-{workerIndex}", async (request, ct) =>
        {
            var results = new List<GroupResult>();
            foreach (var group in request.Batch.Groups)
            {
                var workflow = BuildGroupCardWorkflow(agents, logger);
                results.Add(await WorkflowRunner.RunAsync<GroupResult>(workflow, new GroupCardRequest(request.Day, group, request.Options), ct).ConfigureAwait(false));
            }

            return new WorkerBatchResult(workerIndex, request.Options.MaxParallelGroupWorkers, request.Day, request.Options, results, request.Warnings);
        });

        return new WorkflowBuilder(select)
            .WithName($"EnglishFlashcards.GroupWorker.{workerIndex}.CSharpV2")
            .AddEdge(select, process)
            .WithOutputFrom(process)
            .Build();
    }

    public static Workflow BuildGroupCardWorkflow(IStructuredAgentPort agents, ILogger logger)
    {
        var buildInitial = Bind<GroupCardRequest, TeacherRequest>("build-teacher-request", request =>
            new TeacherRequest(request.Day, request.Group, 1, null, request.Options));
        var buildRevision = Bind<RevisionDecision, TeacherRequest>("build-revision-teacher-request", decision =>
            new TeacherRequest(decision.Day, decision.Group, decision.Draft.Iteration + 1, decision.Feedback, decision.Options));
        var teacher = BindAsync<TeacherRequest, TeacherDraft>("teacher-agent", async (request, ct) =>
        {
            var dto = await agents.GenerateCardsAsync(request, ct).ConfigureAwait(false);
            var cards = (dto.Cards ?? Array.Empty<TeacherCardDto>()).Select(card => new Flashcard(
                card.Front ?? "",
                card.Back ?? "",
                string.IsNullOrWhiteSpace(card.Example) ? null : card.Example,
                string.Equals(card.Direction, "bidirectional", StringComparison.OrdinalIgnoreCase) ? CardDirection.Bidirectional : CardDirection.OneWay,
                request.Group.GroupIndex)).ToArray();
            return new TeacherDraft(request.Day, request.Group, request.Iteration, cards, request.Options, []);
        });
        var validate = Bind<TeacherDraft, TeacherDraft>("validate-teacher-draft", draft =>
        {
            var cards = draft.Cards.Where(c => !string.IsNullOrWhiteSpace(c.Front) && !string.IsNullOrWhiteSpace(c.Back)).ToArray();
            var warnings = draft.Warnings.Concat(cards.Length == 0 ? ["Teacher produced no usable cards."] : Array.Empty<string>()).ToArray();
            return draft with { Cards = cards, Warnings = warnings };
        });
        var critic = BindAsync<TeacherDraft, CriticReview>("critic-agent", async (draft, ct) =>
        {
            var dto = await agents.ReviewCardsAsync(draft, ct).ConfigureAwait(false);
            var verdict = string.Equals(dto.Verdict, "approved", StringComparison.OrdinalIgnoreCase)
                ? CriticVerdict.Approved
                : string.Equals(dto.Verdict, "rejected", StringComparison.OrdinalIgnoreCase) ? CriticVerdict.Rejected : CriticVerdict.NeedsRevision;
            var feedback = string.Join("\n", new[] { dto.Feedback ?? "" }.Concat((dto.Findings ?? Array.Empty<CriticFindingDto>()).Select(f => $"{f.CardFront}: {f.Issue}; {f.Recommendation}")));
            return new CriticReview(draft.Day, draft.Group, draft, verdict, feedback, draft.Warnings);
        });
        var decide = Bind<CriticReview, RevisionDecision>("decide-revision", review =>
        {
            var shouldRevise = review.Verdict == CriticVerdict.NeedsRevision && review.Draft.Iteration < review.Draft.Options.MaxCriticIterations;
            return new RevisionDecision(review.Day, review.Group, review.Draft, review.Verdict, shouldRevise, !shouldRevise, review.Feedback, review.Draft.Options, review.Warnings);
        });
        var finalize = Bind<RevisionDecision, GroupResult>("finalize-group-result", decision =>
        {
            logger.LogInformation("Finalized group '{Group}'. Verdict: {Verdict}. Total iterations: {Iteration}. Cards kept: {Count}", decision.Group.Title, decision.Verdict, decision.Draft.Iteration, decision.Verdict == CriticVerdict.Rejected ? 0 : decision.Draft.Cards.Count);
            return new GroupResult(decision.Group, decision.Verdict == CriticVerdict.Rejected ? [] : decision.Draft.Cards, decision.Draft.Iteration, decision.Verdict == CriticVerdict.Approved, decision.Warnings);
        });

        return new WorkflowBuilder(buildInitial)
            .WithName("EnglishFlashcards.GroupCardWorkflow.CSharpV2")
            .WithDescription("Teacher/critic revision loop for one topic group, bounded by MaxCriticIterations.")
            .AddEdge(buildInitial, teacher)
            .AddEdge(buildRevision, teacher)
            .AddEdge(teacher, validate)
            .AddEdge(validate, critic)
            .AddEdge(critic, decide)
            .AddEdge<RevisionDecision>(decide, buildRevision, d => d is not null && d.ShouldRevise)
            .AddEdge<RevisionDecision>(decide, finalize, d => d is not null && d.ShouldFinalize)
            .WithOutputFrom(finalize)
            .Build();
    }

    private static TopicGroupKind ParseKind(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "vocabulary" => TopicGroupKind.Vocabulary,
        "grammar" => TopicGroupKind.Grammar,
        "phrase" => TopicGroupKind.Phrase,
        "usage" or "usagenote" or "usage-note" => TopicGroupKind.UsageNote,
        _ => TopicGroupKind.Mixed
    };

    private static ExecutorBinding Bind<TIn, TOut>(string id, Func<TIn, TOut> func) =>
        ExecutorBindingExtensions.BindAsExecutor<TIn, TOut>(func, id, null, true);

    private static ExecutorBinding BindAsync<TIn, TOut>(string id, Func<TIn, CancellationToken, Task<TOut>> func) =>
        ExecutorBindingExtensions.BindAsExecutor<TIn, TOut>(
            (input, ct) => new ValueTask<TOut>(func(input, ct)),
            id,
            null,
            true);

}
