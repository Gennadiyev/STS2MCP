from pydantic import BaseModel, model_validator


class PileCard(BaseModel):
    """The card in the player's pile (draw pile, discard pile, exhaust pile)."""

    name: str
    description: str
    cost: str
    star_cost: str | None = None

    def to_markdown(self) -> str:
        """Convert the pile card to a markdown string."""
        star_cost_str = f" + {self.star_cost} star" if self.star_cost else ""
        cost_str = f"{self.cost} energy{star_cost_str}"
        return f"**{self.name}** ({cost_str}) - {self.description}"


class PileCards(BaseModel):
    """The state of the player's pile cards."""

    pile_cards: dict[str, list[int | PileCard]]  # pile card markdown -> [count, pile card]

    def __sub__(self, old: "PileCards") -> "PileCards":
        """Calculate the difference between two pile card states."""
        diff_pile_cards = dict()

        all_pile_cards = set(self.pile_cards.keys()) | set(old.pile_cards.keys())

        for pile_card in all_pile_cards:
            new_pile_card_info = self.pile_cards.get(pile_card, [0, None])
            old_pile_card_info = old.pile_cards.get(pile_card, [0, None])
            # get pile card object and diff count
            pile_card_obj = new_pile_card_info[1] or old_pile_card_info[1]
            diff_count = new_pile_card_info[0] - old_pile_card_info[0]
            if diff_count != 0:
                diff_pile_cards[pile_card] = [diff_count, pile_card_obj]
        return PileCards(pile_cards=diff_pile_cards)

    def to_markdown(self, *, is_diff: bool = False) -> str:
        """Convert the pile cards to a markdown string."""
        if not self.pile_cards:
            return "No pile cards.\n"

        lines = []
        for (count, pile_card) in self.pile_cards.values():
            if is_diff:
                count_action = "Added" if count > 0 else "Removed"
                lines.append(f"- {count_action} {abs(count)}x {pile_card.to_markdown()}\n")
            else:
                lines.append(f"- {count}x {pile_card.to_markdown()}\n")
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
