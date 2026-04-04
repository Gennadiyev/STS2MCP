from typing import Literal

from pydantic import BaseModel

from states.common.card import RewardCard


class Bundle(BaseModel):
    """The bundle object."""

    index: int
    card_count: int
    cards: list[RewardCard]

    def to_markdown(self) -> str:
        lines = [f"[{self.index}] Bundle with {self.card_count} card(s)\n"]
        for card in self.cards:
            star_cost = f" + {card.star_cost} star" if card.star_cost is not None else ""
            lines.append(f"  {card.name} ({card.cost}{star_cost}) [{card.type}] {card.rarity}\n")
        return "".join(lines)


class BundleSelect(BaseModel):
    """The bundle select object."""

    screen_type: str
    prompt: str
    bundles: list[Bundle]
    preview_showing: bool
    preview_cards: list[RewardCard]
    can_confirm: bool
    can_cancel: bool

    def to_markdown(self) -> str:
        lines = ["## Bundle Selection\n"]
        if self.prompt:
            lines.append(f"*{self.prompt}*\n")
        lines.append("\n")

        if self.bundles:
            lines.append("### Bundles\n")
            for bundle in self.bundles:
                lines.append(f"- {bundle.to_markdown()}")
            lines.append("\n")

        if self.preview_showing:
            lines.append("**Preview is showing** - use `confirm_bundle_selection()` to confirm or `cancel_bundle_selection()` to go back.\n")
            if self.preview_cards:
                lines.append("### Preview Cards\n")
                for card in self.preview_cards:
                    star_cost = f" + {card.star_cost} star" if card.star_cost is not None else ""
                    lines.append(f"- **{card.name}** ({card.cost} energy{star_cost}) [{card.type}] {card.rarity} - {card.description}\n")
                lines.append("\n")
        else:
            lines.append(f"Use `select_bundle(index)` to open a bundle preview. Can confirm: {'Yes' if self.can_confirm else 'No'} | Can cancel: {'Yes' if self.can_cancel else 'No'}\n\n")
        return "".join(lines)


class BundleSelectState(BaseModel):
    """The state when the scenario is in the bundle selection screen."""

    state_type: Literal["bundle_select"]
    bundle_select: BundleSelect

    def to_markdown(self) -> str:
        return self.bundle_select.to_markdown()
