using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using EnglishFlashcardGenerator.Core.Prompts;
using EnglishFlashcardGenerator.Core.Telemetry;
using Microsoft.Extensions.Logging;

namespace EnglishFlashcardGenerator.Core.Agents;

public sealed record TopicGroupDto(int SourceOrder, string Kind, string Title, string SourceExcerpt);
public sealed record GroupPlanDto(IReadOnlyList<TopicGroupDto> Groups);
public sealed record TeacherCardDto(string Front, string Back, string? Example);
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
    double? Temperature = null,
    int? MaxOutputTokens = null,
    TimeSpan? NetworkTimeout = null,
    int? MaxNetworkRetries = null);

public sealed class MafStructuredAgentPort(IChatClient chatClient, LlmOptions options, ILogger<MafStructuredAgentPort> logger) : IStructuredAgentPort
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<GroupPlanDto> PlanGroupsAsync(DayChunk day, NoteProcessingRequest request, CancellationToken cancellationToken)
    {
        var result = await RunAsync<GroupPlanDto>(
            "EnglishGroupPlanner",
            "group_plan_output",
            "Semantic topic groups grounded in one day of English-learning Markdown.",
            PromptLoader.GetPrompt("EnglishGroupPlanner"),
            $"Day heading: {day.Heading}\n\nMarkdown:\n{day.Markdown}",
            cancellationToken);
        logger.LogInformation("Planner returned {Count} topic groups for {Heading}.", result.Groups.Count, day.Heading);
        return result;
    }

    public async ValueTask<TeacherOutputDto> GenerateCardsAsync(TeacherRequest request, CancellationToken cancellationToken)
    {
        var result = await RunAsync<TeacherOutputDto>(
            "EnglishFlashcardTeacher",
            "teacher_flashcard_output",
            "Typed flashcards for one English-learning topic group.",
            PromptLoader.GetPrompt("EnglishFlashcardTeacher"),
            $"Group: {request.Group.Title}\nIteration: {request.Iteration}\nCritic feedback: {request.CriticFeedback ?? "none"}\n\nSource excerpt:\n{request.Group.SourceExcerpt}",
            cancellationToken);
        logger.LogInformation("Teacher generated {Count} cards (Iteration {Iteration}) for group '{Group}'.", result.Cards?.Count ?? 0, request.Iteration, request.Group.Title);
        return result;
    }

    public async ValueTask<CriticOutputDto> ReviewCardsAsync(TeacherDraft draft, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(draft.Cards.Select(c => new TeacherCardDto(c.Front, c.Back, c.Example)), JsonOptions);
        var result = await RunAsync<CriticOutputDto>(
            "EnglishFlashcardCritic",
            "critic_flashcard_output",
            "Review verdict and feedback for typed English flashcards.",
            PromptLoader.GetPrompt("EnglishFlashcardCritic"),
            $"Review these flashcards for group {draft.Group.Title}:\n{json}",
            cancellationToken);
        logger.LogInformation("Critic reviewed {Count} cards for group '{Group}'. Verdict: {Verdict}. Findings: {FindingsCount}", draft.Cards.Count, draft.Group.Title, result.Verdict, result.Findings?.Count ?? 0);
        if (result.Findings != null)
        {
            foreach (var finding in result.Findings)
            {
                logger.LogWarning("Critic Finding -> [{Front}]: {Issue} -> {Rec}", finding.CardFront, finding.Issue, finding.Recommendation);
            }
        }
        return result;
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
                ResponseFormat = ChatResponseFormat.ForJsonSchema<T>(JsonOptions, schemaName, schemaDescription)
            };
            if (options.Temperature.HasValue)
            {
                chatOptions.Temperature = (float)options.Temperature.Value;
            }
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

            if (response.Usage?.InputTokenCount is { } inputTokens)
            {
                FlashcardMetrics.PromptTokens.Add(inputTokens, new KeyValuePair<string, object?>("agent", name));
            }
            if (response.Usage?.OutputTokenCount is { } outputTokens)
            {
                FlashcardMetrics.CompletionTokens.Add(outputTokens, new KeyValuePair<string, object?>("agent", name));
            }

            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    public static MafStructuredAgentPort FromOpenAICompatible(LlmOptions options, ILoggerFactory loggerFactory)
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
        return new MafStructuredAgentPort(client.GetChatClient(options.Model).AsIChatClient(), options, loggerFactory.CreateLogger<MafStructuredAgentPort>());
    }
}
