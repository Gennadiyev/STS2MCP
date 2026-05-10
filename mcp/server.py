"""MCP server bridge for Slay the Spire 2.

Connects to the STS2_MCP mod's HTTP server and exposes game actions
as MCP tools for Claude Desktop / Claude Code.
"""

import argparse
import asyncio
import contextvars
import functools
import hashlib
import inspect
import json
import os
import platform
import sys
import time
import uuid
from typing import Any, Awaitable, Callable

import httpx
from mcp.server.fastmcp import FastMCP
from run_logger import RunLogger
from token_usage import TokenEstimator, TokenProfile

mcp = FastMCP("sts2")

_base_url: str = "http://localhost:15526"
_trust_env: bool = True
_http: httpx.AsyncClient | None = None
_run_logger = RunLogger(enabled=False)
_tool_call_id: contextvars.ContextVar[str | None] = contextvars.ContextVar("tool_call_id", default=None)
_tool_name: contextvars.ContextVar[str | None] = contextvars.ContextVar("tool_name", default=None)

COMBAT_STATE_TYPES = {"monster", "elite", "boss"}


def _sp_url() -> str:
    return f"{_base_url}/api/v1/singleplayer"


def _mp_url() -> str:
    return f"{_base_url}/api/v1/multiplayer"


def _profile_url() -> str:
    return f"{_base_url}/api/v1/profile"


def _profiles_url() -> str:
    return f"{_base_url}/api/v1/profiles"


def _get_client() -> httpx.AsyncClient:
    global _http
    if _http is None:
        _http = httpx.AsyncClient(timeout=httpx.Timeout(10), trust_env=_trust_env)
    return _http


async def _request_text(
    method: str,
    url: str,
    *,
    params: dict | None = None,
    json_body: dict | None = None,
) -> str:
    started = time.perf_counter()
    current_tool_call_id = _tool_call_id.get()
    current_tool_name = _tool_name.get()
    await _run_logger.log(
        "http_request",
        {
            "method": method,
            "url": url,
            "params": params or {},
            "json": json_body or {},
        },
        tool_call_id=current_tool_call_id,
        tool_name=current_tool_name,
    )
    try:
        client = _get_client()
        if method == "GET":
            response = await client.get(url, params=params)
        elif method == "POST":
            response = await client.post(url, json=json_body)
        else:
            raise ValueError(f"Unsupported HTTP method: {method}")

        text = response.text
        elapsed_ms = round((time.perf_counter() - started) * 1000, 3)
        await _run_logger.log(
            "http_response",
            {
                "method": method,
                "url": url,
                "status_code": response.status_code,
                "elapsed_ms": elapsed_ms,
                "response": _run_logger.summarize_text(text),
            },
            tool_call_id=current_tool_call_id,
            tool_name=current_tool_name,
        )
        response.raise_for_status()
        return text
    except Exception as exc:
        elapsed_ms = round((time.perf_counter() - started) * 1000, 3)
        await _run_logger.log(
            "http_error",
            {
                "method": method,
                "url": url,
                "elapsed_ms": elapsed_ms,
                "error_type": type(exc).__name__,
                "error": str(exc),
            },
            tool_call_id=current_tool_call_id,
            tool_name=current_tool_name,
        )
        raise


async def _get(params: dict | None = None) -> str:
    return await _request_text("GET", _sp_url(), params=params)


async def _post(body: dict) -> str:
    return await _request_text("POST", _sp_url(), json_body=body)


async def _mp_get(params: dict | None = None) -> str:
    return await _request_text("GET", _mp_url(), params=params)


async def _mp_post(body: dict) -> str:
    return await _request_text("POST", _mp_url(), json_body=body)


async def _profile_get() -> str:
    return await _request_text("GET", _profile_url())


async def _profiles_get() -> str:
    return await _request_text("GET", _profiles_url())


async def _profiles_post(body: dict) -> str:
    return await _request_text("POST", _profiles_url(), json_body=body)


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


