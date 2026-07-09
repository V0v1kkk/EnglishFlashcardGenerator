namespace EnglishFlashcardGenerator.Core

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Agents.AI.Workflows

[<RequireQualifiedAccess>]
module FlashcardWorkflow =
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

    let build (output: OutputOptions) (generation: GenerationOptions) =
        let parser =
            bind "markdown-parser" (fun input ->
                MarkdownDocumentParser.parse (Some input.SourcePath) input.MarkdownText)
        let planner = bind "section-planner" (chooseSection generation.MaxSections)
        let teacher = bindAsync "teacher-agent" (fun request ct -> TeacherAgent.generateAsync generation request ct)
        let reviewer = bindAsync "reviewer-agent" (fun draft ct -> ReviewerAgent.reviewAsync generation draft ct)
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

    let runAsync (input: WorkflowInput) (ct: CancellationToken) = task {
        let workflow = build input.Output input.Generation
        let! run = InProcessExecution.RunAsync(workflow, input, cancellationToken = ct).AsTask()
        return
            run.NewEvents
            |> Seq.choose (function
                | :? WorkflowOutputEvent as output when output.Is<WorkflowRunResult>() -> Some(output.As<WorkflowRunResult>())
                | _ -> None)
            |> Seq.tryLast
            |> Option.defaultWith (fun () -> failwith "Workflow completed without a WorkflowRunResult output.")
    }
