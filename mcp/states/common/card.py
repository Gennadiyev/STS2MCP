from pydantic import BaseModel, model_validator

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

    def _star_cost_str(self) -> str:
        return f" + {self.star_cost} star" if self.star_cost is not None else ""

    def _keywords_str(self) -> str:
        kw_names = list(self.keywords.keywords.keys())
        return f" [{', '.join(kw_names)}]" if kw_names else ""

    def to_markdown(self) -> str:
        """Convert the card to a markdown string."""
        return f"[{self.index}] **{self.name}** ({self.cost} energy{self._star_cost_str()}) [{self.type}]{self._keywords_str()} - {self.description}"


class HandCard(Card):
    """The card in the player's hand."""

    target_type: str
    can_play: bool
    unplayable_reason: str | None = None

    def to_markdown(self) -> str:
        """Convert the hand card to a markdown string."""
        playable = "\u2713" if self.can_play else "\u2717"
        return f"[{self.index}] **{self.name}** ({self.cost} energy{self._star_cost_str()}) [{self.type}] {playable}{self._keywords_str()} - {self.description} (target: {self.target_type})"


class RewardCard(Card):
    """The card in the reward screen."""

    rarity: str

    def to_markdown(self) -> str:
        """Convert the reward card to a markdown string."""
        return f"[{self.index}] **{self.name}** ({self.cost} energy{self._star_cost_str()}) [{self.type}] {self.rarity}{self._keywords_str()} - {self.description}"