def logged_tool(*tool_args: Any, **tool_kwargs: Any) -> Callable[[Callable[..., Awaitable[str]]], Callable[..., Awaitable[str]]]:
    def decorator(func: Callable[..., Awaitable[str]]) -> Callable[..., Awaitable[str]]:
        @functools.wraps(func)
        async def wrapper(*args: Any, **kwargs: Any) -> str:
            call_id = uuid.uuid4().hex
            call_id_token = _tool_call_id.set(call_id)
            name_token = _tool_name.set(func.__name__)
            started = time.perf_counter()
            try:
                bound = inspect.signature(func).bind_partial(*args, **kwargs)
                tool_args_payload = dict(bound.arguments)
            except Exception:
                tool_args_payload = {"args": list(args), "kwargs": kwargs}

            await _run_logger.log(
                "tool_call_start",
                {"args": tool_args_payload},
                tool_call_id=call_id,
                tool_name=func.__name__,
            )
            try:
                result = await func(*args, **kwargs)
            except Exception as exc:
                elapsed_ms = round((time.perf_counter() - started) * 1000, 3)
                await _run_logger.log(
                    "tool_call_error",
                    {
                        "elapsed_ms": elapsed_ms,
                        "error_type": type(exc).__name__,
                        "error": str(exc),
                    },
                    tool_call_id=call_id,
                    tool_name=func.__name__,
                )
                raise
            else:
                elapsed_ms = round((time.perf_counter() - started) * 1000, 3)
                await _run_logger.log(
                    "tool_call_result",
                    {
                        "elapsed_ms": elapsed_ms,
                        "result": _run_logger.summarize_text(result),
                    },
                    tool_call_id=call_id,
                    tool_name=func.__name__,
                )
                return result
            finally:
                _tool_call_id.reset(call_id_token)
                _tool_name.reset(name_token)

        return mcp.tool(*tool_args, **tool_kwargs)(wrapper)

    return decorator


def _coerce_wait_timeout(wait_timeout: float) -> float:
    return max(0.0, min(60.0, wait_timeout))


def _coerce_poll_interval(poll_interval: float) -> float:
    return max(0.1, min(5.0, poll_interval))


def _state_actionability(state: dict[str, Any]) -> tuple[bool, str]:
    state_type = str(state.get("state_type") or "unknown")

    if state_type in COMBAT_STATE_TYPES:
        battle = state.get("battle") if isinstance(state.get("battle"), dict) else {}
        is_play_phase = battle.get("is_play_phase") is True
        turn = str(battle.get("turn") or "").lower()
        if is_play_phase and turn == "player":
            return True, "combat_player_turn"
        return False, "combat_waiting_for_player_turn"

    if state_type == "event":
        event = state.get("event") if isinstance(state.get("event"), dict) else {}
        if event.get("in_dialogue") is True:
            return True, "event_dialogue_actionable"
        options = event.get("options") if isinstance(event.get("options"), list) else []
        if any(isinstance(opt, dict) and opt.get("is_locked") is not True for opt in options):
            return True, "event_option_actionable"
        return False, "event_waiting_for_options"

    if state_type in {"rewards", "rest_site", "shop", "fake_merchant", "crystal_sphere", "treasure"}:
        payload = state.get(state_type) if isinstance(state.get(state_type), dict) else {}
        if payload.get("can_proceed") is True:
            return True, f"{state_type}_can_proceed"
        for key in ("items", "options", "cards", "relics", "cells"):
            value = payload.get(key)
            if isinstance(value, list) and len(value) > 0:
                return True, f"{state_type}_{key}_available"
        return False, f"{state_type}_waiting"

    return True, f"{state_type}_actionable"


