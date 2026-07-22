using System.Text.Json;

namespace EnglishFlashcardGenerator.Core;

public static class ProcessResultHandler
{
    public static ProcessRunResult Process(
        RunSummary summary,
        TimeSpan duration,
        IDictionary<string, double> metricsAggregator,
        IDictionary<string, int> histogramCounts,
        ProcessOptions options,
        string modelName,
        DateTime? utcNowOverride = null)
    {
        var daysSelected = summary.DaysProcessed;
        var daysSucceeded = summary.DaysSucceeded;
        var daysFailed = summary.DaysFailed;
        var cardsKept = summary.CardsWritten;
        var isPartial = daysSucceeded > 0 && daysFailed > 0;

        var cardsGenerated = (int)metricsAggregator.Where(k => k.Key.StartsWith("flashcards.cards.generated")).Sum(k => k.Value);
        var promptTokens = (long)metricsAggregator.Where(k => k.Key.StartsWith("flashcards.tokens.prompt")).Sum(k => k.Value);
        var completionTokens = (long)metricsAggregator.Where(k => k.Key.StartsWith("flashcards.tokens.completion")).Sum(k => k.Value);

        double promptRate = modelName.Contains("mini") ? 0.15 : modelName.Contains("nano") ? 0.05 : 2.50; // per 1M
        double completionRate = modelName.Contains("mini") ? 0.60 : modelName.Contains("nano") ? 0.20 : 10.00; // per 1M

        double totalCost = (promptTokens / 1_000_000.0) * promptRate + (completionTokens / 1_000_000.0) * completionRate;
        double costPerCard = cardsKept > 0 ? totalCost / cardsKept : 0.0;

        var finalMetrics = new Dictionary<string, object>();
        finalMetrics["TotalDurationSeconds"] = duration.TotalSeconds;
        foreach (var kvp in metricsAggregator.OrderBy(k => k.Key))
        {
            if (histogramCounts.TryGetValue(kvp.Key, out var count))
            {
                finalMetrics[kvp.Key] = new { Sum = kvp.Value, Count = count, Average = kvp.Value / (double)count };
            }
            else
            {
                finalMetrics[kvp.Key] = kvp.Value;
            }
        }

        finalMetrics["TotalCostUSD"] = totalCost;
        finalMetrics["CostPerCardUSD"] = costPerCard;
        finalMetrics["TotalPromptTokens"] = promptTokens;
        finalMetrics["TotalCompletionTokens"] = completionTokens;

        var metricsJson = JsonSerializer.Serialize(finalMetrics, new JsonSerializerOptions { WriteIndented = true });

        var utcNow = utcNowOverride ?? DateTime.UtcNow;
        string? metricsPath = MetricsFileManager.WriteMetricsFile(options, metricsJson, cardsKept, isPartial, utcNow);

        var runResult = new ProcessRunResult(
            daysSelected,
            daysSucceeded,
            daysFailed,
            cardsGenerated,
            cardsKept,
            duration,
            promptTokens,
            completionTokens,
            (decimal)totalCost,
            metricsPath,
            isPartial
        );

        if (!string.IsNullOrWhiteSpace(options.SummaryOut))
        {
            SummaryWriter.HandleSummary(options.SummaryOut, runResult);
        }

        if (!options.NoMetricsStdout)
        {
            Console.WriteLine($"days={summary.DaysProcessed} succeeded={summary.DaysSucceeded} failed={summary.DaysFailed} cards={summary.CardsWritten}");
            foreach (var file in summary.OutputFiles)
            {
                Console.WriteLine(file);
            }

            Console.WriteLine("--- METRICS ---");
            Console.WriteLine(metricsJson);

            if (!string.IsNullOrWhiteSpace(metricsPath))
            {
                Console.WriteLine($"Metrics saved to {metricsPath}");
            }
        }

        return runResult;
    }
}
