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

    def to_markdown(self) -> str:
        return f"**{self.item_type}** at ({self.x}, {self.y}) size {self.width}x{self.height}"


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

    def to_markdown(self) -> str:
        lines = ["## Crystal Sphere\n"]
        lines.append(f"**{self.instructions_title}**\n")
        lines.append(f"{self.instructions_description}\n\n")
        lines.append(f"**Tool:** {self.tool} | **Divinations:** {self.divinations_left_text}\n\n")

        if self.clickable_cells:
            lines.append("### Clickable Cells\n")
            for cell in self.clickable_cells:
                lines.append(f"- ({cell.x}, {cell.y})\n")
            lines.append("\n")

        if self.revealed_items:
            lines.append("### Revealed Items\n")
            for item in self.revealed_items:
                lines.append(f"- {item.to_markdown()}\n")
            lines.append("\n")

        if self.can_proceed:
            lines.append("Use `crystal_sphere_proceed()` to continue.\n")
        else:
            lines.append("Use `crystal_sphere_set_tool(tool)` with `big` or `small`, then `crystal_sphere_click_cell(x, y)`.\n")
        lines.append("\n")
        return "".join(lines)


class CrystalSphereState(BaseModel):
    """The state when the scenario is in the crystal sphere event."""

    state_type: Literal["crystal_sphere"]
    crystal_sphere: CrystalSphere

    def to_markdown(self) -> str:
        return self.crystal_sphere.to_markdown()
