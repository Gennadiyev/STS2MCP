from typing import Literal

from pydantic import BaseModel

from states.common.keyword import Keywords


class EventOption(BaseModel):
    """The option in the event."""

    index: int
    title: str
    description: str
    is_locked: bool
    is_proceed: bool
    was_chosen: bool
    relic_name: str | None = None  # only if option has a relic
    relic_description: str | None = None  # only if option has a relic
    keywords: Keywords


class Event(BaseModel):
    """The event object."""

    event_id: str
    event_name: str
    is_ancient: bool
    in_dialogue: bool
    body: str
    options: list[EventOption]


class EventState(BaseModel):
    """The state when the scenario is in an event."""

    state_type: Literal["event"]
    event: Event
