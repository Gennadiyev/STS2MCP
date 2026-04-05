from pydantic import BaseModel


class PlayerSummary(BaseModel):
    """The summary of a player in multiplayer mode."""

    character: str
    hp: int
    max_hp: int
    gold: int
    is_local: bool
    is_alive: bool
    is_ready_to_end_turn: bool | None = None


class MultiplayerState(BaseModel):
    """The state of the multiplayer game."""

    net_type: str
    player_count: int
    local_player_slot: int
    players: list[PlayerSummary]


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