async def _get_smart_state(
    getter: Callable[[dict | None], Awaitable[str]],
    params: dict[str, Any],
    *,
    wait_for_actionable: bool,
    wait_timeout: float,
    poll_interval: float,
) -> str:
    if not wait_for_actionable:
        return await getter(params)

    timeout = _coerce_wait_timeout(wait_timeout)
    interval = _coerce_poll_interval(poll_interval)
    requested_format = str(params.get("format") or "json")
    detection_params = dict(params)
    detection_params["format"] = "json"
    deadline = time.monotonic() + timeout
    attempt = 0
    last_json_text = ""
    last_reason = "not_polled"
    previous_state_hash: str | None = None

    while True:
        attempt += 1
        last_json_text = await getter(detection_params)
        state_hash = hashlib.sha256(last_json_text.encode("utf-8", errors="replace")).hexdigest()
        state_repeated = previous_state_hash == state_hash
        previous_state_hash = state_hash
        try:
            state = json.loads(last_json_text)
        except json.JSONDecodeError:
            hidden_tokens = 0 if requested_format == "json" else _run_logger.token_estimator.estimate_text(last_json_text)
            await _run_logger.log(
                "state_poll",
                {
                    "attempt": attempt,
                    "actionable": True,
                    "reason": "state_json_parse_failed",
                    "hidden_poll_tokens": hidden_tokens,
                    "state_sha256": state_hash,
                    "state_repeated": state_repeated,
                },
                tool_call_id=_tool_call_id.get(),
                tool_name=_tool_name.get(),
            )
            return await getter(params) if requested_format != "json" else last_json_text

        actionable, reason = _state_actionability(state)
        last_reason = reason
        timed_out = time.monotonic() >= deadline
        returns_detection_payload = requested_format == "json" and (actionable or timed_out)
        hidden_poll_tokens = 0 if returns_detection_payload else _run_logger.token_estimator.estimate_text(last_json_text)
        await _run_logger.log(
            "state_poll",
            {
                "attempt": attempt,
                "state_type": state.get("state_type"),
                "game_mode": state.get("game_mode"),
                "actionable": actionable,
                "reason": reason,
                "timeout_seconds": timeout,
                "hidden_poll_tokens": hidden_poll_tokens,
                "state_sha256": state_hash,
                "state_repeated": state_repeated,
            },
            tool_call_id=_tool_call_id.get(),
            tool_name=_tool_name.get(),
        )
        if actionable or timed_out:
            break
        await asyncio.sleep(min(interval, max(0.0, deadline - time.monotonic())))

    if requested_format == "json":
        return last_json_text

    await _run_logger.log(
        "state_poll_final_format",
        {
            "format": requested_format,
            "last_reason": last_reason,
            "attempts": attempt,
        },
        tool_call_id=_tool_call_id.get(),
        tool_name=_tool_name.get(),
    )
    return await getter(params)


# ---------------------------------------------------------------------------
# General
# ---------------------------------------------------------------------------


@logged_tool()
async def get_game_state(
    format: str = "markdown",
    wait_for_actionable: bool = True,
    wait_timeout: float = 8.0,
    poll_interval: float = 1.0,
) -> str:
    """Get the current Slay the Spire 2 game state.

    Returns the full game state including player stats, hand, enemies, potions, etc.
    The state_type field indicates the current screen (combat, map, event, shop,
    fake_merchant, etc.).

    Args:
        format: "markdown" for human-readable output, "json" for structured data.
        wait_for_actionable: Poll briefly through transient states such as enemy turns.
        wait_timeout: Maximum seconds to wait for an actionable state.
        poll_interval: Seconds between state polls while waiting.
    """
    try:
        return await _get_smart_state(
            _get,
            {"format": format},
            wait_for_actionable=wait_for_actionable,
            wait_timeout=wait_timeout,
            poll_interval=poll_interval,
        )
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def log_agent_decision(
    summary: str,
    reasoning: str | None = None,
    intended_action: str | None = None,
    alternatives: list[str] | None = None,
    confidence: float | None = None,
    tags: list[str] | None = None,
) -> str:
    """Record an explicit agent decision in the run log.

    Use before important choices so later run review can connect game state,
    candidate actions, and the agent's stated decision.

    Args:
        summary: Short decision summary.
        reasoning: Optional rationale or policy note.
        intended_action: Optional next tool/action the agent expects to call.
        alternatives: Optional rejected alternatives.
        confidence: Optional confidence score from 0.0 to 1.0.
        tags: Optional labels for later filtering.
    """
    event = {
        "summary": summary,
        "reasoning": reasoning,
        "intended_action": intended_action,
        "alternatives": alternatives or [],
        "confidence": confidence,
        "tags": tags or [],
    }
    await _run_logger.log(
        "agent_decision",
        event,
        tool_call_id=_tool_call_id.get(),
        tool_name=_tool_name.get(),
    )
    return json.dumps({"status": "ok", "logged": event}, indent=2)


