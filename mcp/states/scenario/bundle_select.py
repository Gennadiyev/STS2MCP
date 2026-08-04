from typing import Literal

from pydantic import BaseModel

from states.common.card import RewardCard


class Bundle(BaseModel):
    """The bundle object."""

    index: int
    card_count: int
    cards: list[RewardCard]


class BundleSelect(BaseModel):
    """The bundle select object."""

    screen_type: str
    prompt: str
    bundles: list[Bundle]
    preview_showing: bool
    preview_cards: list[RewardCard]
    can_confirm: bool
    can_cancel: bool


class BundleSelectState(BaseModel):
    """The state when the scenario is in the bundle selection screen."""

    state_type: Literal["bundle_select"]
    bundle_select: BundleSelect
