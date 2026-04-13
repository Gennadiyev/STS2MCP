from pydantic import BaseModel

from states.common.pet import Pet


class PlayerSummary(BaseModel):
    """The summary of a player in multiplayer mode."""

    character: str
    hp: int
    max_hp: int
    block: int
    gold: int
    is_local: bool
    is_alive: bool
    is_ready_to_end_turn: bool | None = None
    pets: list[Pet] | None = None

    def to_markdown(self) -> str:
        """Convert the player summary to a markdown string."""
        local_str = " **(YOU)**" if self.is_local else ""
        alive_str = " [DEAD]" if not self.is_alive else ""
        ready_str = " [READY]" if self.is_ready_to_end_turn else ""
        detail_str = f"HP: {self.hp}/{self.max_hp} | Block: {self.block} | Gold: {self.gold}"

        lines = [f"**{self.character}**{local_str}{alive_str}{ready_str} - {detail_str}\n"]
        if self.pets:
            lines.append("Pets:\n")
            for pet in self.pets:
                lines.append(f"  - {pet.to_markdown(indent=2)}\n")

        return "".join(lines)


class MultiplayerState(BaseModel):
    """The state of the multiplayer game."""

    net_type: str
    player_count: int
    local_player_slot: int
    players: list[PlayerSummary]

    def to_markdown(self) -> str:
        """Convert the multiplayer state to a markdown string."""
        lines = []
        for player in self.players:
            lines.append(f"- {player.to_markdown()}\n")
        return "".join(lines)


class BaseVote(BaseModel):
    """The base class for a vote in multiplayer mode."""

    player: str
    is_local: bool
    voted: bool


class MapVote(BaseVote):
    """A vote for the next map in multiplayer mode."""

    vote_col: int | None = None
    vote_row: int | None = None


class EventVote(BaseVote):
    """A vote for the next event in multiplayer mode."""

    vote_option_index: int | None = None


class TreasureVote(BaseVote):
    """A vote for the next treasure in multiplayer mode."""

    vote_relic_index: int | None = None

