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
