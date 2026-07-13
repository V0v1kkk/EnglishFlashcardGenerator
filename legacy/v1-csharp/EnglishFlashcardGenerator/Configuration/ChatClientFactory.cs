using System.Reflection;
using System.ClientModel;
using AutoGen.Core;
using Azure.AI.OpenAI;
using Azure.AI.Inference;
using Azure;
using OpenAI.Chat;
using Serilog;

namespace EnglishFlashcardGenerator.Configuration;

public static class ChatClientFactory
{
    public static IAgent CreateAgentWithProvider(
        AgentBase agentBase,
        ProviderSettings providerSettings, 
        AgentModelSettings agentSettings,
        ILogger logger)
    {
        if (string.IsNullOrEmpty(providerSettings.Type))
        {
            logger.Error("Provider type is not specified for provider: {ProviderName}", providerSettings.Name);
            throw new ArgumentException($"Provider type is not specified for provider: {providerSettings.Name}");
        }
        
        logger.Information("Creating agent with provider: {ProviderName} of type: {ProviderType}", 
            providerSettings.Name, providerSettings.Type);
        
        // Use the appropriate agent creation method based on provider type
        return providerSettings.Type switch
        {
            "OpenAI" => CreateAgentWithOpenAI(agentBase, providerSettings.OpenAI, agentSettings, logger),
            "Azure" => CreateAgentWithAzure(agentBase, providerSettings.Azure, agentSettings, logger),
            "OpenRouter" => CreateAgentWithOpenRouter(agentBase, providerSettings.OpenRouter, agentSettings, logger),
            _ => throw new ArgumentException($"Unknown provider type: {providerSettings.Type}")
        };
    }
    
    public static IAgent CreateAgentForAgent(
        string agentName,
        AgentBase agentBase,
        AppSettings appSettings,
        ILogger logger)
    {
        // Get the agent-specific settings
        AgentModelSettings? agentModelSettings = agentName switch
        {
            "TeacherAgent" => appSettings.Agents.TeacherAgent,
            "ReviewerAgent" => appSettings.Agents.ReviewerAgent,
            "ExtractorAgent" => appSettings.Agents.ExtractorAgent,
            _ => null
        };
        
        if (agentModelSettings == null)
        {
            logger.Warning("No specific configuration found for agent {AgentName}, using default provider", agentName);
            
            // Use the first provider as default if available
            if (appSettings.Providers.Count > 0)
            {
                return CreateAgentWithProvider(agentBase, appSettings.Providers[0], new AgentModelSettings(), logger);
            }
            
            throw new InvalidOperationException("No providers configured");
        }
        
        // Find the provider by name
        var providerSettings = appSettings.GetProviderByName(agentModelSettings.ProviderName);
        if (providerSettings == null)
        {
            logger.Error("Provider not found: {ProviderName} for agent: {AgentName}", 
                agentModelSettings.ProviderName, agentName);
            throw new ArgumentException($"Provider not found: {agentModelSettings.ProviderName}");
        }
        
        logger.Information("Creating agent {AgentName} using provider: {ProviderName}", 
            agentName, providerSettings.Name);
        
        return CreateAgentWithProvider(agentBase, providerSettings, agentModelSettings, logger);
    }

    private static IAgent CreateAgentWithOpenAI(
        AgentBase agentBase,
        OpenAISettings? settings, 
        AgentModelSettings agentSettings, 
        ILogger logger)
    {
        if (settings == null)
        {
            logger.Error("OpenAI settings are not configured");
            throw new InvalidOperationException("OpenAI settings are not configured");
        }
        
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            logger.Error("OpenAI API key is not configured");
            throw new InvalidOperationException("OpenAI API key is not configured");
        }
        
        
        logger.Information("Creating OpenAI chat client with model: {ModelName}, temperature: {Temperature}", 
            settings.ModelName, agentSettings.Temperature);
        
        // Create OpenAI client using the commented code from the original Program.cs
        // var client = new OpenAIClient(settings.ApiKey);
        // var chatClient = client.GetChatClient(modelName);
        
        // For now, we'll use a workaround by creating a ChatClient directly
        var chatClient = new ChatClient(settings.ApiKey, settings.ModelName);
        
        // Use the CreateOpenAiAgent method for OpenAI
        return agentBase.CreateOpenAiAgent(chatClient);
    }

    private static IAgent CreateAgentWithAzure(
        AgentBase agentBase,
        AzureSettings? settings, 
        AgentModelSettings agentSettings, 
        ILogger logger)
    {
        if (settings == null)
        {
            logger.Error("Azure settings are not configured");
            throw new InvalidOperationException("Azure settings are not configured");
        }
        
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            logger.Error("Azure API key is not configured");
            throw new InvalidOperationException("Azure API key is not configured");
        }
        
        if (string.IsNullOrEmpty(settings.Endpoint))
        {
            logger.Error("Azure endpoint is not configured");
            throw new InvalidOperationException("Azure endpoint is not configured");
        }
        
        
        logger.Information("Creating Azure chat client with model: {ModelName}, temperature: {Temperature}", 
            settings.ModelName, agentSettings.Temperature);
        
        var azureOptions = new AzureOpenAIClientOptions();
        if (settings.ApiVersion != null)
            SetCustomVersion(azureOptions, settings.ApiVersion);
        
        // Create Azure client
        var azureClient = new AzureOpenAIClient(
            new Uri(settings.Endpoint), 
            new AzureKeyCredential(settings.ApiKey), 
            azureOptions);
        
        // Get the chat client
        var chatClient = azureClient.GetChatClient(settings.ModelName);
        
        // Use the CreateOpenAiAgent method for Azure chat client
        return agentBase.CreateOpenAiAgent(chatClient);
    }

    private static IAgent CreateAgentWithOpenRouter(
        AgentBase agentBase,
        OpenRouterSettings? settings, 
        AgentModelSettings agentSettings, 
        ILogger logger)
    {
        if (settings == null)
        {
            logger.Error("OpenRouter settings are not configured");
            throw new InvalidOperationException("OpenRouter settings are not configured");
        }
        
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            logger.Error("OpenRouter API key is not configured");
            throw new InvalidOperationException("OpenRouter API key is not configured");
        }
        
        
        logger.Information("Creating OpenRouter chat client with model: {ModelName}, temperature: {Temperature}", 
            settings.ModelName, agentSettings.Temperature);
        
        // Create OpenRouter client using a direct ChatClient
        // We need to create options with the endpoint
        var options = new OpenAI.OpenAIClientOptions();
        options.Endpoint = new Uri(settings.Endpoint);
        
        var chatClient = new ChatClient(settings.ApiKey, settings.ModelName, options);
        
        // Use the CreateOpenAiAgent method for OpenRouter (same as OpenAI)
        return agentBase.CreateOpenAiAgent(chatClient);
    }

    public static void SetCustomVersion(AzureOpenAIClientOptions options, string customVersion)
    {
        // Get the type of the options object
        Type optionsType = typeof(AzureOpenAIClientOptions);

        // Use reflection to get the backing field of the Version property
        FieldInfo? versionField = optionsType.GetField("<Version>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        if (versionField != null)
        {
            // Set the value of the backing field to your custom version string
            versionField.SetValue(options, customVersion);
        }
        else
        {
            throw new InvalidOperationException("Unable to find the Version backing field.");
        }
    }
}