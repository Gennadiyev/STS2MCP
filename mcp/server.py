"""MCP server bridge for Slay the Spire 2.

Connects to the STS2_MCP mod's HTTP server and exposes game actions
as MCP tools for Claude Desktop / Claude Code.
"""

import argparse
import datetime as dt
import html
import json
from pathlib import Path
import re
import sys
from urllib.parse import quote

import httpx
from mcp.server.fastmcp import FastMCP

mcp = FastMCP("sts2")

_base_url: str = "http://localhost:15526"
_wiki_api_url: str = "https://slaythespire.wiki.gg/api.php"
_wiki_base_url: str = "https://slaythespire.wiki.gg/wiki/"
_mcp_dir: Path = Path(__file__).resolve().parent
_memory_dir: Path = _mcp_dir / "case_memory"
_memory_file: Path = _memory_dir / "replay_turns.jsonl"
_conflict_lexicon_file: Path = _mcp_dir / "sts2_conflict_lexicon.json"
_trust_env: bool = True


def _sp_url() -> str:
    return f"{_base_url}/api/v1/singleplayer"


def _mp_url() -> str:
    return f"{_base_url}/api/v1/multiplayer"


async def _get(params: dict | None = None) -> str:
    async with httpx.AsyncClient(timeout=10, trust_env=_trust_env) as client:
        r = await client.get(_sp_url(), params=params)
        r.raise_for_status()
        return r.text


async def _post(body: dict) -> str:
    async with httpx.AsyncClient(timeout=10, trust_env=_trust_env) as client:
        r = await client.post(_sp_url(), json=body)
        r.raise_for_status()
        return r.text


async def _mp_get(params: dict | None = None) -> str:
    async with httpx.AsyncClient(timeout=10, trust_env=_trust_env) as client:
        r = await client.get(_mp_url(), params=params)
        r.raise_for_status()
        return r.text


async def _mp_post(body: dict) -> str:
    async with httpx.AsyncClient(timeout=10, trust_env=_trust_env) as client:
        r = await client.post(_mp_url(), json=body)
        r.raise_for_status()
        return r.text


async def _wiki_get(params: dict) -> dict:
    async with httpx.AsyncClient(timeout=12) as client:
        r = await client.get(_wiki_api_url, params=params)
        r.raise_for_status()
        return r.json()


def _strip_html(raw_html: str) -> str:
    text = re.sub(r"<style.*?>.*?</style>", " ", raw_html, flags=re.IGNORECASE | re.DOTALL)
    text = re.sub(r"<script.*?>.*?</script>", " ", text, flags=re.IGNORECASE | re.DOTALL)
    text = re.sub(r"<br\\s*/?>", "\n", text, flags=re.IGNORECASE)
    text = re.sub(r"</p\\s*>", "\n\n", text, flags=re.IGNORECASE)
    text = re.sub(r"</li\\s*>", "\n", text, flags=re.IGNORECASE)
    text = re.sub(r"<[^>]+>", " ", text)
    text = html.unescape(text)
    text = re.sub(r"[ \t]+", " ", text)
    text = re.sub(r"\n{3,}", "\n\n", text)
    return text.strip()


async def _wiki_get_plaintext(title: str, intro_only: bool = False) -> str:
    # First try TextExtracts API for clean plaintext.
    extract_params = {
        "action": "query",
        "prop": "extracts",
        "explaintext": 1,
        "redirects": 1,
        "titles": title,
        "format": "json",
    }
    if intro_only:
        extract_params["exintro"] = 1
    data = await _wiki_get(extract_params)
    pages = data.get("query", {}).get("pages", {})
    if pages:
        page = next(iter(pages.values()))
        extract = (page.get("extract") or "").strip()
        if extract:
            return extract

    # Fallback: parse rendered HTML and strip tags.
    parse_params = {
        "action": "parse",
        "page": title,
        "redirects": 1,
        "format": "json",
        "prop": "text",
    }
    parse_data = await _wiki_get(parse_params)
    html_block = parse_data.get("parse", {}).get("text", {}).get("*", "")
    if not html_block:
        return ""

    plain = _strip_html(html_block)
    if intro_only:
        # Keep only first paragraph-equivalent block for summary.
        chunks = [c.strip() for c in plain.split("\n\n") if c.strip()]
        return chunks[0] if chunks else plain
    return plain


