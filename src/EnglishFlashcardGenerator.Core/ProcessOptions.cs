namespace EnglishFlashcardGenerator.Core;

public sealed record ProcessOptions(
    string SourcePath,
    string CardsOutputDirectory,
    string SourceExcerptOutputDirectory,
    bool Apply,
    int? MaxDays,
    int MaxParallelGroupWorkers,
    int MaxCriticIterations,
    int MaxGroupsPerDay,
    bool PruneSource,
    string? MetricsOut,
    string? MetricsOutDir,
    bool SkipEmptyMetrics,
    string? SummaryOut,
    bool NoMetricsStdout);

public static class ProcessOptionsParser
{
    public static bool TryParse(string[] args, out ProcessOptions? options, out string? errorMessage)
    {
        options = null;
        errorMessage = null;

        if (args.Length == 0 || args.Contains("--help"))
        {
            errorMessage = "HELP";
            return false;
        }

        if (args[0] != "process")
        {
            errorMessage = "Expected subcommand: process";
            return false;
        }

        if (!TryGetOption(args, "--source", out var source, out var err) ||
            !TryGetOption(args, "--cards-out", out var cardsOut, out err) ||
            !TryGetOption(args, "--source-notes-out", out var sourceOut, out err) ||
            !TryGetOption(args, "--max-days", out var maxDaysStr, out err) ||
            !TryGetOption(args, "--group-workers", out var workersStr, out err) ||
            !TryGetOption(args, "--max-critic-iterations", out var iterationsStr, out err) ||
            !TryGetOption(args, "--max-groups-per-day", out var maxGroupsStr, out err) ||
            !TryGetOption(args, "--metrics-out", out var metricsOut, out err) ||
            !TryGetOption(args, "--metrics-out-dir", out var metricsOutDir, out err) ||
            !TryGetOption(args, "--summary-out", out var summaryOut, out err))
        {
            errorMessage = err;
            return false;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            errorMessage = "Missing --source";
            return false;
        }
        if (string.IsNullOrWhiteSpace(cardsOut))
        {
            errorMessage = "Missing --cards-out";
            return false;
        }
        if (string.IsNullOrWhiteSpace(sourceOut))
        {
            errorMessage = "Missing --source-notes-out";
            return false;
        }

        if (metricsOut != null && metricsOutDir != null)
        {
            errorMessage = "Error: --metrics-out and --metrics-out-dir are mutually exclusive.";
            return false;
        }

        if (metricsOutDir != null && File.Exists(metricsOutDir))
        {
            errorMessage = $"Error: Metrics output directory path '{metricsOutDir}' is an existing file.";
            return false;
        }

        if (summaryOut != null && Directory.Exists(summaryOut))
        {
            errorMessage = $"Error: Summary output path '{summaryOut}' is an existing directory.";
            return false;
        }

        if (summaryOut != null && metricsOut != null &&
            Path.GetFullPath(summaryOut).Equals(Path.GetFullPath(metricsOut), StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Error: Summary output path and metrics output path cannot be identical.";
            return false;
        }

        var maxDays = int.TryParse(maxDaysStr, out var md) ? md : 1;
        var workers = int.TryParse(workersStr, out var gw) ? gw : 2;
        var iterations = int.TryParse(iterationsStr, out var it) ? it : 2;
        var maxGroups = int.TryParse(maxGroupsStr, out var mg) ? mg : 4;
        var apply = args.Contains("--apply");
        var pruneSource = args.Contains("--prune-source");
        var skipEmptyMetrics = args.Contains("--skip-empty-metrics");
        var noMetricsStdout = args.Contains("--no-metrics-stdout");

        options = new ProcessOptions(
            source,
            cardsOut,
            sourceOut,
            apply,
            maxDays,
            workers,
            iterations,
            maxGroups,
            pruneSource,
            metricsOut,
            metricsOutDir,
            skipEmptyMetrics,
            summaryOut,
            noMetricsStdout
        );

        return true;
    }

    private static bool TryGetOption(string[] args, string name, out string? value, out string? error)
    {
        value = null;
        error = null;
        var index = Array.IndexOf(args, name);
        if (index < 0) return true;

        if (index + 1 >= args.Length || args[index + 1].StartsWith("--"))
        {
            error = $"Option '{name}' requires a value.";
            return false;
        }

        value = args[index + 1];
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"Option '{name}' cannot be empty.";
            return false;
        }
        return true;
    }
}
