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

