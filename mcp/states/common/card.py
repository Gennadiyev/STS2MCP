from pydantic import BaseModel, model_validator

from states.common.keyword import Keywords


class Card(BaseModel):
    """The card object."""

    index: int
    id: str
    name: str
    type: str
    cost: str  # can be int or "X" (variable cost)
    star_cost: int | None = None
    description: str
    is_upgraded: bool
    keywords: Keywords

    def to_markdown(self) -> str:
        """Convert the card to a markdown string."""
        pass


class HandCard(Card):
    """The card in the player's hand."""

    def to_markdown(self) -> str:
        """Convert the hand card to a markdown string."""
        pass
