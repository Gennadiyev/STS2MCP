#!/usr/bin/env python3
"""Audit the STS2_MCP HTTP endpoint surface.

This script intentionally uses only the Python standard library so it can run
from a checkout without installing the MCP package.
"""

from __future__ import annotations

import argparse
import ast
import json
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path


EXPECTED_ENDPOINTS: list[tuple[str, str]] = [
    ("GET", "/api/v1/singleplayer"),
    ("POST", "/api/v1/singleplayer"),
    ("GET", "/api/v1/multiplayer"),
    ("POST", "/api/v1/multiplayer"),
    ("GET", "/api/v1/settings"),
    ("GET", "/api/v1/profile"),
    ("GET", "/api/v1/compendium"),
    ("GET", "/api/v1/bestiary"),
    ("GET", "/api/v1/glossary/cards"),
    ("GET", "/api/v1/glossary/relics"),
    ("GET", "/api/v1/glossary/potions"),
    ("GET", "/api/v1/glossary/keywords"),
    ("GET", "/api/v1/profiles"),
    ("POST", "/api/v1/profiles"),
]


def fail(message: str) -> None:
    print(f"FAIL: {message}", file=sys.stderr)
    raise SystemExit(1)


def load_json_url(url: str, method: str = "GET", body: bytes | None = None) -> tuple[int, object]:
    headers = {"Accept": "application/json"}
    if body is not None:
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=body, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as exc:
        try:
            return exc.code, json.loads(exc.read().decode("utf-8"))
        except Exception:
            raise RuntimeError(f"{url} returned HTTP {exc.code} with non-JSON body") from exc


def load_text_url(url: str) -> tuple[int, str]:
    req = urllib.request.Request(url, headers={"Accept": "text/markdown"})
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            return response.status, response.read().decode("utf-8")
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read().decode("utf-8", errors="replace")


def assert_error_body(path: str, status: int, data: object) -> None:
    if status < 400:
        return
    if not isinstance(data, dict):
        fail(f"{path} returned HTTP {status} with non-object JSON")
    if data.get("status") != "error" or not data.get("error"):
        fail(f"{path} returned HTTP {status} without status/error fields: {data}")


def audit_docs(repo: Path) -> None:
    raw_full = (repo / "docs" / "raw-full.md").read_text(encoding="utf-8")
    documented = set(re.findall(r"- `(GET|POST)\s+([^`]+)`", raw_full))
    expected = set(EXPECTED_ENDPOINTS)
    missing = sorted(expected - documented)
    extra = sorted(documented - expected)
    if missing:
        fail(f"docs/raw-full.md is missing endpoints: {missing}")
    if extra:
        fail(f"docs/raw-full.md documents unexpected endpoints: {extra}")
    print(f"docs: {len(documented)} endpoints documented")


def extract_action_switches(source: str, marker: str) -> set[str]:
    try:
        section = source[source.index(marker):]
    except ValueError:
        fail(f"could not find {marker}")

    match = re.search(r"return action switch\s*\{(.*?)\n\s*_ =>", section, re.S)
    if not match:
        fail(f"could not parse action switch for {marker}")
    return set(re.findall(r'"([a-z_]+)"\s*=>', match.group(1)))


