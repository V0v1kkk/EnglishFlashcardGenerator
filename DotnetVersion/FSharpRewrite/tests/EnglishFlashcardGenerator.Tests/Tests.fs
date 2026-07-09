namespace EnglishFlashcardGenerator.Tests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
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
        let direction = Bidirectional
        let root = Path.Combine(Path.GetTempPath(), "efg-tests", Guid.NewGuid().ToString("N"))
        let cards = Path.Combine(root, "cards")
        let notes = Path.Combine(root, "notes")
        { CardsDirectory = OutputDirectory.createUnsafe cards
          NotesDirectory = OutputDirectory.createUnsafe notes
          Mode = DryRun
          CardDirection = direction }

    let private input output =
        { SourcePath = SourcePath.createUnsafe "sample.md"
          MarkdownText = TestData.sample
          Output = output
          Generation = { GenerationOptions.defaultValue with CardDirection = output.CardDirection } }

    [<Fact>]
    let ``workflow runs parser teacher reviewer normalizer writer with fake agents`` () = task {
        let output = tempOutput ()
        let! result = FlashcardWorkflow.runAsync (input output) CancellationToken.None
        let onlyCard = Assert.Single(result.Cards)
        Assert.Equal("look up", onlyCard.Front)
        Assert.Contains("EnglishFlashcards-2025-03-28.md", result.WritePlan.CardsPath)
        Assert.Equal(DryRun, result.WritePlan.Mode)
    }

    [<Fact>]
    let ``dry run does not write output files`` () = task {
        let output = tempOutput ()
        let! result = FlashcardWorkflow.runAsync (input output) CancellationToken.None
        Assert.False(File.Exists result.WritePlan.CardsPath)
        Assert.False(File.Exists result.WritePlan.NotePath)
        Assert.Contains("look up\n??\nto search for information", result.WritePlan.CardsContent)
        Assert.Contains("*Example sentence: I looked up the word in the dictionary.*", result.WritePlan.CardsContent)
        Assert.DoesNotContain("look up::", result.WritePlan.CardsContent)
    }

    [<Fact>]
    let ``workflow applies configured one-way Obsidian SR marker`` () = task {
        let output = { tempOutput () with CardDirection = OneWay }
        let! result = FlashcardWorkflow.runAsync (input output) CancellationToken.None

        Assert.Contains("look up\n?\nto search for information", result.WritePlan.CardsContent)
        Assert.DoesNotContain("look up\n??\nto search for information", result.WritePlan.CardsContent)
    }

    [<Fact>]
    let ``flashcard formatter keeps legacy bidirectional marker by default`` () =
        let card =
            { Front = "look up"
              Back = "to search for information"
              Example = Some "I looked up the word in the dictionary."
              Direction = None }

        let formatted = FlashcardFormatter.formatCard card

        Assert.Equal("look up\n??\nto search for information\n*Example sentence: I looked up the word in the dictionary.*", formatted)

    [<Fact>]
    let ``flashcard formatter supports one-way Obsidian SR marker`` () =
        let card =
            { Front = "look up"
              Back = "to search for information"
              Example = Some "I looked up the word in the dictionary."
              Direction = None }

        let formatted = FlashcardFormatter.formatCardWithDirection OneWay card

        Assert.Equal("look up\n?\nto search for information\n*Example sentence: I looked up the word in the dictionary.*", formatted)

    [<Fact>]
    let ``formatter never emits legacy double-colon delimiter`` () =
        let card =
            { Front = "front::with delimiter"
              Back = "back::with delimiter"
              Example = None
              Direction = None }

        let formatted = FlashcardFormatter.formatCardWithDirection Bidirectional card

        Assert.DoesNotContain("::", formatted)
        Assert.Contains("front:with delimiter\n??\nback:with delimiter", formatted)

    [<Fact>]
    let ``formatter removes Obsidian SR scheduling metadata from card content`` () =
        let card =
            { Front = "term\n<!--SR:!2025-01-01,1,250-->\n#flashcards"
              Back = "definition\n> [!sr|card-metadata]\nsr-due: 2025-01-01\n#review"
              Example = Some "clean example\n??"
              Direction = None }

        let formatted = FlashcardFormatter.formatCardWithDirection Bidirectional card

        Assert.DoesNotContain("<!--SR:", formatted)
        Assert.DoesNotContain("[!sr|card-metadata]", formatted)
        Assert.DoesNotContain("sr-due", formatted)
        Assert.DoesNotContain("#flashcards", formatted)
        Assert.DoesNotContain("#review", formatted)
        Assert.Equal("term\n??\ndefinition\n*Example sentence: clean example*", formatted)

    [<Fact>]
    let ``formatter removes blank lines standalone separators and case-insensitive SR fields inside cards`` () =
        let card =
            { Front = "\nterm\n\n?\n<!--sr:!2025-01-01,1,250-->\n"
              Back = "definition\n\n??\nSR-DUE: 2025-01-01\nsr-ease: 250"
              Example = Some "\nexample\n\nSR-INTERVAL: 4\n"
              Direction = None }

        let formatted = FlashcardFormatter.formatCardWithDirection Bidirectional card

        Assert.Equal("term\n??\ndefinition\n*Example sentence: example*", formatted)
        Assert.DoesNotContain("\n\n", formatted)
        Assert.DoesNotContain("SR-DUE", formatted)
        Assert.DoesNotContain("sr-ease", formatted)
        Assert.DoesNotContain("SR-INTERVAL", formatted)

    [<Fact>]
    let ``parser preserves mixed one-way and bidirectional card directions`` () =
        let content = """term
?
definition

look up
??
to search for information"""

        let cards = ObsidianSrCardParser.parse content

        Assert.Equal(2, cards.Length)
        Assert.Equal(Some OneWay, cards.[0].Direction)
        Assert.Equal(Some Bidirectional, cards.[1].Direction)

    [<Fact>]
    let ``formatter emits mixed per-card directions with global fallback only for missing direction`` () =
        let cards =
            [ { Front = "term"
                Back = "definition"
                Example = None
                Direction = Some OneWay }
              { Front = "look up"
                Back = "to search for information"
                Example = None
                Direction = Some Bidirectional }
              { Front = "fallback"
                Back = "uses configured default"
                Example = None
                Direction = None } ]

        let formatted = FlashcardFormatter.formatCardsWithFallback OneWay cards

        Assert.Contains("term\n?\ndefinition", formatted)
        Assert.Contains("look up\n??\nto search for information", formatted)
        Assert.Contains("fallback\n?\nuses configured default", formatted)