@logged_tool()
async def log_external_token_usage(usage_json: str) -> str:
    """Record exact or externally supplied model token usage.

    Use this when an agent/client has model API usage data that should be
    reconciled with the local estimates in the run log. The JSON object should
    include a stable reference such as `related_tool_call_id`, `related_event_id`,
    or `stable_id`, plus token fields from the provider.

    Example:
      {"related_tool_call_id":"...", "model":"claude-sonnet-4.6",
       "input_tokens":1200, "output_tokens":400, "token_source":"exact"}

    Args:
        usage_json: JSON object with externally supplied usage metadata.
    """
    try:
        usage = json.loads(usage_json)
    except json.JSONDecodeError as exc:
        return json.dumps({"status": "error", "error": f"Invalid usage_json: {exc}"}, indent=2)

    if not isinstance(usage, dict):
        return json.dumps({"status": "error", "error": "usage_json must be a JSON object"}, indent=2)

    token_usage = TokenEstimator.normalize_external_usage(usage, _run_logger.token_profile)
    payload = dict(usage)
    payload.update(token_usage)
    await _run_logger.log(
        "external_token_usage",
        payload,
        tool_call_id=_tool_call_id.get(),
        tool_name=_tool_name.get(),
        token_usage=token_usage,
    )
    return json.dumps({"status": "ok", "logged": payload}, indent=2, sort_keys=True)


@logged_tool()
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
        return await _post(body)
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def get_profile() -> str:
    """Get the current profile's persistent progress summary.

    Includes character stats, discovered content, achievements, epochs, and global
    run totals for the active profile.
    """
    try:
        return await _profile_get()
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def list_profiles() -> str:
    """List the three profile slots and identify the active slot.

    The has_data field indicates whether a slot currently has progress data.
    """
    try:
        return await _profiles_get()
    except Exception as e:
        return _handle_error(e)


@logged_tool()
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


@logged_tool()
async def delete_profile(profile_id: int) -> str:
    """Delete an inactive profile slot.

    The active profile cannot be deleted through this tool. Switch away first if
    you intend to remove a slot after backing up any data you need.

    Args:
        profile_id: Profile slot to delete. Must be 1, 2, or 3.
    """
    try:
        return await _profiles_post({"action": "delete", "profile_id": profile_id})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def use_potion(slot: int, target: str | None = None) -> str:
    """Use a potion from the player's potion slots.

    Works both during and outside of combat. Combat-only potions require an active battle.

    Args:
        slot: Potion slot index (as shown in game state).
        target: Entity ID of the target enemy (e.g. "JAW_WORM_0"). Required for enemy-targeted potions.
    """
    body: dict = {"action": "use_potion", "slot": slot}
    if target is not None:
        body["target"] = target
    try:
        return await _post(body)
    except Exception as e:
        return _handle_error(e)


@logged_tool()
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


@logged_tool()
async def proceed_to_map() -> str:
    """Proceed from the current screen to the map.

    Works from: rewards screen, rest site, shop, fake merchant.
    Does NOT work for events — use event_choose_option() with the Proceed option's index.
    """
    try:
        return await _post({"action": "proceed"})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Combat (state_type: monster / elite / boss)
# ---------------------------------------------------------------------------


