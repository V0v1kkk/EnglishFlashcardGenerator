using EnglishFlashcardGenerator.Core;
using EnglishFlashcardGenerator.Core.Agents;
using EnglishFlashcardGenerator.Core.Markdown;
using EnglishFlashcardGenerator.Core.Output;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishFlashcardGenerator.Core.Tests;

public sealed class StubAgentPort : IStructuredAgentPort
{
    private int _criticCalls;

    public ValueTask<GroupPlanDto> PlanGroupsAsync(DayChunk day, NoteProcessingRequest options, CancellationToken cancellationToken)
    {
        var groups = new[]
        {
            new TopicGroupDto(0, "Vocabulary", "look up", "**look up** - to search for information"),
            new TopicGroupDto(1, "Grammar", "at", "**at** - used for specific times"),
            new TopicGroupDto(2, "Phrase", "on time", "**on time** - punctual")
        };
        return ValueTask.FromResult(new GroupPlanDto(groups));
    }

    public ValueTask<TeacherOutputDto> GenerateCardsAsync(TeacherRequest request, CancellationToken cancellationToken)
    {
        var suffix = request.Iteration > 1 ? " revised" : string.Empty;
        return ValueTask.FromResult(new TeacherOutputDto([
            new TeacherCardDto($"Meaning of {request.Group.Title}{suffix}", request.Group.SourceExcerpt, "")
        ]));
    }

    public ValueTask<CriticOutputDto> ReviewCardsAsync(TeacherDraft draft, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _criticCalls);
        if (draft.Group.GroupIndex == 0 && call == 1)
        {
            return ValueTask.FromResult(new CriticOutputDto("needs-revision", "make front self-contained", []));
        }

        return ValueTask.FromResult(new CriticOutputDto("approved", "ok", []));
    }
}

public sealed class FailingTeacherAgentPort : IStructuredAgentPort
{
    public ValueTask<GroupPlanDto> PlanGroupsAsync(DayChunk day, NoteProcessingRequest options, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new GroupPlanDto([
            new TopicGroupDto(0, "Vocabulary", "look up", "**look up** - to search")
        ]));

    public ValueTask<TeacherOutputDto> GenerateCardsAsync(TeacherRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromException<TeacherOutputDto>(new AgentBoundaryException(
            "EnglishFlashcardTeacher",
            typeof(TeacherOutputDto),
            "provider returned invalid or incomplete structured JSON after retry",
            new JsonException("Expected end of string, but instead reached end of data.")));

    public ValueTask<CriticOutputDto> ReviewCardsAsync(TeacherDraft draft, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new CriticOutputDto("approved", "ok", []));
}

public class MarkdownAndOutputTests
{
    [Fact]
    public void Splitter_keeps_original_day_block_unchanged()
    {
        var markdown = """
# Notes

## [[2025-03-28-Friday|28.03.2025]]

**look up** - to search for information

```markdown
## not a date
```

## [[2025-03-27-Thursday]]
**at** - used for specific times
""";
        var days = MarkdownDaySplitter.Split(markdown);

        Assert.Equal(2, days.Count);
        Assert.Contains("```markdown\n## not a date\n```", days[0].Markdown);
        Assert.Contains("**look up** - to search for information", days[0].Markdown);
    }

    [Fact]
    public void Formatter_uses_obsidian_sr_multiline_without_legacy_metadata()
    {
        var formatted = ObsidianSrFormatter.FormatCards([
            new Flashcard("term::x\n<!--SR:!2025-01-01,1,250-->", "definition\nsr-due: tomorrow", null, CardDirection.OneWay, 0),
            new Flashcard("look up", "to search", "I looked it up.", CardDirection.OneWay, 0)
        ]);

        Assert.Contains("term:x\n?\ndefinition", formatted);
        Assert.Contains("look up\n?\nto search\n*Example: I looked it up.*", formatted);
        Assert.DoesNotContain("::", formatted);
        Assert.DoesNotContain("<!--SR:", formatted);
        Assert.DoesNotContain("sr-due", formatted);
        Assert.DoesNotContain("[!sr|card-metadata]", formatted);
    }
}

public class WorkflowTests
{
    private static NoteProcessingRequest Request(string root, bool apply = false) => new(
        SourcePath: Path.Combine(root, "source.md"),
        CardsOutputDirectory: Path.Combine(root, "cards"),
        SourceExcerptOutputDirectory: Path.Combine(root, "source-notes"),
        Apply: apply,
        MaxDays: 1,
        MaxParallelGroupWorkers: 2,
        MaxCriticIterations: 2,
        MaxGroupsPerDay: 4);

    private static DayChunk Day => new(0, new DateOnly(2025, 3, 28), "[[2025-03-28-Friday|28.03.2025]]", """
## [[2025-03-28-Friday|28.03.2025]]

**look up** - to search for information

> original note wording must stay as-is
""");

