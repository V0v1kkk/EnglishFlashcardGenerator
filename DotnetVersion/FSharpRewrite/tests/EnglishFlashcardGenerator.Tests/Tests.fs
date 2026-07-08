namespace EnglishFlashcardGenerator.Tests

open System
open System.IO
open System.Threading
open Xunit
open EnglishFlashcardGenerator.Core

module TestData =
    let sample =
        """---
title: sample
---
# English Learning Notes

## Legend

A heading inside a code fence must not be parsed:

```markdown
## not a lesson
```

## [[2025-03-28-Friday|28.03.2025]]

**look up** - to search for information
*I looked up the word in the dictionary.*

[[Vocabulary|vocab link]] must stay unchanged.

## [[2025-03-27-Thursday]]

**at** - used for specific times
*The meeting starts at 3 PM.*
"""

module ParserTests =
    [<Fact>]
    let ``parser reads YAML frontmatter and ignores fenced headings`` () =
        let parsed = MarkdownDocumentParser.parse None TestData.sample
        Assert.Equal("sample", parsed.FrontMatter.["title"])
        Assert.Equal(4, parsed.Sections.Length)
        Assert.DoesNotContain(parsed.Sections, fun s -> s.HeadingText = "not a lesson")

    [<Fact>]
    let ``parser preserves wiki links in raw source spans`` () =
        let parsed = MarkdownDocumentParser.parse None TestData.sample
        let lesson = parsed.Sections |> List.find (fun s -> s.HeadingText.Contains("28.03.2025"))
        Assert.Contains("[[Vocabulary|vocab link]]", lesson.RawText)
        Assert.Contains("**look up**", lesson.RawText)

    [<Theory>]
    [<InlineData("[[2025-03-28-Friday|28.03.2025]]", 2025, 3, 28)>]
    [<InlineData("[[2025-03-27-Thursday]]", 2025, 3, 27)>]
    [<InlineData("26.03.2025", 2025, 3, 26)>]
    let ``date heading parser supports existing forms`` heading year month day =
        let parsed = DateHeadingParser.tryParse heading |> Option.map DateHeading.value
        Assert.True(parsed.IsSome)
        Assert.Equal(DateOnly(year, month, day), parsed.Value)

    [<Fact>]
    let ``parser handles CRLF input`` () =
        let crlf = TestData.sample.Replace("\n", "\r\n")
        let parsed = MarkdownDocumentParser.parse None crlf
        let dated = parsed.Sections |> List.filter (fun s -> s.Date.IsSome)
        Assert.Equal(2, dated.Length)

module WorkflowTests =
    let private tempOutput () =
        let root = Path.Combine(Path.GetTempPath(), "efg-tests", Guid.NewGuid().ToString("N"))
        let cards = Path.Combine(root, "cards")
        let notes = Path.Combine(root, "notes")
        { CardsDirectory = OutputDirectory.createUnsafe cards
          NotesDirectory = OutputDirectory.createUnsafe notes
          Mode = DryRun }

    [<Fact>]
    let ``workflow runs parser teacher reviewer normalizer writer with fake agents`` () = task {
        let output = tempOutput ()
        let input =
            { SourcePath = SourcePath.createUnsafe "sample.md"
              MarkdownText = TestData.sample
              Output = output }
        let! result = FlashcardWorkflow.runAsync input CancellationToken.None
        let onlyCard = Assert.Single(result.Cards)
        Assert.Equal("look up", onlyCard.Front)
        Assert.Contains("EnglishFlashcards-2025-03-28.md", result.WritePlan.CardsPath)
        Assert.Equal(DryRun, result.WritePlan.Mode)
    }

    [<Fact>]
    let ``dry run does not write output files`` () = task {
        let output = tempOutput ()
        let input =
            { SourcePath = SourcePath.createUnsafe "sample.md"
              MarkdownText = TestData.sample
              Output = output }
        let! result = FlashcardWorkflow.runAsync input CancellationToken.None
        Assert.False(File.Exists result.WritePlan.CardsPath)
        Assert.False(File.Exists result.WritePlan.NotePath)
        Assert.Contains("look up\n??\nto search for information", result.WritePlan.CardsContent)
        Assert.Contains("*Example sentence: I looked up the word in the dictionary.*", result.WritePlan.CardsContent)
        Assert.DoesNotContain("look up::", result.WritePlan.CardsContent)
    }

    [<Fact>]
    let ``flashcard formatter uses question marker format`` () =
        let card =
            { Front = "look up"
              Back = "to search for information"
              Example = Some "I looked up the word in the dictionary." }

        let formatted = FlashcardFormatter.formatCard card

        Assert.Equal("look up\n??\nto search for information\n*Example sentence: I looked up the word in the dictionary.*", formatted)
