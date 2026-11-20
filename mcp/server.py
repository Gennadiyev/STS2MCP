"""MCP server bridge for Slay the Spire 2.

Connects to the STS2_MCP mod's HTTP server and exposes game actions
as MCP tools for Claude Desktop / Claude Code.
"""

import argparse
import asyncio
import json
import os
import re
import sys
from pathlib import Path
from typing import Annotated, Literal

import httpx
from mcp.server.fastmcp import FastMCP
from pydantic import BaseModel, ConfigDict, Field

mcp = FastMCP("sts2")

_base_url: str = "http://localhost:15526"
_token_file: Path | None = None
_http: httpx.AsyncClient | None = None


def _sp_url() -> str:
    return f"{_base_url}/api/v1/singleplayer"


def _mp_url() -> str:
    return f"{_base_url}/api/v1/multiplayer"


def _profile_url() -> str:
    return f"{_base_url}/api/v1/profile"


def _compendium_url() -> str:
    return f"{_base_url}/api/v1/compendium"


def _wiki_url() -> str:
    return f"{_base_url}/api/v1/wiki"


def _profiles_url() -> str:
    return f"{_base_url}/api/v1/profiles"


def _default_token_file() -> Path:
    if sys.platform == "win32":
        data_dir = Path(os.environ.get("APPDATA", Path.home() / "AppData" / "Roaming"))
    elif sys.platform == "darwin":
        data_dir = Path.home() / "Library" / "Application Support"
    else:
        data_dir = Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local" / "share"))
    return data_dir / "SlayTheSpire2" / "STS2_MCP.token"


def _get_client() -> httpx.AsyncClient:
    global _http
    if _http is None:
        token_file = _token_file or _default_token_file()
        try:
            token = token_file.read_text(encoding="ascii").strip()
        except (OSError, UnicodeError) as error:
            raise RuntimeError(
                f"Cannot read the bridge token at {token_file}. Start the game with the mod enabled "
                "or pass --token-file using the path printed in the game log."
            ) from error
        if re.fullmatch(r"[0-9A-Fa-f]{64}", token) is None:
            raise RuntimeError(f"Invalid bridge token file at {token_file}.")
        _http = httpx.AsyncClient(
            timeout=httpx.Timeout(10),
            trust_env=False,
            headers={"Authorization": f"Bearer {token}"},
        )
    return _http


async def _get(params: dict | None = None, *, wait_for_ready: bool = False) -> str:
    timeout = {"timeout": httpx.Timeout(15, connect=10)} if wait_for_ready else {}
    r = await _get_client().get(_sp_url(), params=params, **timeout)
    r.raise_for_status()
    return r.text


async def _post(body: dict) -> str:
    timeout = {"timeout": httpx.Timeout(20, connect=10)} if body.get("return_state") else {}
    r = await _get_client().post(_sp_url(), json=body, **timeout)
    r.raise_for_status()
    return r.text


async def _mp_get(params: dict | None = None) -> str:
    r = await _get_client().get(_mp_url(), params=params)
    r.raise_for_status()
    return r.text


async def _mp_post(body: dict) -> str:
    r = await _get_client().post(_mp_url(), json=body)
    r.raise_for_status()
    return r.text


async def _profile_get() -> str:
    r = await _get_client().get(_profile_url())
    r.raise_for_status()
    return r.text


async def _compendium_get() -> str:
    r = await _get_client().get(_compendium_url())
    r.raise_for_status()
    return r.text


async def _wiki_get(query: str, item_type: str = "all", limit: int = 10) -> str:
    r = await _get_client().get(
        _wiki_url(),
        params={"query": query, "item_type": item_type, "limit": limit},
    )
    r.raise_for_status()
    return r.text


async def _profiles_get() -> str:
    r = await _get_client().get(_profiles_url())
    r.raise_for_status()
    return r.text


async def _profiles_post(body: dict) -> str:
    r = await _get_client().post(_profiles_url(), json=body)
    r.raise_for_status()
    return r.text


async def _wait_for_profile(profile_id: int, fallback: str) -> str:
    last_profiles: dict | None = None
    for _ in range(30):
        await asyncio.sleep(0.1)
        profiles_text = await _profiles_get()
        profiles = json.loads(profiles_text)
        last_profiles = profiles
        if profiles.get("current_profile_id") == profile_id:
            return json.dumps(
                {
                    "status": "ok",
                    "message": f"Switched to profile {profile_id}",
                    "current_profile_id": profile_id,
                    "profiles": profiles.get("profiles", []),
                },
                indent=2,
            )

    return json.dumps(
        {
            "status": "error",
            "error": f"Timed out waiting for profile {profile_id} to become active",
            "current_profile_id": (
                last_profiles.get("current_profile_id")
                if isinstance(last_profiles, dict)
                else None
            ),
            "profiles": (
                last_profiles.get("profiles", [])
                if isinstance(last_profiles, dict)
                else []
            ),
            "initial_response": fallback,
        },
        indent=2,
    )


def _handle_error(e: Exception) -> str:
    if isinstance(e, httpx.ConnectError):
        return "Error: Cannot connect to STS2_MCP mod. Is the game running with the mod enabled?"
    if isinstance(e, httpx.HTTPStatusError):
        return f"Error: HTTP {e.response.status_code} — {e.response.text}"
    return f"Error: {e}"


