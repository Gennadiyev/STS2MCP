from typing import Literal

from pydantic import BaseModel, Field, model_validator

from states.common.multiplayer import MultiplayerState
from states.player import PlayerState
from states.scenario import ScenarioState


class RunState(BaseModel):
    """The state of the current run."""

    act: int
    floor: int
    ascension: int

    def to_markdown(self) -> str:
        """Convert the run state to a markdown string."""
        return f"**Act {self.act}** | Floor {self.floor} | Ascension {self.ascension}\n"


class GameState(BaseModel):
    """The state of the game."""

    # gamemode
    game_mode: Literal["singleplayer", "multiplayer"]

    # multiplayer fields
    multiplayer_state: MultiplayerState | None = None

    # common fields
    run: RunState | None = None
    player: PlayerState | None = None

    # scenario-specific fields
    scenario_state: ScenarioState = Field(discriminator="state_type")

    def __sub__(self, old: "GameState") -> "GameState":
        """Calculate the difference between two game states."""
        pass

    def to_markdown(self, *, is_diff: bool = False) -> str:
        """Convert the game state to a markdown string."""
        lines = [f"# {self.game_mode.capitalize()} Game State: {self.scenario_state.state_type}\n\n"]
        # run state
        if self.run:
            lines.append(f"{self.run.to_markdown()}\n")
        # multiplayer state
        if self.multiplayer_state:
            lines.append(f"## Party\n\n{self.multiplayer_state.to_markdown()}\n")
        # player state
        # scenario-specific state
        # TODO

    @model_validator(mode="before")
    @classmethod
    def from_json_state(cls, json_state: dict) -> dict:
        """Create a GameState instance from a JSON state."""
        game_state = dict()
        multiplayer_state = dict()
        scenario_state = dict()

        # get game mode
        game_mode = json_state.get("game_mode", "singleplayer")
        game_state["game_mode"] = game_mode

        # parse scenario-specific fields
        for key in json_state:
            if key in ["run", "player"]:
                game_state[key] = json_state[key]
            elif key in ["net_type", "player_count", "local_player_slot", "players"]:
                multiplayer_state[key] = json_state[key]
            else:
                scenario_state[key] = json_state[key]
        game_state["scenario_state"] = scenario_state
        game_state["multiplayer_state"] = multiplayer_state if multiplayer_state else None
        return game_state
