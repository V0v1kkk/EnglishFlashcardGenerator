# F# rewrite smoke runner

This vertical slice uses Microsoft Agent Framework Workflows for the parser -> teacher -> reviewer -> normalizer -> writer pipeline.

## Obsidian Spaced Repetition output

The formatter emits Obsidian Spaced Repetition multiline cards. In the plugin, `?` creates a one-way multiline basic card. `??` creates a bidirectional card and therefore a reversed sibling card.

The model can choose the direction per card, so one output file may contain both styles:

```text
front
?
back

front
??
back
```

The default fallback remains bidirectional `??` for compatibility with the existing output. Use `--card-mode one-way` when generated or fake cards do not carry their own direction and should fall back to one-way output.

The generator keeps examples on a non-empty back line:

```text
look up
??
to search for information
*Example sentence: I looked up the word in the dictionary.*
```

The parser preserves per-card `?` / `??` direction, and the formatter writes each card with its own direction. The formatter strips accidental scheduling metadata from generated card fields, including `<!--SR:...-->`, `[!sr|card-metadata]`, YAML `sr-*` fields, standalone `?` / `??` lines inside card text, and accidental standalone `#flashcards` / `#review` tags. It also avoids the legacy `::` delimiter.

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
  --max-output-tokens 2048 \
  --llm-disable-thinking
```

The API key is read from `--llm-api-key` or `LITELLM_API_KEY`. Do not commit keys or put them in templates. CLI values take precedence over environment values. The smoke adapter also accepts environment configuration for mode (`LITELLM_MODE` / `GENERATOR_MODE` / `LLM_MODE`), base URL (`LITELLM_BASE_URL` / `OPENAI_BASE_URL`), model (`LITELLM_MODEL` / `OPENAI_MODEL`), timeout (`LITELLM_TIMEOUT` or `LITELLM_TIMEOUT_SECONDS`, capped at 120), max sections (`LITELLM_MAX_SECTIONS`, capped at 2), max tokens (`LITELLM_MAX_TOKENS` or `LITELLM_MAX_OUTPUT_TOKENS`, capped at 2048), and thinking suppression (`LITELLM_DISABLE_THINKING` / `OPENAI_DISABLE_THINKING`). The disable-thinking option sends `chat_template_kwargs.enable_thinking=false`, which is useful for local reasoning models that otherwise spend the bounded token budget on hidden reasoning instead of final card content. The adapter calls the OpenAI-compatible `/chat/completions` endpoint and parses the same multiline Obsidian SR cards that the formatter writes.

The implementation uses a thin injectable HTTP adapter inside the MAF workflow rather than a provider-specific `ChatClientAgent`. That keeps LiteLLM/local smoke testing provider-neutral and avoids hardcoded endpoints, models, or keys while preserving the workflow shape.
