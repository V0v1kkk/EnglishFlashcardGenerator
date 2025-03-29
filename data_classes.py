from pydantic import BaseModel, Field
from typing import List


class FlashCard(BaseModel):
    """
    Represents a single flashcard with front, back, and is_reversed properties.
    """
    front: str
    back: str
    is_reversed: bool = Field(default=False)


class FlashCardsResponse(BaseModel):
    """
    Contains a list of flashcards.
    """
    flash_cards: List[FlashCard] = Field(alias="FlashCards")

    class Config:
        populate_by_name = True
        allow_population_by_field_name = True

    def format(self) -> str:
        """Format the response for output"""
        return self.format_flash_cards()
    
    def format_flash_cards(self) -> str:
        """Format all flashcards"""
        formatted = [self.format_flash_card(card) for card in self.flash_cards]
        return "\n".join(formatted)
    
    @staticmethod
    def format_flash_card(card: FlashCard) -> str:
        """Format a single flashcard"""
        separator = "??" if card.is_reversed else "?"
        return f"{card.front}\n{separator}\n{card.back}\n"