from typing import Literal

from pydantic import BaseModel, Field, model_validator

from states.common.keyword import collect_keywords
from states.player import PlayerState
from states.scenario import ScenarioState
from states.scenario.combat import CombatState
from states.scenario.hand_select import HandSelectState
from states.scenario.menu import MenuState


class RunState(BaseModel):
    """The state of the current run."""

    act: int
    floor: int
    ascension: int

    def to_markdown(self) -> str:
        """Convert the run state to a markdown string."""
        return f"**Act {self.act}** | Floor {self.floor} | Ascension {self.ascension}"


class GameState(BaseModel):
    """The state of the game."""

    # gamemode
    game_mode: Literal["singleplayer", "multiplayer"]

    # common fields
    run: RunState | None = None
    player: PlayerState | None = None

    # scenario-specific fields
    scenario_state: ScenarioState = Field(discriminator="state_type")

    def _is_combat_scenario(self) -> bool:
        """Check if the current scenario involves combat (battle state present)."""
        return isinstance(self.scenario_state, (CombatState, HandSelectState))

    def to_markdown(self) -> str:
        """Convert the game state to a markdown string."""
        lines = []

        # Header
        lines.append(f"# Game State: {self.scenario_state.state_type}\n\n")

        # Run info
        if self.run is not None:
            lines.append(self.run.to_markdown() + "\n\n")

        # Message short-circuit (MenuState)
        if isinstance(self.scenario_state, MenuState):
            lines.append(self.scenario_state.to_markdown())
            return "".join(lines)

        # Player state
        has_battle = self._is_combat_scenario()
        if self.player is not None:
            if has_battle:
                lines.append(self.player.to_markdown_combat())
            else:
                lines.append(self.player.to_markdown_non_combat())

        # Scenario state
        lines.append(self.scenario_state.to_markdown())

        # Keyword glossary
        keywords = collect_keywords(self)
        lines.append(keywords.to_markdown())

        return "".join(lines)

    @model_validator(mode="before")
    @classmethod
    def from_json_state(cls, json_state: dict) -> dict:
        """Create a GameState instance from a JSON state."""
        game_state = dict()
        scenario_state = dict()

        # get game mode
        game_mode = json_state.get("game_mode", "singleplayer")
        game_state["game_mode"] = game_mode

        # parse scenario-specific fields
        for key in json_state:
            if key in ["run", "player"]:
                game_state[key] = json_state[key]
            else:
                scenario_state[key] = json_state[key]
        game_state["scenario_state"] = scenario_state
        return game_state
