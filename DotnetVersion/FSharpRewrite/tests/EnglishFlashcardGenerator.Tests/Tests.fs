namespace EnglishFlashcardGenerator.Tests

open System
open System.Collections.Generic
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.AI
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
        let card: FlashCard =
            { Front = "look up"
              Back = "to search for information"
              Example = Some "I looked up the word in the dictionary."
              Direction = None }

        let formatted = FlashcardFormatter.formatCard card

        Assert.Equal("look up\n??\nto search for information\n*Example sentence: I looked up the word in the dictionary.*", formatted)

    [<Fact>]
    let ``flashcard formatter supports one-way Obsidian SR marker`` () =
        let card: FlashCard =
            { Front = "look up"
              Back = "to search for information"
              Example = Some "I looked up the word in the dictionary."
              Direction = None }

        let formatted = FlashcardFormatter.formatCardWithDirection OneWay card

        Assert.Equal("look up\n?\nto search for information\n*Example sentence: I looked up the word in the dictionary.*", formatted)

    [<Fact>]
    let ``formatter never emits legacy double-colon delimiter`` () =
        let card: FlashCard =
            { Front = "front::with delimiter"
              Back = "back::with delimiter"
              Example = None
              Direction = None }

        let formatted = FlashcardFormatter.formatCardWithDirection Bidirectional card

        Assert.DoesNotContain("::", formatted)
        Assert.Contains("front:with delimiter\n??\nback:with delimiter", formatted)

    [<Fact>]
    let ``formatter removes Obsidian SR scheduling metadata from card content`` () =
        let card: FlashCard =
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
        let card: FlashCard =
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
    let ``formatter emits mixed per-card directions with global fallback only for missing direction`` () =
        let cards: FlashCard list =
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

module StructuredDtoTests =
    [<Fact>]
    let ``teacher DTO conversion preserves mixed per-card directions`` () =
        let parsed = MarkdownDocumentParser.parse None TestData.sample
        let section = parsed.Sections |> List.find (fun s -> s.HeadingText.Contains("28.03.2025"))
        let request = { Document = parsed; Section = section }
        let dto: TeacherOutputDto =
            { Cards =
                [ { Front = "term"
                    Back = "definition"
                    Example = ""
                    Direction = "one-way" }
                  { Front = "look up"
                    Back = "to search for information"
                    Example = "I looked up the word."
                    Direction = "bidirectional" } ] }

        let draft = TeacherOutputDto.toDraft Bidirectional request dto

        Assert.Equal(2, draft.Cards.Length)
        Assert.Equal(Some OneWay, draft.Cards.[0].Direction)
        Assert.Equal(Some Bidirectional, draft.Cards.[1].Direction)
        Assert.Equal(Some "I looked up the word.", draft.Cards.[1].Example)

    [<Fact>]
    let ``teacher DTO conversion rejects empty cards instead of falling back to free-form parsing`` () =
        let parsed = MarkdownDocumentParser.parse None TestData.sample
        let section = parsed.Sections |> List.find (fun s -> s.HeadingText.Contains("28.03.2025"))
        let request = { Document = parsed; Section = section }
        let dto = { Cards = [] }

        let ex = Assert.Throws<InvalidOperationException>(fun () -> TeacherOutputDto.toDraft Bidirectional request dto |> ignore)
        Assert.Contains("did not include any cards", ex.Message)

    [<Fact>]
    let ``teacher DTO conversion rejects unsupported structured direction`` () =
        let parsed = MarkdownDocumentParser.parse None TestData.sample
        let section = parsed.Sections |> List.find (fun s -> s.HeadingText.Contains("28.03.2025"))
        let request = { Document = parsed; Section = section }
        let dto: TeacherOutputDto =
            { Cards =
                [ { Front = "term"
                    Back = "definition"
                    Example = ""
                    Direction = "reverse-cloze" } ] }

        let ex = Assert.Throws<InvalidOperationException>(fun () -> TeacherOutputDto.toDraft Bidirectional request dto |> ignore)
        Assert.Contains("unsupported direction", ex.Message)

    [<Fact>]
    let ``rejected structured review normalizes to no output cards`` () =
        let parsed = MarkdownDocumentParser.parse None TestData.sample
        let section = parsed.Sections |> List.find (fun s -> s.HeadingText.Contains("28.03.2025"))
        let request = { Document = parsed; Section = section }
        let draft =
            { Request = request
              Cards =
                [ { Front = "ambiguous tear"
                    Back = "torn"
                    Example = None
                    Direction = Some OneWay } ] }
        let review = ReviewerOutputDto.toReview 1 draft { Approved = false; Feedback = [ "front is ambiguous" ] }

        let normalized = CardNormalizer.normalize review

        Assert.False(review.Approved)
        Assert.Empty(normalized.Cards)

