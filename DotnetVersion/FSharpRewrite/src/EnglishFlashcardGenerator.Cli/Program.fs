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
          Temperature: float option
          DisableThinking: bool }

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
          Temperature = None
          DisableThinking = false }

    let private parseInt name (value: string) =
        match Int32.TryParse value with
        | true, parsed -> parsed
        | false, _ -> invalidArg name $"{name} must be an integer."

    let private parseFloat name (value: string) =
        match Double.TryParse(value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
        | true, parsed -> parsed
        | false, _ -> invalidArg name $"{name} must be a number."

    let private parseBool name (value: string) =
        match value.Trim().ToLowerInvariant() with
        | "1" | "true" | "yes" | "on" -> true
        | "0" | "false" | "no" | "off" -> false
        | _ -> invalidArg name $"{name} must be true or false."

    let private parseArgs args =
        let rec loop options remaining =
            match remaining with
            | [] -> options
            | "--source" :: value :: tail -> loop { options with Source = Some value } tail
            | "--cards-out" :: value :: tail -> loop { options with CardsOut = Some value } tail
            | "--notes-out" :: value :: tail -> loop { options with NotesOut = Some value } tail
            | "--apply" :: tail -> loop { options with Apply = true } tail
            | "--card-mode" :: value :: tail -> loop { options with CardMode = Some value } tail
            | "--generator-mode" :: value :: tail
            | "--llm-mode" :: value :: tail
            | "--mode" :: value :: tail -> loop { options with GeneratorMode = Some value } tail
            | "--llm-base-url" :: value :: tail
            | "--base-url" :: value :: tail -> loop { options with BaseUrl = Some value } tail
            | "--llm-api-key" :: value :: tail
            | "--api-key" :: value :: tail -> loop { options with ApiKey = Some value } tail
            | "--llm-model" :: value :: tail
            | "--model" :: value :: tail -> loop { options with Model = Some value } tail
            | "--timeout-seconds" :: value :: tail
            | "--timeout" :: value :: tail -> loop { options with TimeoutSeconds = Some(parseInt "timeout-seconds" value) } tail
            | "--max-sections" :: value :: tail -> loop { options with MaxSections = Some(parseInt "max-sections" value) } tail
            | "--max-output-tokens" :: value :: tail
            | "--max-tokens" :: value :: tail -> loop { options with MaxOutputTokens = Some(parseInt "max-output-tokens" value) } tail
            | "--temperature" :: value :: tail -> loop { options with Temperature = Some(parseFloat "temperature" value) } tail
            | "--llm-disable-thinking" :: tail
            | "--disable-thinking" :: tail -> loop { options with DisableThinking = true } tail
            | unknown :: _ -> invalidArg "args" $"Unknown or incomplete argument: {unknown}"
        loop empty (args |> Array.toList)

    let private require name value =
        match value with
        | Some v when not (String.IsNullOrWhiteSpace v) -> v
        | _ -> invalidArg name $"Missing required argument --{name}"

    let private tryGetEnv envName =
        let value = Environment.GetEnvironmentVariable envName
        if String.IsNullOrWhiteSpace value then None else Some value

    let private optionOrEnv option envNames =
        match option with
        | Some value when not (String.IsNullOrWhiteSpace value) -> Some value
        | _ -> envNames |> List.tryPick tryGetEnv

    let private intOptionOrEnv option argumentName envNames =
        match option with
        | Some value -> value
        | None ->
            envNames
            |> List.tryPick tryGetEnv
            |> Option.map (parseInt argumentName)
            |> Option.defaultValue 1

    let private outputTokensOptionOrEnv option =
        match option with
        | Some value -> Some value
        | None ->
            [ "LITELLM_MAX_TOKENS"; "LITELLM_MAX_OUTPUT_TOKENS"; "OPENAI_MAX_TOKENS" ]
            |> List.tryPick tryGetEnv
            |> Option.map (parseInt "max-output-tokens")

    let private optionalIntOptionOrEnv option argumentName envNames =
        match option with
        | Some value -> Some value
        | None -> envNames |> List.tryPick tryGetEnv |> Option.map (parseInt argumentName)

    let private optionalFloatOptionOrEnv option argumentName envNames =
        match option with
        | Some value -> Some value
        | None -> envNames |> List.tryPick tryGetEnv |> Option.map (parseFloat argumentName)

    let private boolFlagOrEnv flag argumentName envNames =
        if flag then true
        else
            envNames
            |> List.tryPick tryGetEnv
            |> Option.map (parseBool argumentName)
            |> Option.defaultValue false

    let private buildGeneration options =
        let cardDirection =
            optionOrEnv options.CardMode [ "FLASHCARD_CARD_MODE"; "CARD_MODE" ]
            |> Option.map CardDirection.parse
            |> Option.defaultValue CardDirection.defaultValue
        let mode =
            optionOrEnv options.GeneratorMode [ "LITELLM_MODE"; "GENERATOR_MODE"; "LLM_MODE" ]
            |> Option.map GeneratorMode.parse
            |> Option.defaultValue Fake
        let maxSections = intOptionOrEnv options.MaxSections "max-sections" [ "LITELLM_MAX_SECTIONS"; "GENERATOR_MAX_SECTIONS"; "MAX_SECTIONS" ]
        if maxSections < 1 || maxSections > 2 then
            invalidArg "max-sections" "Max sections must be 1 or 2."

        match mode with
        | Fake ->
            { Mode = Fake
              CardDirection = cardDirection
              MaxSections = maxSections
              Llm = None }
        | OpenAICompatible ->
            let baseUrl = require "llm-base-url" (optionOrEnv options.BaseUrl [ "LITELLM_BASE_URL"; "OPENAI_BASE_URL" ])
            let apiKey = require "llm-api-key" (optionOrEnv options.ApiKey [ "LITELLM_API_KEY"; "OPENAI_API_KEY" ])
            let model = require "llm-model" (optionOrEnv options.Model [ "LITELLM_MODEL"; "OPENAI_MODEL" ])
            let timeoutSeconds =
                optionalIntOptionOrEnv options.TimeoutSeconds "timeout-seconds" [ "LITELLM_TIMEOUT"; "LITELLM_TIMEOUT_SECONDS"; "OPENAI_TIMEOUT"; "OPENAI_TIMEOUT_SECONDS" ]
                |> Option.defaultValue 120
            if timeoutSeconds < 1 || timeoutSeconds > 120 then
                invalidArg "timeout-seconds" "Timeout must be between 1 and 120 seconds."
            let maxOutputTokens = outputTokensOptionOrEnv options.MaxOutputTokens
            match maxOutputTokens with
            | Some value when value < 1 || value > 32768 ->
                invalidArg "max-output-tokens" "Max output tokens must be between 1 and 32768."
            | _ -> ()
            let temperature =
                optionalFloatOptionOrEnv options.Temperature "temperature" [ "LITELLM_TEMPERATURE"; "OPENAI_TEMPERATURE" ]
                |> Option.defaultValue 0.0
            if temperature < 0.0 || temperature > 1.0 then
                invalidArg "temperature" "Temperature must be between 0 and 1 for bounded smoke runs."
            let disableThinking =
                boolFlagOrEnv options.DisableThinking "llm-disable-thinking" [ "LITELLM_DISABLE_THINKING"; "OPENAI_DISABLE_THINKING" ]
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
                      Temperature = temperature
                      DisableThinking = disableThinking } }

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