def audit_action_surface(repo: Path) -> None:
    sp_actions = extract_action_switches((repo / "McpMod.Actions.cs").read_text(encoding="utf-8"), "ExecuteAction")
    mp_actions = extract_action_switches((repo / "McpMod.MultiplayerActions.cs").read_text(encoding="utf-8"), "ExecuteMultiplayerAction")
    server = (repo / "mcp" / "server.py").read_text(encoding="utf-8")
    docs = "\n".join(
        [
            (repo / "docs" / "raw-simplified.md").read_text(encoding="utf-8"),
            (repo / "docs" / "raw-full.md").read_text(encoding="utf-8"),
            (repo / "mcp" / "README.md").read_text(encoding="utf-8"),
        ]
    )

    mcp_posts = set(re.findall(r'"action"\s*:\s*"([a-z_]+)"', server))
    doc_actions = set(re.findall(r"`([a-z_]+)`", docs))

    missing_mcp = sorted((sp_actions | mp_actions) - mcp_posts)
    if missing_mcp:
        fail(f"implemented actions missing MCP wrappers: {missing_mcp}")

    allowed_non_switch_posts = {"menu_select", "switch", "delete"}
    extra_mcp = sorted(mcp_posts - sp_actions - mp_actions - allowed_non_switch_posts)
    if extra_mcp:
        fail(f"MCP posts unknown actions: {extra_mcp}")

    missing_docs = sorted((sp_actions | mp_actions) - doc_actions)
    if missing_docs:
        fail(f"implemented actions missing docs: {missing_docs}")

    print(f"actions: {len(sp_actions)} singleplayer, {len(mp_actions)} multiplayer actions covered")


def audit_mcp_tool_docs(repo: Path) -> None:
    server_path = repo / "mcp" / "server.py"
    server = server_path.read_text(encoding="utf-8")
    readme = (repo / "mcp" / "README.md").read_text(encoding="utf-8")
    module = ast.parse(server, filename=str(server_path))

    tools: list[str] = []
    for node in module.body:
        if not isinstance(node, ast.AsyncFunctionDef):
            continue
        if any(
            isinstance(decorator, ast.Call)
            and isinstance(decorator.func, ast.Attribute)
            and decorator.func.attr == "tool"
            for decorator in node.decorator_list
        ):
            tools.append(node.name)

    missing_readme = [
        tool for tool in tools
        if f"`{tool}(" not in readme and f"`{tool}()`" not in readme
    ]
    if missing_readme:
        fail(f"MCP tools missing mcp/README.md entries: {missing_readme}")

    print(f"mcp: {len(tools)} tools documented")


def audit_static_formatters(repo: Path) -> None:
    formatting = (repo / "McpMod.Formatting.cs").read_text(encoding="utf-8")
    forbidden_patterns = {
        r'card\["name"\].*card\["cost"\]': "card markdown should use FormatCardDetails so upgrades/keywords stay visible",
        r'relic\["name"\].*relic\["description"\]': "relic markdown should use FormatRelicDetails so rarity/keywords stay visible",
        r"List<string>\s+\w+List": "keyword markdown should read keyword objects, not legacy string lists",
    }
    for pattern, message in forbidden_patterns.items():
        if re.search(pattern, formatting):
            fail(message)
    print("formatters: card/relic metadata helpers enforced")


def audit_static_error_shapes(repo: Path) -> None:
    raw_error_writes: list[str] = []
    for path in repo.glob("McpMod*.cs"):
        text = path.read_text(encoding="utf-8")
        if re.search(r'response\.StatusCode\s*=\s*500\s*;\s*SendJson\(\s*response\s*,\s*new Dictionary<string, object\?>', text, re.S):
            raw_error_writes.append(path.name)

    if raw_error_writes:
        fail(f"500 handlers should use SendError for structured error bodies: {sorted(raw_error_writes)}")
    print("errors: structured 500 response helpers enforced")


def audit_static_glossary_scope(repo: Path) -> None:
    fork_endpoints = (repo / "McpMod.ForkEndpoints.cs").read_text(encoding="utf-8")
    relic_match = re.search(
        r"internal static object BuildGlossaryRelics\(\).*?\n    internal static object BuildGlossaryPotions\(\)",
        fork_endpoints,
        re.S,
    )
    if not relic_match:
        fail("could not locate BuildGlossaryRelics for scope audit")
    if "Assembly.GetTypes()" in relic_match.group(0) or "typeof(RelicModel)" in relic_match.group(0):
        fail("relic glossary must stay scoped to active run relic pools, not reflected assembly-wide relics")
    required_shared_pools = {
        "BuildGlossaryCards": "ModelDb.AllSharedCardPools",
        "BuildGlossaryRelics": "ModelDb.RelicPool<SharedRelicPool>",
        "BuildGlossaryPotions": "ModelDb.PotionPool<SharedPotionPool>",
        "BuildGlossaryKeywords": "ModelDb.AllSharedCardPools",
    }
    for method, required in required_shared_pools.items():
        match = re.search(
            rf"internal static object {method}\(\).*?(?=\n    internal static object|\n    private static|\n    internal static|\n\}})",
            fork_endpoints,
            re.S,
        )
        if not match or required not in match.group(0):
            fail(f"{method} must include active-run shared pool {required}")

    print("glossary: active-run shared/scoped pools enforced")


