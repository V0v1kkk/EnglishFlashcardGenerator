using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;

namespace EnglishFlashcardGenerator.Core.Agents;

public sealed record TopicGroupDto(int SourceOrder, string Kind, string Title, string SourceExcerpt);
public sealed record GroupPlanDto(IReadOnlyList<TopicGroupDto> Groups);
public sealed record TeacherCardDto(string Front, string Back, string? Example, string Direction);
public sealed record TeacherOutputDto(IReadOnlyList<TeacherCardDto> Cards);
public sealed record CriticFindingDto(string CardFront, string Issue, string Recommendation);
public sealed record CriticOutputDto(string Verdict, string Feedback, IReadOnlyList<CriticFindingDto> Findings);

public interface IStructuredAgentPort
{
    ValueTask<GroupPlanDto> PlanGroupsAsync(DayChunk day, NoteProcessingRequest options, CancellationToken cancellationToken);
    ValueTask<TeacherOutputDto> GenerateCardsAsync(TeacherRequest request, CancellationToken cancellationToken);
    ValueTask<CriticOutputDto> ReviewCardsAsync(TeacherDraft draft, CancellationToken cancellationToken);
}

public sealed record LlmOptions(
    string BaseUrl,
    string ApiKey,
    string Model,
    double Temperature = 0,
    int? MaxOutputTokens = null,
    TimeSpan? NetworkTimeout = null,
    int? MaxNetworkRetries = null);

public sealed class MafStructuredAgentPort(IChatClient chatClient, LlmOptions options) : IStructuredAgentPort
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask<GroupPlanDto> PlanGroupsAsync(DayChunk day, NoteProcessingRequest request, CancellationToken cancellationToken) =>
        RunAsync<GroupPlanDto>(
            "EnglishGroupPlanner",
            "group_plan_output",
            "Semantic topic groups grounded in one day of English-learning Markdown.",
            "Identify natural English-learning topic groups. Preserve source order and keep source excerpts copied from the input.",
            $"Day heading: {day.Heading}\n\nMarkdown:\n{day.Markdown}",
            cancellationToken);

    public ValueTask<TeacherOutputDto> GenerateCardsAsync(TeacherRequest request, CancellationToken cancellationToken) =>
        RunAsync<TeacherOutputDto>(
            "EnglishFlashcardTeacher",
            "teacher_flashcard_output",
            "Typed flashcards for one English-learning topic group.",
            "Generate concise, self-contained flashcards as typed data only. Do not return Obsidian markdown or separators.",
            $"Group: {request.Group.Title}\nIteration: {request.Iteration}\nCritic feedback: {request.CriticFeedback ?? "none"}\n\nSource excerpt:\n{request.Group.SourceExcerpt}",
            cancellationToken);

    public ValueTask<CriticOutputDto> ReviewCardsAsync(TeacherDraft draft, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(draft.Cards.Select(c => new TeacherCardDto(c.Front, c.Back, c.Example, c.Direction == CardDirection.Bidirectional ? "bidirectional" : "one-way")), JsonOptions);
        return RunAsync<CriticOutputDto>(
            "EnglishFlashcardCritic",
            "critic_flashcard_output",
            "Review verdict and feedback for typed English flashcards.",
            "Approve only answerable cards. Require self-contained fronts and correct direction. Return structured data only.",
            $"Review these flashcards for group {draft.Group.Title}:\n{json}",
            cancellationToken);
    }

    private async ValueTask<T> RunAsync<T>(string name, string schemaName, string schemaDescription, string instructions, string prompt, CancellationToken cancellationToken)
    {
        return await StructuredAgentRetryPolicy.RunAsync<T>(name, async (attempt, ct) =>
        {
            var jsonOnlyInstructions = "Return exactly one complete JSON object matching the requested schema. Do not include Markdown fences, prose, comments, trailing text, or partial JSON.";
            var attemptInstructions = attempt == 1
                ? instructions + "\n\n" + jsonOnlyInstructions
                : instructions + "\n\nRetry because the previous structured response could not be deserialized. " + jsonOnlyInstructions;
            var chatOptions = new ChatOptions
            {
                Instructions = attemptInstructions,
                Temperature = (float)options.Temperature,
                ResponseFormat = ChatResponseFormat.ForJsonSchema<T>(JsonOptions, schemaName, schemaDescription)
            };
            if (options.MaxOutputTokens is { } maxTokens)
            {
                chatOptions.MaxOutputTokens = maxTokens;
            }

            var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions { Name = name, ChatOptions = chatOptions });
            var response = await agent.RunAsync<T>(prompt, null, JsonOptions, null, ct).ConfigureAwait(false);
            var result = response.Result;
            if (result is null)
            {
                throw new InvalidOperationException("Structured response result was null.");
            }

            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    public static MafStructuredAgentPort FromOpenAICompatible(LlmOptions options)
    {
        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl.TrimEnd('/')) };
        if (options.NetworkTimeout is { } networkTimeout)
        {
            clientOptions.NetworkTimeout = networkTimeout;
        }

        if (options.MaxNetworkRetries is { } maxNetworkRetries)
        {
            clientOptions.RetryPolicy = new ClientRetryPolicy(maxNetworkRetries);
        }

        var client = new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions);
        return new MafStructuredAgentPort(client.GetChatClient(options.Model).AsIChatClient(), options);
    }
}
