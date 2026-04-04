from typing import Literal

from pydantic import BaseModel

from states.common.status_effect import StatusEffect


class Intent(BaseModel):
    """The intent of the enemy."""

    type: str
    label: str
    title: str
    description: str

    def to_markdown(self) -> str:
        title = self.title if self.title else self.type
        type_tag = f" ({self.type})"
        label = f" {self.label}" if self.label else ""
        desc = f" - {self.description}" if self.description else ""
        return f"{title}{type_tag}{label}{desc}"


class Enemy(BaseModel):
    """The enemy object."""

    entity_id: str
    combat_id: int
    name: str
    hp: int
    max_hp: int
    block: int
    status: list[StatusEffect]
    intents: list[Intent]

    def to_markdown(self) -> str:
        lines = [f"### {self.name} (`{self.entity_id}`)\n"]
        lines.append(f"HP: {self.hp}/{self.max_hp} | Block: {self.block}\n")
        if self.intents:
            intent_strs = [i.to_markdown() for i in self.intents]
            lines.append(f"**Intent:** {', '.join(intent_strs)}\n")
        if self.status:
            lines.append("### Status\n")
            for s in self.status:
                lines.append(f"  - {s.to_markdown()}\n")
        lines.append("\n")
        return "".join(lines)


class BattleState(BaseModel):
    """The state of the battle."""

    round: int
    turn: Literal["player", "enemy"]
    is_play_phase: bool
    enemies: list[Enemy]

    def to_markdown(self) -> str:
        lines = [f"**Round {self.round}** | Turn: {self.turn} | Play Phase: {self.is_play_phase}\n\n"]
        if self.enemies:
            lines.append("## Enemies\n")
            for enemy in self.enemies:
                lines.append(enemy.to_markdown())
        return "".join(lines)


class CombatState(BaseModel):
    """The state when the scenario is in the combat (monster or elite or boss)."""

    state_type: Literal["monster", "elite", "boss"]
    message: str | None = None
    battle: BattleState | None = None

    def to_markdown(self) -> str:
        if self.message is not None:
            return self.message + "\n"
        if self.battle is not None:
            return self.battle.to_markdown()
        return ""
