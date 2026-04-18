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

    def __sub__(self, old: "PlayerState") -> "PlayerState" | None:
        """Calculate the difference between two player states."""
        if self.combat_state is None or old.combat_state is None:
            # in this case, the diff state is meaningless
            # so we return None to indicate that there is no diff state
            return None
        else:
            data = self.model_dump()
            data["combat_state"] = self.combat_state - old.combat_state
            return PlayerState(**data)

    def _character_to_markdown(self) -> str:
        """Convert the player's character to a markdown string."""
        hp_block_str = f"HP: {self.hp}/{self.max_hp} | Block: {self.block}"
        gold_str = f" | Gold: {self.gold}"
        if self.combat_state is not None:
            energy_str = self.combat_state.energy_to_markdown()
        else:
            energy_str = ""
        return f"**{self.character}** - {hp_block_str}{energy_str}{gold_str}"

    def _status_to_markdown(self) -> str:
        """Convert the player's status effects to a markdown string."""
        if not self.status:
            return ""
        lines = ["### Status\n\n"]
        for status in self.status:
            lines.append(f"- {status.to_markdown()}\n")
        lines.append("\n")
        return "".join(lines)

    def _relics_to_markdown(self) -> str:
        """Convert the player's relics to a markdown string."""
        return f"### Relics\n\n{self.relics.to_markdown()}\n"

    def _potions_to_markdown(self) -> str:
        """Convert the player's potions to a markdown string."""
        if not self.potions:
            return ""
        lines = ["### Potions\n\n"]
        for potion in self.potions:
            lines.append(f"- {potion.to_markdown()}\n")
        lines.append("\n")
        return "".join(lines)

    def to_markdown(self, *, is_diff: bool = False) -> str:
        """Convert the player state to a markdown string."""
        lines = ["## Player (YOU)\n\n"]
        lines.append(f"{self._character_to_markdown()}\n\n")
        lines.append(self._status_to_markdown())
        lines.append(self._relics_to_markdown())
        lines.append(self._potions_to_markdown())
        if self.combat_state is not None:
            lines.append(self.combat_state.to_markdown(is_diff=is_diff))
        return "".join(lines)

    @model_validator(mode="before")
    @classmethod
    def from_json_state(cls, json_state: dict) -> dict:
        """Create a player state from a JSON state dict."""
        player_state = dict()
        combat_state = dict()

        for key, value in json_state.items():
            if key in ["character", "hp", "max_hp", "block", "gold", "status", "relics", "potions"]:
                player_state[key] = value
            else:
                combat_state[key] = value

        player_state["combat_state"] = combat_state if combat_state else None
        return player_state
