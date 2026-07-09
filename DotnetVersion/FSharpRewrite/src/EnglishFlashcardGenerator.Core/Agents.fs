namespace EnglishFlashcardGenerator.Core

open System
open System.ClientModel
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Agents.AI
open Microsoft.Extensions.AI
open OpenAI

[<RequireQualifiedAccess>]
module FakeTeacherAgent =
    let private tryParseVocabularyLine fallbackDirection (lines: string array) index (line: string) =
        let trimmed = line.Trim()
        if not (trimmed.StartsWith("**", StringComparison.Ordinal)) then
            None
        else
            let closing = trimmed.IndexOf("**", 2, StringComparison.Ordinal)
            if closing <= 2 then
                None
            else
                let front = trimmed.Substring(2, closing - 2).Trim()
                let rest = trimmed.Substring(closing + 2).TrimStart()
                let delimiter = "-"
                if not (rest.StartsWith(delimiter, StringComparison.Ordinal)) then
                    None
                else
                    let back = rest.Substring(delimiter.Length).Trim()
                    let example =
                        if index + 1 < lines.Length then
                            let next = lines.[index + 1].Trim()
                            if next.StartsWith("*", StringComparison.Ordinal) && next.EndsWith("*", StringComparison.Ordinal) && next.Length > 2 then
                                Some(next.Trim('*').Trim())
                            else None
                        else None
                    if String.IsNullOrWhiteSpace front || String.IsNullOrWhiteSpace back then None
                    else Some ({ Front = front; Back = back; Example = example; Direction = Some fallbackDirection } : FlashCard)

    let generate direction (request: GenerationRequest) =
        let lines = request.Section.RawText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
        let cards =
            lines
            |> Array.mapi (tryParseVocabularyLine direction lines)
            |> Array.choose id
            |> Array.toList
        ({ Request = request; Cards = cards } : TeacherDraft)

