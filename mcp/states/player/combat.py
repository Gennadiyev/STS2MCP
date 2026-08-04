from pydantic import BaseModel

from states.common.card import HandCard
from states.common.pet import Pet
from states.player.orb import Orb
from states.player.pile_card import PileCards


class CombatPlayerState(BaseModel):
    """The combat-specific state of the player."""

    energy: int
    max_energy: int
    stars: int | None = None
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
    orbs: list[Orb] | None = None
    orb_slots: int | None = None
    orb_empty_slots: int | None = None
    # pets
    pets: list[Pet] | None = None

    def __sub__(self, old: "CombatPlayerState") -> "CombatPlayerState":
        """Calculate the difference between two combat states."""
        data = self.model_dump()
        # replace with diff
        data["draw_pile_count"] = self.draw_pile_count - old.draw_pile_count
        data["discard_pile_count"] = self.discard_pile_count - old.discard_pile_count
        data["exhaust_pile_count"] = self.exhaust_pile_count - old.exhaust_pile_count
        data["draw_pile"] = self.draw_pile - old.draw_pile
        data["discard_pile"] = self.discard_pile - old.discard_pile
        data["exhaust_pile"] = self.exhaust_pile - old.exhaust_pile
        return CombatPlayerState(**data)

    def energy_to_markdown(self) -> str:
        """Convert the player's energy to a markdown string."""
        star_str = f" | Stars: {self.stars}" if self.stars is not None else ""
        return f" | Energy: {self.energy}/{self.max_energy}{star_str}"

    def _hand_to_markdown(self) -> str:
        """Convert the player's hand to a markdown string."""
        if not self.hand:
            return ""
        lines = ["### Hand\n\n"]
        for card in self.hand:
            lines.append(f"- {card.to_markdown()}\n")
        lines.append("\n")
        return "".join(lines)

    def _piles_to_markdown(self, *, is_diff: bool = False) -> str:
        """Convert the player's piles to a markdown string."""
        lines = ["### Deck Information\n\n"]
        # draw pile
        lines.append(f"#### Draw Pile ({self.draw_pile_count} cards, sorted by rarity)\n\n")
        lines.append(f"{self.draw_pile.to_markdown(is_diff=is_diff)}\n")
        # discard pile
        lines.append(f"#### Discard Pile ({self.discard_pile_count} cards)\n\n")
        lines.append(f"{self.discard_pile.to_markdown(is_diff=is_diff)}\n")
        # exhaust pile
        lines.append(f"#### Exhaust Pile ({self.exhaust_pile_count} cards)\n\n")
        lines.append(f"{self.exhaust_pile.to_markdown(is_diff=is_diff)}\n")
        return "".join(lines)

    def _orbs_to_markdown(self) -> str:
        """Convert the player's orbs to a markdown string."""
        lines = [f"### Orbs ({len(self.orbs)}/{self.orb_slots} slots)\n\n"]
        for orb in self.orbs:
            lines.append(f"- {orb.to_markdown()}\n")
        if self.orb_empty_slots and self.orb_empty_slots > 0:
            lines.append(f"- {self.orb_empty_slots} empty slots\n")
        lines.append("\n")
        return "".join(lines)

    def _pets_to_markdown(self) -> str:
        """Convert the player's pets to a markdown string."""
        lines = ["### Pets\n\n"]
        for pet in self.pets:
            lines.append(f"- {pet.to_markdown()}\n")
        lines.append("\n")
        return "".join(lines)

    def to_markdown(self, *, is_diff: bool = False) -> str:
        """Convert the combat player state to a markdown string."""
        lines = []
        lines.append(self._hand_to_markdown())
        lines.append(self._piles_to_markdown(is_diff=is_diff))
        if self.orbs is not None:
            lines.append(self._orbs_to_markdown())
        if self.pets is not None:
            lines.append(self._pets_to_markdown())
        return "".join(lines)
