from pydantic import BaseModel, model_validator


class PileCard(BaseModel):
    """The card in the player's pile (draw pile, discard pile, exhaust pile)."""

    name: str
    description: str
    cost: str
    star_cost: str | None = None

    def to_markdown(self) -> str:
        """Convert the pile card to a markdown string."""
        star_cost = f" + {self.star_cost} star" if self.star_cost is not None else ""
        return f"{self.name} ({self.cost}{star_cost}): {self.description}"


class PileCards(BaseModel):
    """The state of the player's pile cards."""

    pile_cards: dict[str, list[int | PileCard]]  # pile card markdown -> [count, pile card]

    def __sub__(self, old_pile_cards: "PileCards") -> "PileCards":
        """Calculate the difference between two pile card states."""
        pass

    def to_markdown(self) -> str:
        """Convert the pile cards to a markdown string."""
        if not self.pile_cards:
            return "- *(empty)*\n"
        lines = []
        for markdown_key in self.pile_cards:
            lines.append(f"- {markdown_key}\n")
        return "".join(lines)

    @model_validator(mode="before")
    @classmethod
    def from_pile_card_list(cls, pile_card_list: list[dict]) -> dict:
        """Create a pile cards dict from a list of pile card dicts."""
        pile_cards = dict()
        for pile_card in pile_card_list:
            pile_card_obj = PileCard.model_validate(pile_card)
            pile_card_markdown = pile_card_obj.to_markdown()
            if pile_card_markdown not in pile_cards:
                pile_cards[pile_card_markdown] = [0, pile_card_obj]
            else:
                pile_cards[pile_card_markdown][0] += 1
        return {"pile_cards": pile_cards}
