from typing import Literal

from pydantic import BaseModel

from states.common.card import RewardCard

_SCREEN_LABELS = {
    "transform": "Transform",
    "upgrade": "Upgrade",
    "select": "Select",
    "simple_select": "Select",
}


class CardSelect(BaseModel):
    """The card select object."""

    screen_type: str
    prompt: str
    cards: list[RewardCard]
    preview_showing: bool
    can_confirm: bool
    can_cancel: bool

    def to_markdown(self) -> str:
        screen_label = _SCREEN_LABELS.get(self.screen_type, self.screen_type)
        lines = [f"## Card Selection: {screen_label}\n"]
        if self.prompt:
            lines.append(f"*{self.prompt}*\n")
        lines.append("\n")

        if self.cards:
            lines.append("### Cards\n")
            for card in self.cards:
                lines.append(f"- {card.to_markdown()}\n")
            lines.append("\n")

        if self.preview_showing:
            lines.append("**Preview is showing** - use `confirm_selection` to confirm or `cancel_selection` to go back.\n")
        else:
            lines.append(f"**Select cards** using `select_card(index)`. Can confirm: {'Yes' if self.can_confirm else 'No'} | Can cancel: {'Yes' if self.can_cancel else 'No'}\n")
        lines.append("\n")
        return "".join(lines)


class CardSelectState(BaseModel):
    """The state when the scenario is in the card selection screen."""

    state_type: Literal["card_select"]
    card_select: CardSelect

    def to_markdown(self) -> str:
        return self.card_select.to_markdown()
