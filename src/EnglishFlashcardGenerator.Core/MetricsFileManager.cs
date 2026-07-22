using System.Globalization;
using System.Text;

namespace EnglishFlashcardGenerator.Core;

public static class MetricsFileManager
{
    public static string GenerateFileName(DateTime utcNow, bool isPartial)
    {
        var timestamp = utcNow.ToString("yyyy-MM-ddTHH-mm-ss.fffZ", CultureInfo.InvariantCulture);
        return isPartial
            ? $"EnglishFlashcardMetrics-{timestamp}-partial.json"
            : $"EnglishFlashcardMetrics-{timestamp}.json";
    }

    public static string DetermineFinalPath(string targetDir, string fileName)
    {
        var basePath = Path.Combine(targetDir, fileName);
        if (!File.Exists(basePath))
        {
            return Path.GetFullPath(basePath);
        }

        var isPartial = fileName.EndsWith("-partial.json", StringComparison.OrdinalIgnoreCase);
        var extension = isPartial ? "-partial.json" : ".json";
        var nameWithoutExt = fileName.Substring(0, fileName.Length - extension.Length);

        var suffix = 1;
        while (true)
        {
            var candidate = Path.Combine(targetDir, $"{nameWithoutExt}-{suffix}{extension}");
            if (!File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
            suffix++;
        }
    }

    public static string? WriteMetricsFile(
        ProcessOptions options,
        string metricsJson,
        int cardsKept,
        bool isPartial,
        DateTime utcNow)
    {
        if (options.SkipEmptyMetrics && cardsKept == 0)
        {
            return null;
        }

        string? finalPath = null;
        if (!string.IsNullOrWhiteSpace(options.MetricsOutDir))
        {
            Directory.CreateDirectory(options.MetricsOutDir);
            var fileName = GenerateFileName(utcNow, isPartial);
            finalPath = DetermineFinalPath(options.MetricsOutDir, fileName);
        }
        else if (!string.IsNullOrWhiteSpace(options.MetricsOut))
        {
            var dir = Path.GetDirectoryName(options.MetricsOut);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            finalPath = Path.GetFullPath(options.MetricsOut);
        }
        else
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(options.SummaryOut) &&
            Path.GetFullPath(options.SummaryOut).Equals(finalPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Error: Summary output path and metrics output path cannot be identical.");
        }

        var dirName = Path.GetDirectoryName(finalPath) ?? ".";
        var tempPath = Path.Combine(dirName, $".tmp_metrics_{Guid.NewGuid():N}.json");

        File.WriteAllText(tempPath, metricsJson, Encoding.UTF8);
        try
        {
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }

        return finalPath;
    }
}
