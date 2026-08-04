from typing import Literal

from pydantic import BaseModel, ConfigDict


class RewardItem(BaseModel):
    """The reward item."""

    index: int
    type: str
    description: str

    # there are several types of reward items, each type has its own fields
    # therefore we allow extra fields for different types of reward items
    model_config = ConfigDict(extra="allow")


class Rewards(BaseModel):
    """The rewards after a combat or event."""

    items: list[RewardItem]
    can_proceed: bool


class RewardsState(BaseModel):
    """The state when the scenario is in the rewards screen after a combat or event."""

    state_type: Literal["rewards"]
    rewards: Rewards
