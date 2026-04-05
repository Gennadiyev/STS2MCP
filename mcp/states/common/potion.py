from pydantic import BaseModel

from states.common.keyword import Keywords


class Potion(BaseModel):
    """The potion object."""

    id: str
    name: str
    description: str
    slot: int
    can_use_in_combat: bool
    target_type: str
    keywords: Keywords