def _handle_error(e: Exception) -> str:
    if isinstance(e, httpx.ConnectError):
        return "Error: Cannot connect to STS2_MCP mod. Is the game running with the mod enabled?"
    if isinstance(e, httpx.HTTPStatusError):
        return f"Error: HTTP {e.response.status_code} — {e.response.text}"
    return f"Error: {e}"


def _handle_wiki_error(e: Exception) -> str:
    if isinstance(e, httpx.ConnectError):
        return "Error: Cannot connect to wiki.gg."
    if isinstance(e, httpx.TimeoutException):
        return "Error: wiki.gg request timed out."
    if isinstance(e, httpx.HTTPStatusError):
        return f"Error: Wiki HTTP {e.response.status_code} — {e.response.text}"
    return f"Error: {e}"


def _wiki_page_url(title: str) -> str:
    return f"{_wiki_base_url}{quote(title.replace(' ', '_'), safe=':/_()')}"


def _read_json_file(path: Path) -> dict:
    if not path.exists():
        return {}
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception:
        return {}


def _extract_incoming_damage_from_markdown(game_state_markdown: str) -> int:
    # Rough parser for lines like "Attack) 8", "Attack) 9×2", "Attack) 8x2"
    total = 0
    for base, count in re.findall(r"Attack\)\s*(\d+)\s*[×x]\s*(\d+)", game_state_markdown):
        total += int(base) * int(count)
    for base in re.findall(r"Attack\)\s*(\d+)(?!\s*[×x])", game_state_markdown):
        total += int(base)
    return total


def _extract_player_hp_block(game_state_markdown: str) -> tuple[int | None, int | None]:
    hp_match = re.search(r"HP:\s*(\d+)\s*/\s*(\d+)", game_state_markdown)
    block_match = re.search(r"Block:\s*(\d+)", game_state_markdown)
    hp = int(hp_match.group(1)) if hp_match else None
    block = int(block_match.group(1)) if block_match else None
    return hp, block


def _load_conflict_lexicon() -> dict:
    data = _read_json_file(_conflict_lexicon_file)
    if not data:
        return {
            "sts1_terms_to_review": [
                "Artifact",
                "Intangible",
                "Dark Embrace",
                "Corruption",
                "Dead Branch",
            ],
            "notes": "Default lightweight fallback lexicon.",
        }
    return data


def _append_memory(record: dict) -> None:
    _memory_dir.mkdir(parents=True, exist_ok=True)
    with _memory_file.open("a", encoding="utf-8") as f:
        f.write(json.dumps(record, ensure_ascii=False) + "\n")


# ---------------------------------------------------------------------------
# General
# ---------------------------------------------------------------------------


