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
          Apply: bool
          CardMode: string option
          GeneratorMode: string option
          BaseUrl: string option
          ApiKey: string option
          Model: string option
          TimeoutSeconds: int option
          MaxSections: int option
          MaxOutputTokens: int option
          Temperature: float option }

    let private empty =
        { Source = None
          CardsOut = None
          NotesOut = None
          Apply = false
          CardMode = None
          GeneratorMode = None
          BaseUrl = None
          ApiKey = None
          Model = None
          TimeoutSeconds = None
          MaxSections = None
          MaxOutputTokens = None
          Temperature = None }

    let private parseInt name (value: string) =
        match Int32.TryParse value with
        | true, parsed -> parsed
        | false, _ -> invalidArg name $"{name} must be an integer."

    let private parseFloat name (value: string) =
        match Double.TryParse(value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
        | true, parsed -> parsed
        | false, _ -> invalidArg name $"{name} must be a number."

    let private parseArgs args =
        let rec loop options remaining =
            match remaining with
            | [] -> options
            | "--source" :: value :: tail -> loop { options with Source = Some value } tail
            | "--cards-out" :: value :: tail -> loop { options with CardsOut = Some value } tail
            | "--notes-out" :: value :: tail -> loop { options with NotesOut = Some value } tail
            | "--apply" :: tail -> loop { options with Apply = true } tail
            | "--card-mode" :: value :: tail -> loop { options with CardMode = Some value } tail
            | "--generator-mode" :: value :: tail -> loop { options with GeneratorMode = Some value } tail
            | "--llm-base-url" :: value :: tail -> loop { options with BaseUrl = Some value } tail
            | "--llm-api-key" :: value :: tail -> loop { options with ApiKey = Some value } tail
            | "--llm-model" :: value :: tail -> loop { options with Model = Some value } tail
            | "--timeout-seconds" :: value :: tail -> loop { options with TimeoutSeconds = Some(parseInt "timeout-seconds" value) } tail
            | "--max-sections" :: value :: tail -> loop { options with MaxSections = Some(parseInt "max-sections" value) } tail
            | "--max-output-tokens" :: value :: tail -> loop { options with MaxOutputTokens = Some(parseInt "max-output-tokens" value) } tail
            | "--temperature" :: value :: tail -> loop { options with Temperature = Some(parseFloat "temperature" value) } tail
            | unknown :: _ -> invalidArg "args" $"Unknown or incomplete argument: {unknown}"
        loop empty (args |> Array.toList)

    let private require name value =
        match value with
        | Some v when not (String.IsNullOrWhiteSpace v) -> v
        | _ -> invalidArg name $"Missing required argument --{name}"

    let private optionOrEnv option envName =
        match option with
        | Some value when not (String.IsNullOrWhiteSpace value) -> Some value
        | _ ->
            let value = Environment.GetEnvironmentVariable envName
            if String.IsNullOrWhiteSpace value then None else Some value

    let private buildGeneration options =
        let cardDirection =
            options.CardMode
            |> Option.map CardDirection.parse
            |> Option.defaultValue CardDirection.defaultValue
        let mode =
            options.GeneratorMode
            |> Option.map GeneratorMode.parse
            |> Option.defaultValue Fake
        let maxSections = options.MaxSections |> Option.defaultValue 1
        if maxSections < 1 || maxSections > 2 then
            invalidArg "max-sections" "Max sections must be 1 or 2."

        match mode with
        | Fake ->
            { Mode = Fake
              CardDirection = cardDirection
              MaxSections = maxSections
              Llm = None }
        | OpenAICompatible ->
            let baseUrl = require "llm-base-url" (optionOrEnv options.BaseUrl "LITELLM_BASE_URL")
            let apiKey = require "llm-api-key" (optionOrEnv options.ApiKey "LITELLM_API_KEY")
            let model = require "llm-model" (optionOrEnv options.Model "LITELLM_MODEL")
            let timeoutSeconds = options.TimeoutSeconds |> Option.defaultValue 120
            if timeoutSeconds < 1 || timeoutSeconds > 120 then
                invalidArg "timeout-seconds" "Timeout must be between 1 and 120 seconds."
            let maxOutputTokens = options.MaxOutputTokens |> Option.defaultValue 2048
            if maxOutputTokens < 1 || maxOutputTokens > 2048 then
                invalidArg "max-output-tokens" "Max output tokens must be between 1 and 2048."
            { Mode = OpenAICompatible
              CardDirection = cardDirection
              MaxSections = maxSections
              Llm =
                Some
                    { BaseUrl = baseUrl
                      ApiKey = apiKey
                      Model = model
                      TimeoutSeconds = timeoutSeconds
                      MaxOutputTokens = maxOutputTokens
                      Temperature = options.Temperature |> Option.defaultValue 0.0 } }

    [<EntryPoint>]
    let main args =
        try
            let options = parseArgs args
            let source = require "source" options.Source
            let cardsOut = require "cards-out" options.CardsOut
            let notesOut = require "notes-out" options.NotesOut
            let generation = buildGeneration options
            let sourcePath = SourcePath.createUnsafe source
            let output =
                { CardsDirectory = OutputDirectory.createUnsafe cardsOut
                  NotesDirectory = OutputDirectory.createUnsafe notesOut
                  Mode = if options.Apply then Apply else DryRun
                  CardDirection = generation.CardDirection }
            let markdown = File.ReadAllText source
            let input = { SourcePath = sourcePath; MarkdownText = markdown; Output = output; Generation = generation }
            use cts = new CancellationTokenSource(TimeSpan.FromSeconds(float (generation.Llm |> Option.map _.TimeoutSeconds |> Option.defaultValue 120)))
            let result = FlashcardWorkflow.runAsync input cts.Token |> Async.AwaitTask |> Async.RunSynchronously
            printfn "mode=%A" output.Mode
            printfn "generator=%A" generation.Mode
            printfn "cardMode=%A" generation.CardDirection
            printfn "maxSections=%d" generation.MaxSections
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
