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
        val_str = f"Passive: {self.passive_val}, Evoke: {self.evoke_val}"
        keyword_names_str = self.keywords.get_keyword_names_str()
        return f"**{self.name}** ({val_str}){keyword_names_str} - {self.description}"
