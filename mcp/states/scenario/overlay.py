from typing import Literal

from pydantic import BaseModel


class Overlay(BaseModel):
    """The information of the overlay."""

    screen_type: str
    message: str

    def to_markdown(self) -> str:
        """Convert the overlay information to markdown format."""
        return f"{self.message}\n\n**Screen Type**: {self.screen_type}"


class OverlayState(BaseModel):
    """The state when an unrecognized overlay is active."""

    state_type: Literal["overlay"] = "overlay"
    overlay: Overlay

    def to_markdown(self) -> str:
        """Convert the overlay state to markdown format."""
        return f"## Overlay State\n\n{self.overlay.to_markdown()}"
