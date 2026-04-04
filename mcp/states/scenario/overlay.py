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

    def to_markdown(self) -> str:
        """Convert the overlay state to a markdown string."""
        lines = []
        lines.append(f"## Overlay: {self.overlay.screen_type}\n")
        lines.append(self.overlay.message + "\n\n")
        return "".join(lines)
