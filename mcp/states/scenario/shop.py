from typing import Literal, TypeAlias

from pydantic import BaseModel

from states.common.keyword import Keywords


class BaseShopItem(BaseModel):
    """The item in the shop."""

    index: int
    price: int
    is_stocked: bool
    can_afford: bool


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


class ShopRelic(BaseShopItem):
    """The relic in the shop."""

    category: Literal["relic"]
    relic_id: str | None = None  # None when purchased
    relic_name: str | None = None
    relic_description: str | None = None
    keywords: Keywords | None = None


class ShopPotion(BaseShopItem):
    """The potion in the shop."""

    category: Literal["potion"]
    potion_id: str | None = None  # None when purchased
    potion_name: str | None = None
    potion_description: str | None = None
    keywords: Keywords | None = None


class ShopCardRemoval(BaseShopItem):
    """The card removal option in the shop."""

    category: Literal["card_removal"]


ShopItem: TypeAlias = ShopCard | ShopRelic | ShopPotion | ShopCardRemoval


class Shop(BaseModel):
    """The state of the shop."""

    items: list[ShopItem]
    can_proceed: bool
    error: str | None = None  # only present if inventory isn't ready; retry in a moment


class ShopState(BaseModel):
    """The state when the scenario is in the shop."""

    state_type: Literal["shop"]
    shop: Shop