# ---------------------------------------------------------------------------
# General
# ---------------------------------------------------------------------------


@mcp.tool()
async def get_game_state(format: str = "markdown", compact: bool = False, wait_for_ready: bool = False) -> str:
    """Get the current Slay the Spire 2 game state.

    Returns the full game state including player stats, hand, enemies, potions, etc.
    The state_type field indicates the current screen (combat, map, event, shop,
    fake_merchant, etc.).
    For active play, use compact=true and wait_for_ready=true to read hand, intents,
    and resources in one call. Use return_state=true on card plays and end turns
    to get the next state with the action receipt.

    Args:
        format: "markdown" for human-readable output, "json" for structured data.
        compact: Keep pile counts and omit individual draw, discard, and exhaust cards.
        wait_for_ready: Wait up to seven seconds during combat or treasure transitions.
            Selection prompts and other screens return immediately. Check can_act or treasure readiness on return.
    """
    try:
        params = {"format": format}
        if compact:
            params["compact"] = "true"
        if wait_for_ready:
            params["wait_for_ready"] = "true"
        return await _get(params, wait_for_ready=wait_for_ready)
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def menu_select(option: str, seed: str | None = None) -> str:
    """Select a visible menu option.

    Use with state_type "menu" or "game_over". Covers main-menu navigation,
    singleplayer / multiplayer submenus, multiplayer host & join lobbies,
    multiplayer load lobby (resume saved co-op run), character select for SP
    and MP (with `unready` once readied in MP), profile switching, timeline
    controls, tutorial prompts, blocking popups, and game-over main-menu return.

    Multiplayer flow tips:
      - On menu_screen "multiplayer_join", use refresh / back / join_<index> /
        join_<player_id> (e.g. "join_0" or "join_76561198000000000").
      - On menu_screen "multiplayer_load_lobby", use confirm (or embark) to
        ready up; the run resumes once everyone is connected and ready.
      - On menu_screen "character_select" while in MP, the state includes a
        `lobby` block with the roster, ready states, and ascension; "unready"
        becomes available after you confirm/embark.

    Args:
        option: Option ID from the current menu state's options list. If an
            option is listed under blocked_options, selecting it returns the
            API's manual-action response instead of forcing UI entry.
        seed: Optional seed for supported embark flows. Standard mode rejects seeds.
    """
    body: dict = {"action": "menu_select", "option": option}
    if seed is not None:
        body["seed"] = seed
    try:
        try:
            return await _post(body)
        except httpx.HTTPStatusError as error:
            if error.response.status_code != 409:
                raise
            # The single-player route rejects active multiplayer runs before dispatch.
            return await _mp_post(body)
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def get_profile() -> str:
    """Get the current profile's persistent progress summary.

    Includes character stats, discovered content, achievements, epochs, and global
    run totals for the active profile.
    """
    try:
        return await _profile_get()
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def get_compendium() -> str:
    """Get the active profile's Compendium-shaped progress summary.

    Mirrors the visible Compendium cards: Card Library, Relic Collection,
    Potion Lab, Bestiary, Character Stats, and Run History. This is a
    profile-level view, so it is useful at the main menu as well as during a run.
    Card/relic/potion rules text is still supplied by game state when those
    objects are visible in run-specific contexts.
    """
    try:
        return await _compendium_get()
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def search_wiki(query: str, item_type: str = "all", limit: int = 10) -> str:
    """Look up card, relic, and character knowledge from the game.

    Card and relic searches use the active profile's discoveries. Upgradable
    cards include base and upgraded rules, costs, and keywords. Character
    searches cover unlocked characters and include their localized descriptions,
    starting stats, deck, and relics.

    Args:
        query: A name or ID such as "FALLING_STAR", "silver spoon", or "REGENT".
            Use "character" with item_type="character" for the unlocked roster.
        item_type: "all" for cards and relics, or "card", "relic", or "character".
        limit: Maximum results to return. Defaults to 10; the mod clamps it to
            a bounded maximum.
    """
    try:
        return await _wiki_get(query, item_type, limit)
    except Exception as e:
        return _handle_error(e)


class GameNameReference(BaseModel):
    model_config = ConfigDict(extra="forbid")

    kind: Literal[
        "act", "affliction", "card", "character", "enchantment", "encounter",
        "enemy", "event", "orb", "potion", "power", "relic",
    ]
    id: str = Field(min_length=1, max_length=100, description="Stable game ID or current enemy entity ID.")


