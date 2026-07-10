using System.Text.RegularExpressions;

namespace EnglishFlashcardGenerator.Core.Markdown;

public static partial class MarkdownDaySplitter
{
    public static IReadOnlyList<DayChunk> Split(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var chunks = new List<DayChunk>();
        var start = -1;
        var heading = string.Empty;
        var index = 0;
        var inFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }

            if (!inFence && line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (start >= 0)
                {
                    AddChunk(chunks, index++, heading, string.Join('\n', lines[start..i]));
                }

                start = i;
                heading = line[3..].Trim();
            }
        }

        if (start >= 0)
        {
            AddChunk(chunks, index, heading, string.Join('\n', lines[start..]));
        }

        return chunks.Where(c => c.Date is not null).ToArray();
    }

    private static void AddChunk(List<DayChunk> chunks, int index, string heading, string markdown)
    {
        chunks.Add(new DayChunk(index, TryParseDate(heading), heading, markdown.TrimEnd()));
    }

    public static DateOnly? TryParseDate(string heading)
    {
        var candidates = DatePattern().Matches(heading).Select(m => m.Value);
        foreach (var candidate in candidates)
        {
            var parts = candidate.Contains('-') ? candidate.Split('-') : candidate.Split('.').Reverse().ToArray();
            if (parts.Length >= 3 && int.TryParse(parts[0], out var year) && int.TryParse(parts[1], out var month) && int.TryParse(parts[2], out var day))
            {
                return new DateOnly(year, month, day);
            }
        }

        return null;
    }

    [GeneratedRegex(@"(?:20\d{2}-\d{2}-\d{2})|(?:\d{2}\.\d{2}\.20\d{2})")]
    private static partial Regex DatePattern();
}
