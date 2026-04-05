from typing import Literal

from pydantic import BaseModel

from states.common.card import RewardCard


class CardSelect(BaseModel):
    """The card select object."""

    screen_type: str
    prompt: str
    cards: list[RewardCard]
    preview_showing: bool
    can_confirm: bool
    can_cancel: bool


class CardSelectState(BaseModel):
    """The state when the scenario is in the card selection screen."""

    state_type: Literal["card_select"]
    card_select: CardSelect
