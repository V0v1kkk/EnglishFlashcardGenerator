namespace EnglishFlashcardGenerator.Core

open System
open System.IO
open System.Text

[<RequireQualifiedAccess>]
module FlashcardFormatter =
    let formatCard (card: FlashCard) =
        let back =
            match card.Example with
            | Some example when not (String.IsNullOrWhiteSpace example) -> $"{card.Back}\n\n{example}"
            | _ -> card.Back
        $"{card.Front}:: {back}"

    let formatCards cards =
        cards
        |> List.map formatCard
        |> String.concat "\n\n"

[<RequireQualifiedAccess>]
module OutputPathResolver =
    let private datePart (section: MarkdownSection) =
        section.Date
        |> Option.map (DateHeading.value >> fun d -> d.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture))
        |> Option.defaultValue "undated"

    let cardsPath (output: OutputOptions) section =
        Path.Combine(OutputDirectory.value output.CardsDirectory, $"EnglishFlashcards-{datePart section}.md")

    let notePath (output: OutputOptions) section =
        Path.Combine(OutputDirectory.value output.NotesDirectory, $"EnglishLearningNote-{datePart section}.md")

[<RequireQualifiedAccess>]
module MarkdownOutputWriter =
    let private stripHeadingLine (rawText: string) =
        let normalized = rawText.Replace("\r\n", "\n")
        let firstBreak = normalized.IndexOf('\n')
        if firstBreak < 0 then "" else normalized.Substring(firstBreak + 1).TrimStart('\n')

    let buildPlan (output: OutputOptions) (normalized: NormalizedCards) =
        let section = normalized.Section
        let cardBody = FlashcardFormatter.formatCards normalized.Cards
        let noteBody = stripHeadingLine section.RawText
        let cardsContent =
            $"---\ntags:\n  - english\n  - flashcards\nsource: {section.HeadingText}\n---\n\n# Flashcards for {section.HeadingText}\n\n{cardBody}\n"
        let noteContent =
            $"---\ntags:\n  - english\nsource: {section.HeadingText}\n---\n\n# English learning note {section.HeadingText}\n\n{noteBody}\n"
        { CardsPath = OutputPathResolver.cardsPath output section
          NotePath = OutputPathResolver.notePath output section
          CardsContent = cardsContent
          NoteContent = noteContent
          Mode = output.Mode }

    let execute (plan: WritePlan) =
        match plan.Mode with
        | DryRun -> plan
        | Apply ->
            Directory.CreateDirectory(Path.GetDirectoryName plan.CardsPath) |> ignore
            Directory.CreateDirectory(Path.GetDirectoryName plan.NotePath) |> ignore
            File.WriteAllText(plan.CardsPath, plan.CardsContent, Encoding.UTF8)
            File.WriteAllText(plan.NotePath, plan.NoteContent, Encoding.UTF8)
            plan
