from typing import Literal

from pydantic import BaseModel

from states.common.keyword import Keywords


class StatusEffect(BaseModel):
    """The status effect object."""

    id: str
    name: str
    amount: int | None = None
    type: Literal["Buff", "Debuff"]
    description: str
    keywords: Keywords

    def to_markdown(self) -> str:
        """Convert the status effect to a markdown string."""
        if self.amount == -1 or self.amount is None:
            amount_str = "indefinite"
        else:
            amount_str = str(self.amount)

        keyword_names_str = self.keywords.get_keyword_names_str()
        return f"**{self.name}** ({amount_str}){keyword_names_str} - {self.description}"
