from typing import Literal

from pydantic import BaseModel

from states.common.relic import RewardRelic


class Treasure(BaseModel):
    """The treasure object."""

    message: str
    relics: list[RewardRelic]
    can_proceed: bool

    def to_markdown(self) -> str:
        lines = []
        if self.relics:
            lines.append("## Treasure Relics\n")
            for relic in self.relics:
                rarity = f" ({relic.rarity})" if relic.rarity else ""
                lines.append(f"- [{relic.index}] **{relic.name}**{rarity} - {relic.description}\n")
            lines.append("\n")
            lines.append("Use `treasure_claim_relic(relic_index)` to claim a relic.\n")
        else:
            lines.append("Chest is opening...\n")
        lines.append("\n")

        if self.can_proceed:
            lines.append("**Can proceed:** Yes\n")
        lines.append("\n")
        return "".join(lines)


class TreasureState(BaseModel):
    """The state when the scenario is in the treasure room."""

    state_type: Literal["treasure"]
    treasure: Treasure

    def to_markdown(self) -> str:
        return self.treasure.to_markdown()
