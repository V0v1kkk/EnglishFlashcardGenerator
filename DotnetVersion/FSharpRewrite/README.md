# F# rewrite smoke runner

This vertical slice uses Microsoft Agent Framework Workflows for the parser -> teacher -> reviewer -> normalizer -> writer pipeline.

## Obsidian Spaced Repetition output

The formatter emits Obsidian Spaced Repetition multiline cards. The default remains bidirectional for compatibility with the existing output:

```text
front
??
back
```

Use `--card-mode one-way` to emit one-way cards instead:

```text
front
?
back
```

In the Obsidian Spaced Repetition plugin, `?` creates a one-way multiline basic card. `??` creates a bidirectional card and therefore a reversed sibling card. The generator keeps examples on a non-empty back line:

```text
look up
??
to search for information
*Example sentence: I looked up the word in the dictionary.*
```

The formatter strips accidental scheduling metadata from generated card fields, including `<!--SR:...-->`, `[!sr|card-metadata]`, YAML `sr-*` fields, standalone `?` / `??` lines inside card text, and accidental standalone `#flashcards` / `#review` tags. It also avoids the legacy `::` delimiter.

## Generator modes

The default mode is deterministic fake generation for tests and dry runs:

```bash
dotnet run --project src/EnglishFlashcardGenerator.Cli \
  --source sample.md \
  --cards-out ./out/cards \
  --notes-out ./out/notes
```

A bounded OpenAI-compatible adapter is available for local or LiteLLM smoke tests:

```bash
dotnet run --project src/EnglishFlashcardGenerator.Cli \
  --source sample.md \
  --cards-out ./out/cards \
  --notes-out ./out/notes \
  --generator-mode local \
  --llm-base-url https://example.test/v1 \
  --llm-model LocalModel \
  --timeout-seconds 120 \
  --max-sections 1 \
  --max-output-tokens 2048
```

The API key is read from `--llm-api-key` or `LITELLM_API_KEY`. Do not commit keys or put them in templates. The adapter calls the OpenAI-compatible `/chat/completions` endpoint and parses the same multiline Obsidian SR cards that the formatter writes.

The implementation uses a thin injectable HTTP adapter inside the MAF workflow rather than a provider-specific `ChatClientAgent`. That keeps LiteLLM/local smoke testing provider-neutral and avoids hardcoded endpoints, models, or keys while preserving the workflow shape.
