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

    def to_markdown(self) -> str:
        """Convert the orb to a markdown string."""
        desc = f" - {self.description}" if self.description else ""
        return f"**{self.name}** (passive: {self.passive_val}, evoke: {self.evoke_val}){desc}"