@mcp.tool()
async def get_game_state(format: str = "markdown") -> str:
    """Get the current Slay the Spire 2 game state.

    Returns the full game state including player stats, hand, enemies, potions, etc.
    The state_type field indicates the current screen (combat, map, event, shop,
    fake_merchant, etc.).

    Args:
        format: "markdown" for human-readable output, "json" for structured data.
    """
    try:
        return await _get({"format": format})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
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
async def proceed_to_map() -> str:
    """Proceed from the current screen to the map.

    Works from: rewards screen, rest site, shop, fake merchant.
    Does NOT work for events — use event_choose_option() with the Proceed option's index.
    """
    try:
        return await _post({"action": "proceed"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def sts2_wiki_search(query: str, limit: int = 5) -> str:
    """Search the Slay the Spire 2 wiki.gg for relevant pages.

    Use this before making gameplay decisions to avoid mixing STS1 data with STS2 data.

    Args:
        query: Search keyword (card, relic, enemy, event, mechanic, etc.).
        limit: Maximum number of search results to return (1-10).
    """
    max_results = max(1, min(limit, 10))
    params = {
        "action": "query",
        "list": "search",
        "srsearch": query,
        "srlimit": max_results,
        "format": "json",
    }
    try:
        data = await _wiki_get(params)
        search_items = data.get("query", {}).get("search", [])
        if not search_items:
            return f"No wiki results found for: {query}"

        lines = [f"Wiki search results for '{query}':"]
        for i, item in enumerate(search_items, start=1):
            title = item.get("title", "Unknown")
            snippet = item.get("snippet", "").replace("<span class=\"searchmatch\">", "").replace("</span>", "")
            page_url = _wiki_page_url(title)
            lines.append(f"{i}. {title}")
            if snippet:
                lines.append(f"   snippet: {snippet}")
            lines.append(f"   url: {page_url}")
        return "\n".join(lines)
    except Exception as e:
        return _handle_wiki_error(e)


@mcp.tool()
async def sts2_wiki_get_summary(title: str) -> str:
    """Fetch a concise summary for a specific STS2 wiki page title.

    Args:
        title: Exact or near-exact wiki page title.
    """
    try:
        # Resolve canonical page title first for better URL and redirect handling.
        title_data = await _wiki_get(
            {"action": "query", "titles": title, "redirects": 1, "format": "json"}
        )
        pages = title_data.get("query", {}).get("pages", {})
        page = next(iter(pages.values())) if pages else {}
        page_title = page.get("title", title)

        extract = await _wiki_get_plaintext(page_title, intro_only=True)
        if not extract:
            return f"Page found but no summary extract available: {page_title}\nurl: {_wiki_page_url(page_title)}"
        return f"{page_title}\nurl: {_wiki_page_url(page_title)}\n\n{extract}"
    except Exception as e:
        return _handle_wiki_error(e)


@mcp.tool()
async def sts2_wiki_get_page(title: str, section_chars: int = 4000) -> str:
    """Fetch richer plain-text page content from STS2 wiki.

    Args:
        title: Wiki page title.
        section_chars: Max characters to return from page extract (200-12000).
    """
    char_limit = max(200, min(section_chars, 12000))
    try:
        title_data = await _wiki_get(
            {"action": "query", "titles": title, "redirects": 1, "format": "json"}
        )
        pages = title_data.get("query", {}).get("pages", {})
        page = next(iter(pages.values())) if pages else {}
        page_title = page.get("title", title)

        extract = await _wiki_get_plaintext(page_title, intro_only=False)
        if not extract:
            return f"Page found but extract is empty: {page_title}\nurl: {_wiki_page_url(page_title)}"
        if len(extract) > char_limit:
            extract = extract[:char_limit].rstrip() + "\n\n... (truncated)"
        return f"{page_title}\nurl: {_wiki_page_url(page_title)}\n\n{extract}"
    except Exception as e:
        return _handle_wiki_error(e)


@mcp.tool()
async def sts2_knowledge_gate(question: str, terms: list[str] | None = None, per_term_limit: int = 3) -> str:
    """Run STS2-only knowledge gating before decision making.

    This checks potentially ambiguous terms against STS2 wiki search results and
    flags terms that need confirmation before use.

    Args:
        question: Current decision question or context.
        terms: Optional explicit terms to verify. If omitted, coarse terms are auto-extracted.
        per_term_limit: Search results per term (1-5).
    """
    max_results = max(1, min(per_term_limit, 5))
    lexicon = _load_conflict_lexicon()
    suspect_terms = lexicon.get("sts1_terms_to_review", [])

    if terms:
        check_terms = [t.strip() for t in terms if t and t.strip()]
    else:
        tokens = re.findall(r"[A-Za-z][A-Za-z0-9_+'-]{2,}", question)
        check_terms = sorted(set(tokens))[:8]

    conflict_hits = [t for t in check_terms if t in suspect_terms]
    verified: list[dict] = []
    unknown: list[str] = []

    for term in check_terms:
        try:
            data = await _wiki_get(
                {
                    "action": "query",
                    "list": "search",
                    "srsearch": term,
                    "srlimit": max_results,
                    "format": "json",
                }
            )
            items = data.get("query", {}).get("search", [])
            if not items:
                unknown.append(term)
                continue
            top = items[0]
            title = top.get("title", term)
            verified.append(
                {
                    "term": term,
                    "top_title": title,
                    "top_url": _wiki_page_url(title),
                    "results": len(items),
                }
            )
        except Exception:
            unknown.append(term)

    result = {
        "question": question,
        "sts2_only": True,
        "status": "needs_review" if conflict_hits or unknown else "ok",
        "conflict_hits": conflict_hits,
        "unknown_terms": unknown,
        "verified_terms": verified,
        "guidance": [
            "Use only terms with STS2 wiki evidence in this turn.",
            "If status is needs_review, mark uncertain mechanics explicitly.",
            "Do not import STS1 assumptions without STS2 citation.",
        ],
    }
    return json.dumps(result, ensure_ascii=False, indent=2)


@mcp.tool()
async def sts2_replay_log_turn(entry_json: str) -> str:
    """Store one structured replay turn record into local memory index.

    Args:
        entry_json: JSON string following replay_schema fields.
    """
    try:
        entry = json.loads(entry_json)
    except json.JSONDecodeError as e:
        return f"Error: invalid JSON input: {e}"

    required = ["battle_id", "turn", "snapshot", "intent", "expected", "actual", "lessons"]
    missing = [k for k in required if k not in entry]
    if missing:
        return f"Error: missing required fields: {', '.join(missing)}"

    record = {
        "created_at": dt.datetime.utcnow().isoformat() + "Z",
        "source": "sts2_replay_log_turn",
        **entry,
    }
    try:
        _append_memory(record)
        return f"ok: replay turn stored in {_memory_file}"
    except Exception as e:
        return f"Error: failed writing memory: {e}"


@mcp.tool()
async def sts2_replay_search(query: str, limit: int = 5) -> str:
    """Search local replay memory by keyword for similar cases.

    Args:
        query: Keyword(s), usually enemy/card/relic/mechanic.
        limit: Max records to return (1-20).
    """
    if not _memory_file.exists():
        return "No replay memory yet. Use sts2_replay_log_turn first."

    max_results = max(1, min(limit, 20))
    q = query.lower().strip()
    matches: list[dict] = []
    with _memory_file.open("r", encoding="utf-8") as f:
        for raw in f:
            raw = raw.strip()
            if not raw:
                continue
            if q in raw.lower():
                try:
                    matches.append(json.loads(raw))
                except Exception:
                    continue
            if len(matches) >= max_results:
                break

    if not matches:
        return f"No replay matches found for: {query}"

    lines = [f"Replay matches for '{query}' ({len(matches)}):"]
    for i, m in enumerate(matches, start=1):
        lines.append(
            f"{i}. battle={m.get('battle_id')} turn={m.get('turn')} "
            f"enemy={m.get('snapshot', {}).get('enemy_name', 'n/a')} "
            f"lesson={m.get('lessons', '')}"
        )
    return "\n".join(lines)


@mcp.tool()
async def sts2_heuristic_think(game_state_markdown: str, top_k: int = 2) -> str:
    """Generate a standardized 6-step heuristic chain-of-thought scaffold.

    Args:
        game_state_markdown: Current game state in markdown format.
        top_k: Number of candidate action lines to produce (2-5).
    """
    k = max(2, min(top_k, 5))
    incoming = _extract_incoming_damage_from_markdown(game_state_markdown)
    hp, block = _extract_player_hp_block(game_state_markdown)
    effective_hp = (hp or 0) + (block or 0) if hp is not None and block is not None else None
    lethal_now = effective_hp is not None and incoming >= effective_hp

    lines = [
        "HeuristicCoT:",
        f"1) ThreatCheck: incoming={incoming}, hp={hp}, block={block}, effective_hp={effective_hp}, lethal_now={lethal_now}",
        "2) WinCondition: identify the shortest survivable path to reduce next-turn lethal pressure.",
        "3) ResourcePlan: assign energy/potion/power windows before committing attacks.",
        "4) TargetPriority: focus the unit that most increases incoming damage or revive pressure.",
        "5) ActionSequence: play index-sensitive cards safely (prefer re-read after each critical play).",
        "6) PostCheck: estimate remaining incoming damage and fallback options.",
        "",
        f"Top-{k} candidate lines:",
    ]
    for idx in range(1, k + 1):
        lines.append(f"- Candidate {idx}: [plan] [expected_block] [expected_damage] [risk_after_turn]")
    return "\n".join(lines)


@mcp.tool()
async def sts2_guard_action_plan(
    game_state_markdown: str, action_plan: str, reread_hand_before_each_play: bool = False
) -> str:
    """Run rule-based safety checks for a planned action line.

    Args:
        game_state_markdown: Current game state markdown.
        action_plan: Natural-language plan or tool-call sequence.
        reread_hand_before_each_play: Whether hand indices are explicitly re-checked.
    """
    incoming = _extract_incoming_damage_from_markdown(game_state_markdown)
    hp, block = _extract_player_hp_block(game_state_markdown)
    effective_hp = (hp or 0) + (block or 0) if hp is not None and block is not None else None
    high_risk = effective_hp is not None and incoming >= max(1, effective_hp - 3)

    lexicon = _load_conflict_lexicon()
    suspect_terms = lexicon.get("sts1_terms_to_review", [])
    sts1_pollution = [t for t in suspect_terms if re.search(rf"\b{re.escape(t)}\b", action_plan)]

    plan_lower = action_plan.lower()
    mentions_defense = any(k in plan_lower for k in ["defend", "block", "防御", "格挡", "shrug", "iron wave"])

    issues: list[str] = []
    if sts1_pollution:
        issues.append(f"Potential STS1 contamination terms: {', '.join(sts1_pollution)}")
    if high_risk and not mentions_defense:
        issues.append("High-risk turn without explicit defensive action in plan.")
    if "combat_play_card" in action_plan and not reread_hand_before_each_play:
        issues.append("Index-shift risk: combat_play_card used without hand re-read flag.")

    verdict = "unsafe" if issues else "pass"
    result = {
        "verdict": verdict,
        "incoming_damage": incoming,
        "hp": hp,
        "block": block,
        "effective_hp": effective_hp,
        "issues": issues,
        "suggestions": [
            "Re-read hand indices after each critical card play.",
            "If lethal risk is high, prioritize survivability before setup.",
            "Cite STS2 wiki evidence for ambiguous mechanics.",
        ],
    }
    return json.dumps(result, ensure_ascii=False, indent=2)


@mcp.tool()
async def sts2_decision_workflow(
    question: str,
    action_plan: str | None = None,
    terms: list[str] | None = None,
    top_k: int = 2,
    include_wiki_summary: bool = False,
) -> str:
    """Preferred single-entry decision workflow for STS2 turns.

    This orchestrates STS2-only gating + heuristic CoT + optional guard check in one call.
    Use this tool first before calling combat/map/event action tools.

    Args:
        question: Current turn goal/problem statement.
        action_plan: Optional planned line to validate with guard.
        terms: Optional explicit terms to verify against STS2 wiki.
        top_k: Candidate count for heuristic scaffold (2-5).
        include_wiki_summary: If true, include short summaries for verified terms (up to 2 terms).
    """
    try:
        game_state_markdown = await _get({"format": "markdown"})
    except Exception as e:
        return _handle_error(e)

    gate_raw = await sts2_knowledge_gate(question=question, terms=terms, per_term_limit=3)
    try:
        gate = json.loads(gate_raw)
    except Exception:
        gate = {"status": "needs_review", "raw": gate_raw}

    heuristic = await sts2_heuristic_think(game_state_markdown=game_state_markdown, top_k=top_k)

    guard_result: dict | None = None
    if action_plan:
        guard_raw = await sts2_guard_action_plan(
            game_state_markdown=game_state_markdown,
            action_plan=action_plan,
            reread_hand_before_each_play=False,
        )
        try:
            guard_result = json.loads(guard_raw)
        except Exception:
            guard_result = {"verdict": "unknown", "raw": guard_raw}

    summaries: list[dict] = []
    if include_wiki_summary:
        verified = gate.get("verified_terms", []) if isinstance(gate, dict) else []
        for item in verified[:2]:
            title = item.get("top_title")
            if not title:
                continue
            summary = await sts2_wiki_get_summary(title=title)
            summaries.append({"title": title, "summary": summary})

    result = {
        "recommended_entrypoint": "sts2_decision_workflow",
        "question": question,
        "workflow": {
            "knowledge_gate": gate,
            "heuristic_cot": heuristic,
            "guard": guard_result,
            "wiki_summaries": summaries,
        },
        "next_step": "Revise/confirm action plan, then call gameplay tools like combat_play_card.",
    }
    return json.dumps(result, ensure_ascii=False, indent=2)


# ---------------------------------------------------------------------------
# Combat (state_type: monster / elite / boss)
# ---------------------------------------------------------------------------


@mcp.tool()
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


@mcp.tool()
async def combat_end_turn() -> str:
    """[Combat] End the player's current turn."""
    try:
        return await _post({"action": "end_turn"})
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

    Works for both regular events and ancients (after dialogue ends).
    Also used to click the Proceed option after an event resolves.

    Args:
        option_index: 0-based index of the unlocked option.
    """
    try:
        return await _post({"action": "choose_event_option", "index": option_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
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

    Returns full game state for ALL players: HP, powers, relics, potions,
    plus multiplayer-specific data: map votes, event votes, treasure bids,
    end-turn ready status. Only works during a multiplayer run.

    Args:
        format: "markdown" for human-readable output, "json" for structured data.
    """
    try:
        return await _mp_get({"format": format})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
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


@mcp.tool()
async def mp_combat_end_turn() -> str:
    """[Multiplayer Combat] Submit end-turn vote.

    In multiplayer, ending the turn is a VOTE — the turn only ends when ALL
    players have submitted. Use mp_combat_undo_end_turn() to retract.
    """
    try:
        return await _mp_post({"action": "end_turn"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_combat_undo_end_turn() -> str:
    """[Multiplayer Combat] Retract end-turn vote.

    If you submitted end turn but want to play more cards, use this to undo.
    Only works if other players haven't all committed yet.
    """
    try:
        return await _mp_post({"action": "undo_end_turn"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
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


@mcp.tool()
async def mp_discard_potion(slot: int) -> str:
    """[Multiplayer] Discard a potion from the local player's potion slots to free up space.

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

    In multiplayer, map selection is a vote — travel happens when all players
    agree. Re-voting for the same node sends a ping to other players.

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

    For shared events: this is a vote (resolves when all players vote).
    For individual events: immediate choice, same as singleplayer.

    Args:
        option_index: 0-based index of the unlocked option.
    """
    try:
        return await _mp_post({"action": "choose_event_option", "index": option_index})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_event_advance_dialogue() -> str:
    """[Multiplayer Event] Advance ancient event dialogue."""
    try:
        return await _mp_post({"action": "advance_dialogue"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
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
async def mp_rewards_skip_card() -> str:
    """[Multiplayer Rewards] Skip the card reward."""
    try:
        return await _mp_post({"action": "skip_card_reward"})
    except Exception as e:
        return _handle_error(e)


@mcp.tool()
async def mp_proceed_to_map() -> str:
    """[Multiplayer] Proceed from the current screen to the map.

    Works from: rewards screen, rest site, shop.
    """
    try:
        return await _mp_post({"action": "proceed"})
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
    parser.add_argument("--host", type=str, default="localhost", help="Game HTTP server host")
    parser.add_argument("--no-trust-env", action="store_true", help="Ignore HTTP_PROXY/HTTPS_PROXY environment variables")
    args = parser.parse_args()

    global _base_url, _trust_env
    _base_url = f"http://{args.host}:{args.port}"
    _trust_env = not args.no_trust_env

    mcp.run(transport="stdio")


if __name__ == "__main__":
    main()
