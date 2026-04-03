from pydantic import BaseModel


class PileCard(BaseModel):
    """The card in the player's pile (draw pile, discard pile, exhaust pile)."""

    name: str
    description: str

    def to_markdown(self) -> str:
        """Convert the pile card to a markdown string."""
        pass