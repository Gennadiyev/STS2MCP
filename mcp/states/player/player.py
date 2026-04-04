from pydantic import BaseModel, model_validator

from states.common.potion import Potion
from states.common.relic import Relics
from states.common.status_effect import StatusEffect
from states.player.combat import CombatPlayerState


class PlayerState(BaseModel):
    """The state of the player."""

    # common fields
    character: str
    hp: int
    max_hp: int
    block: int
    gold: int

    # combat-only fields
    combat_state: CombatPlayerState | None = None

    # always present fields
    status: list[StatusEffect]
    relics: Relics
    potions: list[Potion]

    def _status_to_markdown(self, indent: str = "") -> str:
        if not self.status:
            return ""
        lines = ["### Status\n"]
        for s in self.status:
            lines.append(f"{indent}- {s.to_markdown()}\n")
        lines.append("\n")
        return "".join(lines)

    def _relics_to_markdown(self) -> str:
        if not self.relics.relics:
            return ""
        lines = ["### Relics\n"]
        lines.append(self.relics.to_markdown())
        lines.append("\n")
        return "".join(lines)

    def _potions_to_markdown(self) -> str:
        if not self.potions:
            return ""
        lines = ["### Potions\n"]
        for p in self.potions:
            lines.append(f"- {p.to_markdown()}\n")
        lines.append("\n")
        return "".join(lines)

    def to_markdown_non_combat(self) -> str:
        """Render player summary for non-combat scenarios (map, event, etc.)."""
        lines = ["## Player (You)\n"]
        stars = f" | Stars: {self.combat_state.stars}" if self.combat_state and self.combat_state.stars else ""
        lines.append(f"**{self.character}** - HP: {self.hp}/{self.max_hp} | Gold: {self.gold}{stars}\n\n")
        lines.append(self._relics_to_markdown())
        lines.append(self._potions_to_markdown())
        return "".join(lines)

    def to_markdown_combat(self) -> str:
        """Render player details for combat scenarios."""
        lines = ["## Player (You)\n"]
        cs = self.combat_state
        stars = f" | Stars: {cs.stars}" if cs and cs.stars else ""
        energy = f" | Energy: {cs.energy}/{cs.max_energy}" if cs else ""
        lines.append(f"**{self.character}** - HP: {self.hp}/{self.max_hp} | Block: {self.block}{energy}{stars} | Gold: {self.gold}\n\n")
        lines.append(self._status_to_markdown())
        lines.append(self._relics_to_markdown())
        lines.append(self._potions_to_markdown())
        if cs:
            lines.append(cs.hand_to_markdown())
            lines.append(cs.piles_to_markdown())
            lines.append(cs.orbs_to_markdown())
        return "".join(lines)
