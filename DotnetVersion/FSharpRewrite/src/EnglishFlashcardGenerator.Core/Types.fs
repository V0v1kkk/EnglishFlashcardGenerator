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

type FlashCard =
    { Front: string
      Back: string
      Example: string option }

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

type NormalizedCards =
    { Section: MarkdownSection
      Cards: FlashCard list }

type WriteMode =
    | DryRun
    | Apply

type OutputOptions =
    { CardsDirectory: OutputDirectory
      NotesDirectory: OutputDirectory
      Mode: WriteMode }

type WritePlan =
    { CardsPath: string
      NotePath: string
      CardsContent: string
      NoteContent: string
      Mode: WriteMode }

type WorkflowInput =
    { SourcePath: SourcePath
      MarkdownText: string
      Output: OutputOptions }

type WorkflowRunResult =
    { ParsedSections: int
      Cards: FlashCard list
      WritePlan: WritePlan }
