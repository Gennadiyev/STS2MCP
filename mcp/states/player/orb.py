from pydantic import BaseModel

from states.common.keyword import Keywords


class Orb(BaseModel):
    """The orb object."""

    id: str
    name: str
    description: str
    passive_val: int
    evoke_val: int
    keywords: Keywords
