using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace EnglishFlashcardGenerator.Configuration;

public class AppSettings
{
    public FilePathSettings FilePaths { get; set; } = new();
    public List<ProviderSettings> Providers { get; set; } = new();
    public AgentSettings Agents { get; set; } = new();

    public static AppSettings LoadFromConfiguration(IConfiguration configuration)
    {
        var settings = new AppSettings();
        
        // Bind the FilePaths section
        configuration.GetSection("FilePaths").Bind(settings.FilePaths);
        
        // Bind the Providers section
        configuration.GetSection("Providers").Bind(settings.Providers);
        
        // Bind the Agents section
        configuration.GetSection("Agents").Bind(settings.Agents);
        
        return settings;
    }
    
    // Helper method to find a provider by name
    public ProviderSettings? GetProviderByName(string providerName)
    {
        return Providers.Find(p => p.Name == providerName);
    }
}

public class FilePathSettings
{
    public string SourceNotePath { get; set; } = string.Empty;
    public string ResultCardsFolderPath { get; set; } = string.Empty;
    public string ResultNotesFolderPath { get; set; } = string.Empty;
    public string CardTemplatePath { get; set; } = string.Empty;
    public string NoteTemplatePath { get; set; } = string.Empty;
}

public class ProviderSettings
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "OpenAI", "Azure", "OpenRouter"
    public OpenAISettings? OpenAI { get; set; }
    public AzureSettings? Azure { get; set; }
    public OpenRouterSettings? OpenRouter { get; set; }
    
    // Helper method to get the appropriate settings based on the provider type
    public object? GetSettings()
    {
        return Type switch
        {
            "OpenAI" => OpenAI,
            "Azure" => Azure,
            "OpenRouter" => OpenRouter,
            _ => null
        };
    }
}

public class OpenAISettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = "gpt-4o-2024-08-06";
}

public class AzureSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string? ApiVersion { get; set; }
}

public class OpenRouterSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "https://openrouter.ai/api/";
    public string ModelName { get; set; } = "openai/o1-mini";
}

public class AgentSettings
{
    public AgentModelSettings TeacherAgent { get; set; } = new();
    public AgentModelSettings ReviewerAgent { get; set; } = new();
    public AgentModelSettings ExtractorAgent { get; set; } = new();
}

public class AgentModelSettings
{
    public string ProviderName { get; set; } = "Default"; // Reference to a named provider
    public float Temperature { get; set; } = 0.7f;
    public int MaxTokens { get; set; } = 16384;
}