# EnglishFlashcardGenerator (C# Microsoft Agent Framework)

> [!NOTE]
> This is the primary implementation of the EnglishFlashcardGenerator using the Microsoft Agent Framework. 
> Previous implementations can be found in the `legacy/` directory:
> - [v1 (C# AutoGen)](legacy/v1-csharp)
> - [v2 (Python AG2)](legacy/v2-python)

This is a C#-first Microsoft Agent Framework workflow spike. It is intentionally separate from the earlier AutoGen C# implementation and the F# vertical slice.

## Research Notes: Small Models & Prompt Engineering

During our experiments with smaller, weaker models (e.g., `gpt-5.4-nano`), we discovered severe limitations in how they handle inline Markdown formatting instructions:
1. **Formatting Failures:** Small models consistently fail to correctly apply inline string formatting (such as mixing our `??` bidirectional delimiter with standard text). They often hallucinate duplicate delimiters, multi-line blocks, or merge JSON fields with plaintext instructions.
2. **Loss of Coverage:** Weaker models tend to drop 50-80% of items from source notes if not strictly micromanaged. 
3. **The Fix:** We completely eliminated manual string formatting from the Teacher prompt. Instead of asking the LLM to format cards with `??` inline, we shifted all structural constraints into the JSON schema itself (using `Front`, `Back`, `Example`, and `Direction` properties) and let the C# code build the Markdown strings. This decoupled formatting from reasoning, which drastically improved output consistency even on weak models. We also added explicit "Coverage Auditor" rules and "Few-Shot" BAD/GOOD examples to the prompt to force the model to adhere to masking constraints.

## Shape

- `src/EnglishFlashcardGenerator.Core` contains typed domain records, deterministic Markdown/output helpers, MAF workflow factories, and `ChatClientAgent`/`AIAgent` structured-output adapters.
- `src/EnglishFlashcardGenerator.Cli` is the bounded live entrypoint for an OpenAI-compatible local or remote model route.
- `tests/EnglishFlashcardGenerator.Core.Tests` contains deterministic stub workflow tests for routing and invariants.

## Workflow levels

- `NoteWorkflow`: read source, split dated days, select bounded days, run `DayWorkflow` sequentially, emit `RunSummary`.
- `DayWorkflow`: plan groups, validate, partition into a fixed worker pool, fan out to `GroupWorkerWorkflow[0..N-1]`, fan in with a barrier, dedupe, copy the source excerpt unchanged, format deterministic Obsidian SR Markdown, and write/dry-run.
- `GroupCardWorkflow`: teacher/critic revision loop with a hard max iteration bound.

## Live CLI

The live CLI always calls the configured LiteLLM/OpenAI-compatible provider; there is no fake product mode. Use deterministic tests for offline workflow routing checks.

Run the smoke commands from the repository root. The checked-in `./example/input/sample_notes.md` fixture is an Obsidian-style Markdown note with wiki-link date headings and more than two dated day sections, so it is suitable for both the minimal and representative live smokes.

Environment shape for live local-model smoke runs (set real values in your shell or secret manager; do not commit them):

```bash
export LLM_BASE_URL="http://127.0.0.1:4000/v1"
export LLM_API_KEY="<set-outside-git>"
export LLM_MODEL="local-model-name"
export LLM_TEMPERATURE=0
# Leave LLM_MAX_OUTPUT_TOKENS unset for Qwen/llama.cpp thinking-model smokes unless you are deliberately testing truncation behavior.
```

### Minimal single-day smoke: fast wiring check only

This dry-run processes one day and at most two planned groups. It is useful for a quick provider/CLI wiring check, but it is not representative coverage because it does not exercise the multi-day path or the write path.

```bash
dotnet run --project src/EnglishFlashcardGenerator.Cli -- process \
  --source ./example/input/sample_notes.md \
  --cards-out ./out/cards \
  --source-notes-out ./out/source-notes \
  --max-days 1 \
  --max-groups-per-day 2 \
  --group-workers 1 \
  --max-critic-iterations 2
```

### Representative sequential local-model smoke

Use this as the representative live smoke before relying on the C# MAF workflow v2 path. It processes at least two Obsidian-style dated day sections and up to two groups per day, runs group workers sequentially, allows two teacher/critic iterations, and writes outputs with `--apply`. For local single-concurrency model routes, keep `--group-workers 1`:

```bash
dotnet run --project src/EnglishFlashcardGenerator.Cli -- process \
  --source ./example/input/sample_notes.md \
  --cards-out ./out/cards \
  --source-notes-out ./out/source-notes \
  --max-days 2 \
  --max-groups-per-day 2 \
  --group-workers 1 \
  --max-critic-iterations 2 \
  --apply
```

The representative smoke exercises the multi-day/day-group/write path that the minimal single-day dry-run skips. It may still block on an exact local provider failure or malformed model output, but only after the improved error surfacing has labelled the failing workflow/agent boundary with the agent name and DTO context instead of appearing as an unlabelled hang or opaque parse failure.

Required environment for live mode:

- `LLM_BASE_URL`
- `LLM_API_KEY`
- `LLM_MODEL`

Optional:

- `LLM_TEMPERATURE`
- `LLM_MAX_OUTPUT_TOKENS`
- `LLM_NETWORK_TIMEOUT_SECONDS` (defaults to `600` for slow local models)
- `LLM_MAX_NETWORK_RETRIES` (defaults to `5` to handle rate limits)

The product CLI does not provide fake teacher/reviewer mode. Tests use deterministic stubs only for workflow routing and structured-output plumbing. If a provider returns malformed structured JSON, the agent boundary retries once with stricter JSON-only instructions and then fails with `AgentBoundaryException` naming the agent and DTO type.
