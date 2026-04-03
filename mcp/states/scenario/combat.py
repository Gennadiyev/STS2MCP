from typing import Literal

from pydantic import BaseModel


class Intent(BaseModel):
    """The intent of the enemy."""

    type: str
    label: str
    title: str
    description: str


class Enemy(BaseModel):
    """The enemy object."""

    entity_id: str
    combat_id: int
    name: str
    hp: int
    max_hp: int
    block: int
    status: 
    intents: list[Intent]


class BattleState(BaseModel):
    """The state of the battle."""

    round: int
    turn: Literal["player", "enemy"]
    is_play_phase: bool
    enemies: list[Enemy]


class CombatState(BaseModel):
    """The state when the scenario is in the combat (monster or elite or boss)."""

    state_type: Literal["monster", "elite", "boss"]
    battle: BattleState
