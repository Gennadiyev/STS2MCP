from typing import Literal

from pydantic import BaseModel


class Coordinate(BaseModel):
    """The coordinate of the crystal sphere."""

    x: int
    y: int


class Cell(Coordinate):
    """The cell in the crystal sphere."""

    coordinate: Coordinate
    is_hidden: bool
    is_clickable: bool
    is_highlighted: bool
    is_hovered: bool
    item_type: str | None = None  # only on revealed cells
    is_good: bool | None = None  # only on revealed cells


class RevealedItem(Coordinate):
    """The revealed item in the crystal sphere."""

    item_type: str
    width: int
    height: int
    is_good: bool


class CrystalSphere(BaseModel):
    """The crystal sphere event state."""

    instructions_title: str
    instructions_description: str
    grid_width: int
    grid_height: int
    cells: list[Cell]
    clickable_cells: list[Coordinate]
    revealed_items: list[RevealedItem]
    tool: str  # big, small, or none
    can_use_big_tool: bool
    can_use_small_tool: bool
    divinations_left_text: str
    can_proceed: bool


class CrystalSphereState(BaseModel):
    """The state when the scenario is in the crystal sphere event."""

    state_type: Literal["crystal_sphere"]
    crystal_sphere: CrystalSphere
