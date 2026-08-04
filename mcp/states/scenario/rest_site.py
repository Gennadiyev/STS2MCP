from typing import Literal

from pydantic import BaseModel


class RestSiteOption(BaseModel):
    """The option at the rest site."""

    index: int
    id: str
    name: str
    description: str
    is_enabled: bool


class RestSite(BaseModel):
    """The rest site object."""

    options: list[RestSiteOption]
    can_proceed: bool


class RestSiteState(BaseModel):
    """The state when the scenario is at the rest site."""

    state_type: Literal["rest_site"]
    rest_site: RestSite
