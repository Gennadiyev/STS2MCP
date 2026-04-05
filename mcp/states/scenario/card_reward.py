from typing import Literal

from pydantic import BaseModel

from states.common.card import RewardCard


class CardReward(BaseModel):
    """The card reward object."""

    cards: list[RewardCard]
    can_skip: bool


class CardRewardState(BaseModel):
    """The state when the scenario is card reward selection."""

    state_type: Literal["card_reward"]
    card_reward: CardReward
