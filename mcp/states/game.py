from pydantic import BaseModel, Field, model_validator

from states.player import PlayerState



class RunState(BaseModel):
    """The state of the current run."""

    act: int
    floor: int
    ascension: int


class GameState(BaseModel):
    """The state of the game."""

    # common fields
    run: RunState
    player: PlayerState

    # scenario-specific fields
    scenario_state: ScenarioState = Field(discriminator="state_type")