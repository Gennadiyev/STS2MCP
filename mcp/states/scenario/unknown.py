from typing import Literal

from pydantic import BaseModel, ConfigDict


class UnknownState(BaseModel):
    """The state when the scenario is unknown (not recognized)."""

    state_type: Literal["unknown"]

    # As the scenario is unknown, we allow any extra fields to be stored in the model.
    model_config = ConfigDict(extra="allow")

    def to_markdown(self) -> str:
        return ""
