from pydantic import BaseModel, model_validator

from states.common.keyword import Keywords


class Relic(BaseModel):
    """The relic object."""

    id: str
    name: str
    description: str
    counter: int | None = None  # number if relic shows a counter, null otherwise
    keywords: Keywords

    def _get_base_markdown_str(self) -> str:
        """Get the base markdown string for the relic, without description and keywords."""
        counter_str = f" [{self.counter}]" if self.counter is not None else ""
        return f"**{self.name}**{counter_str}"

    def to_markdown(self) -> str:
        """Convert the relic to a markdown string."""
        base_markdown_str = self._get_base_markdown_str()
        keyword_names_str = self.keywords.get_keyword_names_str()
        return f"{base_markdown_str}{keyword_names_str} - {self.description}"


class RewardRelic(Relic):
    """The relic in the reward screen."""

    index: int
    rarity: str

    def to_markdown(self) -> str:
        """Convert the reward relic to a markdown string."""
        base_markdown_str = self._get_base_markdown_str()
        keyword_names_str = self.keywords.get_keyword_names_str()
        return f"[{self.index}] {base_markdown_str} {self.rarity}{keyword_names_str} - {self.description}"


class Relics(BaseModel):
    """The state of the player's relics."""

    relics: dict[str, list[int | Relic]]  # relic markdown -> [count, relic]

    def to_markdown(self) -> str:
        """Convert the relics to a markdown string."""
        if not self.relics:
            return "No relics.\n"

        lines = []
        for (count, relic) in self.relics.values():
            lines.append(f"- {count}x {relic.to_markdown()}\n")
        return "".join(lines)

    @model_validator(mode="before")
    @classmethod
    def from_relic_list(cls, relic_list: list[dict]) -> dict:
        """Create a relics dict from a list of relic dicts."""
        relics = dict()
        for relic in relic_list:
            relic_obj = Relic.model_validate(relic)
            relic_markdown = relic_obj.to_markdown()
            if relic_markdown not in relics:
                relics[relic_markdown] = [0, relic_obj]
            else:
                relics[relic_markdown][0] += 1
        return {"relics": relics}
