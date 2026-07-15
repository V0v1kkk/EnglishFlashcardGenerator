using System.Text.RegularExpressions;

namespace EnglishFlashcardGenerator.Core.Output;

public static partial class ObsidianSrFormatter
{
    public static string FormatCards(IEnumerable<Flashcard> cards) => string.Join("\n\n", cards.Select(FormatCard));

    public static string FormatCard(Flashcard card)
    {
        var front = CleanCardText(card.Front);
        var back = CleanCardText(card.Back);
        var separator = card.Direction == CardDirection.Bidirectional ? "??" : "?";
        var parts = new List<string> { front, separator, back };
        var example = CleanCardText(card.Example ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(example))
        {
            parts.Add($"*Example: {example}*");
        }

        return string.Join('\n', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    public static string CleanCardText(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n')
            .Select(line => SrComment().Replace(line, string.Empty).Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.Equals("?", StringComparison.Ordinal) && !line.Equals("??", StringComparison.Ordinal))
            .Where(line => !line.Contains("[!sr|card-metadata]", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.StartsWith("sr-", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Equals("#flashcards", StringComparison.OrdinalIgnoreCase) && !line.Equals("#review", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Replace("::", ":"));
        return string.Join('\n', lines).Trim();
    }

    [GeneratedRegex(@"<!--\s*SR:.*?-->", RegexOptions.IgnoreCase)]
    private static partial Regex SrComment();
}

public static class OutputPathBuilder
{
    public static DayWritePlan Build(DayOutputDraft draft)
    {
        var date = draft.Day.Date?.ToString("yyyy-MM-dd") ?? $"day-{draft.Day.DayIndex + 1}";
        var creationTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        
        var cardsPath = Path.Combine(draft.Options.CardsOutputDirectory, $"EnglishFlashcards-{date}.md");
        var sourcePath = Path.Combine(draft.Options.SourceExcerptOutputDirectory, $"EnglishLearningNote-{date}.md");

        var cardsMarkdown = $"""
---
tags:
  - english
  - english-flashcard
creationTime: {creationTime}
---
References: [[EnglishLearningNote-{date}]]

#flashcards/{date}

{ObsidianSrFormatter.FormatCards(draft.Cards)}
""";
        
        var lines = draft.DailySourceExcerptMarkdown.Split('\n');
        var body = lines.Length > 1 ? string.Join('\n', lines.Skip(1)).TrimStart() : string.Empty;

        var sourceMarkdown = $"""
---
tags:
  - english
  - english-learning-note
creationTime: {creationTime}
---
References: [[English Learning notes]], [[{draft.Day.Heading}]]

{body}
""";

        return new DayWritePlan(draft.Day, cardsPath, cardsMarkdown, draft.Cards.Count, sourcePath, sourceMarkdown, draft.Options.Apply, draft.Warnings);
    }
}

public static class AtomicMarkdownWriter
{
    public static DayResult Execute(DayWritePlan plan)
    {
        if (plan.Apply)
        {
            WriteAtomically(plan.CardsPath, plan.CardsMarkdown);
            WriteAtomically(plan.SourceExcerptPath, plan.SourceExcerptMarkdown);
        }

        return new DayResult(plan.Day, true, plan.CardsCount, [plan.CardsPath, plan.SourceExcerptPath], plan.Warnings);
    }

    private static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

}
