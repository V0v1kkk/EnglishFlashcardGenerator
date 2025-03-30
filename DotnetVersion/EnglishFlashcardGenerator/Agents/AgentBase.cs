using AutoGen.Core;
using Azure.AI.Inference;
using OpenAI.Chat;

namespace EnglishFlashcardGenerator;

public abstract class AgentBase
{
    protected readonly float Temperature;
    protected readonly int MaxTokens;

    protected AgentBase(float temperature, int maxTokens)
    {
        Temperature = temperature;
        MaxTokens = maxTokens;
    }

    public abstract string Introduction { get; }
    
    public abstract IAgent CreateOpenAiAgent(ChatClient client);
    public abstract IAgent CreateAzureAgent(ChatCompletionsClient client, string modelName);
}