from pydantic import BaseModel

from states.common.status_effect import StatusEffect


class Pet(BaseModel):
    """The pet object."""

    id: str
    name: str
    alive: bool | None = None
    hp: int | None = None
    max_hp: int | None = None
    block: int | None = None
    status: list[StatusEffect] | None = None

    def to_markdown(self, indent: int = 0) -> str:
        indent_str = " " * indent
        """Convert the pet to a markdown string."""
        if self.alive is None:
            alive_str = "Unknown"
        else:
            hp = self.hp or 0
            max_hp = self.max_hp or 0
            block = self.block or 0
            alive_str = f"HP: {hp}/{max_hp} | Block: {block}" if self.alive else "Dead"

        lines = [f"**{self.name}** - (`{self.id}`) - {alive_str}\n"]
        if self.status:
            lines.append("Status Effects:\n")
            for status_effect in self.status:
                lines.append(f"  - {status_effect.to_markdown()}\n")
        return indent_str.join(lines)