[<RequireQualifiedAccess>]
module StructuredLlmAgent =
    let private createJsonOptions () =
        JsonSerializerOptions(JsonSerializerDefaults.Web)

    let private teacherInstructions =
        """You are an English teacher generating typed flashcard data.
Return only structured data matching the requested schema.
Do not return Obsidian Spaced Repetition markdown, card separators, YAML scheduling fields, HTML comments, #flashcards, #review, or :: delimiters.
Each card must contain front, back, optional example, and direction.
Use direction `one-way` for grammar notes, usage notes, examples, verb forms, and any cue that asks for a specific answer.
Use direction `bidirectional` only for clean term <-> definition or phrase <-> meaning pairs where both sides work as prompts.
Every front must be self-contained. Do not create ambiguous bare-front cards like `tear` -> `torn`; rewrite them as a clear cue such as `three forms of the verb "tear"` -> `tear - tore - torn`."""

    let private reviewerInstructions =
        """You review typed English-learning flashcards.
Return only structured data matching the requested schema.
Approve only when every card front is answerable without the source note, every back answers that front, examples are clean sentence text, and directions are correctly assigned as `one-way` or `bidirectional`.
Reject ambiguous bare-word fronts for verb forms or usage notes and explain the problem in feedback."""

    let private teacherPrompt defaultDirection (request: GenerationRequest) =
        $"""Generate at most 5 concise English learning flashcards for this note section.
Use `{CardDirection.separator defaultDirection}` only as the fallback concept for direction when the direction is unclear; the structured `direction` field must be `one-way` or `bidirectional`.

Section heading:
{request.Section.HeadingText}

Section markdown:
{request.Section.RawText}"""

    let private reviewerPrompt (draft: TeacherDraft) =
        let dto =
            { Cards =
                draft.Cards
                |> List.map (fun card ->
                    ({ Front = card.Front
                       Back = card.Back
                       Example = card.Example |> Option.defaultValue ""
                       Direction = card.Direction |> Option.defaultValue CardDirection.defaultValue |> CardDirection.label } : TeacherCardDto)) }
        let draftJson = JsonSerializer.Serialize(dto, createJsonOptions ())
        $"""Review this typed teacher output. Return approved=true only if the cards are ready to format as Obsidian SR at the writer boundary.

Teacher output JSON:
{draftJson}"""

    let private chatOptions<'output> instructions schemaName schemaDescription (options: LlmOptions) =
        let chatOptions = ChatOptions()
        chatOptions.Instructions <- instructions
        chatOptions.Temperature <- Nullable<float32>(float32 options.Temperature)
        chatOptions.MaxOutputTokens <- (options.MaxOutputTokens |> Option.map Nullable<int> |> Option.defaultValue (Nullable<int>()))
        chatOptions.ResponseFormat <- ChatResponseFormat.ForJsonSchema<'output>(createJsonOptions (), schemaName, schemaDescription)
        chatOptions

    let private createAgent name instructions schemaName schemaDescription options (chatClient: IChatClient) =
        let agentOptions = ChatClientAgentOptions()
        agentOptions.Name <- name
        agentOptions.ChatOptions <- chatOptions instructions schemaName schemaDescription options
        ChatClientAgent(chatClient, agentOptions)

    let createOpenAICompatibleChatClient (options: LlmOptions) =
        if options.DisableThinking then
            invalidOp "--llm-disable-thinking is not supported through Microsoft.Extensions.AI/OpenAI chat abstractions without provider-specific raw HTTP JSON. Run without that flag or use a provider-supported reasoning option."
        let baseUrl = options.BaseUrl.TrimEnd('/')
        if baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) then
            invalidOp "OpenAI-compatible base URL must be a service root such as https://host/v1, not a /chat/completions endpoint."
        let clientOptions = OpenAIClientOptions()
        clientOptions.Endpoint <- Uri(baseUrl)
        let client = OpenAIClient(ApiKeyCredential(options.ApiKey), clientOptions)
        client.GetChatClient(options.Model).AsIChatClient()

    let generateWithChatClient (chatClient: IChatClient) (options: LlmOptions) direction (request: GenerationRequest) (ct: CancellationToken) = task {
        let agent = createAgent "EnglishFlashcardTeacher" teacherInstructions "teacher_flashcard_output" "Typed English flashcards generated from one markdown section." options chatClient
        let! response = agent.RunAsync<TeacherOutputDto>(teacherPrompt direction request, null, createJsonOptions (), null, ct)
        return TeacherOutputDto.toDraft direction request response.Result
    }

    let reviewWithChatClient (chatClient: IChatClient) (options: LlmOptions) (draft: TeacherDraft) (ct: CancellationToken) = task {
        let agent = createAgent "EnglishFlashcardReviewer" reviewerInstructions "reviewer_flashcard_output" "Review decision, feedback, and optional card-specific findings for typed English flashcards." options chatClient
        let! response = agent.RunAsync<ReviewerOutputDto>(reviewerPrompt draft, null, createJsonOptions (), null, ct)
        return ReviewerOutputDto.toReview 1 draft response.Result
    }

    let generate options direction request ct = task {
        use chatClient = createOpenAICompatibleChatClient options
        return! generateWithChatClient chatClient options direction request ct
    }

    let review options draft ct = task {
        use chatClient = createOpenAICompatibleChatClient options
        return! reviewWithChatClient chatClient options draft ct
    }

[<RequireQualifiedAccess>]
module TeacherAgent =
    let generateAsync (options: GenerationOptions) (request: GenerationRequest) (ct: CancellationToken) = task {
        match options.Mode, options.Llm with
        | Fake, _ -> return FakeTeacherAgent.generate options.CardDirection request
        | OpenAICompatible, Some llm -> return! StructuredLlmAgent.generate llm options.CardDirection request ct
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
module ReviewerAgent =
    let reviewAsync (options: GenerationOptions) (draft: TeacherDraft) (ct: CancellationToken) = task {
        match options.Mode, options.Llm with
        | Fake, _ -> return FakeReviewerAgent.review draft
        | OpenAICompatible, Some llm -> return! StructuredLlmAgent.review llm draft ct
        | OpenAICompatible, None -> return invalidOp "OpenAI-compatible generator mode requires LLM options."
    }

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
                    ({ Front = CardContentSanitizer.clean card.Front
                       Back = CardContentSanitizer.clean card.Back
                       Example = card.Example |> Option.map CardContentSanitizer.clean |> Option.filter (String.IsNullOrWhiteSpace >> not)
                       Direction = card.Direction } : FlashCard))
                |> List.filter (fun card -> not (String.IsNullOrWhiteSpace card.Front) && not (String.IsNullOrWhiteSpace card.Back))
                |> List.distinctBy (fun c -> c.Front.Trim().ToLowerInvariant()) }