    [Fact]
    public async Task GroupCardWorkflow_loops_once_on_critic_revision_then_finalizes()
    {
        var group = new TopicGroup(0, 0, TopicGroupKind.Vocabulary, "look up", "**look up** - to search", 0);
        var request = Request(Path.GetTempPath());
        var workflow = CSharpMafWorkflowFactory.BuildGroupCardWorkflow(new StubAgentPort(), NullLogger.Instance);

        var result = await WorkflowRunner.RunAsync<GroupResult>(workflow, new GroupCardRequest(Day, group, request), TestContext.Current.CancellationToken);

        Assert.True(result.Accepted);
        Assert.Equal(2, result.Iterations);
        Assert.Contains("revised", result.Cards.Single().Front);
    }

    [Fact]
    public async Task GroupWorkerWorkflow_empty_batch_emits_one_worker_result()
    {
        var request = Request(Path.GetTempPath());
        var plan = new WorkerPartitionPlan(Day, [new WorkerBatch(0, [])], request, []);
        var workflow = CSharpMafWorkflowFactory.BuildGroupWorkerWorkflow(0, new StubAgentPort(), NullLogger.Instance);

        var result = await WorkflowRunner.RunAsync<WorkerBatchResult>(workflow, plan, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.WorkerIndex);
        Assert.Empty(result.Groups);
        Assert.Equal(2, result.ExpectedWorkers);
    }

    [Fact]
    public async Task DayWorkflow_fans_out_to_fixed_workers_and_fans_in_results()
    {
        var root = Path.Combine(Path.GetTempPath(), "efg-csharp-v2", Guid.NewGuid().ToString("N"));
        var request = Request(root);
        var workflow = CSharpMafWorkflowFactory.BuildDayWorkflow(new StubAgentPort(), 2, NullLogger.Instance);

        var result = await WorkflowRunner.RunAsync<DayResult>(workflow, new DayProcessingRequest(Day, request), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.CardsCount);
        Assert.Contains("EnglishFlashcards-2025-03-28.md", result.OutputFiles[0]);
    }

    [Fact]
    public async Task DayWorkflow_copies_source_excerpt_note_without_ai_rewriting()
    {
        var root = Path.Combine(Path.GetTempPath(), "efg-csharp-v2", Guid.NewGuid().ToString("N"));
        var request = Request(root, apply: true);
        var workflow = CSharpMafWorkflowFactory.BuildDayWorkflow(new StubAgentPort(), 2, NullLogger.Instance);

        var result = await WorkflowRunner.RunAsync<DayResult>(workflow, new DayProcessingRequest(Day, request), TestContext.Current.CancellationToken);
        var excerpt = await File.ReadAllTextAsync(result.OutputFiles[1], TestContext.Current.CancellationToken);

        Assert.Contains("**look up** - to search for information", excerpt);
        Assert.Contains("> original note wording must stay as-is", excerpt);
    }

    [Fact]
    public async Task NoteWorkflow_runs_top_level_steps_and_emits_summary()
    {
        var root = Path.Combine(Path.GetTempPath(), "efg-csharp-v2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var request = Request(root);
        await File.WriteAllTextAsync(request.SourcePath, """
# Notes

## [[2025-03-28-Friday|28.03.2025]]
**look up** - to search for information

## [[2025-03-27-Thursday]]
**at** - used for specific times
""", TestContext.Current.CancellationToken);
        var workflow = CSharpMafWorkflowFactory.BuildNoteWorkflow(new StubAgentPort(), NullLogger.Instance);

        var summary = await WorkflowRunner.RunAsync<RunSummary>(workflow, request, TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.DaysProcessed);
        Assert.Equal(1, summary.DaysSucceeded);
        Assert.Equal(0, summary.Warnings.Count);
    }

    [Fact]
    public async Task NoteWorkflow_prunes_source_note_when_prune_flag_is_enabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "efg-csharp-v2", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "input.md");
        Directory.CreateDirectory(root);
        
        var originalMarkdown = """
        ## [[2025-03-28-Friday|28.03.2025]]
        
        **look up** - to search for information
        
        ## [[2025-03-29-Saturday|29.03.2025]]
        
        **give up** - to stop trying
        """;
        await File.WriteAllTextAsync(sourcePath, originalMarkdown);

        var request = new NoteProcessingRequest(
            SourcePath: sourcePath,
            CardsOutputDirectory: Path.Combine(root, "cards"),
            SourceExcerptOutputDirectory: Path.Combine(root, "source-notes"),
            Apply: true,
            PruneSource: true,
            MaxDays: 1,
            MaxParallelGroupWorkers: 1,
            MaxCriticIterations: 1,
            MaxGroupsPerDay: 1
        );

