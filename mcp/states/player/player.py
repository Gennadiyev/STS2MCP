from pydantic import BaseModel, model_validator

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
    status: 
    relics: 
    potions: 
