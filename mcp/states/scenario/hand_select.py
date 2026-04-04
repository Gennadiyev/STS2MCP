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

    def to_markdown(self) -> str:
        lines = ["## In-Combat Card Selection\n"]
        lines.append(f"*{self.prompt}*\n")

        if self.mode == "upgrade_select":
            lines.append("**Mode:** Upgrade selection\n")
        lines.append("\n")

        if self.cards:
            lines.append("### Selectable Cards\n")
            for card in self.cards:
                star_cost = f" + {card.star_cost} star" if card.star_cost is not None else ""
                lines.append(f"- [{card.index}] **{card.name}** ({card.cost} energy{star_cost}) [{card.type}] - {card.description}\n")
            lines.append("\n")

        if self.selected_cards:
            lines.append("### Already Selected\n")
            for card in self.selected_cards:
                lines.append(f"- {card.name}\n")
            lines.append("\n")

        can_confirm_str = "Yes - use `combat_confirm_selection`" if self.can_confirm else "No - select more cards"
        lines.append(f"Use `combat_select_card(card_index)` to select. Can confirm: {can_confirm_str}\n\n")
        return "".join(lines)


class HandSelectState(CombatState):
    """The state when the scenario is in-combat card selection.

    As the full battle state is included for context, this class inherits from CombatState.
    """

    state_type: Literal["hand_select"]
    hand_select: HandSelect

    def to_markdown(self) -> str:
        lines = []
        # Render battle context first (from parent CombatState)
        lines.append(super().to_markdown())
        lines.append(self.hand_select.to_markdown())
        return "".join(lines)
