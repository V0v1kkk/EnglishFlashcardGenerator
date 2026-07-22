using System.Globalization;
using System.Text;

namespace EnglishFlashcardGenerator.Core;

public static class SummaryWriter
{
    public static string FormatSummary(ProcessRunResult result)
    {
        var sb = new StringBuilder();

        if (result.IsPartial)
        {
            sb.AppendLine("⚠ English flashcards partially generated");
            sb.AppendLine();
            sb.AppendLine($"Days succeeded: {result.DaysSucceeded}");
            sb.AppendLine($"Days failed: {result.DaysFailed}");
        }
        else
        {
            sb.AppendLine("✅ English flashcards generated");
            sb.AppendLine();
            sb.AppendLine($"Days processed: {result.DaysSelected}");
        }

        sb.AppendLine($"Cards kept: {result.CardsKept}");
        sb.AppendLine($"Duration: {result.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
        sb.AppendLine($"Prompt tokens: {result.PromptTokens}");
        sb.AppendLine($"Completion tokens: {result.CompletionTokens}");

        if (!string.IsNullOrWhiteSpace(result.MetricsPath))
        {
            sb.AppendLine($"Metrics: {result.MetricsPath}");
        }

        return sb.ToString().TrimEnd();
    }

    public static void HandleSummary(string summaryOutPath, ProcessRunResult result)
    {
        var fullPath = Path.GetFullPath(summaryOutPath);

        if (result.CardsKept == 0)
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            return;
        }

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var content = FormatSummary(result);

        var parentDir = dir ?? ".";
        var tempPath = Path.Combine(parentDir, $".tmp_summary_{Guid.NewGuid():N}.txt");

        File.WriteAllText(tempPath, content, Encoding.UTF8);
        try
        {
            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }
    }
}
