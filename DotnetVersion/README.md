# English Flashcard Generator

A tool for automatically generating flashcards from English learning notes using AI.

## Overview

This application processes English learning notes in markdown format and generates flashcards to help with memorization. It uses AI to extract important vocabulary, phrases, and concepts from your notes and formats them as flashcards.

The application uses a multi-agent workflow with specialized AI agents:
- **English Teacher Agent**: Analyzes notes and identifies important content for flashcards
- **Flashcard Reviewer Agent**: Reviews and provides feedback on the generated flashcards
- **Flashcard Extractor Agent**: Formats the final flashcards in a structured format

## Features

- Processes markdown notes with a specific format
- Extracts vocabulary, phrases, and concepts automatically
- Generates both flashcards and formatted learning notes
- Supports multiple AI providers (OpenAI, Azure OpenAI, OpenRouter)
- Configurable file paths and templates
- Comprehensive logging with Serilog

## Requirements

- .NET 8.0 or higher
- An API key for one of the supported AI providers:
  - OpenAI
  - Azure OpenAI
  - OpenRouter

## Setup

1. Clone the repository
2. Copy `appsettings.template.json` to `appsettings.json`
3. Configure your API keys and file paths in `appsettings.json`
4. Build and run the application

## Configuration

The application is configured through the `appsettings.json` file:

```json
{
  "FilePaths": {
    "SourceNotePath": "/path/to/your/notes.md",
    "ResultCardsFolderPath": "/path/to/your/flashcards/folder/",
    "ResultNotesFolderPath": "/path/to/your/notes/folder/",
    "CardTemplatePath": "Templates/cardTemplate.md",
    "NoteTemplatePath": "Templates/noteTemplate.md"
  },
  "ApiConfiguration": {
    "Provider": "OpenAI",
    "OpenAI": {
      "ApiKey": "your-openai-api-key",
      "ModelName": "gpt-4o-2024-08-06"
    },
    "Azure": {
      "ApiKey": "your-azure-api-key",
      "Endpoint": "https://your-resource-name.openai.azure.com/",
      "ModelName": "your-deployment-name",
      "ApiVersion": "2024-08-01-preview"
    },
    "OpenRouter": {
      "ApiKey": "your-openrouter-api-key",
      "Endpoint": "https://openrouter.ai/api/",
      "ModelName": "openai/o1-mini"
    }
  }
}
```

## Note Format

The application expects notes in a specific markdown format, with second-level headers (`## `) separating different learning sessions. The first line of each section should contain a date in one of these formats:
- `[[yyyy-MM-dd-DayOfWeek|dd.MM.yyyy]]`
- `[[yyyy-MM-dd-DayOfWeek]]`
- `dd.MM.yyyy`

Example:

```markdown
## [[2023-05-15-Monday|15.05.2023]]

Today I learned about **phrasal verbs** with "get":

- **get up** - to rise from bed *I get up at 7 AM every day*
- **get along** - to have a good relationship *We get along well with our neighbors*
- **get over** - to recover from something *It took me a week to get over the flu*

??? Is "get by" also a phrasal verb?
Yes, it means to manage with difficulty.
```

## Output

The application generates two types of files:
1. Flashcard files (in the `ResultCardsFolderPath` directory)
2. Formatted learning note files (in the `ResultNotesFolderPath` directory)

### Flashcard Format

Flashcards are formatted with a front side (question) and back side (answer), with an option for double-sided cards.

Example:
```
get up
?
to rise from bed
```

## License

[MIT License](LICENSE)