namespace EnglishFlashcardGenerator.Core

open System

[<Struct>]
type SourcePath = private SourcePath of string with
    member this.Value = let (SourcePath v) = this in v

[<RequireQualifiedAccess>]
module SourcePath =
    let value (SourcePath v) = v
    let create (s: string) =
        if String.IsNullOrWhiteSpace s then Error "SourcePath must not be empty"
        else Ok (SourcePath s)
    let createUnsafe s = SourcePath s

[<Struct>]
type OutputDirectory = private OutputDirectory of string with
    member this.Value = let (OutputDirectory v) = this in v

[<RequireQualifiedAccess>]
module OutputDirectory =
    let value (OutputDirectory v) = v
    let create (s: string) =
        if String.IsNullOrWhiteSpace s then Error "OutputDirectory must not be empty"
        else Ok (OutputDirectory s)
    let createUnsafe s = OutputDirectory s

[<Struct>]
type DateHeading = private DateHeading of DateOnly with
    member this.Value = let (DateHeading v) = this in v

[<RequireQualifiedAccess>]
module DateHeading =
    let value (DateHeading v) = v
    let create (value: DateOnly) = DateHeading value

type MarkdownSection =
    { Level: int
      HeadingText: string
      Date: DateHeading option
      Start: int
      EndExclusive: int
      RawText: string }

type ParsedDocument =
    { SourcePath: SourcePath option
      FrontMatter: Map<string, string>
      Sections: MarkdownSection list
      OriginalText: string }

type CardDirection =
    | OneWay
    | Bidirectional

[<RequireQualifiedAccess>]
module CardDirection =
    let defaultValue = Bidirectional

    let separator = function
        | OneWay -> "?"
        | Bidirectional -> "??"

    let label = function
        | OneWay -> "one-way"
        | Bidirectional -> "bidirectional"

    let tryParse (value: string) =
        match value.Trim().ToLowerInvariant() with
        | "one-way" | "oneway" | "one_way" | "forward" | "?" -> Some OneWay
        | "bidirectional" | "bi-directional" | "reverse" | "reversed" | "??" -> Some Bidirectional
        | _ -> None

    let parse (value: string) =
        match tryParse value with
        | Some direction -> direction
        | None -> invalidArg "card-mode" "Card mode must be one-way or bidirectional."

type FlashCard =
    { Front: string
      Back: string
      Example: string option
      Direction: CardDirection option }

[<CLIMutable>]
type StructuredFlashCardDto =
    { Front: string
      Back: string
      Example: string
      Direction: string }

[<CLIMutable>]
type TeacherOutputDto =
    { Cards: StructuredFlashCardDto list }

[<CLIMutable>]
type ReviewerOutputDto =
    { Approved: bool
      Feedback: string list }

type GenerationRequest =
    { Document: ParsedDocument
      Section: MarkdownSection }

type TeacherDraft =
    { Request: GenerationRequest
      Cards: FlashCard list }

type ReviewResult =
    { Draft: TeacherDraft
      Approved: bool
      Feedback: string list
      Iteration: int }

[<RequireQualifiedAccess>]
module ReviewerOutputDto =
    let toReview iteration draft (dto: ReviewerOutputDto) =
        let feedback =
            if isNull (box dto) || isNull (box dto.Feedback) then []
            else dto.Feedback |> List.filter (String.IsNullOrWhiteSpace >> not)
        { Draft = draft
          Approved = dto.Approved
          Feedback = feedback
          Iteration = iteration }

[<RequireQualifiedAccess>]
module StructuredFlashCardDto =
    let toFlashCard fallbackDirection (dto: StructuredFlashCardDto) : FlashCard =
        let requireText fieldName (value: string) =
            if String.IsNullOrWhiteSpace value then
                invalidOp $"Structured teacher output included an empty {fieldName}."
            value.Trim()

        let direction =
            if String.IsNullOrWhiteSpace dto.Direction then
                fallbackDirection
            else
                match CardDirection.tryParse dto.Direction with
                | Some parsed -> parsed
                | None -> invalidOp $"Structured teacher output included unsupported direction '{dto.Direction}'."

        ({ Front = requireText "front" dto.Front
           Back = requireText "back" dto.Back
           Example = if String.IsNullOrWhiteSpace dto.Example then None else Some(dto.Example.Trim())
           Direction = Some direction } : FlashCard)

[<RequireQualifiedAccess>]
module TeacherOutputDto =
    let toDraft fallbackDirection request (dto: TeacherOutputDto) =
        if isNull (box dto) || isNull (box dto.Cards) then
            invalidOp "Structured teacher output did not include cards."
        let cards = dto.Cards |> List.map (StructuredFlashCardDto.toFlashCard fallbackDirection)
        if cards.IsEmpty then
            invalidOp "Structured teacher output did not include any cards."
        ({ Request = request; Cards = cards } : TeacherDraft)

type NormalizedCards =
    { Section: MarkdownSection
      Cards: FlashCard list }

type WriteMode =
    | DryRun
    | Apply

type GeneratorMode =
    | Fake
    | OpenAICompatible

[<RequireQualifiedAccess>]
module GeneratorMode =
    let tryParse (value: string) =
        match value.Trim().ToLowerInvariant() with
        | "fake" -> Some Fake
        | "openai" | "local" | "litellm" -> Some OpenAICompatible
        | _ -> None

    let parse (value: string) =
        match tryParse value with
        | Some mode -> mode
        | None -> invalidArg "generator-mode" "Generator mode must be fake, openai, local, or litellm."

type LlmOptions =
    { BaseUrl: string
      ApiKey: string
      Model: string
      TimeoutSeconds: int
      MaxOutputTokens: int option
      Temperature: float
      DisableThinking: bool }

type GenerationOptions =
    { Mode: GeneratorMode
      CardDirection: CardDirection
      MaxSections: int
      Llm: LlmOptions option }

[<RequireQualifiedAccess>]
module GenerationOptions =
    let defaultValue =
        { Mode = Fake
          CardDirection = CardDirection.defaultValue
          MaxSections = 1
          Llm = None }

type OutputOptions =
    { CardsDirectory: OutputDirectory
      NotesDirectory: OutputDirectory
      Mode: WriteMode
      CardDirection: CardDirection }

type WritePlan =
    { CardsPath: string
      NotePath: string
      CardsContent: string
      NoteContent: string
      Mode: WriteMode }

type WorkflowInput =
    { SourcePath: SourcePath
      MarkdownText: string
      Output: OutputOptions
      Generation: GenerationOptions }

type WorkflowRunResult =
    { ParsedSections: int
      Cards: FlashCard list
      WritePlan: WritePlan }
