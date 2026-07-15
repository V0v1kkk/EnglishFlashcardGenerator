using EnglishFlashcardGenerator.Core;
using EnglishFlashcardGenerator.Core.Agents;
using Microsoft.Extensions.Logging;

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
    Console.WriteLine("english-flashcards process --source <path> --cards-out <dir> --source-notes-out <dir> [--max-days 1] [--max-groups-per-day 4] [--group-workers 2] [--max-critic-iterations 2] [--apply] [--prune-source]");
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

var baseUrl = Environment.GetEnvironmentVariable("LLM_BASE_URL");
var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
var model = Environment.GetEnvironmentVariable("LLM_MODEL");
if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
{
    Console.Error.WriteLine("Live MAF workflow requires LLM_BASE_URL, LLM_API_KEY, and LLM_MODEL. Deterministic test stubs are available only in tests, not product mode.");
    return 3;
}

var temperature = double.TryParse(Environment.GetEnvironmentVariable("LLM_TEMPERATURE"), out var temp) ? temp : 0;
var maxTokens = int.TryParse(Environment.GetEnvironmentVariable("LLM_MAX_OUTPUT_TOKENS"), out var mt) ? mt : (int?)null;
var networkTimeout = OptionalSecondsEnvironment("LLM_NETWORK_TIMEOUT_SECONDS") ?? TimeSpan.FromSeconds(600);
var maxNetworkRetries = OptionalNonNegativeEnvironment("LLM_MAX_NETWORK_RETRIES") ?? 5;
var request = new NoteProcessingRequest(source, cardsOut, sourceOut, apply, maxDays, workers, iterations, maxGroups, pruneSource);

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});
var logger = loggerFactory.CreateLogger("Workflow");

var agents = MafStructuredAgentPort.FromOpenAICompatible(new LlmOptions(baseUrl, apiKey, model, temperature, maxTokens, networkTimeout, maxNetworkRetries), loggerFactory);
var workflow = CSharpMafWorkflowFactory.BuildNoteWorkflow(agents, logger);
var summary = await WorkflowRunner.RunAsync<RunSummary>(workflow, request);

Console.WriteLine($"days={summary.DaysProcessed} succeeded={summary.DaysSucceeded} failed={summary.DaysFailed} cards={summary.CardsWritten}");
foreach (var file in summary.OutputFiles)
{
    Console.WriteLine(file);
}

return summary.DaysFailed == 0 ? 0 : 1;
