namespace EnglishFlashcardGenerator;

public class FlashCard
{
    public required string Front { get; set; }
    public required string Back { get; set; }
    public bool IsReversed { get; set; }
}

public class FlashCardsResponse
{
    public required List<FlashCard> FlashCards { get; set; }
}