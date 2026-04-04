from pydantic import BaseModel

from states.common.card import HandCard
from states.player.pile_card import PileCards
from states.player.orb import Orb


class CombatPlayerState(BaseModel):
    """The combat-specific state of the player."""

    energy: int
    max_energy: int
    stars: int
    # cards
    hand: list[HandCard]
    # piles
    draw_pile_count: int
    discard_pile_count: int
    exhaust_pile_count: int
    draw_pile: PileCards
    discard_pile: PileCards
    exhaust_pile: PileCards
    # orbs
    orbs: list[Orb]
    orb_slots: int
    orb_empty_slots: int

    def hand_to_markdown(self) -> str:
        """Convert the hand to a markdown string."""
        if not self.hand:
            return ""
        lines = ["### Hand\n"]
        for card in self.hand:
            lines.append(f"- {card.to_markdown()}\n")
        lines.append("\n")
        return "".join(lines)

    def piles_to_markdown(self) -> str:
        """Convert deck piles to markdown."""
        lines = ["### Deck Information\n\n"]
        for pile, count, label, suffix in [
            (self.draw_pile, self.draw_pile_count, "Draw Pile", " in random order"),
            (self.discard_pile, self.discard_pile_count, "Discard Pile", ""),
            (self.exhaust_pile, self.exhaust_pile_count, "Exhaust Pile", ""),
        ]:
            comma = "," if suffix else ""
            lines.append(f"#### {label} ({count} cards{comma}{suffix})\n")
            lines.append(pile.to_markdown())
            lines.append("\n")
        return "".join(lines)

    def orbs_to_markdown(self) -> str:
        """Convert orbs to markdown."""
        if not self.orbs:
            return ""
        lines = [f"### Orbs ({len(self.orbs)}/{self.orb_slots} slots)\n"]
        for orb in self.orbs:
            lines.append(f"- {orb.to_markdown()}\n")
        if self.orb_empty_slots > 0:
            lines.append(f"- *{self.orb_empty_slots} empty slot(s)*\n")
        lines.append("\n")
        return "".join(lines)
