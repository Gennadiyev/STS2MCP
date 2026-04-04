from typing import Literal

from pydantic import BaseModel, ConfigDict


class RewardItem(BaseModel):
    """The reward item."""

    index: int
    type: str
    description: str

    # there are several types of reward items, each type has its own fields
    # therefore we allow extra fields for different types of reward items
    model_config = ConfigDict(extra="allow")

    def to_markdown(self) -> str:
        extra = ""
        # Access extra fields via model_extra
        extras = self.model_extra or {}
        if "gold_amount" in extras and extras["gold_amount"] is not None:
            extra = f" ({extras['gold_amount']} gold)"
        elif "potion_description" in extras and extras["potion_description"] is not None:
            extra = f" - {extras['potion_description']}"
        elif "potion_name" in extras and extras["potion_name"] is not None:
            extra = f" ({extras['potion_name']})"
        return f"[{self.index}] **{self.type}**: {self.description}{extra}"


class Rewards(BaseModel):
    """The rewards after a combat or event."""

    items: list[RewardItem]
    can_proceed: bool

    def to_markdown(self) -> str:
        lines = ["## Rewards\n"]
        if self.items:
            for item in self.items:
                lines.append(f"- {item.to_markdown()}\n")
        else:
            lines.append("No rewards available.\n")
        lines.append(f"\n**Can proceed:** {'Yes' if self.can_proceed else 'No'}\n\n")
        return "".join(lines)


class RewardsState(BaseModel):
    """The state when the scenario is in the rewards screen after a combat or event."""

    state_type: Literal["rewards"]
    rewards: Rewards

    def to_markdown(self) -> str:
        return self.rewards.to_markdown()
