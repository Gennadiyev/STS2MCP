from pydantic import BaseModel, model_validator

from states.common.keyword import Keywords


class Relic(BaseModel):
    """The relic object."""

    id: str
    name: str
    description: str
    counter: int | None = None  # number if relic shows a counter, null otherwise
    keywords: Keywords


class RewardRelic(Relic):
    """The relic in the reward screen."""

    index: int
    rarity: str


class Relics(BaseModel):
    """The state of the player's relics."""

    relics: dict[str, list[int | Relic]]  # relic markdown -> [count, relic]

    def __sub__(self, old_relics: "Relics") -> "Relics":
        """Calculate the difference between two relic states."""
        pass

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
