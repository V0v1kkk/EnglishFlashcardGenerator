namespace EnglishFlashcardGenerator.Core

open System
open System.IO
open System.Text
open System.Text.RegularExpressions

[<RequireQualifiedAccess>]
module CardContentSanitizer =
    let private forbiddenLine (line: string) =
        let trimmed = line.Trim()
        trimmed = "?"
        || trimmed = "??"
        || trimmed.StartsWith("<!--SR:", StringComparison.OrdinalIgnoreCase)
        || trimmed.Contains("> [!sr|card-metadata]", StringComparison.OrdinalIgnoreCase)
        || Regex.IsMatch(trimmed, @"^sr-[A-Za-z0-9_-]+\s*:")
        || trimmed.Equals("#flashcards", StringComparison.OrdinalIgnoreCase)
        || trimmed.Equals("#review", StringComparison.OrdinalIgnoreCase)

    let clean (value: string) =
        if isNull value then
            ""
        else
            value.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
            |> Array.map (fun line -> line.Replace("::", ":").Trim())
            |> Array.filter (fun line -> not (String.IsNullOrWhiteSpace line))
            |> Array.filter (forbiddenLine >> not)
            |> String.concat " "
            |> fun s -> s.Trim()

[<RequireQualifiedAccess>]
module FlashcardFormatter =
    let formatCardWithFallback fallbackDirection (card: FlashCard) =
        let front = CardContentSanitizer.clean card.Front
        let back = CardContentSanitizer.clean card.Back
        let direction = card.Direction |> Option.defaultValue fallbackDirection
        let separator = CardDirection.separator direction
        let lines =
            match card.Example |> Option.map CardContentSanitizer.clean with
            | Some example when not (String.IsNullOrWhiteSpace example) ->
                [ front
                  separator
                  back
                  $"*Example sentence: {example}*" ]
            | _ ->
                [ front
                  separator
                  back ]
        lines
        |> List.filter (String.IsNullOrWhiteSpace >> not)
        |> String.concat "\n"

    let formatCard card = formatCardWithFallback CardDirection.defaultValue card

    let formatCardsWithFallback fallbackDirection cards =
        cards
        |> List.map (formatCardWithFallback fallbackDirection)
        |> String.concat "\n\n"

    let formatCardWithDirection direction card =
        formatCardWithFallback direction { card with Direction = Some direction }

    let formatCardsWithDirection direction cards = formatCardsWithFallback direction cards

    let formatCards cards = formatCardsWithFallback CardDirection.defaultValue cards

[<RequireQualifiedAccess>]
module ObsidianSrCardParser =
    let private tryParseBlock (block: string) =
        let lines =
            block.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
            |> Array.map (fun line -> line.Trim())
            |> Array.filter (String.IsNullOrWhiteSpace >> not)
            |> Array.toList

        let separatorIndex = lines |> List.tryFindIndex (fun line -> line = "?" || line = "??")
        match separatorIndex with
        | None -> None
        | Some index when index = 0 || index = lines.Length - 1 -> None
        | Some index ->
            let direction = CardDirection.parse lines.[index]
            let front = lines |> List.take index |> String.concat " " |> CardContentSanitizer.clean
            let backLines = lines |> List.skip (index + 1)
            let examplePrefix = "*Example sentence:"
            let example, definitionLines =
                match backLines |> List.tryLast with
                | Some last when last.StartsWith(examplePrefix, StringComparison.OrdinalIgnoreCase) && last.EndsWith("*") ->
                    let example = last.Substring(examplePrefix.Length).Trim().TrimEnd('*').Trim()
                    Some (CardContentSanitizer.clean example), backLines |> List.take (backLines.Length - 1)
                | _ -> None, backLines
            let back = definitionLines |> String.concat " " |> CardContentSanitizer.clean
            if String.IsNullOrWhiteSpace front || String.IsNullOrWhiteSpace back then None
            else Some { Front = front; Back = back; Example = example; Direction = Some direction }

    let parse (content: string) =
        content.Replace("\r\n", "\n").Replace("\r", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
        |> Array.choose tryParseBlock
        |> Array.toList

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
        let cardBody = FlashcardFormatter.formatCardsWithFallback output.CardDirection normalized.Cards
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
