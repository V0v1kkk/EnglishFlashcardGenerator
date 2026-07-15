using EnglishFlashcardGenerator.Core;
using EnglishFlashcardGenerator.Core.Agents;
using EnglishFlashcardGenerator.Core.Telemetry;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;
using System.Text.Json;

static string? Option(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static TimeSpan? OptionalSecondsEnvironment(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    return int.TryParse(value, out var seconds) && seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
}

static int? OptionalNonNegativeEnvironment(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    return int.TryParse(value, out var parsed) && parsed >= 0 ? parsed : null;
}

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine("english-flashcards process --source <path> --cards-out <dir> --source-notes-out <dir> [--max-days 1] [--max-groups-per-day 4] [--group-workers 2] [--max-critic-iterations 2] [--apply] [--prune-source] [--metrics-out <path>]");
    return 0;
}

if (args[0] != "process")
{
    Console.Error.WriteLine("Expected subcommand: process");
    return 2;
}

var source = Option(args, "--source") ?? throw new ArgumentException("Missing --source");
var cardsOut = Option(args, "--cards-out") ?? throw new ArgumentException("Missing --cards-out");
var sourceOut = Option(args, "--source-notes-out") ?? throw new ArgumentException("Missing --source-notes-out");
var maxDays = int.TryParse(Option(args, "--max-days"), out var md) ? md : 1;
var workers = int.TryParse(Option(args, "--group-workers"), out var gw) ? gw : 2;
var iterations = int.TryParse(Option(args, "--max-critic-iterations"), out var it) ? it : 2;
var maxGroups = int.TryParse(Option(args, "--max-groups-per-day"), out var mg) ? mg : 4;
var apply = args.Contains("--apply");
var pruneSource = args.Contains("--prune-source");
var metricsOut = Option(args, "--metrics-out");

var baseUrl = Environment.GetEnvironmentVariable("LLM_BASE_URL");
var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
var model = Environment.GetEnvironmentVariable("LLM_MODEL");
if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
{
    Console.Error.WriteLine("Live MAF workflow requires LLM_BASE_URL, LLM_API_KEY, and LLM_MODEL. Deterministic test stubs are available only in tests, not product mode.");
    return 3;
}

var temperature = double.TryParse(Environment.GetEnvironmentVariable("LLM_TEMPERATURE"), out var temp) ? (double?)temp : null;
var maxTokens = int.TryParse(Environment.GetEnvironmentVariable("LLM_MAX_OUTPUT_TOKENS"), out var mt) ? mt : (int?)null;
var networkTimeout = OptionalSecondsEnvironment("LLM_NETWORK_TIMEOUT_SECONDS") ?? TimeSpan.FromSeconds(600);
var maxNetworkRetries = OptionalNonNegativeEnvironment("LLM_MAX_NETWORK_RETRIES") ?? 5;
var request = new NoteProcessingRequest(source, cardsOut, sourceOut, apply, maxDays, workers, iterations, maxGroups, pruneSource);

var metricsAggregator = new Dictionary<string, double>();
var histogramCounts = new Dictionary<string, int>();

using var meterListener = new MeterListener();
meterListener.InstrumentPublished = (instrument, listener) =>
{
    if (instrument.Meter.Name == FlashcardMetrics.MeterName)
    {
        listener.EnableMeasurementEvents(instrument);
    }
};

meterListener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
{
    var tagString = tags.Length > 0 
        ? $"[{string.Join(",", tags.ToArray().Select(t => $"{t.Key}={t.Value}"))}]" 
        : "";
    var key = $"{instrument.Name}{tagString}";

    if (instrument is Histogram<int>)
    {
        metricsAggregator.TryGetValue(key, out var sum);
        metricsAggregator[key] = sum + measurement;
        
        histogramCounts.TryGetValue(key, out var count);
        histogramCounts[key] = count + 1;
    }
    else
    {
        metricsAggregator.TryGetValue(key, out var sum);
        metricsAggregator[key] = sum + measurement;
    }
});

meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
{
    var tagString = tags.Length > 0 
        ? $"[{string.Join(",", tags.ToArray().Select(t => $"{t.Key}={t.Value}"))}]" 
        : "";
    var key = $"{instrument.Name}{tagString}";

    metricsAggregator.TryGetValue(key, out var sum);
    metricsAggregator[key] = sum + measurement;
});

meterListener.Start();

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});
var logger = loggerFactory.CreateLogger("Workflow");

var agents = MafStructuredAgentPort.FromOpenAICompatible(new LlmOptions(baseUrl, apiKey, model, temperature, maxTokens, networkTimeout, maxNetworkRetries), loggerFactory);
var workflow = CSharpMafWorkflowFactory.BuildNoteWorkflow(agents, logger);
var sw = System.Diagnostics.Stopwatch.StartNew();
var summary = await WorkflowRunner.RunAsync<RunSummary>(workflow, request);
sw.Stop();

meterListener.RecordObservableInstruments();

Console.WriteLine($"days={summary.DaysProcessed} succeeded={summary.DaysSucceeded} failed={summary.DaysFailed} cards={summary.CardsWritten}");
foreach (var file in summary.OutputFiles)
{
    Console.WriteLine(file);
}

var finalMetrics = new Dictionary<string, object>();
finalMetrics["TotalDurationSeconds"] = sw.Elapsed.TotalSeconds;
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

// Calculate Total Cost
double promptRate = model.Contains("mini") ? 0.15 : model.Contains("nano") ? 0.05 : 2.50; // per 1M
double completionRate = model.Contains("mini") ? 0.60 : model.Contains("nano") ? 0.20 : 10.00; // per 1M

double totalPromptTokens = metricsAggregator.Where(k => k.Key.StartsWith("flashcards.tokens.prompt")).Sum(k => k.Value);
double totalCompletionTokens = metricsAggregator.Where(k => k.Key.StartsWith("flashcards.tokens.completion")).Sum(k => k.Value);

double totalCost = (totalPromptTokens / 1_000_000.0) * promptRate + (totalCompletionTokens / 1_000_000.0) * completionRate;
double costPerCard = summary.CardsWritten > 0 ? totalCost / summary.CardsWritten : 0.0;

finalMetrics["TotalCostUSD"] = totalCost;
finalMetrics["CostPerCardUSD"] = costPerCard;
finalMetrics["TotalPromptTokens"] = totalPromptTokens;
finalMetrics["TotalCompletionTokens"] = totalCompletionTokens;


var metricsJson = JsonSerializer.Serialize(finalMetrics, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine("--- METRICS ---");
Console.WriteLine(metricsJson);
if (!string.IsNullOrWhiteSpace(metricsOut))
{
    File.WriteAllText(metricsOut, metricsJson);
    Console.WriteLine($"Metrics saved to {metricsOut}");
}

return summary.DaysFailed == 0 ? 0 : 1;