def audit_static_card_glossary_metadata(repo: Path) -> None:
    fork_endpoints = (repo / "McpMod.ForkEndpoints.cs").read_text(encoding="utf-8")
    state_builder = (repo / "McpMod.StateBuilder.cs").read_text(encoding="utf-8")
    match = re.search(
        r"private static void AddCardsFromPool\(.*?\n    internal static object BuildGlossaryRelics\(\)",
        fork_endpoints,
        re.S,
    )
    if not match:
        fail("could not locate AddCardsFromPool for card glossary audit")
    body = match.group(0)
    required = [
        "is_upgradable",
        "current_upgrade_level",
        "max_upgrade_level",
        "upgrade_preview_type",
        "upgrade_preview_cost",
        "upgrade_preview_star_cost",
        "upgrade_preview_description",
        "SafeGetCardUpgradePreviewDescription",
    ]
    missing = [field for field in required if field not in body]
    if missing:
        fail(f"card glossary missing upgrade metadata: {missing}")

    state_match = re.search(
        r"private static Dictionary<string, object\?> BuildCardInfo\(.*?\n    private static Dictionary<string, object\?> BuildCardState\(",
        state_builder,
        re.S,
    )
    if not state_match:
        fail("could not locate BuildCardInfo for card metadata audit")
    state_body = state_match.group(0)
    missing_state = [field for field in required if field not in state_body]
    if missing_state:
        fail(f"state card serialization missing upgrade metadata: {missing_state}")

    card_state_match = re.search(
        r"private static Dictionary<string, object\?> BuildCardState\(.*?\n    private static List<Dictionary<string, object\?>> BuildPileCardList\(",
        state_builder,
        re.S,
    )
    if not card_state_match:
        fail("could not locate BuildCardState for can_play audit")
    card_state_body = card_state_match.group(0)
    for required_guard in ["IsPlayPhase", "PlayerActionsDisabled", "NotInPlayPhase"]:
        if required_guard not in card_state_body:
            fail(f"hand card can_play missing action guard: {required_guard}")

    helpers = (repo / "McpMod.Helpers.cs").read_text(encoding="utf-8")
    helper_match = re.search(
        r"private static CardModel\? SafeBuildUpgradedCardPreview\(.*?\n    private static string\? SafeGetCardUpgradePreviewDescription\(",
        helpers,
        re.S,
    )
    if not helper_match:
        fail("could not locate upgraded card preview helper")
    helper_body = helper_match.group(0)
    helper_required = ["ToMutable", "MutableClone", "UpgradeInternal"]
    missing_helper = [field for field in helper_required if field not in helper_body]
    if missing_helper:
        fail(f"upgraded card preview helper missing clone/upgrade path: {missing_helper}")

    shop_match = re.search(
        r"private static Dictionary<string, object\?> BuildShopState\(.*?\n    private static Dictionary<string, object\?> BuildMapState\(",
        state_builder,
        re.S,
    )
    if not shop_match:
        fail("could not locate BuildShopState for shop card metadata audit")
    shop_body = shop_match.group(0)
    shop_required = [
        "inventory_open",
        "can_close_inventory",
        "card_is_upgradable",
        "card_current_upgrade_level",
        "card_max_upgrade_level",
        "card_upgrade_preview_type",
        "card_upgrade_preview_cost",
        "card_upgrade_preview_star_cost",
        "card_upgrade_preview_description",
    ]
    missing_shop = [field for field in shop_required if field not in shop_body]
    if missing_shop:
        fail(f"shop card serialization missing upgrade metadata: {missing_shop}")
    if "proceedButton?.IsEnabled == true || canCloseInventory" not in shop_body:
        fail("shop can_proceed must mirror ExecuteProceed close-inventory behavior")
    print("cards: upgrade metadata enforced for glossary and state payloads")


