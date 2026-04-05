from pydantic import BaseModel, model_validator


class PileCard(BaseModel):
    """The card in the player's pile (draw pile, discard pile, exhaust pile)."""

    name: str
    description: str
    cost: str | None = None
    star_cost: str | None = None


class PileCards(BaseModel):
    """The state of the player's pile cards."""

    pile_cards: dict[str, list[int | PileCard]]  # pile card markdown -> [count, pile card]

    def __sub__(self, old_pile_cards: "PileCards") -> "PileCards":
        """Calculate the difference between two pile card states."""
        pass

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
