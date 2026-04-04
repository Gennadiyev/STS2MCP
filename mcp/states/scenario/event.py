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

    def to_markdown(self) -> str:
        tag = ""
        if self.is_locked:
            tag = " (LOCKED)"
        elif self.was_chosen:
            tag = " (CHOSEN)"
        elif self.is_proceed:
            tag = " (PROCEED)"
        relic = f" [Relic: {self.relic_name}]" if self.relic_name is not None else ""
        return f"[{self.index}] **{self.title}**{tag}{relic} - {self.description}"


class Event(BaseModel):
    """The event object."""

    event_id: str
    event_name: str
    is_ancient: bool
    in_dialogue: bool
    body: str | None
    options: list[EventOption]

    def to_markdown(self) -> str:
        label = "Ancient" if self.is_ancient else "Event"
        lines = [f"## {label}: {self.event_name}\n\n"]

        if self.in_dialogue:
            lines.append("*Ancient dialogue in progress - use `advance_dialogue` to continue.*\n\n")
            return "".join(lines)

        if self.options:
            lines.append("### Options\n")
            for opt in self.options:
                lines.append(f"- {opt.to_markdown()}\n")
            lines.append("\n")
        else:
            lines.append("No options available.\n\n")
        return "".join(lines)


class EventState(BaseModel):
    """The state when the scenario is in an event."""

    state_type: Literal["event"]
    event: Event

    def to_markdown(self) -> str:
        return self.event.to_markdown()
