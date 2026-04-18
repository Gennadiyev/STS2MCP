from typing import Literal

from pydantic import BaseModel

from states.common.relic import RewardRelic


class RelicSelect(BaseModel):
    """The relic select object."""

    prompt: str
    relics: list[RewardRelic]
    can_skip: bool


class RelicSelectState(BaseModel):
    """The state when the scenario is in the relic selection screen."""

    state_type: Literal["relic_select"]
    relic_select: RelicSelect
