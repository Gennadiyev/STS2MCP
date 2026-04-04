from typing import Literal

from pydantic import BaseModel


class MenuState(BaseModel):
    """The state when the scenario is in the menu."""

    state_type: Literal["menu"]
    message: str

    def to_markdown(self) -> str:
        return self.message + "\n"
