from typing import Literal

from pydantic import BaseModel

from states.common.status_effect import StatusEffect


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
    status: list[StatusEffect]
    intents: list[Intent]


class Battle(BaseModel):
    """The state of the battle."""

    round: int
    turn: Literal["player", "enemy"]
    is_play_phase: bool
    enemies: list[Enemy]

    # multiplayer fields
    all_players_ready: bool | None = None


class CombatState(BaseModel):
    """The state when the scenario is in the combat (monster or elite or boss)."""

    state_type: Literal["monster", "elite", "boss"]
    message: str | None = None
    battle: Battle | None = None
