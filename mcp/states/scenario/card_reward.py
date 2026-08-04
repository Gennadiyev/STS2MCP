from typing import Literal

from pydantic import BaseModel

from states.common.card import RewardCard


class CardReward(BaseModel):
    """The card reward object."""

    cards: list[RewardCard]
    can_skip: bool

    def to_markdown(self) -> str:
        """Convert the card reward information to markdown format."""
        if not self.cards:
            return "No card rewards available.\n"

        lines = []
        for card in self.cards:
            lines.append(f"- {card.to_markdown()}\n")
        lines.append("\n")

        can_skip_str = "Yes" if self.can_skip else "No"
        lines.append(f"**Can Skip**: {can_skip_str}\n")
        return "".join(lines)


class CardRewardState(BaseModel):
    """The state when the scenario is card reward selection."""

    state_type: Literal["card_reward"]
    card_reward: CardReward

    def to_markdown(self) -> str:
        """Convert the state to markdown format."""
        base_str = "## Card Reward Selection State\n\nChoose a card to add to your deck:"
        return f"{base_str}\n\n{self.card_reward.to_markdown()}"
