namespace EnglishFlashcardGenerator.Cli

open System
open System.IO
open System.Threading
open EnglishFlashcardGenerator.Core

module Program =
    type CliOptions =
        { Source: string option
          CardsOut: string option
          NotesOut: string option
          Apply: bool }

    let private empty = { Source = None; CardsOut = None; NotesOut = None; Apply = false }

    let private parseArgs args =
        let rec loop options remaining =
            match remaining with
            | [] -> options
            | "--source" :: value :: tail -> loop { options with Source = Some value } tail
            | "--cards-out" :: value :: tail -> loop { options with CardsOut = Some value } tail
            | "--notes-out" :: value :: tail -> loop { options with NotesOut = Some value } tail
            | "--apply" :: tail -> loop { options with Apply = true } tail
            | unknown :: _ -> invalidArg "args" $"Unknown or incomplete argument: {unknown}"
        loop empty (args |> Array.toList)

    let private require name value =
        match value with
        | Some v when not (String.IsNullOrWhiteSpace v) -> v
        | _ -> invalidArg name $"Missing required argument --{name}"

    [<EntryPoint>]
    let main args =
        try
            let options = parseArgs args
            let source = require "source" options.Source
            let cardsOut = require "cards-out" options.CardsOut
            let notesOut = require "notes-out" options.NotesOut
            let sourcePath = SourcePath.createUnsafe source
            let output =
                { CardsDirectory = OutputDirectory.createUnsafe cardsOut
                  NotesDirectory = OutputDirectory.createUnsafe notesOut
                  Mode = if options.Apply then Apply else DryRun }
            let markdown = File.ReadAllText source
            let input = { SourcePath = sourcePath; MarkdownText = markdown; Output = output }
            let result = FlashcardWorkflow.runAsync input CancellationToken.None |> Async.AwaitTask |> Async.RunSynchronously
            printfn "mode=%A" output.Mode
            printfn "cards=%d" result.Cards.Length
            printfn "cardsPath=%s" result.WritePlan.CardsPath
            printfn "notePath=%s" result.WritePlan.NotePath
            if output.Mode = DryRun then
                printfn "dryRun=true"
                printfn "cardsPreviewStart"
                printfn "%s" result.WritePlan.CardsContent
                printfn "cardsPreviewEnd"
            0
        with ex ->
            eprintfn "%s" ex.Message
            1
