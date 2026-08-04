from typing import TypeAlias

from states.scenario.bundle_select import BundleSelectState
from states.scenario.card_reward import CardRewardState
from states.scenario.card_select import CardSelectState
from states.scenario.combat import CombatState
from states.scenario.crystal_sphere import CrystalSphereState
from states.scenario.event import EventState
from states.scenario.hand_select import HandSelectState
from states.scenario.map import MapState
from states.scenario.menu import MenuState
from states.scenario.overlay import OverlayState
from states.scenario.relic_select import RelicSelectState
from states.scenario.rest_site import RestSiteState
from states.scenario.rewards import RewardsState
from states.scenario.shop import ShopState
from states.scenario.treasure import TreasureState
from states.scenario.unknown import UnknownState

ScenarioState: TypeAlias = (
    BundleSelectState
    | CardRewardState
    | CardSelectState
    | CombatState
    | CrystalSphereState
    | EventState
    | HandSelectState
    | MapState
    | MenuState
    | OverlayState
    | RelicSelectState
    | RestSiteState
    | RewardsState
    | ShopState
    | TreasureState
    | UnknownState
)

__all__ = ["ScenarioState"]
