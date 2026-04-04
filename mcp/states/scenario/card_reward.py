from typing import Literal

from pydantic import BaseModel

from states.common.card import RewardCard


class CardReward(BaseModel):
    """The card reward object."""

    cards: list[RewardCard]
    can_skip: bool

    def to_markdown(self) -> str:
        lines = ["## Card Reward Selection\n"]
        lines.append("Choose a card to add to your deck:\n\n")
        if self.cards:
            for card in self.cards:
                lines.append(f"- {card.to_markdown()}\n")
            lines.append("\n")
        lines.append(f"**Can skip:** {'Yes' if self.can_skip else 'No'}\n\n")
        return "".join(lines)


class CardRewardState(BaseModel):
    """The state when the scenario is card reward selection."""

    state_type: Literal["card_reward"]
    card_reward: CardReward

    def to_markdown(self) -> str:
        return self.card_reward.to_markdown()
