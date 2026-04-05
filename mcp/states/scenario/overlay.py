from typing import Literal

from pydantic import BaseModel


class Overlay(BaseModel):
    """The information of the overlay."""

    screen_type: str
    message: str


class OverlayState(BaseModel):
    """The state when an unrecognized overlay is active."""

    state_type: Literal["overlay"] = "overlay"
    overlay: Overlay
