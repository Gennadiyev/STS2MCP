from typing import Literal

from pydantic import BaseModel, model_validator

from states.common.keyword import Keywords


class StatusEffect(BaseModel):
    """The status effect object."""

    id: str
    name: str
    amount: int
    type: Literal["Buff", "Debuff"]
    description: str
    keywords: Keywords

    def to_markdown(self) -> str:
        """Convert the status effect to a markdown string."""
        amount_str = "indefinite" if self.amount == -1 else str(self.amount)
        return f"**{self.name}** ({amount_str}): {self.description}"