@mcp.tool()
async def get_simplified_chinese_names(
    entries: Annotated[list[GameNameReference], Field(min_length=1, max_length=24)],
) -> str:
    """Look up official Simplified Chinese names from the installed game.

    Use game IDs from state or wiki results and enemy entity IDs from combat.
    Works even while this copy of the game is in English. Use these names when
    writing a co-op prompt or chat message to a teammate who uses Simplified
    Chinese. Keep the original IDs for game actions.
    """
    try:
        response = await _get_client().get(
            f"{_base_url}/api/v1/chinese-names",
            params=[("entry", f"{entry.kind}:{entry.id}") for entry in entries],
        )
        response.raise_for_status()
        return response.text
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def list_profiles() -> str:
    """List the three profile slots and identify the active slot.

    The has_data field indicates whether a slot currently has progress data.
    """
    try:
        return await _profiles_get()
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def switch_profile(profile_id: int) -> str:
    """Switch to a profile slot through the game's profile UI.

    Use an empty slot to create or enter a fresh profile for testing. This cannot
    be used while a run is in progress.

    Args:
        profile_id: Profile slot to switch to. Must be 1, 2, or 3.
    """
    try:
        body = {"action": "switch", "profile_id": profile_id}
        result = await _profiles_post(body)
        try:
            parsed = json.loads(result)
            if parsed.get("status") == "error":
                return result

            message = parsed.get("message", "")
            if isinstance(message, str) and message.startswith("Opened profile screen"):
                for _ in range(20):
                    await asyncio.sleep(0.1)
                    state_text = await _get({"format": "json"})
                    state = json.loads(state_text)
                    if state.get("menu_screen") == "profile_select":
                        result = await _profiles_post(body)
                        parsed = json.loads(result)
                        if parsed.get("status") == "error":
                            return result
                        break
            result = await _wait_for_profile(profile_id, result)
        except json.JSONDecodeError:
            pass
        return result
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def delete_profile(profile_id: int) -> str:
    """Delete an inactive profile slot if enabled in STS2_MCP.conf.

    Profile deletion is disabled by default. The active profile cannot be deleted through this tool. Switch away first if
    you intend to remove a slot after backing up any data you need.

    Args:
        profile_id: Profile slot to delete. Must be 1, 2, or 3.
    """
    try:
        return await _profiles_post({"action": "delete", "profile_id": profile_id})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def use_potion(slot: int, target: str | None = None) -> str:
    """Use a potion from the player's potion slots.

    Works both during and outside of combat. Combat-only potions require an active battle.

    Args:
        slot: Potion slot index (as shown in game state).
        target: Enemy entity ID from the current combat. Required for enemy-targeted potions.
    """
    body: dict = {"action": "use_potion", "slot": slot}
    if target is not None:
        body["target"] = target
    try:
        return await _post(body)
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def discard_potion(slot: int) -> str:
    """Discard a potion from the player's potion slots to free up space.

    Use this when all potion slots are full and you need room for incoming potions
    (e.g. before collecting a potion reward).

    Args:
        slot: Potion slot index to discard (as shown in game state).
    """
    try:
        return await _post({"action": "discard_potion", "slot": slot})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def proceed_to_map(leave_shop: bool = False) -> str:
    """Proceed from the current screen to the map.

    Works from rewards, rest sites, shops, Fake Merchant, and treasure.

    Args:
        leave_shop: Leave a shop in one call, closing its inventory first if needed.
            With the default false, an open inventory is only closed.

    Does NOT work for events — use event_choose_option() with the Proceed option's index.
    """
    try:
        return await _post({"action": "proceed", "leave_shop": leave_shop})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Combat (state_type: monster / elite / boss)
# ---------------------------------------------------------------------------


class PlannedCardPlay(BaseModel):
    model_config = ConfigDict(extra="forbid")

    card_index: int = Field(ge=0, description="Index in the starting hand, before any of the planned plays.")
    card_id: str = Field(min_length=1, description="Expected card ID at that index.")
    target: str | None = Field(default=None, description="Enemy entity ID for a single-target card.")


@mcp.tool()
async def combat_play_sequence(
    plays: Annotated[list[PlannedCardPlay], Field(min_length=1, max_length=6)], end_turn: bool = False
) -> str:
    """[Combat] Play a short single-player plan and return the next compact state.

    Use distinct indices and expected card IDs from the starting hand. The game resolves each card before
    playing the next. A choice, new danger warning, changed enemy roster, unplayable card, or turn change
    stops the sequence. Read the returned state and submitted prefix before deciding what to do next.

    Args:
        plays: Up to six plays in the intended execution order.
        end_turn: End the turn if every play settles and the original turn still accepts input.
    """
    try:
        return await _post({
            "action": "play_card_sequence",
            "plays": [play.model_dump(exclude_none=True) for play in plays],
            "end_turn": end_turn,
            "return_state": True,
        })
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def combat_play_card(card_index: int, target: str | None = None, return_state: bool = False) -> str:
    """[Combat] Play a card from the player's hand.

    Args:
        card_index: Index of the card in hand (0-based, as shown in game state).
        target: Enemy entity ID from the current combat. It stays assigned to that enemy until combat ends.
            Required for single-target cards.
        return_state: For consecutive plays, include compact JSON state after effects resolve or a choice opens.
            If state_wait_timed_out or state_error is returned, refresh state and continue from it.

    Note that the index can change as cards are played - playing a card will shift the indices of remaining cards in hand.
    Refer to the latest game state for accurate indices. New cards are drawn to the right, so playing cards from right to left can help maintain more stable indices for remaining cards.
    """
    body: dict = {"action": "play_card", "card_index": card_index}
    if target is not None:
        body["target"] = target
    if return_state:
        body["return_state"] = True
    try:
        return await _post(body)
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def combat_end_turn(return_state: bool = False) -> str:
    """[Combat] End the player's current turn.

    Args:
        return_state: Include compact JSON for the next playable turn or new screen.
            If state_wait_timed_out or state_error is returned, refresh state and continue from it.
    """
    body = {"action": "end_turn"}
    if return_state:
        body["return_state"] = True
    try:
        return await _post(body)
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# In-Combat Card Selection (state_type: hand_select)
# ---------------------------------------------------------------------------


