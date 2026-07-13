namespace EnglishFlashcardGenerator;

public static class NoteTools
{
    /// <summary>
    /// Parses a date from the given input string.
    /// </summary>
    /// <param name="input">The input string containing the date to parse. The input can be in various formats:
    /// <list type="bullet">
    /// <item><description>"[[yyyy-MM-dd-DayOfWeek|dd.MM.yyyy]]"</description></item>
    /// <item><description>"[[yyyy-MM-dd-DayOfWeek]]"</description></item>
    /// <item><description>"dd.MM.yyyy"</description></item>
    /// </list>
    /// </param>
    /// <returns>The parsed <see cref="DateTime"/> object.</returns>
    /// <exception cref="FormatException">Thrown when the input string is not in a valid date format.</exception>
    public static DateTime ParseDate(string input)
    {
        // Remove leading '#' characters and whitespace
        input = input.TrimStart('#', ' ').Trim();

        if (input.StartsWith("[[") && input.EndsWith("]]"))
        {
            var content = input.Substring(2, input.Length - 4); // Remove "[[" and "]]"

            if (content.Contains("|"))
            {
                // Split the content on '|'
                var parts = content.Split('|');
                var dateStr1 = parts[0]; // "yyyy-MM-dd-DayOfWeek"
                var dateStr2 = parts[1]; // "dd.MM.yyyy"

                // Extract "yyyy-MM-dd" from dateStr1
                var datePart1Tokens = dateStr1.Split('-');
                if (datePart1Tokens.Length >= 3)
                {
                    var datePart1 = string.Join("-", datePart1Tokens[0], datePart1Tokens[1], datePart1Tokens[2]);
                    if (DateTime.TryParseExact(datePart1, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime date))
                    {
                        return date;
                    }
                }

                // Try parsing dateStr2
                if (DateTime.TryParseExact(dateStr2, "dd.MM.yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime date2))
                {
                    return date2;
                }

                throw new FormatException("Invalid date format in input with '|'");
            }
            else
            {
                // Content is "yyyy-MM-dd-DayOfWeek"
                var datePartTokens = content.Split('-');
                if (datePartTokens.Length >= 3)
                {
                    var datePart = string.Join("-", datePartTokens[0], datePartTokens[1], datePartTokens[2]);
                    if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime date))
                    {
                        return date;
                    }
                }
                throw new FormatException("Invalid date format in input without '|'");
            }
        }
        else
        {
            // Try parsing as "dd.MM.yyyy"
            if (DateTime.TryParseExact(input, "dd.MM.yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime date))
            {
                return date;
            }
            else
            {
                throw new FormatException("Invalid date format in simple input");
            }
        }
    }
    
}