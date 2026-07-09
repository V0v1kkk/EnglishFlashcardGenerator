namespace EnglishFlashcardGenerator.Core

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

[<RequireQualifiedAccess>]
module FakeTeacherAgent =
    let private cardPattern = Regex(@"\*\*(?<front>[^*]+)\*\*\s*-\s*(?<back>[^\r\n]+)(?:\r?\n\*(?<example>[^*]+)\*)?", RegexOptions.Compiled)

    let generate direction (request: GenerationRequest) =
        let cards =
            cardPattern.Matches(request.Section.RawText)
            |> Seq.cast<Match>
            |> Seq.map (fun m ->
                { Front = m.Groups.["front"].Value.Trim()
                  Back = m.Groups.["back"].Value.Trim()
                  Example =
                    let value = m.Groups.["example"].Value.Trim()
                    if String.IsNullOrWhiteSpace value then None else Some value
                  Direction = Some direction })
            |> Seq.toList
        { Request = request; Cards = cards }

[<RequireQualifiedAccess>]
module OpenAICompatibleTeacherAgent =
    let private systemPrompt =
        """You are an English teacher generating Obsidian Spaced Repetition flashcards.
Return only flashcards in multiline format:
front
?
back
or
front
??
back
Do not use Markdown tables, JSON, scheduling metadata, YAML sr-* fields, <!--SR:...-->, [!sr|card-metadata], :: delimiters, #flashcards, or #review tags inside card text.
If you include an example, put it on the next non-empty back line exactly as: *Example sentence: ...*
Do not put blank lines inside a single card. Separate cards with one blank line."""

    let private userPrompt defaultDirection (request: GenerationRequest) =
        let separator = CardDirection.separator defaultDirection
        $"""Generate concise English learning flashcards for this note section.
Choose the separator per card: `?` for one-way facts, `??` when the card is useful in both directions and should create a reversed sibling card.
Use `{separator}` as the fallback separator only when the direction is not clear.
Return at most 5 cards.

Section heading:
{request.Section.HeadingText}

Section markdown:
{request.Section.RawText}"""

    let private requireJsonString (propertyName: string) (element: JsonElement) =
        match element.TryGetProperty(propertyName) with
        | true, value when value.ValueKind = JsonValueKind.String -> value.GetString()
        | _ -> invalidOp $"OpenAI-compatible response did not include {propertyName}."

    let private extractContent (json: string) =
        use document = JsonDocument.Parse(json)
        let root = document.RootElement
        match root.TryGetProperty("choices") with
        | false, _ -> invalidOp "OpenAI-compatible response did not include choices."
        | true, choices when choices.ValueKind <> JsonValueKind.Array || choices.GetArrayLength() = 0 ->
            invalidOp "OpenAI-compatible response choices were empty."
        | true, choices ->
            let first = choices.[0]
            match first.TryGetProperty("message") with
            | true, message -> requireJsonString "content" message
            | false, _ ->
                match first.TryGetProperty("text") with
                | true, text when text.ValueKind = JsonValueKind.String -> text.GetString()
                | _ -> invalidOp "OpenAI-compatible response did not include message.content."

    let private buildRequestJson options direction request =
        let message (role: string) (content: string) =
            let node = JsonObject()
            node["role"] <- JsonValue.Create(role)
            node["content"] <- JsonValue.Create(content)
            node

        let payload = JsonObject()
        payload["model"] <- JsonValue.Create(options.Model)
        payload["messages"] <- JsonArray(message "system" systemPrompt, message "user" (userPrompt direction request))
        payload["temperature"] <- JsonValue.Create(options.Temperature)
        payload["max_tokens"] <- JsonValue.Create(options.MaxOutputTokens)
        if options.DisableThinking then
            let chatTemplate = JsonObject()
            chatTemplate["enable_thinking"] <- JsonValue.Create(false)
            payload["chat_template_kwargs"] <- chatTemplate
        payload.ToJsonString()

    let private endpoint (baseUrl: string) =
        let trimmed = baseUrl.TrimEnd('/')
        if trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) then trimmed
        else $"{trimmed}/chat/completions"

    let generateWithClient (client: HttpClient) (options: LlmOptions) direction (request: GenerationRequest) (ct: CancellationToken) = task {
        use message = new HttpRequestMessage(HttpMethod.Post, endpoint options.BaseUrl)
        message.Headers.Authorization <- AuthenticationHeaderValue("Bearer", options.ApiKey)
        message.Content <- new StringContent(buildRequestJson options direction request, Encoding.UTF8, "application/json")
        use! response = client.SendAsync(message, ct)
        let! body = response.Content.ReadAsStringAsync(ct)
        if not response.IsSuccessStatusCode then
            return invalidOp $"OpenAI-compatible chat request failed with HTTP {int response.StatusCode}."
        else
            let content = extractContent body
            let cards = ObsidianSrCardParser.parse content
            if cards.IsEmpty then
                return invalidOp "OpenAI-compatible chat response did not contain any parseable Obsidian SR cards."
            else
                return { Request = request; Cards = cards }
    }

    let generate options direction request ct = task {
        use client = new HttpClient(Timeout = TimeSpan.FromSeconds(float options.TimeoutSeconds))
        return! generateWithClient client options direction request ct
    }

[<RequireQualifiedAccess>]
module TeacherAgent =
    let generateAsync (options: GenerationOptions) (request: GenerationRequest) (ct: CancellationToken) = task {
        match options.Mode, options.Llm with
        | Fake, _ -> return FakeTeacherAgent.generate options.CardDirection request
        | OpenAICompatible, Some llm -> return! OpenAICompatibleTeacherAgent.generate llm options.CardDirection request ct
        | OpenAICompatible, None -> return invalidOp "OpenAI-compatible generator mode requires LLM options."
    }

[<RequireQualifiedAccess>]
module FakeReviewerAgent =
    let review (draft: TeacherDraft) =
        let feedback =
            if draft.Cards.IsEmpty then [ "No flashcards were extracted from the selected section." ]
            else []
        { Draft = draft
          Approved = draft.Cards |> List.isEmpty |> not
          Feedback = feedback
          Iteration = 1 }

[<RequireQualifiedAccess>]
module CardNormalizer =
    let normalize (review: ReviewResult) =
        if not review.Approved then
            { Section = review.Draft.Request.Section; Cards = [] }
        else
            { Section = review.Draft.Request.Section
              Cards =
                review.Draft.Cards
                |> List.map (fun card ->
                    { Front = CardContentSanitizer.clean card.Front
                      Back = CardContentSanitizer.clean card.Back
                      Example = card.Example |> Option.map CardContentSanitizer.clean |> Option.filter (String.IsNullOrWhiteSpace >> not)
                      Direction = card.Direction })
                |> List.filter (fun card -> not (String.IsNullOrWhiteSpace card.Front) && not (String.IsNullOrWhiteSpace card.Back))
                |> List.distinctBy (fun c -> c.Front.Trim().ToLowerInvariant()) }
