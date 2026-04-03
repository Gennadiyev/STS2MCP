from pydantic import BaseModel


class CombatPlayerState(BaseModel):
    """The combat-specific state of the player."""

    energy: int
    max_energy: int
    stars: int
