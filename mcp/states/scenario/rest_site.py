from typing import Literal

from pydantic import BaseModel


class RestSiteOption(BaseModel):
    """The option at the rest site."""

    index: int
    id: str
    name: str
    description: str
    is_enabled: bool

    def to_markdown(self) -> str:
        enabled = "" if self.is_enabled else " (DISABLED)"
        return f"[{self.index}] **{self.name}**{enabled} - {self.description}"


class RestSite(BaseModel):
    """The rest site object."""

    options: list[RestSiteOption]
    can_proceed: bool

    def to_markdown(self) -> str:
        lines = []
        if self.options:
            lines.append("## Rest Site Options\n")
            for opt in self.options:
                lines.append(f"- {opt.to_markdown()}\n")
            lines.append("\n")
        lines.append(f"**Can proceed:** {'Yes' if self.can_proceed else 'No'}\n\n")
        return "".join(lines)


class RestSiteState(BaseModel):
    """The state when the scenario is at the rest site."""

    state_type: Literal["rest_site"]
    rest_site: RestSite

    def to_markdown(self) -> str:
        return self.rest_site.to_markdown()
