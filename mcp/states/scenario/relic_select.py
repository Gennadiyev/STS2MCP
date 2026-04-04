from typing import Literal

from pydantic import BaseModel

from states.common.relic import RewardRelic


class RelicSelect(BaseModel):
    """The relic select object."""

    prompt: str
    relics: list[RewardRelic]
    can_skip: bool

    def to_markdown(self) -> str:
        lines = ["## Relic Selection\n"]
        if self.prompt:
            lines.append(f"*{self.prompt}*\n")
        lines.append("\n")

        if self.relics:
            for relic in self.relics:
                lines.append(f"- [{relic.index}] {relic.to_markdown()}\n")
            lines.append("\n")

        lines.append(f"Use `select_relic(index)` to choose. Can skip: {'Yes' if self.can_skip else 'No'}\n\n")
        return "".join(lines)


class RelicSelectState(BaseModel):
    """The state when the scenario is in the relic selection screen."""

    state_type: Literal["relic_select"]
    relic_select: RelicSelect

    def to_markdown(self) -> str:
        return self.relic_select.to_markdown()
