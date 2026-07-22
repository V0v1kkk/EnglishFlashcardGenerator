namespace EnglishFlashcardGenerator.Core;

public sealed record ProcessRunResult(
    int DaysSelected,
    int DaysSucceeded,
    int DaysFailed,
    int CardsGenerated,
    int CardsKept,
    TimeSpan Duration,
    long PromptTokens,
    long CompletionTokens,
    decimal TotalCostUsd,
    string? MetricsPath,
    bool IsPartial);
