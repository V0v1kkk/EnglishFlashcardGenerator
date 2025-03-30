# English Flashcard Generator - Knowledge Transfer Document

## Project Overview

The English Flashcard Generator is a tool designed to automatically generate flashcards from English learning notes using AI. The application processes markdown-formatted notes, extracts important vocabulary, phrases, and concepts, and generates flashcards to help with memorization.

The application uses a multi-agent workflow with specialized AI agents:
- **English Teacher Agent**: Analyzes notes and identifies important content for flashcards
- **Flashcard Reviewer Agent**: Reviews and provides feedback on the generated flashcards
- **Flashcard Extractor Agent**: Formats the final flashcards in a structured JSON format

## Architecture

The application follows a modular architecture:

1. **Configuration System**
   - Uses Microsoft.Extensions.Configuration for settings management
   - Supports JSON configuration files and environment variables
   - Implements a flexible provider-based configuration for AI services

2. **Logging System**
   - Uses Serilog for structured logging
   - Supports console and Seq logging
   - Provides detailed context for debugging and monitoring

3. **Agent System**
   - Uses AutoGen for agent-based workflows
   - Implements a graph-based workflow for agent communication
   - Supports multiple AI providers (Azure, OpenAI, OpenRouter)

4. **File Processing**
   - Parses markdown notes with specific formatting
   - Generates flashcards and formatted learning notes
   - Uses templates for consistent output

## Configuration System

### Provider Configuration

The application supports multiple named providers, each with its own configuration. This allows for flexibility in choosing which AI service to use for each agent.

```json
"Providers": [
  {
    "Name": "Default",
    "Type": "Azure",
    "Azure": {
      "ApiKey": "your-azure-api-key",
      "Endpoint": "https://your-resource-name.openai.azure.com/",
      "ModelName": "your-deployment-name",
      "ApiVersion": "2024-08-01-preview"
    }
  },
  {
    "Name": "OpenAIProvider",
    "Type": "OpenAI",
    "OpenAI": {
      "ApiKey": "your-openai-api-key",
      "ModelName": "gpt-4o-2024-08-06"
    }
  }
]
```

Each provider has:
- **Name**: A unique identifier used to reference the provider
- **Type**: The provider type (Azure, OpenAI, OpenRouter)
- **Type-specific settings**: Configuration specific to the provider type

### Agent Configuration

Each agent can be configured to use a specific provider and model:

```json
"Agents": {
  "TeacherAgent": {
    "ProviderName": "Default",
    "ModelName": "gpt-4o",
    "Temperature": 0.7,
    "MaxTokens": 16384
  }
}
```

Agent settings include:
- **ProviderName**: Reference to a named provider
- **ModelName**: Optional model name (overrides the provider's default)
- **Temperature**: Controls randomness in responses
- **MaxTokens**: Maximum tokens for the response

## Agent System

### Agent Base Class

The `AgentBase` abstract class defines the interface for all agents:

```csharp
public abstract class AgentBase
{
    public abstract string Introduction { get; }
    
    public abstract IAgent CreateOpenAiAgent(ChatClient client);
    public abstract IAgent CreateAzureAgent(ChatCompletionsClient client, string modelName);
}
```

Each agent implementation must provide:
- An introduction describing its purpose
- Methods to create agent instances for different provider types

### Agent Creation

The `ChatClientFactory` handles agent creation based on provider type:

```csharp
public static IAgent CreateAgentForAgent(
    string agentName,
    AgentBase agentBase,
    AppSettings appSettings,
    ILogger logger)
{
    // Get agent settings
    // Find provider by name
    // Create agent with provider
}
```

The factory uses the appropriate creation method based on provider type:
- For Azure: Uses `CreateAzureAgent`
- For OpenAI/OpenRouter: Uses `CreateOpenAiAgent`

### Workflow

The agent workflow is defined using a graph-based approach:

```csharp
var workflow = new Graph(
    [
        userProxy2TeacherTransition,
        teacher2ReviewerTransition,
        reviewer2TeacherTransition,
        reviewer2ExtractorTransition,
        extractor2AdminTransition,
    ]);
```

This defines the allowed transitions between agents, creating a structured conversation flow.

## How to Add New Providers

To add a new provider:

1. **Update the ProviderSettings class** (if needed)
   - Add a new property for the provider type
   - Add a new case in the `GetSettings()` method

2. **Update the ChatClientFactory**
   - Add a new case in the `CreateAgentWithProvider` method
   - Implement a new `CreateAgentWith[ProviderName]` method

3. **Add the provider to appsettings.json**
   - Create a new entry in the Providers array
   - Configure the provider-specific settings

## How to Customize Agents

To customize an agent:

1. **Update the agent's instruction**
   - Modify the `_instruction` field in the agent class
   - Adjust the system message to change the agent's behavior

2. **Update the agent's configuration**
   - Modify the agent settings in appsettings.json
   - Adjust temperature, model, or provider

3. **Update the workflow**
   - Modify the transitions in `CreateGroupAndTheLastAgent`
   - Adjust the transition conditions to change the conversation flow

## Error Handling and Retries

The application implements a robust error handling and retry mechanism:

1. **JSON Deserialization**
   - Retries up to `MaxProcessingAttempts` times
   - Logs detailed error information
   - Skips sections that consistently fail

2. **Agent Communication**
   - Uses a structured workflow to handle agent transitions
   - Logs detailed information about agent interactions
   - Provides context for debugging

## Troubleshooting

### Common Issues

1. **API Key Issues**
   - Check that the API keys are correctly configured in appsettings.json
   - Ensure the keys have the necessary permissions

2. **Model Availability**
   - Ensure the specified models are available in your subscription
   - Check that the model names match the provider's requirements

3. **JSON Deserialization Errors**
   - Check the agent instructions for the Extractor agent
   - Ensure the JSON schema is correctly specified

### Logging

The application uses Serilog for logging. To enable more detailed logging:

1. Update the MinimumLevel in appsettings.json:
```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Debug",
    "Override": {
      "Microsoft": "Warning"
    }
  }
}
```

2. Check the logs for detailed information about errors and agent interactions.

## Future Improvements

Potential areas for improvement:

1. **Additional Providers**
   - Add support for more AI providers
   - Implement provider-specific optimizations

2. **Enhanced Error Handling**
   - Implement more sophisticated retry strategies
   - Add support for fallback providers

3. **Performance Optimizations**
   - Implement parallel processing for sections
   - Add caching for frequently used responses

4. **User Interface**
   - Add a web or desktop interface
   - Implement real-time progress monitoring