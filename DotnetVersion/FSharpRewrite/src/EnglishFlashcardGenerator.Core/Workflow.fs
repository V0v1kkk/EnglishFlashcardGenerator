namespace EnglishFlashcardGenerator.Core

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Agents.AI.Workflows

[<RequireQualifiedAccess>]
module FlashcardWorkflow =
    type TeacherGenerator = GenerationRequest -> CancellationToken -> Task<TeacherDraft>
    type ReviewerGenerator = TeacherDraft -> CancellationToken -> Task<ReviewResult>

    let private bind name (f: 'input -> 'output) : ExecutorBinding =
        let fn = Func<'input, 'output>(f)
        ExecutorBindingExtensions.BindAsExecutor<'input, 'output>(fn, name, null, false)

    let private bindAsync name (f: 'input -> CancellationToken -> Task<'output>) : ExecutorBinding =
        let fn = Func<'input, CancellationToken, ValueTask<'output>>(fun input ct -> ValueTask<'output>(f input ct))
        ExecutorBindingExtensions.BindAsExecutor<'input, 'output>(fn, name, null, false)

    let private chooseSection maxSections document =
        if maxSections < 1 || maxSections > 2 then
            invalidArg "max-sections" "Max sections must be 1 or 2 for bounded smoke runs."
        match SectionPlanner.chooseLatestDatedSection document with
        | Some request -> request
        | None -> failwith "No dated level-2 markdown section found."

    let private buildWithAgents (output: OutputOptions) (generation: GenerationOptions) (teacherAgent: TeacherGenerator) (reviewerAgent: ReviewerGenerator) =
        let parser =
            bind "markdown-parser" (fun input ->
                MarkdownDocumentParser.parse (Some input.SourcePath) input.MarkdownText)
        let planner = bind "section-planner" (chooseSection generation.MaxSections)
        let teacher = bindAsync "teacher-agent" teacherAgent
        let reviewer = bindAsync "reviewer-agent" reviewerAgent
        let normalizer = bind "card-normalizer" CardNormalizer.normalize
        let writer =
            bind "output-writer" (fun normalized ->
                let plan = MarkdownOutputWriter.buildPlan output normalized
                let executed = MarkdownOutputWriter.execute plan
                { ParsedSections = normalized.Section.RawText.Length
                  Cards = normalized.Cards
                  WritePlan = executed })

        WorkflowBuilder(parser)
            .AddEdge(parser, planner)
            .AddEdge(planner, teacher)
            .AddEdge(teacher, reviewer)
            .AddEdge(reviewer, normalizer)
            .AddEdge(normalizer, writer)
            .WithOutputFrom(writer)
            .WithName("EnglishFlashcardGenerator.FSharpVerticalSlice")
            .WithDescription("F# Microsoft Agent Framework workflow: Markdig parser -> teacher generator -> reviewer -> normalizer -> writer")
            .Build()

    let build (output: OutputOptions) (generation: GenerationOptions) =
        buildWithAgents
            output
            generation
            (fun request ct -> TeacherAgent.generateAsync generation request ct)
            (fun draft ct -> ReviewerAgent.reviewAsync generation draft ct)

    let private exceptionMessage (error: obj) =
        match error with
        | null -> "unknown error"
        | :? exn as ex -> $"{ex.GetType().FullName}: {ex.Message}"
        | other -> string other

    let private noOutputError (events: WorkflowEvent list) =
        let failures =
            events
            |> List.choose (function
                | :? ExecutorFailedEvent as failed ->
                    Some($"executor `{failed.ExecutorId}` failed: {exceptionMessage failed.Data}")
                | :? SubworkflowErrorEvent as failed ->
                    Some($"subworkflow `{failed.SubworkflowId}` failed: {exceptionMessage failed.Exception}")
                | :? WorkflowErrorEvent as failed ->
                    Some($"workflow failed: {exceptionMessage failed.Exception}")
                | _ -> None)

        let eventTypes =
            events
            |> List.map (fun event -> event.GetType().Name)
            |> List.distinct

        let detail =
            match failures with
            | [] ->
                let observedEvents = String.Join(", ", eventTypes)
                $"Observed workflow events: {observedEvents}."
            | _ -> String.Join(Environment.NewLine, failures)

        InvalidOperationException($"Workflow completed without a WorkflowRunResult output.{Environment.NewLine}{detail}")

    let private runWorkflowAsync (workflow: Workflow) (input: WorkflowInput) (ct: CancellationToken) = task {
        let! run = InProcessExecution.RunAsync(workflow, input, cancellationToken = ct).AsTask()
        let events = run.NewEvents |> Seq.toList
        return
            events
            |> Seq.choose (function
                | :? WorkflowOutputEvent as output when output.Is<WorkflowRunResult>() -> Some(output.As<WorkflowRunResult>())
                | _ -> None)
            |> Seq.tryLast
            |> Option.defaultWith (fun () -> raise (noOutputError events))
    }

    let runWithAgentsAsync (input: WorkflowInput) (teacherAgent: TeacherGenerator) (reviewerAgent: ReviewerGenerator) (ct: CancellationToken) = task {
        let workflow = buildWithAgents input.Output input.Generation teacherAgent reviewerAgent
        return! runWorkflowAsync workflow input ct
    }

    let runAsync (input: WorkflowInput) (ct: CancellationToken) = task {
        let workflow = build input.Output input.Generation
        return! runWorkflowAsync workflow input ct
    }