@logged_tool()
async def combat_play_card(card_index: int, target: str | None = None) -> str:
    """[Combat] Play a card from the player's hand.

    Args:
        card_index: Index of the card in hand (0-based, as shown in game state).
        target: Entity ID of the target enemy (e.g. "JAW_WORM_0"). Required for single-target cards.

    Note that the index can change as cards are played - playing a card will shift the indices of remaining cards in hand.
    Refer to the latest game state for accurate indices. New cards are drawn to the right, so playing cards from right to left can help maintain more stable indices for remaining cards.
    """
    body: dict = {"action": "play_card", "card_index": card_index}
    if target is not None:
        body["target"] = target
    try:
        return await _post(body)
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def combat_end_turn() -> str:
    """[Combat] End the player's current turn."""
    try:
        return await _post({"action": "end_turn"})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# In-Combat Card Selection (state_type: hand_select)
# ---------------------------------------------------------------------------


@logged_tool()
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


@logged_tool()
async def combat_confirm_selection() -> str:
    """[Combat Selection] Confirm the in-combat card selection.

    After selecting the required number of cards from hand (exhaust, discard, etc.),
    use this to confirm the selection. Only works when the confirm button is enabled.
    """
    try:
        return await _post({"action": "combat_confirm_selection"})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Rewards (state_type: rewards / card_reward)
# ---------------------------------------------------------------------------


@logged_tool()
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


@logged_tool()
async def rewards_pick_card(card_index: int) -> str:
    """[Rewards] Select a card from the card reward selection screen.

    Args:
        card_index: 0-based index of the card to add to the deck.
    """
    try:
        return await _post({"action": "select_card_reward", "card_index": card_index})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def rewards_skip_card() -> str:
    """[Rewards] Skip the card reward without selecting a card."""
    try:
        return await _post({"action": "skip_card_reward"})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Map (state_type: map)
# ---------------------------------------------------------------------------


@logged_tool()
async def map_choose_node(node_index: int) -> str:
    """[Map] Choose a map node to travel to.

    Args:
        node_index: 0-based index of the node from the next_options list.
    """
    try:
        return await _post({"action": "choose_map_node", "index": node_index})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Rest Site (state_type: rest_site)
# ---------------------------------------------------------------------------


@logged_tool()
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


@logged_tool()
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


@logged_tool()
async def event_choose_option(option_index: int) -> str:
    """[Event] Choose an event option.

    Works for both regular events and ancients (after dialogue ends).
    Also used to click the Proceed option after an event resolves.

    Args:
        option_index: 0-based index of the option from the event state.
    """
    try:
        return await _post({"action": "choose_event_option", "index": option_index})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def event_advance_dialogue() -> str:
    """[Event] Advance ancient event dialogue.

    Click through dialogue text in ancient events. Call repeatedly until options appear.
    """
    try:
        return await _post({"action": "advance_dialogue"})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Card Selection (state_type: card_select)
# ---------------------------------------------------------------------------


@logged_tool()
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


@logged_tool()
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


@logged_tool()
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


@logged_tool()
async def bundle_select(bundle_index: int) -> str:
    """[Bundle Selection] Open a bundle preview.

    Args:
        bundle_index: 0-based index of the bundle.
    """
    try:
        return await _post({"action": "select_bundle", "index": bundle_index})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def bundle_confirm_selection() -> str:
    """[Bundle Selection] Confirm the currently previewed bundle."""
    try:
        return await _post({"action": "confirm_bundle_selection"})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def bundle_cancel_selection() -> str:
    """[Bundle Selection] Cancel the current bundle preview."""
    try:
        return await _post({"action": "cancel_bundle_selection"})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Relic Selection (state_type: relic_select)
# ---------------------------------------------------------------------------


@logged_tool()
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


@logged_tool()
async def relic_skip() -> str:
    """[Relic Selection] Skip the relic selection without choosing a relic."""
    try:
        return await _post({"action": "skip_relic_selection"})
    except Exception as e:
        return _handle_error(e)


