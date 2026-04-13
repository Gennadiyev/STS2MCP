from pydantic import BaseModel

from states.common.keyword import Keywords


class Card(BaseModel):
    """The card object."""

    index: int
    id: str
    name: str
    type: str
    cost: str  # can be int or "X" (variable cost)
    star_cost: str | None = None
    description: str
    is_upgraded: bool
    keywords: Keywords

    def _get_cost_str(self) -> str:
        """Get the cost of the card"""
        star_cost_str = f" + {self.star_cost} star" if self.star_cost else ""
        return f"{self.cost} energy{star_cost_str}"

    def _get_base_markdown_str(self) -> str:
        """Get the base markdown string for the card, without description and keywords."""
        cost_str = self._get_cost_str()
        return f"[{self.index}] **{self.name}** ({cost_str}) [{self.type}]"

    def to_markdown(self) -> str:
        """Convert the card to a markdown string."""
        base_markdown_str = self._get_base_markdown_str()
        keyword_names_str = self.keywords.get_keyword_names_str()
        return f"{base_markdown_str}{keyword_names_str} - {self.description}"


class HandCard(Card):
    """The card in the player's hand."""

    target_type: str
    can_play: bool
    unplayable_reason: str | None = None

    def to_markdown(self) -> str:
        """Convert the hand card to a markdown string."""
        if self.can_play:
            playability_str = "\u2713"  # check mark
        else:
            unplayable_reason_str = f" ({self.unplayable_reason})" if self.unplayable_reason else ""
            playability_str = f"\u2717{unplayable_reason_str}"  # cross mark

        target_str = f"(target: {self.target_type})"

        base_markdown_str = self._get_base_markdown_str()
        keyword_names_str = self.keywords.get_keyword_names_str()
        return f"{base_markdown_str} {playability_str}{keyword_names_str} - {self.description} {target_str}"


class RewardCard(Card):
    """The card in the reward screen."""

    rarity: str

    def to_markdown(self) -> str:
        """Convert the reward card to a markdown string."""
        base_markdown_str = self._get_base_markdown_str()
        keyword_names_str = self.keywords.get_keyword_names_str()
        return f"{base_markdown_str} {self.rarity}{keyword_names_str} - {self.description}"
