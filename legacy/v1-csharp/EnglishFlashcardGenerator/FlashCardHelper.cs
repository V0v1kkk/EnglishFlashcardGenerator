namespace EnglishFlashcardGenerator;

using System.Text.Json;

public static class FlashCardHelper
{
    public static List<FlashCard> DeserializeFlashCards(this string json)
    {
        return JsonSerializer.Deserialize<FlashCardsResponse>(json)!?.FlashCards!;
    }

    public static string FormatFlashCard(FlashCard card)
    {
        return card.IsReversed ? $"{card.Front}\n??\n{card.Back}\n" : $"{card.Front}\n?\n{card.Back}\n";
    }

    public static string FormatFlashCards(this List<FlashCard> cards)
    {
        var formated = cards.Select(FormatFlashCard);
        return string.Join('\n', formated);
    }
}