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
    let ``flashcard formatter keeps legacy bidirectional marker by default`` () =
        let card =
            { Front = "look up"
              Back = "to search for information"
              Example = Some "I looked up the word in the dictionary." }

        let formatted = FlashcardFormatter.formatCard card

        Assert.Equal("look up\n??\nto search for information\n*Example sentence: I looked up the word in the dictionary.*", formatted)

    [<Fact>]
    let ``flashcard formatter supports one-way Obsidian SR marker`` () =
        let card =
            { Front = "look up"
              Back = "to search for information"
              Example = Some "I looked up the word in the dictionary." }

        let formatted = FlashcardFormatter.formatCardWithDirection OneWay card

        Assert.Equal("look up\n?\nto search for information\n*Example sentence: I looked up the word in the dictionary.*", formatted)

    [<Fact>]
    let ``formatter never emits legacy double-colon delimiter`` () =
        let card =
            { Front = "front::with delimiter"
              Back = "back::with delimiter"
              Example = None }

        let formatted = FlashcardFormatter.formatCardWithDirection Bidirectional card

        Assert.DoesNotContain("::", formatted)
        Assert.Contains("front:with delimiter\n??\nback:with delimiter", formatted)

    [<Fact>]
    let ``formatter removes Obsidian SR scheduling metadata from card content`` () =
        let card =
            { Front = "term\n<!--SR:!2025-01-01,1,250-->\n#flashcards"
              Back = "definition\n> [!sr|card-metadata]\nsr-due: 2025-01-01\n#review"
              Example = Some "clean example\n??" }

        let formatted = FlashcardFormatter.formatCardWithDirection Bidirectional card

        Assert.DoesNotContain("<!--SR:", formatted)
        Assert.DoesNotContain("[!sr|card-metadata]", formatted)
        Assert.DoesNotContain("sr-due", formatted)
        Assert.DoesNotContain("#flashcards", formatted)
        Assert.DoesNotContain("#review", formatted)
        Assert.Equal("term\n??\ndefinition\n*Example sentence: clean example*", formatted)

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
    let ``OpenAI-compatible adapter posts bounded chat request and parses Obsidian SR cards`` () = task {
        let responseBody = """{"choices":[{"message":{"content":"look up\n?\nto search for information\n*Example sentence: I looked up the word.*"}}]}"""
        let mutable sawRequest = false
        use client =
            new HttpClient(
                new StubHandler(responseBody, fun req ->
                    sawRequest <- true
                    Assert.Equal(HttpMethod.Post, req.Method)
                    Assert.Equal("Bearer test-key", req.Headers.Authorization.ToString())
                    Assert.EndsWith("/chat/completions", req.RequestUri.ToString())
                    let body = req.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                    Assert.Contains("LocalModel", body)
                    Assert.Contains("max_tokens", body)
                    Assert.Contains("temperature", body)))
        let options =
            { BaseUrl = "https://example.test/v1"
              ApiKey = "test-key"
              Model = "LocalModel"
              TimeoutSeconds = 120
              MaxOutputTokens = 128
              Temperature = 0.0 }

        let! draft = OpenAICompatibleTeacherAgent.generateWithClient client options OneWay request CancellationToken.None

        Assert.True(sawRequest)
        let card = Assert.Single(draft.Cards)
        Assert.Equal("look up", card.Front)
        Assert.Equal("to search for information", card.Back)
        Assert.Equal(Some "I looked up the word.", card.Example)
    }