type StubHandler(responseBody: string, inspect: HttpRequestMessage -> unit) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        inspect request
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        response.Content <- new StringContent(responseBody, Encoding.UTF8, "application/json")
        Task.FromResult response

module OpenAICompatibleAdapterTests =
    let private request =
        let parsed = MarkdownDocumentParser.parse None TestData.sample
        let section = parsed.Sections |> List.find (fun s -> s.HeadingText.Contains("28.03.2025"))
        { Document = parsed; Section = section }

    [<Fact>]
    let ``OpenAI-compatible adapter posts bounded chat request and preserves mixed card directions`` () = task {
        let responseBody = """{"choices":[{"message":{"content":"term\n?\ndefinition\n\nlook up\n??\nto search for information\n*Example sentence: I looked up the word.*"}}]}"""
        let mutable sawRequest = false
        use client =
            new HttpClient(
                new StubHandler(responseBody, fun req ->
                    sawRequest <- true
                    Assert.Equal(HttpMethod.Post, req.Method)
                    Assert.Equal("Bearer", req.Headers.Authorization.Scheme)
                    Assert.Equal("test-key", req.Headers.Authorization.Parameter)
                    Assert.EndsWith("/chat/completions", req.RequestUri.ToString())
                    let body = req.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    Assert.Contains("LocalModel", body)
                    Assert.Contains("max_tokens", body)
                    Assert.Contains("temperature", body)
                    Assert.Contains("Choose the separator per card", body)))
        let options =
            { BaseUrl = "https://example.test/v1"
              ApiKey = "test-key"
              Model = "LocalModel"
              TimeoutSeconds = 120
              MaxOutputTokens = 128
              Temperature = 0.0 }

        let! draft = OpenAICompatibleTeacherAgent.generateWithClient client options OneWay request CancellationToken.None

        Assert.True(sawRequest)
        Assert.Equal(2, draft.Cards.Length)
        Assert.Equal("term", draft.Cards.[0].Front)
        Assert.Equal(Some OneWay, draft.Cards.[0].Direction)
        Assert.Equal("look up", draft.Cards.[1].Front)
        Assert.Equal("to search for information", draft.Cards.[1].Back)
        Assert.Equal(Some "I looked up the word.", draft.Cards.[1].Example)
        Assert.Equal(Some Bidirectional, draft.Cards.[1].Direction)
    }