def audit_static_save_roots(repo: Path) -> None:
    compendium = (repo / "McpMod.Compendium.cs").read_text(encoding="utf-8")
    match = re.search(
        r"private static IEnumerable<string> EnumerateSaveRoots\(\).*?\n    private static IEnumerable<string> EnumerateSteamDataRoots\(\)",
        compendium,
        re.S,
    )
    if not match:
        fail("could not locate EnumerateSaveRoots for save-root audit")
    body = match.group(0)
    if "accountRoots.Count == 1" in body or "yielded.Count > 0" in body:
        fail("save-root fallback must search all Steam account roots after the active account")
    if "Directory.GetDirectories(steamRoot)" not in body:
        fail("save-root fallback must enumerate Steam account directories")
    print("saves: multi-account fallback lookup enforced")


def audit_state_surface(repo: Path) -> None:
    state_builder = (repo / "McpMod.StateBuilder.cs").read_text(encoding="utf-8")
    multiplayer_state = (repo / "McpMod.MultiplayerState.cs").read_text(encoding="utf-8")
    formatting = (repo / "McpMod.Formatting.cs").read_text(encoding="utf-8")
    actions = (repo / "McpMod.Actions.cs").read_text(encoding="utf-8")
    docs = "\n".join(
        [
            (repo / "docs" / "raw-simplified.md").read_text(encoding="utf-8"),
            (repo / "docs" / "raw-full.md").read_text(encoding="utf-8"),
            (repo / "mcp" / "README.md").read_text(encoding="utf-8"),
        ]
    )

    state_types = set(re.findall(r'\["state_type"\]\s*=\s*"([^"]+)"', state_builder))
    multiplayer_state_types = set(re.findall(r'\["state_type"\]\s*=\s*"([^"]+)"', multiplayer_state))
    missing_mp_parity = sorted(state_types - multiplayer_state_types)
    if missing_mp_parity:
        fail(f"multiplayer state surface missing singleplayer states: {missing_mp_parity}")

    state_types |= multiplayer_state_types
    missing_docs = sorted(state_type for state_type in state_types if state_type not in docs)
    if missing_docs:
        fail(f"state types missing docs: {missing_docs}")

    formatter_refs = set()
    for match in re.findall(r'TryGetValue\("([a-z_]+)"|stateType\s*==\s*"([a-z_]+)"', formatting):
        formatter_refs.update(value for value in match if value)
    covered_by_battle = {"monster", "elite", "boss"}
    intentionally_unformatted = {"unknown"}
    missing_formatters = sorted(state_types - formatter_refs - covered_by_battle - intentionally_unformatted)
    if missing_formatters:
        fail(f"state types missing markdown formatter coverage: {missing_formatters}")

    card_select_match = re.search(
        r"private static Dictionary<string, object\?> BuildCardSelectState\(.*?\n    private static Dictionary<string, object\?> BuildChooseCardState\(",
        state_builder,
        re.S,
    )
    if not card_select_match:
        fail("could not locate BuildCardSelectState for selection audit")
    card_select_body = card_select_match.group(0)
    for required_field in [
        "is_selected",
        "can_select",
        "selected_cards",
        "selected_count",
        "min_select",
        "max_select",
        "preview_cards",
    ]:
        if required_field not in card_select_body and required_field not in state_builder:
            fail(f"card_select state missing selection metadata: {required_field}")
        if required_field not in docs:
            fail(f"docs missing card_select selection metadata: {required_field}")

    select_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteSelectCard\(.*?\n    private static Dictionary<string, object\?> ExecuteConfirmSelection\(",
        actions,
        re.S,
    )
    if not select_action_match:
        fail("could not locate ExecuteSelectCard for preview guard audit")
    select_action_body = select_action_match.group(0)
    if "preview is already open" not in select_action_body or "%PreviewContainer" not in select_action_body:
        fail("select_card must reject grid selection while a preview is open")

    card_reward_match = re.search(
        r"private static Dictionary<string, object\?> BuildCardRewardState\(.*?\n    private static Dictionary<string, object\?> BuildCardSelectState\(",
        state_builder,
        re.S,
    )
    if not card_reward_match:
        fail("could not locate BuildCardRewardState for reward-card audit")
    card_reward_body = card_reward_match.group(0)
    reward_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteSelectCardReward\(.*?\n    private static Dictionary<string, object\?> ExecuteProceed\(",
        actions,
        re.S,
    )
    if not reward_action_match:
        fail("could not locate card reward actions for reward-card audit")
    reward_action_body = reward_action_match.group(0)
    for required_fragment in ["IsVisibleInTree", "IsEnabled", "AddCardHolderState"]:
        if required_fragment not in card_reward_body:
            fail(f"card_reward state missing visibility/enabled metadata: {required_fragment}")
    for required_fragment in ["IsVisibleInTree", "IsEnabled"]:
        if required_fragment not in reward_action_body:
            fail(f"card_reward action missing visibility/enabled guard: {required_fragment}")

    print(f"states: {len(state_types)} documented, markdown coverage enforced")


def audit_live(base_url: str) -> None:
    root_status, root = load_json_url(base_url.rstrip("/") + "/")
    if root_status != 200 or not isinstance(root, dict):
        fail(f"root returned HTTP {root_status}: {root}")

    endpoint_rows = root.get("endpoints")
    if not isinstance(endpoint_rows, list):
        fail("root response does not include endpoints list")

    live_index = {
        (str(row.get("method")), str(row.get("path")))
        for row in endpoint_rows
        if isinstance(row, dict)
    }
    expected = set(EXPECTED_ENDPOINTS)
    if live_index != expected:
        fail(f"root endpoint index mismatch. missing={sorted(expected - live_index)} extra={sorted(live_index - expected)}")
    print(f"root: {len(live_index)} endpoints advertised")

    glossary_payloads: dict[str, dict] = {}
    for method, path in EXPECTED_ENDPOINTS:
        if method != "GET":
            continue
        status, data = load_json_url(base_url.rstrip("/") + path)
        assert_error_body(path, status, data)

        if path in {"/api/v1/settings", "/api/v1/profile", "/api/v1/compendium", "/api/v1/bestiary", "/api/v1/profiles"} and status != 200:
            fail(f"{path} expected HTTP 200, got {status}: {data}")

        if path.startswith("/api/v1/glossary/") and status not in {200, 409}:
            fail(f"{path} expected HTTP 200 or 409, got {status}: {data}")
        if path.startswith("/api/v1/glossary/") and status == 200:
            expected_kind = path.rsplit("/", 1)[-1]
            if not isinstance(data, dict):
                fail(f"{path} expected structured glossary object, got {type(data).__name__}")
            if data.get("status") != "ok" or data.get("kind") != expected_kind:
                fail(f"{path} expected status ok and kind {expected_kind}, got {data}")
            if not isinstance(data.get("items"), list) or data.get("count") != len(data["items"]):
                fail(f"{path} expected items list with matching count, got {data}")
            current_run = data.get("current_run")
            if not isinstance(current_run, dict) or not current_run.get("run_id") or not current_run.get("seed"):
                fail(f"{path} expected current_run run_id and seed, got {current_run}")
            glossary_payloads[expected_kind] = data

    if {"keywords", "relics", "potions"}.issubset(glossary_payloads):
        keyword_names = {
            str(item.get("name"))
            for item in glossary_payloads["keywords"]["items"]
            if isinstance(item, dict)
        }
        item_names = {
            str(item.get("name"))
            for kind in ("relics", "potions")
            for item in glossary_payloads[kind]["items"]
            if isinstance(item, dict) and item.get("name")
        }
        leaked = sorted(keyword_names & item_names)
        if leaked:
            fail(f"/api/v1/glossary/keywords leaked item self-tooltips: {leaked}")

    status, data = load_json_url(base_url.rstrip("/") + "/api/v1/singleplayer?format=json")
    if status != 200 or not isinstance(data, dict) or "state_type" not in data:
        fail(f"/api/v1/singleplayer?format=json expected JSON state, got HTTP {status}: {data}")

    markdown_status, markdown = load_text_url(base_url.rstrip("/") + "/api/v1/singleplayer?format=markdown")
    if markdown_status != 200 or "# Game State:" not in markdown:
        fail(f"/api/v1/singleplayer?format=markdown expected markdown state, got HTTP {markdown_status}: {markdown[:120]}")

    post_validation_checks = [
        ("/api/v1/singleplayer", b"{", 400),
        ("/api/v1/singleplayer", b"{}", 400),
        ("/api/v1/singleplayer", b'{"action": 1}', 400),
        ("/api/v1/singleplayer", b'{"action": "menu_select", "option": 1}', 400),
        ("/api/v1/profiles", b"{", 400),
        ("/api/v1/profiles", b"{}", 400),
        ("/api/v1/profiles", b'{"action": 1}', 400),
        ("/api/v1/profiles", b'{"action": "switch", "profile_id": "1"}', 400),
        ("/api/v1/profiles", b'{"action": "switch", "profile_id": 1.5}', 400),
        ("/api/v1/profiles", b'{"action": "switch", "profile_id": 999999999999}', 400),
    ]
    for path, body, expected_status in post_validation_checks:
        status, data = load_json_url(base_url.rstrip("/") + path, "POST", body)
        assert_error_body(path, status, data)
        if status != expected_status:
            fail(f"{path} expected HTTP {expected_status} for validation check, got {status}: {data}")

    for body in (b"{", b"{}"):
        status, data = load_json_url(base_url.rstrip("/") + "/api/v1/multiplayer", "POST", body)
        assert_error_body("/api/v1/multiplayer", status, data)
        if status not in {400, 409}:
            fail(f"/api/v1/multiplayer expected HTTP 400 in MP or 409 outside MP, got {status}: {data}")

    get_only_paths = [
        "/",
        "/api/v1/settings",
        "/api/v1/profile",
        "/api/v1/compendium",
        "/api/v1/bestiary",
        "/api/v1/glossary/cards",
        "/api/v1/glossary/relics",
        "/api/v1/glossary/potions",
        "/api/v1/glossary/keywords",
    ]
    for path in get_only_paths:
        status, data = load_json_url(base_url.rstrip("/") + path, "POST", b"{}")
        assert_error_body(path, status, data)
        if status != 405:
            fail(f"{path} expected HTTP 405 for POST, got {status}: {data}")

    status, data = load_json_url(base_url.rstrip("/") + "/api/v1/does-not-exist")
    assert_error_body("/api/v1/does-not-exist", status, data)
    if status != 404:
        fail(f"/api/v1/does-not-exist expected HTTP 404, got {status}: {data}")

    print("live: GET endpoint smoke checks passed")
    print("live: state format checks passed")
    print("live: safe POST validation checks passed")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://localhost:15526", help="STS2_MCP HTTP base URL")
    parser.add_argument("--skip-live", action="store_true", help="Only check checked-in docs")
    args = parser.parse_args()

    repo = Path(__file__).resolve().parents[1]
    audit_docs(repo)
    audit_action_surface(repo)
    audit_mcp_tool_docs(repo)
    audit_static_formatters(repo)
    audit_static_error_shapes(repo)
    audit_static_glossary_scope(repo)
    audit_static_card_glossary_metadata(repo)
    audit_static_save_roots(repo)
    audit_state_surface(repo)
    if not args.skip_live:
        audit_live(args.base_url)


if __name__ == "__main__":
    main()