        var workflow = CSharpMafWorkflowFactory.BuildNoteWorkflow(new StubAgentPort(), NullLogger.Instance);
        var summary = await WorkflowRunner.RunAsync<RunSummary>(workflow, request, TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.DaysSucceeded);
        
        var prunedMarkdown = await File.ReadAllTextAsync(sourcePath);
        Assert.DoesNotContain("2025-03-28", prunedMarkdown);
        Assert.Contains("2025-03-29", prunedMarkdown);
    }

    [Fact]
    public async Task WorkflowRunner_surfaces_executor_failure_as_warnings()
    {
        var group = new TopicGroup(0, 0, TopicGroupKind.Vocabulary, "look up", "**look up** - to search", 0);
        var request = Request(Path.GetTempPath());
        var workflow = CSharpMafWorkflowFactory.BuildGroupCardWorkflow(new FailingTeacherAgentPort(), NullLogger.Instance);

        var result = await WorkflowRunner.RunAsync<GroupResult>(workflow, new GroupCardRequest(Day, group, request), TestContext.Current.CancellationToken);

        Assert.Empty(result.Cards);
        Assert.Contains(result.Warnings, w => w.Contains("Teacher agent failed"));
    }

    [Fact]
    public async Task NoteWorkflow_surfaces_nested_workflow_failure_as_warnings()
    {
        var root = Path.Combine(Path.GetTempPath(), "efg-csharp-v2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var request = Request(root);
        await File.WriteAllTextAsync(request.SourcePath, """
# Notes

## [[2025-03-28-Friday|28.03.2025]]
**look up** - to search for information
""", TestContext.Current.CancellationToken);
        var workflow = CSharpMafWorkflowFactory.BuildNoteWorkflow(new FailingTeacherAgentPort(), NullLogger.Instance);

        var summary = await WorkflowRunner.RunAsync<RunSummary>(workflow, request, TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.CardsWritten);
        Assert.Contains(summary.Warnings, w => w.Contains("Teacher agent failed"));
    }

    [Fact]
    public async Task StructuredAgentRetryPolicy_retries_once_after_json_failure()
    {
        var attempts = 0;

        var result = await StructuredAgentRetryPolicy.RunAsync<TeacherOutputDto>(
            "EnglishFlashcardTeacher",
            (attempt, _) =>
            {
                attempts = attempt;
                if (attempt == 1)
                {
                    throw new JsonException("truncated JSON");
                }

                return ValueTask.FromResult(new TeacherOutputDto([
                    new TeacherCardDto("look up", "search", null)
                ]));
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        Assert.Single(result.Cards);
    }

    [Fact]
    public async Task StructuredAgentRetryPolicy_retries_once_after_invalid_structured_result()
    {
        var attempts = 0;

        var result = await StructuredAgentRetryPolicy.RunAsync<TeacherOutputDto>(
            "EnglishFlashcardTeacher",
            (attempt, _) =>
            {
                attempts = attempt;
                if (attempt == 1)
                {
                    throw new InvalidOperationException("Structured response result was null.");
                }

                return ValueTask.FromResult(new TeacherOutputDto([
                    new TeacherCardDto("look up", "search", null)
                ]));
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, attempts);
        Assert.Single(result.Cards);
    }

    [Fact]
    public async Task StructuredAgentRetryPolicy_fails_with_agent_boundary_context_after_retry()
    {
        var attempts = 0;

        var ex = await Assert.ThrowsAsync<AgentBoundaryException>(() =>
            StructuredAgentRetryPolicy.RunAsync<TeacherOutputDto>(
                "EnglishFlashcardTeacher",
                (attempt, _) =>
                {
                    attempts = attempt;
                    throw new JsonException("truncated JSON");
                },
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, attempts);
        Assert.Equal("EnglishFlashcardTeacher", ex.AgentName);
        Assert.Equal(typeof(TeacherOutputDto), ex.OutputType);
        Assert.Contains("TeacherOutputDto", ex.Message);
        Assert.IsType<JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task StructuredAgentRetryPolicy_wraps_provider_failure_with_agent_boundary_context()
    {
        var providerFailure = new HttpRequestException("local provider disconnected while streaming JSON");

        var ex = await Assert.ThrowsAsync<AgentBoundaryException>(() =>
            StructuredAgentRetryPolicy.RunAsync<TeacherOutputDto>(
                "EnglishFlashcardTeacher",
                (_, _) => throw providerFailure,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("EnglishFlashcardTeacher", ex.AgentName);
        Assert.Equal(typeof(TeacherOutputDto), ex.OutputType);
        Assert.Contains("TeacherOutputDto", ex.Message);
        Assert.Same(providerFailure, ex.InnerException);
    }

    private static IEnumerable<Exception> EnumerateInnerExceptions(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
