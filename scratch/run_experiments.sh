#!/bin/bash
MODELS=("gpt-5.4-nano" "gpt-5.4-mini" "gpt-5.6-luna" "gpt-5.6-terra" "gpt-5.6-sol")

export LLM_TEMPERATURE="1"
source local-secrets.env

for MODEL in "${MODELS[@]}"; do
    echo "==========================================="
    echo "Running $MODEL..."
    echo "==========================================="
    export LLM_MODEL="$MODEL"
    rm -rf "test_run/cards_$MODEL" "test_run/source-notes_$MODEL"
    dotnet run --project src/EnglishFlashcardGenerator.Cli -- process --source example/input/sample_notes_20_days.md --cards-out "test_run/cards_$MODEL" --source-notes-out "test_run/source-notes_$MODEL" --max-days 20 --metrics-out "test_run/metrics_$MODEL.json" --apply
done
