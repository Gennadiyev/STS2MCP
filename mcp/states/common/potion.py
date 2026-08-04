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

    def to_markdown(self) -> str:
        """Convert the potion to markdown string."""
        keyword_names_str = self.keywords.get_keyword_names_str()
        target_str = f"(target: {self.target_type})"
        return f"[{self.slot}] **{self.name}**{keyword_names_str} - {self.description} {target_str}"
