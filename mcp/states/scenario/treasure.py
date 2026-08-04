from typing import Literal

from pydantic import BaseModel

from states.common.multiplayer import TreasureVote
from states.common.relic import RewardRelic


class Treasure(BaseModel):
    """The treasure object."""

    message: str | None = None
    relics: list[RewardRelic] | None = None  # only present when loaded
    can_proceed: bool | None = None

    # multiplayer fields
    is_bidding_phase: bool | None = None
    bids: list[TreasureVote] | None = None
    all_bid: bool | None = None


class TreasureState(BaseModel):
    """The state when the scenario is in the treasure room."""

    state_type: Literal["treasure"]
    treasure: Treasure
