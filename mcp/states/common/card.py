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


class HandCard(Card):
    """The card in the player's hand."""

    target_type: str
    can_play: bool
    unplayable_reason: str | None = None


class RewardCard(Card):
    """The card in the reward screen."""

    rarity: str