type StubChatClient(responseJson: string, inspect: ChatOptions -> unit) =
    interface IChatClient with
        member _.GetResponseAsync(messages: IEnumerable<ChatMessage>, options: ChatOptions, cancellationToken: CancellationToken) =
            inspect options
            let message = ChatMessage(ChatRole.Assistant, responseJson)
            Task.FromResult(ChatResponse(message))

        member _.GetStreamingResponseAsync(messages: IEnumerable<ChatMessage>, options: ChatOptions, cancellationToken: CancellationToken) =
            raise (NotSupportedException("Streaming is not used by these tests."))

        member _.GetService(serviceType: Type, serviceKey: obj) = null

        member _.Dispose() = ()

module StructuredLlmAgentTests =
    let private request =
        let parsed = MarkdownDocumentParser.parse None TestData.sample
        let section = parsed.Sections |> List.find (fun s -> s.HeadingText.Contains("28.03.2025"))
        { Document = parsed; Section = section }

    let private llmOptions =
        { BaseUrl = "https://example.test/v1"
          ApiKey = "test-key"
          Model = "LocalModel"
          TimeoutSeconds = 120
          MaxOutputTokens = Some 128
          Temperature = 0.0
          DisableThinking = false }

    [<Fact>]
    let ``structured teacher agent maps typed JSON output into flashcards without Obsidian parser`` () = task {
        let responseBody = """{"cards":[{"front":"term","back":"definition","example":"","direction":"one-way"},{"front":"look up","back":"to search for information","example":"I looked up the word.","direction":"bidirectional"}]}"""
        let mutable sawStructuredOptions = false
        use client =
            new StubChatClient(responseBody, fun options ->
                sawStructuredOptions <- true
                Assert.NotNull(options.ResponseFormat)
                Assert.Equal(Nullable<int>(128), options.MaxOutputTokens)
                Assert.Equal(Nullable<float32>(0.0f), options.Temperature))

        let! draft = StructuredLlmAgent.generateWithChatClient client llmOptions OneWay request CancellationToken.None

        Assert.True(sawStructuredOptions)
        Assert.Equal(2, draft.Cards.Length)
        Assert.Equal("term", draft.Cards.[0].Front)
        Assert.Equal(Some OneWay, draft.Cards.[0].Direction)
        Assert.Equal("look up", draft.Cards.[1].Front)
        Assert.Equal("to search for information", draft.Cards.[1].Back)
        Assert.Equal(Some "I looked up the word.", draft.Cards.[1].Example)
        Assert.Equal(Some Bidirectional, draft.Cards.[1].Direction)
    }

    [<Fact>]
    let ``structured reviewer agent maps typed approval output`` () = task {
        let draft =
            { Request = request
              Cards =
                [ { Front = "three forms of the verb \"tear\""
                    Back = "tear - tore - torn"
                    Example = None
                    Direction = Some OneWay } ] }
        use client = new StubChatClient("""{"approved":true,"feedback":[]}""", fun options -> Assert.NotNull(options.ResponseFormat))

        let! review = StructuredLlmAgent.reviewWithChatClient client llmOptions draft CancellationToken.None

        Assert.True(review.Approved)
        Assert.Empty(review.Feedback)
        Assert.Equal(draft, review.Draft)
    }

    [<Fact>]
    let ``disable thinking is reported as unsupported instead of smuggling raw HTTP JSON`` () =
        let options = { llmOptions with DisableThinking = true }
        let ex = Assert.Throws<InvalidOperationException>(fun () -> StructuredLlmAgent.createOpenAICompatibleChatClient options |> ignore)
        Assert.Contains("not supported through Microsoft.Extensions.AI/OpenAI chat abstractions", ex.Message)

    [<Fact>]
    let ``chat completions endpoint is rejected before creating framework chat client`` () =
        let options = { llmOptions with BaseUrl = "https://example.test/v1/chat/completions" }
        let ex = Assert.Throws<InvalidOperationException>(fun () -> StructuredLlmAgent.createOpenAICompatibleChatClient options |> ignore)
        Assert.Contains("service root", ex.Message)
