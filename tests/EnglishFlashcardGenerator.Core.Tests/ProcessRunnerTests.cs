using System.Text.Json;
using EnglishFlashcardGenerator.Core;
using Xunit;

namespace EnglishFlashcardGenerator.Core.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public void OptionParser_rejects_conflicting_metrics_options()
    {
        var args = new[]
        {
            "process",
            "--source", "notes.md",
            "--cards-out", "out/cards",
            "--source-notes-out", "out/source",
            "--metrics-out", "out/metrics.json",
            "--metrics-out-dir", "out/metrics_dir"
        };

        var success = ProcessOptionsParser.TryParse(args, out var options, out var error);

        Assert.False(success);
        Assert.Null(options);
        Assert.Contains("mutually exclusive", error);
    }

    [Fact]
    public void OptionParser_rejects_metrics_dir_when_it_is_an_existing_file()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var args = new[]
            {
                "process",
                "--source", "notes.md",
                "--cards-out", "out/cards",
                "--source-notes-out", "out/source",
                "--metrics-out-dir", tempFile
            };

            var success = ProcessOptionsParser.TryParse(args, out var options, out var error);

            Assert.False(success);
            Assert.Null(options);
            Assert.Contains("existing file", error);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void OptionParser_rejects_summary_out_when_it_is_an_existing_directory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var args = new[]
            {
                "process",
                "--source", "notes.md",
                "--cards-out", "out/cards",
                "--source-notes-out", "out/source",
                "--summary-out", tempDir
            };

            var success = ProcessOptionsParser.TryParse(args, out var options, out var error);

            Assert.False(success);
            Assert.Null(options);
            Assert.Contains("existing directory", error);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void Scenario_NoWork_with_skip_empty_metrics()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var metricsDir = Path.Combine(tempDir, "metrics");
            var summaryFile = Path.Combine(tempDir, "summary.txt");

            var args = new[]
            {
                "process",
                "--source", "notes.md",
                "--cards-out", "out/cards",
                "--source-notes-out", "out/source",
                "--metrics-out-dir", metricsDir,
                "--skip-empty-metrics",
                "--summary-out", summaryFile,
                "--no-metrics-stdout"
            };

            Assert.True(ProcessOptionsParser.TryParse(args, out var options, out var error));

            var summary = new RunSummary(0, 0, 0, 0, Array.Empty<string>(), Array.Empty<string>());
            var aggregator = new Dictionary<string, double>();
            var histograms = new Dictionary<string, int>();

            var result = ProcessResultHandler.Process(
                summary,
                TimeSpan.FromSeconds(1.5),
                aggregator,
                histograms,
                options!,
                "gpt-4o-mini"
            );

            Assert.Equal(0, result.DaysSelected);
            Assert.Equal(0, result.CardsKept);
            Assert.Null(result.MetricsPath);
            Assert.False(File.Exists(summaryFile));
            if (Directory.Exists(metricsDir))
            {
                Assert.Empty(Directory.GetFiles(metricsDir));
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Scenario_CardsCreated_successful_run()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var metricsDir = Path.Combine(tempDir, "metrics");
            var summaryFile = Path.Combine(tempDir, "summary.txt");

            var args = new[]
            {
                "process",
                "--source", "notes.md",
                "--cards-out", "out/cards",
                "--source-notes-out", "out/source",
                "--metrics-out-dir", metricsDir,
                "--summary-out", summaryFile
            };

            Assert.True(ProcessOptionsParser.TryParse(args, out var options, out var error));

            var summary = new RunSummary(1, 1, 0, 15, new[] { "out/cards/2025-01-01.md" }, Array.Empty<string>());
            var aggregator = new Dictionary<string, double>
            {
                ["flashcards.tokens.prompt"] = 1000,
                ["flashcards.tokens.completion"] = 500,
                ["flashcards.cards.kept"] = 15,
                ["flashcards.cards.generated"] = 18
            };
            var histograms = new Dictionary<string, int>();

            var testTime = DateTime.Parse("2026-07-22T18:45:32.417Z").ToUniversalTime();

            var result = ProcessResultHandler.Process(
                summary,
                TimeSpan.FromSeconds(10.2),
                aggregator,
                histograms,
                options!,
                "gpt-4o-mini",
                testTime
            );

            Assert.False(result.IsPartial);
            Assert.Equal(15, result.CardsKept);
            Assert.NotNull(result.MetricsPath);
            Assert.True(File.Exists(result.MetricsPath));
            Assert.EndsWith("EnglishFlashcardMetrics-2026-07-22T18-45-32.417Z.json", result.MetricsPath);

            var jsonContent = File.ReadAllText(result.MetricsPath);
            using var doc = JsonDocument.Parse(jsonContent);
            Assert.True(doc.RootElement.TryGetProperty("TotalDurationSeconds", out var dur));

            Assert.True(File.Exists(summaryFile));
            var summaryText = File.ReadAllText(summaryFile);
            Assert.Contains("✅ English flashcards generated", summaryText);
            Assert.Contains("Days processed: 1", summaryText);
            Assert.Contains("Cards kept: 15", summaryText);
            Assert.Contains("Metrics: " + result.MetricsPath, summaryText);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Scenario_AllCardsRejected_with_skip_empty_metrics()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var metricsDir = Path.Combine(tempDir, "metrics");
            var summaryFile = Path.Combine(tempDir, "summary.txt");

            var args = new[]
            {
                "process",
                "--source", "notes.md",
                "--cards-out", "out/cards",
                "--source-notes-out", "out/source",
                "--metrics-out-dir", metricsDir,
                "--skip-empty-metrics",
                "--summary-out", summaryFile
            };

            Assert.True(ProcessOptionsParser.TryParse(args, out var options, out var error));

            var summary = new RunSummary(1, 1, 0, 0, Array.Empty<string>(), Array.Empty<string>());
            var aggregator = new Dictionary<string, double>
            {
                ["flashcards.cards.generated"] = 5,
                ["flashcards.cards.kept"] = 0
            };
            var histograms = new Dictionary<string, int>();

            var result = ProcessResultHandler.Process(
                summary,
                TimeSpan.FromSeconds(5.0),
                aggregator,
                histograms,
                options!,
                "gpt-4o-mini"
            );

            Assert.Equal(0, result.CardsKept);
            Assert.Null(result.MetricsPath);
            Assert.False(File.Exists(summaryFile));
            if (Directory.Exists(metricsDir))
            {
                Assert.Empty(Directory.GetFiles(metricsDir));
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Scenario_PartialSuccess_has_partial_suffix_and_warning_summary()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var metricsDir = Path.Combine(tempDir, "metrics");
            var summaryFile = Path.Combine(tempDir, "summary.txt");

            var args = new[]
            {
                "process",
                "--source", "notes.md",
                "--cards-out", "out/cards",
                "--source-notes-out", "out/source",
                "--metrics-out-dir", metricsDir,
                "--summary-out", summaryFile
            };

            Assert.True(ProcessOptionsParser.TryParse(args, out var options, out var error));

            var summary = new RunSummary(3, 2, 1, 31, new[] { "out/cards/day1.md" }, Array.Empty<string>());
            var aggregator = new Dictionary<string, double>
            {
                ["flashcards.tokens.prompt"] = 12000,
                ["flashcards.tokens.completion"] = 4000,
                ["flashcards.cards.kept"] = 31
            };
            var histograms = new Dictionary<string, int>();

            var testTime = new DateTime(2026, 7, 22, 18, 45, 32, 417, DateTimeKind.Utc);

            var result = ProcessResultHandler.Process(
                summary,
                TimeSpan.FromSeconds(418.7),
                aggregator,
                histograms,
                options!,
                "gpt-4o-mini",
                testTime
            );

            Assert.True(result.IsPartial);
            Assert.NotNull(result.MetricsPath);
            Assert.EndsWith("EnglishFlashcardMetrics-2026-07-22T18-45-32.417Z-partial.json", result.MetricsPath);

            Assert.True(File.Exists(summaryFile));
            var summaryText = File.ReadAllText(summaryFile);
            Assert.Contains("⚠ English flashcards partially generated", summaryText);
            Assert.Contains("Days succeeded: 2", summaryText);
            Assert.Contains("Days failed: 1", summaryText);
            Assert.Contains("Cards kept: 31", summaryText);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Scenario_BackwardCompatibility_old_metrics_out_writes_metrics_even_with_zero_cards()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var metricsFile = Path.Combine(tempDir, "metrics.json");

            var args = new[]
            {
                "process",
                "--source", "notes.md",
                "--cards-out", "out/cards",
                "--source-notes-out", "out/source",
                "--metrics-out", metricsFile
            };

            Assert.True(ProcessOptionsParser.TryParse(args, out var options, out var error));

            var summary = new RunSummary(1, 1, 0, 0, Array.Empty<string>(), Array.Empty<string>());
            var aggregator = new Dictionary<string, double>();
            var histograms = new Dictionary<string, int>();

            var result = ProcessResultHandler.Process(
                summary,
                TimeSpan.FromSeconds(1.0),
                aggregator,
                histograms,
                options!,
                "gpt-4o-mini"
            );

            Assert.NotNull(result.MetricsPath);
            Assert.Equal(Path.GetFullPath(metricsFile), result.MetricsPath);
            Assert.True(File.Exists(metricsFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Collision_handling_appends_suffix()
    {
        var targetDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);

        try
        {
            var baseName = "EnglishFlashcardMetrics-2026-07-22T18-45-32.417Z.json";
            File.WriteAllText(Path.Combine(targetDir, baseName), "{}");

            var finalPath = MetricsFileManager.DetermineFinalPath(targetDir, baseName);
            Assert.EndsWith("EnglishFlashcardMetrics-2026-07-22T18-45-32.417Z-1.json", finalPath);

            File.WriteAllText(finalPath, "{}");
            var finalPath2 = MetricsFileManager.DetermineFinalPath(targetDir, baseName);
            Assert.EndsWith("EnglishFlashcardMetrics-2026-07-22T18-45-32.417Z-2.json", finalPath2);
        }
        finally
        {
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
        }
    }

    [Fact]
    public void Scenario_FullFailure_zero_cards_no_metrics_or_summary()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var metricsDir = Path.Combine(tempDir, "metrics");
            var summaryFile = Path.Combine(tempDir, "summary.txt");

            var args = new[]
            {
                "process",
                "--source", "notes.md",
                "--cards-out", "out/cards",
                "--source-notes-out", "out/source",
                "--metrics-out-dir", metricsDir,
                "--skip-empty-metrics",
                "--summary-out", summaryFile
            };

            Assert.True(ProcessOptionsParser.TryParse(args, out var options, out var error));

            var summary = new RunSummary(1, 0, 1, 0, Array.Empty<string>(), new[] { "Fatal error" });
            var aggregator = new Dictionary<string, double>();
            var histograms = new Dictionary<string, int>();

            var result = ProcessResultHandler.Process(
                summary,
                TimeSpan.FromSeconds(2.0),
                aggregator,
                histograms,
                options!,
                "gpt-4o-mini"
            );

            Assert.Equal(0, result.CardsKept);
            Assert.Equal(1, result.DaysFailed);
            Assert.Null(result.MetricsPath);
            Assert.False(File.Exists(summaryFile));
            if (Directory.Exists(metricsDir))
            {
                Assert.Empty(Directory.GetFiles(metricsDir));
            }
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void OptionParser_rejects_identical_summary_and_metrics_paths()
    {
        var targetFile = Path.Combine(Path.GetTempPath(), "same_path.txt");

        var args = new[]
        {
            "process",
            "--source", "notes.md",
            "--cards-out", "out/cards",
            "--source-notes-out", "out/source",
            "--metrics-out", targetFile,
            "--summary-out", targetFile
        };

        var success = ProcessOptionsParser.TryParse(args, out var options, out var error);

        Assert.False(success);
        Assert.Null(options);
        Assert.Contains("cannot be identical", error);
    }

    [Fact]
    public void SummaryWriter_cleans_up_stale_file_when_cards_kept_is_zero()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "stale content from previous run");

        try
        {
            var args = new[]
            {
                "process",
                "--source", "notes.md",
                "--cards-out", "out/cards",
                "--source-notes-out", "out/source",
                "--summary-out", tempFile
            };

            Assert.True(ProcessOptionsParser.TryParse(args, out var options, out var error));

            var summary = new RunSummary(1, 1, 0, 0, Array.Empty<string>(), Array.Empty<string>());
            var aggregator = new Dictionary<string, double>();
            var histograms = new Dictionary<string, int>();

            ProcessResultHandler.Process(
                summary,
                TimeSpan.FromSeconds(1.0),
                aggregator,
                histograms,
                options!,
                "gpt-4o-mini"
            );

            Assert.False(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SummaryWriter_omits_metrics_line_when_metrics_disabled()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var summaryFile = Path.Combine(tempDir, "summary.txt");

            var args = new[]
            {
                "process",
                "--source", "notes.md",
                "--cards-out", "out/cards",
                "--source-notes-out", "out/source",
                "--summary-out", summaryFile
            };

            Assert.True(ProcessOptionsParser.TryParse(args, out var options, out var error));

            var summary = new RunSummary(1, 1, 0, 10, new[] { "out/cards/day1.md" }, Array.Empty<string>());
            var aggregator = new Dictionary<string, double> { ["flashcards.cards.kept"] = 10 };
            var histograms = new Dictionary<string, int>();

            var result = ProcessResultHandler.Process(
                summary,
                TimeSpan.FromSeconds(2.0),
                aggregator,
                histograms,
                options!,
                "gpt-4o-mini"
            );

            Assert.Null(result.MetricsPath);
            Assert.True(File.Exists(summaryFile));
            var summaryText = File.ReadAllText(summaryFile);
            Assert.Contains("Cards kept: 10", summaryText);
            Assert.DoesNotContain("Metrics:", summaryText);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}


