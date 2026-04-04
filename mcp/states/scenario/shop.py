from typing import Literal, TypeAlias

from pydantic import BaseModel

from states.common.keyword import Keywords


class BaseShopItem(BaseModel):
    """The item in the shop."""

    index: int
    cost: int
    is_stocked: bool
    can_afford: bool

    def _cost_tag(self) -> str:
        return f"{self.cost}g" if self.is_stocked else "SOLD"

    def _afford_tag(self) -> str:
        return " (can't afford)" if self.is_stocked and not self.can_afford else ""


class ShopCard(BaseShopItem):
    """The card in the shop."""

    category: Literal["card"]
    on_sale: bool | None = None
    card_id: str | None = None  # None when purchased
    card_name: str | None = None
    card_type: str | None = None
    card_rarity: str | None = None
    card_cost: str | None = None  # can be int or "X" (variable cost)
    card_star_cost: str | None = None
    card_description: str | None = None
    keywords: Keywords | None = None

    def to_markdown(self) -> str:
        star_cost = f" ({self.card_star_cost} star)" if self.card_star_cost is not None else ""
        desc = f"**{self.card_name}** [{self.card_type}]{star_cost} {self.card_rarity} - {self.card_description}"
        sale = " **SALE**" if self.on_sale else ""
        return f"[{self.index}] {desc} - {self._cost_tag()}{sale}{self._afford_tag()}"


class ShopRelic(BaseShopItem):
    """The relic in the shop."""

    category: Literal["relic"]
    relic_id: str | None = None  # None when purchased
    relic_name: str | None = None
    relic_description: str | None = None
    keywords: Keywords | None = None

    def to_markdown(self) -> str:
        desc = f"**{self.relic_name}** - {self.relic_description}"
        return f"[{self.index}] {desc} - {self._cost_tag()}{self._afford_tag()}"


class ShopPotion(BaseShopItem):
    """The potion in the shop."""

    category: Literal["potion"]
    potion_id: str | None = None  # None when purchased
    potion_name: str | None = None
    potion_description: str | None = None
    keywords: Keywords | None = None

    def to_markdown(self) -> str:
        desc = f"**{self.potion_name}** - {self.potion_description}"
        return f"[{self.index}] {desc} - {self._cost_tag()}{self._afford_tag()}"


class ShopCardRemoval(BaseShopItem):
    """The card removal option in the shop."""

    category: Literal["card_removal"]

    def to_markdown(self) -> str:
        desc = "**Remove a card** from your deck"
        return f"[{self.index}] {desc} - {self._cost_tag()}{self._afford_tag()}"


ShopItem: TypeAlias = ShopCard | ShopRelic | ShopPotion | ShopCardRemoval

_CATEGORY_HEADERS = {
    "card": "Cards",
    "relic": "Relics",
    "potion": "Potions",
    "card_removal": "Services",
}


class Shop(BaseModel):
    """The state of the shop."""

    items: list[ShopItem]
    can_proceed: bool
    error: str | None = None  # only present if inventory isn't ready; retry in a moment

    def to_markdown(self) -> str:
        lines = []
        if self.error is not None:
            lines.append("## Shop\n")
            lines.append(f"**Note:** {self.error}\n\n")

        if self.items:
            lines.append("## Shop Inventory\n")
            last_category = None
            for item in self.items:
                category = item.category
                if category != last_category:
                    header = _CATEGORY_HEADERS.get(category, category)
                    lines.append(f"### {header}\n")
                    last_category = category
                lines.append(f"- {item.to_markdown()}\n")
            lines.append("\n")

        lines.append(f"**Can proceed:** {'Yes' if self.can_proceed else 'No'}\n\n")
        return "".join(lines)


class ShopState(BaseModel):
    """The state when the scenario is in the shop."""

    state_type: Literal["shop"]
    shop: Shop

    def to_markdown(self) -> str:
        return self.shop.to_markdown()