@mcp.tool()
async def combat_select_card(card_index: int) -> str:
    """[Combat Selection] Select a card from hand during an in-combat card selection prompt.

    Used when a card effect asks you to select a card to exhaust, discard, etc.
    This is different from deck_select_card which handles out-of-combat card selection overlays.

    Args:
        card_index: 0-based index of the card in the selectable hand cards (as shown in game state).
    """
    try:
        return await _post({"action": "combat_select_card", "card_index": card_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def combat_confirm_selection(return_state: bool = False) -> str:
    """[Combat Selection] Confirm the in-combat card selection.

    After selecting the required number of cards from hand (exhaust, discard, etc.),
    use this to confirm the selection. Only works when the confirm button is enabled.

    Args:
        return_state: Include compact JSON after the selection resolves or another choice appears.
            If state_wait_timed_out or state_error is returned, refresh state before continuing.
    """
    body: dict = {"action": "combat_confirm_selection"}
    if return_state:
        body["return_state"] = True
    try:
        return await _post(body)
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Rewards (state_type: rewards / card_reward)
# ---------------------------------------------------------------------------


@mcp.tool()
async def rewards_claim(reward_index: int) -> str:
    """[Rewards] Claim a reward from the post-combat rewards screen.

    Gold, potion, and relic rewards are claimed immediately.
    Card rewards open the card selection screen (state changes to card_reward).

    Args:
        reward_index: 0-based index of the reward on the rewards screen.

    Note that claiming a reward may change the indices of remaining rewards, so refer to the latest game state for accurate indices.
    Claiming from right to left can help maintain more stable indices for remaining rewards, as rewards will always shift left to fill in gaps.
    """
    try:
        return await _post({"action": "claim_reward", "index": reward_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def rewards_pick_card(card_index: int) -> str:
    """[Rewards] Select a card from the card reward selection screen.

    Args:
        card_index: 0-based index of the card to add to the deck.
    """
    try:
        return await _post({"action": "select_card_reward", "card_index": card_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def rewards_choose_alternative(alternative_id: str) -> str:
    """[Rewards] Choose an enabled card reward alternative by the exact ID reported in game state, such as REROLL or SACRIFICE."""
    try:
        return await _post({"action": "select_card_reward_alternative", "alternative_id": alternative_id})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def rewards_skip_card() -> str:
    """[Rewards] Skip the card reward without selecting a card."""
    try:
        return await _post({"action": "skip_card_reward"})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Map (state_type: map)
# ---------------------------------------------------------------------------


@mcp.tool()
async def map_choose_node(node_index: int, return_state: bool = False) -> str:
    """[Map] Choose a map node to travel to.

    Args:
        node_index: 0-based index of the node from the next_options list.
        return_state: Include compact JSON after reaching the destination and its next playable turn or choice.
            If state_wait_timed_out or state_error is returned, refresh state before continuing.
    """
    body: dict = {"action": "choose_map_node", "index": node_index}
    if return_state:
        body["return_state"] = True
    try:
        return await _post(body)
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Rest Site (state_type: rest_site)
# ---------------------------------------------------------------------------


@mcp.tool()
async def rest_choose_option(option_index: int) -> str:
    """[Rest Site] Choose a rest site option (rest, smith, etc.).

    Args:
        option_index: 0-based index of the option from the rest site state.
    """
    try:
        return await _post({"action": "choose_rest_option", "index": option_index})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Shop (state_type: shop)
# ---------------------------------------------------------------------------


@mcp.tool()
async def shop_purchase(item_index: int) -> str:
    """[Shop / Fake Merchant] Purchase an item from the shop.

    Works for both regular shops (state_type: shop) and the fake merchant
    event (state_type: fake_merchant). The fake merchant only sells relics.

    Args:
        item_index: 0-based index of the item from the shop state.
    """
    try:
        return await _post({"action": "shop_purchase", "index": item_index})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Event (state_type: event)
# ---------------------------------------------------------------------------


@mcp.tool()
async def event_choose_option(option_index: int) -> str:
    """[Event] Choose an event option.

    Use an option with can_choose=true from the current event state.
    This also handles Proceed, and the Architect's Continue and Respond choices.

    Args:
        option_index: Native index of the option from the event state.
    """
    try:
        return await _post({"action": "choose_event_option", "index": option_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def event_advance_dialogue() -> str:
    """[Event] Advance ancient event dialogue.

    Use while can_advance_dialogue is true. Check the event state for each line.
    """
    try:
        return await _post({"action": "advance_dialogue"})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Card Selection (state_type: card_select)
# ---------------------------------------------------------------------------


@mcp.tool()
async def deck_select_card(card_index: int) -> str:
    """[Card Selection] Select or deselect a card in the card selection screen.

    Used when the game asks you to choose cards from your deck (transform, upgrade,
    remove, discard) or pick a card from offered choices (potions, effects).

    For deck selections: toggles card selection. For choose-a-card: picks immediately.

    Args:
        card_index: 0-based index of the card (as shown in game state).
    """
    try:
        return await _post({"action": "select_card", "index": card_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def deck_confirm_selection() -> str:
    """[Card Selection] Confirm the current card selection.

    After selecting the required number of cards, use this to confirm.
    If a preview is showing (e.g., transform preview), this confirms the preview.
    Not needed for choose-a-card screens where picking is immediate.
    """
    try:
        return await _post({"action": "confirm_selection"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def deck_cancel_selection() -> str:
    """[Card Selection] Cancel the current card selection.

    If a preview is showing, goes back to the selection grid.
    For choose-a-card screens, clicks the skip button (if available).
    Otherwise, closes the card selection screen (only if cancellation is allowed).
    """
    try:
        return await _post({"action": "cancel_selection"})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Bundle Selection (state_type: bundle_select)
# ---------------------------------------------------------------------------


@mcp.tool()
async def bundle_select(bundle_index: int) -> str:
    """[Bundle Selection] Open a bundle preview.

    Args:
        bundle_index: 0-based index of the bundle.
    """
    try:
        return await _post({"action": "select_bundle", "index": bundle_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def bundle_confirm_selection() -> str:
    """[Bundle Selection] Confirm the currently previewed bundle."""
    try:
        return await _post({"action": "confirm_bundle_selection"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def bundle_cancel_selection() -> str:
    """[Bundle Selection] Cancel the current bundle preview."""
    try:
        return await _post({"action": "cancel_bundle_selection"})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Relic Selection (state_type: relic_select)
# ---------------------------------------------------------------------------


@mcp.tool()
async def relic_select(relic_index: int) -> str:
    """[Relic Selection] Select a relic from the relic selection screen.

    Used when the game offers a choice of relics (e.g., boss relic rewards).

    Args:
        relic_index: 0-based index of the relic (as shown in game state).
    """
    try:
        return await _post({"action": "select_relic", "index": relic_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def relic_skip() -> str:
    """[Relic Selection] Skip the relic selection without choosing a relic."""
    try:
        return await _post({"action": "skip_relic_selection"})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Treasure (state_type: treasure)
# ---------------------------------------------------------------------------


@mcp.tool()
async def treasure_claim_relic(relic_index: int) -> str:
    """[Treasure] Claim a relic from the treasure chest.

    State polling opens the chest. Wait for treasure.can_claim before claiming
    and for treasure.can_proceed before calling proceed_to_map().

    Args:
        relic_index: 0-based index of the relic (as shown in game state).
    """
    try:
        return await _post({"action": "claim_treasure_relic", "index": relic_index})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Crystal Sphere (state_type: crystal_sphere)
# ---------------------------------------------------------------------------


@mcp.tool()
async def crystal_sphere_set_tool(tool: str) -> str:
    """[Crystal Sphere] Switch the active divination tool.

    Args:
        tool: Either "big" or "small".
    """
    try:
        return await _post({"action": "crystal_sphere_set_tool", "tool": tool})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def crystal_sphere_click_cell(x: int, y: int) -> str:
    """[Crystal Sphere] Click a hidden cell on the Crystal Sphere grid.

    Args:
        x: Cell x-coordinate.
        y: Cell y-coordinate.
    """
    try:
        return await _post({"action": "crystal_sphere_click_cell", "x": x, "y": y})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def crystal_sphere_proceed() -> str:
    """[Crystal Sphere] Continue after the Crystal Sphere minigame finishes."""
    try:
        return await _post({"action": "crystal_sphere_proceed"})
    except Exception as e:
        return _handle_error(e)


# ===========================================================================
# MULTIPLAYER tools — all route through /api/v1/multiplayer
# ===========================================================================


@mcp.tool()
async def mp_get_game_state(format: str = "markdown") -> str:
    """[Multiplayer] Get the current multiplayer game state.

    Returns a summary of all players (HP, gold, alive status) plus full
    detail for the local player (relics, potions, deck, etc.), along with
    multiplayer-specific data: map votes, event votes, treasure bids,
    end-turn ready status. Only works during a multiplayer run.
    Wait for battle.can_act before ordinary local combat actions. The separate
    battle.can_undo_end_turn flag indicates when you can retract your vote.
    On the map, use map.vote_phase and run.is_traveling to follow the party.
    run.map_coord is the actual location. Use treasure readiness fields before
    claiming, skipping, or proceeding.

    Args:
        format: "markdown" for human-readable output, "json" for structured data.
    """
    try:
        return await _mp_get({"format": format})
    except Exception as e:
        return _handle_error(e)


class CoordinationOption(BaseModel):
    model_config = ConfigDict(extra="forbid")

    label: str | None = Field(
        default=None, min_length=1, max_length=40,
        description="Button text. Omit for a built-in translated label. Custom choices require text.",
    )
    kind: Literal["choice", "agent_first", "teammate_first", "focus_enemy", "no_preference"] = "choice"
    target: str | None = Field(default=None, description="Current enemy entity ID for a focus_enemy choice.")


@mcp.tool()
async def mp_coordination_prompt(
    recipient_slot: Annotated[int, Field(ge=0)],
    options: Annotated[list[CoordinationOption], Field(min_length=2, max_length=4)],
    question: Annotated[str | None, Field(min_length=1, max_length=140)] = None,
    allow_text: bool = True,
    lifetime_seconds: Annotated[int, Field(ge=5, le=60)] = 25,
) -> str:
    """[Multiplayer Coordination] Ask one teammate a combat question with quick replies.

    Requires the in-game coordination companion on the participating clients.
    Keep the affected combat action on hold while the prompt is pending. Only
    one outgoing prompt can be active per seat. Read mp_coordination_state()
    for the answer, then refresh multiplayer game state before acting.

    Args:
        recipient_slot: Teammate's zero-based index in the state players array.
        question: What you need the teammate to decide. Omit for a standard
            question in the recipient's language. Write custom text in their language.
        options: Two to four button labels and their meanings. A focus_enemy
            choice needs a target from the current enemy entity IDs. Omit
            built-in labels to use the recipient's language and native enemy names. A
            teammate_first choice waits for the teammate to finish or say Go ahead.
        allow_text: Allow a free-text answer alongside the buttons.
        lifetime_seconds: Seconds to wait for a reply or a teammate's first play.
    """
    try:
        return await _mp_post({
            "action": "coordination_prompt",
            "recipient_slot": recipient_slot,
            "question": question,
            "options": [option.model_dump(exclude_none=True) for option in options],
            "allow_text": allow_text,
            "lifetime_seconds": lifetime_seconds,
        })
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_coordination_state(after_id: Annotated[int, Field(ge=0)] = 0) -> str:
    """[Multiplayer Coordination] Read your prompts and recent messages.

    Returns the incoming and outgoing prompts and events after after_id. Use
    the returned latest_id for the next call within this run. This is a quick
    read. Allow a teammate time to respond between reads.

    Look up the recipient in teammates by player_slot. If language is "zhs",
    write custom prompts and chat in Simplified Chinese and use
    get_simplified_chinese_names() for game terms. Standard prompts and
    built-in reply labels can be generated in the recipient's language.

    Check for new events before committing to a turn or route. Human players
    can start a conversation using Chat at any point in the run, even without
    an active prompt. Reply to a direct message with mp_coordination_message()
    using the event's sender_slot.

    Keep the affected action on hold while the outgoing prompt is pending or
    waiting_for_teammate. If it is answered, refresh the fight state before
    executing the agreed action with its coordination_prompt_id. An expired
    or cancelled prompt no longer authorizes the proposed action.
    """
    try:
        return await _mp_post({"action": "coordination_state", "after_id": after_id})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_coordination_reply(
    prompt_id: Annotated[str, Field(min_length=1)],
    choice_index: Annotated[int | None, Field(ge=0)] = None,
    text: Annotated[str | None, Field(min_length=1, max_length=200)] = None,
    go_ahead: bool = False,
) -> str:
    """[Multiplayer Coordination] Reply to your incoming prompt.

    Supply exactly one reply. Use a zero-based choice_index to select a
    button, text when the prompt permits a text answer, or go_ahead=true
    after choosing to go first and completing your play. A reply changes
    coordination state without playing a card or ending anyone's turn.
    """
    reply_count = sum((choice_index is not None, text is not None, go_ahead))
    if reply_count != 1:
        return "Error: supply exactly one of choice_index, text, or go_ahead=true."
    body: dict = {"action": "coordination_reply", "prompt_id": prompt_id}
    if choice_index is not None:
        body["choice_index"] = choice_index
    if text is not None:
        body["text"] = text
    if go_ahead:
        body["go_ahead"] = True
    try:
        return await _mp_post(body)
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_coordination_message(
    recipient_slot: Annotated[int, Field(ge=0)],
    text: Annotated[str, Field(min_length=1, max_length=200)],
) -> str:
    """[Multiplayer Coordination] Send a short text message to one teammate.

    Recipient slot is the teammate's zero-based index in the state players
    array. Use mp_coordination_reply() for a response to a specific prompt.
    """
    try:
        return await _mp_post({
            "action": "coordination_message",
            "recipient_slot": recipient_slot,
            "text": text,
        })
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_coordination_cancel(prompt_id: Annotated[str, Field(min_length=1)]) -> str:
    """[Multiplayer Coordination] Withdraw your outgoing prompt and free its slot."""
    try:
        return await _mp_post({"action": "coordination_cancel", "prompt_id": prompt_id})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_combat_play_card(
    card_index: int, target: str | None = None, coordination_prompt_id: str | None = None,
    target_player_slot: Annotated[int | None, Field(ge=0)] = None,
) -> str:
    """[Multiplayer Combat] Play a card from the local player's hand.

    Same as singleplayer combat_play_card but routed through the multiplayer
    endpoint for sync safety.

    Args:
        card_index: Index of the card in hand (0-based).
        target: Enemy entity ID from the current combat. Required for enemy-targeted cards.
        target_player_slot: Required for AnyAlly or AnyPlayer cards. Use a living player's
            player_slot from the current players list. AnyAlly excludes the caster.
        coordination_prompt_id: Bind this action to an answered combat coordination prompt.
            Refresh the hand and enemy state before using it.
    """
    body: dict = {"action": "play_card", "card_index": card_index}
    if target is not None:
        body["target"] = target
    if target_player_slot is not None:
        body["target_player_slot"] = target_player_slot
    if coordination_prompt_id is not None:
        body["coordination_prompt_id"] = coordination_prompt_id
    try:
        return await _mp_post(body)
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_combat_end_turn(
    allow_playable_cards: bool = False, coordination_prompt_id: str | None = None
) -> str:
    """[Multiplayer Combat] Submit end-turn vote.

    Finish planned card plays and check the current hand before ending. If
    playable cards remain, the tool returns their indices without submitting.
    Use mp_combat_undo_end_turn() while battle.can_undo_end_turn is true.

    Args:
        allow_playable_cards: Set true to deliberately end with playable cards
            remaining, for example when every useful play is complete.
        coordination_prompt_id: Bind ending the turn to an answered
            coordination prompt for the current combat turn.
    """
    body: dict = {"action": "end_turn", "allow_playable_cards": allow_playable_cards}
    if coordination_prompt_id is not None:
        body["coordination_prompt_id"] = coordination_prompt_id
    try:
        return await _mp_post(body)
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_combat_undo_end_turn() -> str:
    """[Multiplayer Combat] Retract end-turn vote.

    Use while battle.can_undo_end_turn is true. After submitting, wait for
    battle.can_act before playing more cards.
    """
    try:
        return await _mp_post({"action": "undo_end_turn"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_use_potion(
    slot: int, target: str | None = None, coordination_prompt_id: str | None = None,
    target_player_slot: Annotated[int | None, Field(ge=0)] = None,
) -> str:
    """[Multiplayer] Use a potion from the local player's potion slots.

    Args:
        slot: Potion slot index (as shown in game state).
        target: Enemy entity ID from the current combat. Required for enemy-targeted potions.
        target_player_slot: Living player's player_slot from the current players list.
            Required for AnyAlly potions. AnyPlayer potions default to the user.
        coordination_prompt_id: Bind this action to an answered combat coordination prompt.
    """
    body: dict = {"action": "use_potion", "slot": slot}
    if target is not None:
        body["target"] = target
    if target_player_slot is not None:
        body["target_player_slot"] = target_player_slot
    if coordination_prompt_id is not None:
        body["coordination_prompt_id"] = coordination_prompt_id
    try:
        return await _mp_post(body)
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_discard_potion(slot: int) -> str:
    """[Multiplayer] Discard a potion from the local player's potion slots to free up space.

    This queues a synchronized action. Confirm the slot is empty in fresh game
    state before claiming a replacement.

    Args:
        slot: Potion slot index to discard (as shown in game state).
    """
    try:
        return await _mp_post({"action": "discard_potion", "slot": slot})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_map_vote(node_index: int) -> str:
    """[Multiplayer Map] Vote for a map node to travel to.

    After a split vote, the party can travel to another player's choice.
    Check run.map_coord and run.is_traveling in multiplayer state before
    acting in the new room. Re-selecting your node sends a ping.

    Args:
        node_index: 0-based index of the node from the next_options list.
    """
    try:
        return await _mp_post({"action": "choose_map_node", "index": node_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_event_choose_option(option_index: int) -> str:
    """[Multiplayer Event] Choose or vote for an event option.

    Use an option with can_choose=true from the current event state.
    A shared choice returns choice_status=vote_submitted. Check state for the party's result.

    Args:
        option_index: Native index of the option from the event state.
    """
    try:
        return await _mp_post({"action": "choose_event_option", "index": option_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_event_advance_dialogue() -> str:
    """[Multiplayer Event] Advance ancient dialogue while can_advance_dialogue is true."""
    try:
        return await _mp_post({"action": "advance_dialogue"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_rest_choose_option(
    option_index: int, target_player_slot: Annotated[int | None, Field(ge=0)] = None,
) -> str:
    """[Multiplayer Rest Site] Choose a rest site option (rest, smith, etc.).

    Per-player choice — no voting needed.

    Args:
        option_index: 0-based index of the option.
        target_player_slot: Required for Mend. Use one of the option's
            target_player_slots from the current rest site state.
    """
    body = {"action": "choose_rest_option", "index": option_index}
    if target_player_slot is not None:
        body["target_player_slot"] = target_player_slot
    try:
        return await _mp_post(body)
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_shop_purchase(item_index: int) -> str:
    """[Multiplayer Shop] Purchase an item from the shop.

    Per-player inventory — no voting needed.

    Args:
        item_index: 0-based index of the item.
    """
    try:
        return await _mp_post({"action": "shop_purchase", "index": item_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_rewards_claim(reward_index: int) -> str:
    """[Multiplayer Rewards] Claim a reward from the post-combat rewards screen.

    Args:
        reward_index: 0-based index of the reward.
    """
    try:
        return await _mp_post({"action": "claim_reward", "index": reward_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_rewards_pick_card(card_index: int) -> str:
    """[Multiplayer Rewards] Select a card from the card reward screen.

    Args:
        card_index: 0-based index of the card to add to the deck.
    """
    try:
        return await _mp_post({"action": "select_card_reward", "card_index": card_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_rewards_choose_alternative(alternative_id: str) -> str:
    """[Multiplayer Rewards] Choose an enabled card reward alternative by the exact ID reported in game state, such as REROLL or SACRIFICE."""
    try:
        return await _mp_post({"action": "select_card_reward_alternative", "alternative_id": alternative_id})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_rewards_skip_card() -> str:
    """[Multiplayer Rewards] Skip the card reward."""
    try:
        return await _mp_post({"action": "skip_card_reward"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_proceed_to_map(leave_shop: bool = False) -> str:
    """[Multiplayer] Proceed from the current screen to the map.

    Works from rewards, rest sites, shops, Fake Merchant, and treasure.

    Args:
        leave_shop: Leave a shop in one call, closing its inventory first if needed.
            With the default false, an open inventory is only closed.
    """
    try:
        return await _mp_post({"action": "proceed", "leave_shop": leave_shop})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_deck_select_card(card_index: int) -> str:
    """[Multiplayer Card Selection] Select or deselect a card in the card selection screen.

    Args:
        card_index: 0-based index of the card.
    """
    try:
        return await _mp_post({"action": "select_card", "index": card_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_deck_confirm_selection() -> str:
    """[Multiplayer Card Selection] Confirm the current card selection."""
    try:
        return await _mp_post({"action": "confirm_selection"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_deck_cancel_selection() -> str:
    """[Multiplayer Card Selection] Cancel the current card selection."""
    try:
        return await _mp_post({"action": "cancel_selection"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_bundle_select(bundle_index: int) -> str:
    """[Multiplayer Bundle Selection] Open a bundle preview.

    Args:
        bundle_index: 0-based index of the bundle.
    """
    try:
        return await _mp_post({"action": "select_bundle", "index": bundle_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_bundle_confirm_selection() -> str:
    """[Multiplayer Bundle Selection] Confirm the currently previewed bundle."""
    try:
        return await _mp_post({"action": "confirm_bundle_selection"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_bundle_cancel_selection() -> str:
    """[Multiplayer Bundle Selection] Cancel the current bundle preview."""
    try:
        return await _mp_post({"action": "cancel_bundle_selection"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_combat_select_card(card_index: int) -> str:
    """[Multiplayer Combat Selection] Select a card from hand during in-combat card selection.

    Args:
        card_index: 0-based index of the card in the selectable hand cards.
    """
    try:
        return await _mp_post({"action": "combat_select_card", "card_index": card_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_combat_confirm_selection() -> str:
    """[Multiplayer Combat Selection] Confirm the in-combat card selection."""
    try:
        return await _mp_post({"action": "combat_confirm_selection"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_relic_select(relic_index: int) -> str:
    """[Multiplayer Relic Selection] Select a relic (boss relic rewards).

    Args:
        relic_index: 0-based index of the relic.
    """
    try:
        return await _mp_post({"action": "select_relic", "index": relic_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_relic_skip() -> str:
    """[Multiplayer Relic Selection] Skip the relic selection."""
    try:
        return await _mp_post({"action": "skip_relic_selection"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_treasure_claim_relic(relic_index: int) -> str:
    """[Multiplayer Treasure] Bid on / claim a relic from the treasure chest.

    In multiplayer, this is a bid — if multiple players pick the same relic,
    a "relic fight" determines the winner. Others get consolation prizes.

    Args:
        relic_index: 0-based index of the relic.
    """
    try:
        return await _mp_post({"action": "claim_treasure_relic", "index": relic_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_crystal_sphere_set_tool(tool: str) -> str:
    """[Multiplayer Crystal Sphere] Switch the active divination tool.

    Args:
        tool: Either "big" or "small".
    """
    try:
        return await _mp_post({"action": "crystal_sphere_set_tool", "tool": tool})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_crystal_sphere_click_cell(x: int, y: int) -> str:
    """[Multiplayer Crystal Sphere] Click a hidden cell on the Crystal Sphere grid.

    Args:
        x: Cell x-coordinate.
        y: Cell y-coordinate.
    """
    try:
        return await _mp_post({"action": "crystal_sphere_click_cell", "x": x, "y": y})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_crystal_sphere_proceed() -> str:
    """[Multiplayer Crystal Sphere] Continue after the Crystal Sphere minigame finishes."""
    try:
        return await _mp_post({"action": "crystal_sphere_proceed"})
    except Exception as e:
        return _handle_error(e)


def main():
    parser = argparse.ArgumentParser(description="STS2 MCP Server")
    parser.add_argument("--port", type=int, default=15526, help="Game HTTP server port")
    parser.add_argument("--host", choices=("localhost", "127.0.0.1"), default="localhost",
                        help="Loopback address of the game HTTP server")
    parser.add_argument("--token-file", type=Path, help="Path to the token file printed in the game log")
    parser.add_argument("--no-trust-env", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()

    global _base_url, _token_file
    _base_url = f"http://{args.host}:{args.port}"
    _token_file = args.token_file.expanduser() if args.token_file else None

    mcp.run(transport="stdio")


if __name__ == "__main__":
    main()
