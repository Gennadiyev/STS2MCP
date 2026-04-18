from typing import Literal

from pydantic import BaseModel

from states.common.card import Card
from states.scenario.combat import CombatState


class SelectedCard(BaseModel):
    """The card selected by the player during hand selection."""

    index: int
    name: str


class HandSelect(BaseModel):
    """The state of the hand selection."""

    mode: str
    prompt: str
    cards: list[Card]
    selected_cards: list[SelectedCard] | None = None  # only present if cards has been selected
    can_confirm: bool


class HandSelectState(CombatState):
    """The state when the scenario is in-combat card selection.

    As the full battle state is included for context, this class inherits from CombatState.
    """

    state_type: Literal["hand_select"]
    hand_select: HandSelect
