using EnglishFlashcardGenerator.Core;
using EnglishFlashcardGenerator.Core.Agents;
using EnglishFlashcardGenerator.Core.Telemetry;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;

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

if (!ProcessOptionsParser.TryParse(args, out var options, out var parseError))
{
    if (parseError == "HELP")
    {
        Console.WriteLine("english-flashcards process --source <path> --cards-out <dir> --source-notes-out <dir> [--max-days 1] [--max-groups-per-day 4] [--group-workers 2] [--max-critic-iterations 2] [--apply] [--prune-source] [--metrics-out <path>] [--metrics-out-dir <dir>] [--skip-empty-metrics] [--summary-out <path>] [--no-metrics-stdout]");
        return 0;
    }

    Console.Error.WriteLine(parseError);
    return 2;
}

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

var request = new NoteProcessingRequest(
    options!.SourcePath,
    options.CardsOutputDirectory,
    options.SourceExcerptOutputDirectory,
    options.Apply,
    options.MaxDays,
    options.MaxParallelGroupWorkers,
    options.MaxCriticIterations,
    options.MaxGroupsPerDay,
    options.PruneSource);

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

RunSummary summary;
var sw = System.Diagnostics.Stopwatch.StartNew();
try
{
    summary = await WorkflowRunner.RunAsync<RunSummary>(workflow, request);
}
catch (Exception ex)
{
    sw.Stop();
    Console.Error.WriteLine($"Workflow execution failed: {ex.Message}");
    if (!string.IsNullOrWhiteSpace(options.SummaryOut) && File.Exists(options.SummaryOut))
    {
        try { File.Delete(options.SummaryOut); } catch { }
    }
    return 1;
}
sw.Stop();

meterListener.RecordObservableInstruments();

try
{
    var runResult = ProcessResultHandler.Process(summary, sw.Elapsed, metricsAggregator, histogramCounts, options, model);
    return summary.DaysFailed == 0 ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error processing metrics/summary output: {ex.Message}");
    return 1;
}