# ---------------------------------------------------------------------------
# Treasure (state_type: treasure)
# ---------------------------------------------------------------------------


@logged_tool()
async def treasure_claim_relic(relic_index: int) -> str:
    """[Treasure] Claim a relic from the treasure chest.

    The chest is auto-opened when entering the treasure room.
    After claiming, use proceed_to_map() to continue.

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


@logged_tool()
async def crystal_sphere_set_tool(tool: str) -> str:
    """[Crystal Sphere] Switch the active divination tool.

    Args:
        tool: Either "big" or "small".
    """
    try:
        return await _post({"action": "crystal_sphere_set_tool", "tool": tool})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
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


@logged_tool()
async def crystal_sphere_proceed() -> str:
    """[Crystal Sphere] Continue after the Crystal Sphere minigame finishes."""
    try:
        return await _post({"action": "crystal_sphere_proceed"})
    except Exception as e:
        return _handle_error(e)


# ===========================================================================
# MULTIPLAYER tools — all route through /api/v1/multiplayer
# ===========================================================================


@logged_tool()
async def mp_get_game_state(
    format: str = "markdown",
    wait_for_actionable: bool = True,
    wait_timeout: float = 8.0,
    poll_interval: float = 1.0,
) -> str:
    """[Multiplayer] Get the current multiplayer game state.

    Returns a summary of all players (HP, gold, alive status) plus full
    detail for the local player (relics, potions, deck, etc.), along with
    multiplayer-specific data: map votes, event votes, treasure bids,
    end-turn ready status. Only works during a multiplayer run.

    Args:
        format: "markdown" for human-readable output, "json" for structured data.
        wait_for_actionable: Poll briefly through transient states such as enemy turns.
        wait_timeout: Maximum seconds to wait for an actionable state.
        poll_interval: Seconds between state polls while waiting.
    """
    try:
        return await _get_smart_state(
            _mp_get,
            {"format": format},
            wait_for_actionable=wait_for_actionable,
            wait_timeout=wait_timeout,
            poll_interval=poll_interval,
        )
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_combat_play_card(card_index: int, target: str | None = None) -> str:
    """[Multiplayer Combat] Play a card from the local player's hand.

    Same as singleplayer combat_play_card but routed through the multiplayer
    endpoint for sync safety.

    Args:
        card_index: Index of the card in hand (0-based).
        target: Entity ID of the target enemy (e.g. "JAW_WORM_0"). Required for single-target cards.
    """
    body: dict = {"action": "play_card", "card_index": card_index}
    if target is not None:
        body["target"] = target
    try:
        return await _mp_post(body)
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_combat_end_turn() -> str:
    """[Multiplayer Combat] Submit end-turn vote.

    In multiplayer, ending the turn is a VOTE — the turn only ends when ALL
    players have submitted. Use mp_combat_undo_end_turn() to retract.
    """
    try:
        return await _mp_post({"action": "end_turn"})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_combat_undo_end_turn() -> str:
    """[Multiplayer Combat] Retract end-turn vote.

    If you submitted end turn but want to play more cards, use this to undo.
    Only works if other players haven't all committed yet.
    """
    try:
        return await _mp_post({"action": "undo_end_turn"})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_use_potion(slot: int, target: str | None = None) -> str:
    """[Multiplayer] Use a potion from the local player's potion slots.

    Args:
        slot: Potion slot index (as shown in game state).
        target: Entity ID of the target enemy. Required for enemy-targeted potions.
    """
    body: dict = {"action": "use_potion", "slot": slot}
    if target is not None:
        body["target"] = target
    try:
        return await _mp_post(body)
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_discard_potion(slot: int) -> str:
    """[Multiplayer] Discard a potion from the local player's potion slots to free up space.

    Args:
        slot: Potion slot index to discard (as shown in game state).
    """
    try:
        return await _mp_post({"action": "discard_potion", "slot": slot})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_map_vote(node_index: int) -> str:
    """[Multiplayer Map] Vote for a map node to travel to.

    In multiplayer, map selection is a vote — travel happens when all players
    agree. Re-voting for the same node sends a ping to other players.

    Args:
        node_index: 0-based index of the node from the next_options list.
    """
    try:
        return await _mp_post({"action": "choose_map_node", "index": node_index})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_event_choose_option(option_index: int) -> str:
    """[Multiplayer Event] Choose or vote for an event option.

    For shared events: this is a vote (resolves when all players vote).
    For individual events: immediate choice, same as singleplayer.

    Args:
        option_index: 0-based index of the option from the event state.
    """
    try:
        return await _mp_post({"action": "choose_event_option", "index": option_index})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_event_advance_dialogue() -> str:
    """[Multiplayer Event] Advance ancient event dialogue."""
    try:
        return await _mp_post({"action": "advance_dialogue"})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_rest_choose_option(option_index: int) -> str:
    """[Multiplayer Rest Site] Choose a rest site option (rest, smith, etc.).

    Per-player choice — no voting needed.

    Args:
        option_index: 0-based index of the option.
    """
    try:
        return await _mp_post({"action": "choose_rest_option", "index": option_index})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
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


@logged_tool()
async def mp_rewards_claim(reward_index: int) -> str:
    """[Multiplayer Rewards] Claim a reward from the post-combat rewards screen.

    Args:
        reward_index: 0-based index of the reward.
    """
    try:
        return await _mp_post({"action": "claim_reward", "index": reward_index})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_rewards_pick_card(card_index: int) -> str:
    """[Multiplayer Rewards] Select a card from the card reward screen.

    Args:
        card_index: 0-based index of the card to add to the deck.
    """
    try:
        return await _mp_post({"action": "select_card_reward", "card_index": card_index})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_rewards_skip_card() -> str:
    """[Multiplayer Rewards] Skip the card reward."""
    try:
        return await _mp_post({"action": "skip_card_reward"})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_proceed_to_map() -> str:
    """[Multiplayer] Proceed from the current screen to the map.

    Works from: rewards screen, rest site, shop.
    """
    try:
        return await _mp_post({"action": "proceed"})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_deck_select_card(card_index: int) -> str:
    """[Multiplayer Card Selection] Select or deselect a card in the card selection screen.

    Args:
        card_index: 0-based index of the card.
    """
    try:
        return await _mp_post({"action": "select_card", "index": card_index})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_deck_confirm_selection() -> str:
    """[Multiplayer Card Selection] Confirm the current card selection."""
    try:
        return await _mp_post({"action": "confirm_selection"})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_deck_cancel_selection() -> str:
    """[Multiplayer Card Selection] Cancel the current card selection."""
    try:
        return await _mp_post({"action": "cancel_selection"})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_bundle_select(bundle_index: int) -> str:
    """[Multiplayer Bundle Selection] Open a bundle preview.

    Args:
        bundle_index: 0-based index of the bundle.
    """
    try:
        return await _mp_post({"action": "select_bundle", "index": bundle_index})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_bundle_confirm_selection() -> str:
    """[Multiplayer Bundle Selection] Confirm the currently previewed bundle."""
    try:
        return await _mp_post({"action": "confirm_bundle_selection"})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_bundle_cancel_selection() -> str:
    """[Multiplayer Bundle Selection] Cancel the current bundle preview."""
    try:
        return await _mp_post({"action": "cancel_bundle_selection"})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_combat_select_card(card_index: int) -> str:
    """[Multiplayer Combat Selection] Select a card from hand during in-combat card selection.

    Args:
        card_index: 0-based index of the card in the selectable hand cards.
    """
    try:
        return await _mp_post({"action": "combat_select_card", "card_index": card_index})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_combat_confirm_selection() -> str:
    """[Multiplayer Combat Selection] Confirm the in-combat card selection."""
    try:
        return await _mp_post({"action": "combat_confirm_selection"})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_relic_select(relic_index: int) -> str:
    """[Multiplayer Relic Selection] Select a relic (boss relic rewards).

    Args:
        relic_index: 0-based index of the relic.
    """
    try:
        return await _mp_post({"action": "select_relic", "index": relic_index})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
async def mp_relic_skip() -> str:
    """[Multiplayer Relic Selection] Skip the relic selection."""
    try:
        return await _mp_post({"action": "skip_relic_selection"})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
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


@logged_tool()
async def mp_crystal_sphere_set_tool(tool: str) -> str:
    """[Multiplayer Crystal Sphere] Switch the active divination tool.

    Args:
        tool: Either "big" or "small".
    """
    try:
        return await _mp_post({"action": "crystal_sphere_set_tool", "tool": tool})
    except Exception as e:
        return _handle_error(e)


@logged_tool()
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


@logged_tool()
async def mp_crystal_sphere_proceed() -> str:
    """[Multiplayer Crystal Sphere] Continue after the Crystal Sphere minigame finishes."""
    try:
        return await _mp_post({"action": "crystal_sphere_proceed"})
    except Exception as e:
        return _handle_error(e)


def main():
    parser = argparse.ArgumentParser(description="STS2 MCP Server")
    parser.add_argument("--port", type=int, default=15526, help="Game HTTP server port")
    parser.add_argument("--host", type=str, default="localhost", help="Game HTTP server host")
    parser.add_argument("--no-trust-env", action="store_true", help="Ignore HTTP_PROXY/HTTPS_PROXY environment variables")
    parser.add_argument(
        "--log-dir",
        default=os.environ.get("STS2_MCP_LOG_DIR", "logs"),
        help="Directory for structured JSONL run logs (default: logs)",
    )
    parser.add_argument("--disable-run-log", action="store_true", help="Disable MCP tool/http run logging")
    parser.add_argument(
        "--log-preview-chars",
        type=int,
        default=4000,
        help="Maximum characters stored in each logged text preview",
    )
    parser.add_argument(
        "--log-full-text",
        action="store_true",
        help="Store complete tool and HTTP response text in logs instead of previews plus hashes only",
    )
    parser.add_argument(
        "--tokenizer-profile",
        default="generic_regex_v1",
        help="Token accounting profile: generic_regex_v1, openai_cl100k_proxy, anthropic_claude_proxy, or a custom name",
    )
    parser.add_argument("--token-model-family", default=None, help="Override token accounting model family")
    parser.add_argument("--tokenizer-name", default=None, help="Override tokenizer name recorded in logs")
    parser.add_argument("--tokenizer-version", default=None, help="Override tokenizer version recorded in logs")
    parser.add_argument("--token-estimation-method", default=None, help="Override token estimation method recorded in logs")
    args = parser.parse_args()

    global _base_url, _trust_env, _run_logger
    _base_url = f"http://{args.host}:{args.port}"
    _trust_env = not args.no_trust_env
    token_profile = TokenProfile.from_profile_name(args.tokenizer_profile).with_overrides(
        model_family=args.token_model_family,
        tokenizer_name=args.tokenizer_name,
        tokenizer_version=args.tokenizer_version,
        estimation_method=args.token_estimation_method,
    )
    _run_logger = RunLogger(
        enabled=not args.disable_run_log,
        log_dir=args.log_dir,
        preview_chars=args.log_preview_chars,
        include_full_text=args.log_full_text,
        token_profile=token_profile,
    )
    _run_logger.start(
        {
            "base_url": _base_url,
            "trust_env": _trust_env,
            "argv": sys.argv,
            "pid": os.getpid(),
            "python_version": sys.version,
            "platform": platform.platform(),
            "cwd": os.getcwd(),
            "log_full_text": args.log_full_text,
            "log_preview_chars": args.log_preview_chars,
            "token_profile": token_profile.to_dict(),
        }
    )

    # Eagerly initialize the shared httpx client so the first request is fast
    _get_client()

    mcp.run(transport="stdio")


if __name__ == "__main__":
    main()
