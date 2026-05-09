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


def assert_sorted_strings(path: str, field: str, values: object) -> None:
    if not isinstance(values, list):
        fail(f"{path} expected {field} to be a list, got {values}")
    strings = [str(value) for value in values]
    if strings != sorted(strings):
        fail(f"{path} expected {field} to be sorted")


def assert_sorted_objects(path: str, field: str, values: object, key: str) -> None:
    if not isinstance(values, list):
        fail(f"{path} expected {field} to be a list, got {values}")
    keys = []
    for item in values:
        if not isinstance(item, dict):
            fail(f"{path} expected {field} entries to be objects, got {item}")
        keys.append(str(item.get(key)))
        nested = item.get("by_character")
        if nested is not None:
            assert_sorted_objects(path, f"{field}.by_character", nested, "character")
    if keys != sorted(keys):
        fail(f"{path} expected {field} to be sorted by {key}")


def assert_context_paths_normalized(path: str, data: object) -> None:
    if not isinstance(data, dict):
        return

    for field in ["progress_path", "resolved_progress_path", "profile_root", "save_path", "history_path"]:
        value = data.get(field)
        if isinstance(value, str) and "\\" in value:
            fail(f"{path} expected {field} to use forward slashes, got {value!r}")

    current_run = data.get("current_run")
    if isinstance(current_run, dict):
        assert_context_paths_normalized(f"{path}.current_run", current_run)

    sections = data.get("sections")
    if isinstance(sections, dict):
        run_history = sections.get("run_history")
        if isinstance(run_history, dict):
            assert_context_paths_normalized(f"{path}.sections.run_history", run_history)


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
    fork_endpoints = (repo / "McpMod.ForkEndpoints.cs").read_text(encoding="utf-8")
    settings_match = re.search(
        r"internal static object BuildSettings\(\).*?\n    private static void HandleGetBestiary",
        fork_endpoints,
        re.S,
    )
    if not settings_match:
        fail("could not locate BuildSettings for settings audit")
    settings_body = settings_match.group(0)
    for required_fragment in ["SaveManager.Instance", "Save manager is not available", "Settings data is not available", "status", "kind"]:
        if required_fragment not in settings_body:
            fail(f"settings endpoint missing startup-safe structured field: {required_fragment}")
    mcp_mod = (repo / "McpMod.cs").read_text(encoding="utf-8")
    for required_fragment in ["TryValidateStateFormat", "invalid_format", 'format is "json" or "markdown"']:
        if required_fragment not in mcp_mod:
            fail(f"state endpoints missing format validation: {required_fragment}")
    actions = (repo / "McpMod.Actions.cs").read_text(encoding="utf-8")
    multiplayer_actions = (repo / "McpMod.MultiplayerActions.cs").read_text(encoding="utf-8")
    for required_fragment in ["SendActionResultJson", "unknown_action", "run_not_in_progress", "local_player_unavailable"]:
        if required_fragment not in mcp_mod + actions:
            fail(f"singleplayer actions missing structured dispatch error handling: {required_fragment}")
    if "response.StatusCode = 400;" not in mcp_mod:
        fail("action result sender must return non-2xx for uncoded action errors")
    for required_fragment in ["missing_menu_option", "unknown_menu_option", "not_on_menu"]:
        if required_fragment not in mcp_mod + actions:
            fail(f"menu_select missing structured dispatch error handling: {required_fragment}")
    for required_fragment in ["unknown_multiplayer_action", "not_multiplayer_run", "run_not_in_progress", "local_player_unavailable"]:
        if required_fragment not in mcp_mod + multiplayer_actions:
            fail(f"multiplayer actions missing structured dispatch error handling: {required_fragment}")
    mcp_server = (repo / "mcp" / "server.py").read_text(encoding="utf-8")
    for required_fragment in ["_format_structured_http_error", "http_status", 'data.get("status") != "error"']:
        if required_fragment not in mcp_server:
            fail(f"MCP server must preserve structured non-2xx endpoint errors: {required_fragment}")
    mcp_readme = (repo / "mcp" / "README.md").read_text(encoding="utf-8")
    if "MCP wrappers preserve those structured JSON error bodies" not in mcp_readme:
        fail("mcp/README.md must document structured non-2xx error propagation")
    print("errors: structured 500 response helpers enforced")


def audit_static_glossary_scope(repo: Path) -> None:
    fork_endpoints = (repo / "McpMod.ForkEndpoints.cs").read_text(encoding="utf-8")
    docs = "\n".join(
        [
            (repo / "docs" / "raw-simplified.md").read_text(encoding="utf-8"),
            (repo / "docs" / "raw-full.md").read_text(encoding="utf-8"),
            (repo / "mcp" / "README.md").read_text(encoding="utf-8"),
            (repo / "mcp" / "server.py").read_text(encoding="utf-8"),
        ]
    )
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

    send_match = re.search(
        r"private static void SendGlossaryJson\(.*?\n    private static Dictionary<string, object\?> GlossaryError\(",
        fork_endpoints,
        re.S,
    )
    if not send_match:
        fail("could not locate SendGlossaryJson for glossary context audit")
    send_body = send_match.group(0)
    for required_fragment in ["profile_id", "progress_path", "resolved_progress_path", "profile_root", "save_scope", "current_run"]:
        if required_fragment not in send_body:
            fail(f"glossary success payload missing profile/save context: {required_fragment}")
        if required_fragment not in docs:
            fail(f"docs missing glossary profile/save context: {required_fragment}")

    print("glossary: active-run shared/scoped pools and profile context enforced")


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
        "GetCostDisplay",
        "star_cost",
        "GetStarCostDisplay",
        "is_upgraded",
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
    for required_fragment in ["requires_target", "valid_targets", "BuildEnemyTargetRefs"]:
        if required_fragment not in card_state_body:
            fail(f"hand card state missing target metadata: {required_fragment}")
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
        "can_purchase",
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
    for required_fragment in ["can_proceed", "canCloseInventory", "IsControlVisibleOrActionable"]:
        if required_fragment not in shop_body:
            fail(f"shop can_proceed must mirror ExecuteProceed visible close/proceed behavior: {required_fragment}")
    print("cards: upgrade metadata enforced for glossary and state payloads")


def audit_static_save_roots(repo: Path) -> None:
    compendium = (repo / "McpMod.Compendium.cs").read_text(encoding="utf-8")
    profile = (repo / "McpMod.Profile.cs").read_text(encoding="utf-8")
    fork_endpoints = (repo / "McpMod.ForkEndpoints.cs").read_text(encoding="utf-8")
    helpers = (repo / "McpMod.Helpers.cs").read_text(encoding="utf-8")
    mcp_server = (repo / "mcp" / "server.py").read_text(encoding="utf-8")
    mcp_readme = (repo / "mcp" / "README.md").read_text(encoding="utf-8")
    raw_full = (repo / "docs" / "raw-full.md").read_text(encoding="utf-8")

    if "NormalizePathForJson" not in helpers:
        fail("profile/save context paths must be normalized at the JSON boundary")
    for source_name, source in [
        ("compendium", compendium),
        ("profile", profile),
        ("glossary", fork_endpoints),
    ]:
        if "NormalizePathForJson" not in source:
            fail(f"{source_name} endpoint missing path normalization for profile/save context")
    if "\\saves\\progress.save" in raw_full:
        fail("docs/raw-full.md should show normalized profile/save paths with forward slashes")

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

    compendium_response_match = re.search(
        r"private static object BuildCompendiumResponse\(.*?\n    private sealed class CompendiumSnapshot",
        compendium,
        re.S,
    )
    if not compendium_response_match:
        fail("could not locate BuildCompendiumResponse for profile context audit")
    compendium_response = compendium_response_match.group(0)
    for required_fragment in ["status", "kind", "profile_id", "progress_path", "resolved_progress_path", "profile_root", "save_scope", "current_run"]:
        if required_fragment not in compendium_response:
            fail(f"compendium endpoint missing profile/save context: {required_fragment}")

    profile_match = re.search(
        r"internal static object BuildProfile\(\).*?\n    \}",
        profile,
        re.S,
    )
    if not profile_match:
        fail("could not locate BuildProfile for profile context audit")
    profile_body = profile_match.group(0)
    for required_fragment in ["status", "kind", "profile_id", "progress_path", "resolved_progress_path", "profile_root", "save_scope", "current_run", "BuildActiveRunContext"]:
        if required_fragment not in profile_body:
            fail(f"profile endpoint missing identity/run context: {required_fragment}")

    profiles_match = re.search(
        r"private static Dictionary<string, object\?> BuildProfilesSummary\(\).*?\n    private static Dictionary<string, object\?> ExecuteProfileAction",
        profile,
        re.S,
    )
    if not profiles_match:
        fail("could not locate BuildProfilesSummary for profile slots audit")
    profiles_body = profiles_match.group(0)
    for required_fragment in ["status", "kind", "count", "profile_id", "progress_path", "resolved_progress_path", "profile_root", "save_scope"]:
        if required_fragment not in profiles_body:
            fail(f"profiles endpoint missing structured slot context: {required_fragment}")
    for required_fragment in [
        "SendProfileActionJson",
        "invalid_profile_id",
        "unknown_profile_action",
        "run_in_progress",
        "active_profile_delete",
        "save_manager_unavailable",
    ]:
        if required_fragment not in profile:
            fail(f"profiles POST missing structured HTTP error handling: {required_fragment}")

    for doc_name, doc_text in [("mcp/server.py", mcp_server), ("mcp/README.md", mcp_readme)]:
        for required_fragment in ["progress_path", "resolved_progress_path", "profile_root", "save_scope", "current_run"]:
            if required_fragment not in doc_text:
                fail(f"{doc_name} missing profile/compendium context documentation: {required_fragment}")
    print("saves: multi-account fallback and profile/compendium context enforced")


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

    for source_name, source in [("singleplayer", state_builder), ("multiplayer", multiplayer_state)]:
        if '["current_run"] = BuildActiveRunContext()' not in source:
            fail(f"{source_name} state missing current_run identity context")
    if '["kind"] = "singleplayer_state"' not in state_builder:
        fail("singleplayer state missing status/kind response envelope")
    if '["kind"] = "multiplayer_state"' not in multiplayer_state:
        fail("multiplayer state missing status/kind response envelope")
    for required_fragment in ['status: "ok"', "singleplayer_state", "multiplayer_state"]:
        if required_fragment not in docs:
            fail(f"docs missing state response envelope detail: {required_fragment}")
    for required_fragment in ["run_id", "progress_path", "resolved_progress_path", "profile_root", "save_scope"]:
        if required_fragment not in docs:
            fail(f"docs missing current_run context: {required_fragment}")

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
    for required_fragment in ["IsCardHolderSelectable", "does not contain a card", "is not selectable"]:
        if required_fragment not in select_action_body:
            fail(f"select_card action missing selectable-card guard: {required_fragment}")
    confirm_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteConfirmSelection\(.*?\n    private static Dictionary<string, object\?> ExecuteCancelSelection\(",
        actions,
        re.S,
    )
    if not confirm_action_match:
        fail("could not locate ExecuteConfirmSelection for visible button audit")
    confirm_action_body = confirm_action_match.group(0)
    if "IsControlVisibleOrActionable" not in confirm_action_body:
        fail("confirm_selection action must require visible enabled buttons")
    cancel_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteCancelSelection\(.*?\n    private static Dictionary<string, object\?> ExecuteSelectBundle\(",
        actions,
        re.S,
    )
    if not cancel_action_match:
        fail("could not locate ExecuteCancelSelection for visible button audit")
    cancel_action_body = cancel_action_match.group(0)
    if "IsControlVisibleOrActionable" not in cancel_action_body:
        fail("cancel_selection action must require visible enabled buttons")

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

    event_state_match = re.search(
        r"private static Dictionary<string, object\?> BuildEventState\(.*?\n    private static Dictionary<string, object\?> BuildFakeMerchantState\(",
        state_builder,
        re.S,
    )
    if not event_state_match:
        fail("could not locate BuildEventState for event-option audit")
    event_state_body = event_state_match.group(0)
    event_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteChooseEventOption\(.*?\n    private static Dictionary<string, object\?> ExecuteAdvanceDialogue\(",
        actions,
        re.S,
    )
    if not event_action_match:
        fail("could not locate ExecuteChooseEventOption for event-option audit")
    event_action_body = event_action_match.group(0)
    for required_fragment in ["IsVisibleInTree", "is_enabled", "can_choose"]:
        if required_fragment not in event_state_body:
            fail(f"event option state missing visibility/enabled metadata: {required_fragment}")
    if "eventRoom.LocalMutableEvent ?? eventRoom.CanonicalEvent" not in event_state_body:
        fail("event state must prefer LocalMutableEvent so multi-step event text does not go stale")
    for required_fragment in ["IsVisibleInTree", "IsEnabled"]:
        if required_fragment not in event_action_body:
            fail(f"event option action missing visibility/enabled guard: {required_fragment}")

    fake_merchant_match = re.search(
        r"private static Dictionary<string, object\?> BuildFakeMerchantState\(.*?\n    private static Dictionary<string, object\?> BuildFakeMerchantShopItems\(",
        state_builder,
        re.S,
    )
    if not fake_merchant_match:
        fail("could not locate BuildFakeMerchantState for fake-merchant proceed audit")
    fake_merchant_body = fake_merchant_match.group(0)
    for required_fragment in ["can_proceed", "can_close_inventory", "IsControlVisibleOrActionable"]:
        if required_fragment not in fake_merchant_body:
            fail(f"fake_merchant state must gate proceed/close on visible enabled buttons: {required_fragment}")

    rest_state_match = re.search(
        r"private static Dictionary<string, object\?> BuildRestSiteState\(.*?\n    private static Dictionary<string, object\?> BuildShopState\(",
        state_builder,
        re.S,
    )
    if not rest_state_match:
        fail("could not locate BuildRestSiteState for rest-option audit")
    rest_state_body = rest_state_match.group(0)
    rest_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteChooseRestOption\(.*?\n    private static Dictionary<string, object\?> ExecuteShopPurchase\(",
        actions,
        re.S,
    )
    if not rest_action_match:
        fail("could not locate ExecuteChooseRestOption for rest-option audit")
    rest_action_body = rest_action_match.group(0)
    for required_fragment in ["IsVisibleInTree", "is_visible", "can_choose"]:
        if required_fragment not in rest_state_body:
            fail(f"rest option state missing visibility/enabled metadata: {required_fragment}")
    if "can_proceed" not in rest_state_body or "IsControlVisibleOrActionable" not in rest_state_body:
        fail("rest_site state must gate can_proceed on a visible enabled proceed button")
    for required_fragment in ["IsVisibleInTree", "IsEnabled"]:
        if required_fragment not in rest_action_body:
            fail(f"rest option action missing visibility/enabled guard: {required_fragment}")

    relic_state_match = re.search(
        r"private static Dictionary<string, object\?> BuildRelicSelectState\(.*?\n    private static Dictionary<string, object\?> BuildCrystalSphereState\(",
        state_builder,
        re.S,
    )
    if not relic_state_match:
        fail("could not locate BuildRelicSelectState for relic-select audit")
    relic_state_body = relic_state_match.group(0)
    relic_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteSelectRelic\(.*?\n    private static Dictionary<string, object\?> ExecuteClaimTreasureRelic\(",
        actions,
        re.S,
    )
    if not relic_action_match:
        fail("could not locate relic selection actions for relic-select audit")
    relic_action_body = relic_action_match.group(0)
    for required_fragment in ["IsVisibleInTree", "is_visible", "can_select"]:
        if required_fragment not in relic_state_body:
            fail(f"relic_select state missing visibility/enabled metadata: {required_fragment}")
    for required_fragment in ["IsVisibleInTree", "IsEnabled"]:
        if required_fragment not in relic_action_body:
            fail(f"relic_select action missing visibility/enabled guard: {required_fragment}")

    bundle_state_match = re.search(
        r"private static Dictionary<string, object\?> BuildBundleSelectState\(.*?\n    private static Dictionary<string, object\?> BuildHandSelectState\(",
        state_builder,
        re.S,
    )
    if not bundle_state_match:
        fail("could not locate BuildBundleSelectState for bundle-select audit")
    bundle_state_body = bundle_state_match.group(0)
    bundle_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteSelectBundle\(.*?\n    private static Dictionary<string, object\?> ExecuteConfirmBundleSelection\(",
        actions,
        re.S,
    )
    if not bundle_action_match:
        fail("could not locate ExecuteSelectBundle for bundle-select audit")
    bundle_action_body = bundle_action_match.group(0)
    for required_fragment in ["IsVisibleInTree", "is_visible", "can_select"]:
        if required_fragment not in bundle_state_body:
            fail(f"bundle_select state missing visibility/enabled metadata: {required_fragment}")
    for required_fragment in ["IsVisibleInTree", "Hitbox.IsEnabled"]:
        if required_fragment not in bundle_action_body:
            fail(f"bundle_select action missing visibility/enabled guard: {required_fragment}")
    bundle_confirm_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteConfirmBundleSelection\(.*?\n    private static Dictionary<string, object\?> ExecuteCancelBundleSelection\(",
        actions,
        re.S,
    )
    if not bundle_confirm_match:
        fail("could not locate ExecuteConfirmBundleSelection for visible button audit")
    if "IsControlVisibleOrActionable" not in bundle_confirm_match.group(0):
        fail("confirm_bundle_selection action must require a visible enabled button")
    bundle_cancel_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteCancelBundleSelection\(.*?\n    private static Dictionary<string, object\?> ExecuteCombatSelectCard\(",
        actions,
        re.S,
    )
    if not bundle_cancel_match:
        fail("could not locate ExecuteCancelBundleSelection for visible button audit")
    if "IsControlVisibleOrActionable" not in bundle_cancel_match.group(0):
        fail("cancel_bundle_selection action must require a visible enabled button")

    hand_state_match = re.search(
        r"private static Dictionary<string, object\?> BuildHandSelectState\(.*?\n    private static Dictionary<string, object\?> BuildRelicSelectState\(",
        state_builder,
        re.S,
    )
    if not hand_state_match:
        fail("could not locate BuildHandSelectState for hand-select audit")
    hand_state_body = hand_state_match.group(0)
    for required_fragment in ["AddCardHolderState", "IsControlVisibleOrActionable"]:
        if required_fragment not in hand_state_body:
            fail(f"hand_select state missing selectable/confirm metadata: {required_fragment}")
    hand_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteCombatSelectCard\(.*?\n    private static Dictionary<string, object\?> ExecuteCombatConfirmSelection\(",
        actions,
        re.S,
    )
    if not hand_action_match:
        fail("could not locate ExecuteCombatSelectCard for hand-select audit")
    hand_action_body = hand_action_match.group(0)
    for required_fragment in ["IsCardHolderSelectable", "does not contain a card", "is not selectable"]:
        if required_fragment not in hand_action_body:
            fail(f"combat_select_card action missing selectable-card guard: {required_fragment}")
    hand_confirm_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteCombatConfirmSelection\(.*?\n    private static Dictionary<string, object\?> ExecuteSelectRelic\(",
        actions,
        re.S,
    )
    if not hand_confirm_match:
        fail("could not locate ExecuteCombatConfirmSelection for hand-select audit")
    if "IsControlVisibleOrActionable" not in hand_confirm_match.group(0):
        fail("combat_confirm_selection action must require a visible enabled button")

    proceed_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteProceed\(.*?\n    private static Dictionary<string, object\?> ExecuteSelectCard\(",
        actions,
        re.S,
    )
    if not proceed_action_match:
        fail("could not locate ExecuteProceed for visible proceed audit")
    proceed_action_body = proceed_action_match.group(0)
    if "IsControlVisibleOrActionable" not in proceed_action_body:
        fail("proceed action must require visible enabled proceed/close buttons")

    treasure_state_match = re.search(
        r"private static Dictionary<string, object\?> BuildTreasureState\(.*?\n    private static string GetRewardTypeName\(",
        state_builder,
        re.S,
    )
    if not treasure_state_match:
        fail("could not locate BuildTreasureState for treasure audit")
    treasure_state_body = treasure_state_match.group(0)
    treasure_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteClaimTreasureRelic\(.*?\n    private static Dictionary<string, object\?> ExecuteCrystalSphereSetTool\(",
        actions,
        re.S,
    )
    if not treasure_action_match:
        fail("could not locate ExecuteClaimTreasureRelic for treasure audit")
    treasure_action_body = treasure_action_match.group(0)
    for required_fragment in ["IsVisibleInTree", "is_visible", "can_claim"]:
        if required_fragment not in treasure_state_body:
            fail(f"treasure state missing visibility/enabled metadata: {required_fragment}")
    if "can_proceed" not in treasure_state_body or "IsControlVisibleOrActionable" not in treasure_state_body:
        fail("treasure state must gate can_proceed on a visible enabled proceed button")
    for required_fragment in ["IsVisibleInTree", "IsEnabled"]:
        if required_fragment not in treasure_action_body:
            fail(f"treasure action missing visibility/enabled guard: {required_fragment}")

    crystal_state_match = re.search(
        r"private static Dictionary<string, object\?> BuildCrystalSphereState\(.*?\n    private static Dictionary<string, object\?> BuildTreasureState\(",
        state_builder,
        re.S,
    )
    if not crystal_state_match:
        fail("could not locate BuildCrystalSphereState for crystal-sphere audit")
    crystal_state_body = crystal_state_match.group(0)
    crystal_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteCrystalSphereSetTool\(.*?\n    private static Creature\? ResolveTarget\(",
        actions,
        re.S,
    )
    if not crystal_action_match:
        fail("could not locate Crystal Sphere actions for crystal-sphere audit")
    crystal_action_body = crystal_action_match.group(0)
    if "IsVisibleInTree" not in crystal_state_body:
        fail("crystal_sphere state missing visible-in-tree guards")
    if "IsVisibleInTree" not in crystal_action_body:
        fail("crystal_sphere actions missing visible-in-tree guards")

    player_state_match = re.search(
        r"private static Dictionary<string, object\?> BuildPlayerState\(.*?\n    private static string GetCostDisplay\(",
        state_builder,
        re.S,
    )
    if not player_state_match:
        fail("could not locate BuildPlayerState for potion audit")
    player_state_body = player_state_match.group(0)
    potion_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteUsePotion\(.*?\n    private static Dictionary<string, object\?> ExecuteDiscardPotion\(",
        actions,
        re.S,
    )
    if not potion_action_match:
        fail("could not locate ExecuteUsePotion for potion audit")
    potion_action_body = potion_action_match.group(0)
    for required_fragment in [
        "can_use",
        "use_blocked_reason",
        "can_discard",
        "discard_blocked_reason",
        "requires_target",
        "valid_targets",
        "GetPotionUseBlockedReason",
        "EnemyTargetRequiresCombat",
    ]:
        if required_fragment not in player_state_body:
            fail(f"potion state missing use readiness metadata: {required_fragment}")
    for required_fragment in ["IsQueued", "PassesCustomUsabilityCheck", "PlayerActionsDisabled", "Enemy-targeted potions can only be used in combat"]:
        if required_fragment not in potion_action_body:
            fail(f"use_potion action missing readiness guard: {required_fragment}")
    discard_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteDiscardPotion\(.*?\n    private static Dictionary<string, object\?> ExecuteChooseMapNode\(",
        actions,
        re.S,
    )
    if not discard_action_match:
        fail("could not locate ExecuteDiscardPotion for potion audit")
    if "IsQueued" not in discard_action_match.group(0):
        fail("discard_potion action missing queued guard")

    battle_state_match = re.search(
        r"private static Dictionary<string, object\?> BuildBattleState\(.*?\n    private static Dictionary<string, object\?> BuildPlayerState\(",
        state_builder,
        re.S,
    )
    if not battle_state_match:
        fail("could not locate BuildBattleState for end-turn audit")
    battle_state_body = battle_state_match.group(0)
    end_turn_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteEndTurn\(.*?\n    private static Dictionary<string, object\?> ExecuteUsePotion\(",
        actions,
        re.S,
    )
    if not end_turn_action_match:
        fail("could not locate ExecuteEndTurn for end-turn audit")
    end_turn_action_body = end_turn_action_match.group(0)
    for required_fragment in ["can_end_turn", "end_turn_blocked_reason", "GetEndTurnBlockedReason"]:
        if required_fragment not in battle_state_body:
            fail(f"battle state missing end-turn readiness metadata: {required_fragment}")
    for required_fragment in ["PlayerActionsDisabled", "InCardPlay", "CurrentMode != NPlayerHand.Mode.Play"]:
        if required_fragment not in end_turn_action_body:
            fail(f"end_turn action missing readiness guard: {required_fragment}")

    mp_battle_match = re.search(
        r"private static Dictionary<string, object\?> BuildMultiplayerBattleState\(.*?\n    private static Dictionary<string, object\?> BuildMultiplayerMapState\(",
        multiplayer_state,
        re.S,
    )
    if not mp_battle_match:
        fail("could not locate BuildMultiplayerBattleState for multiplayer end-turn audit")
    mp_battle_body = mp_battle_match.group(0)
    mp_actions = (repo / "McpMod.MultiplayerActions.cs").read_text(encoding="utf-8")
    for required_fragment in [
        "local_player_ready_to_end_turn",
        "can_end_turn",
        "end_turn_blocked_reason",
        "can_undo_end_turn",
        "undo_end_turn_blocked_reason",
        "GetMultiplayerEndTurnBlockedReason",
        "GetMultiplayerUndoEndTurnBlockedReason",
    ]:
        if required_fragment not in mp_battle_body:
            fail(f"multiplayer battle state missing end-turn readiness metadata: {required_fragment}")
    for required_fragment in ["Already submitted end turn", "Not ready to end turn"]:
        if required_fragment not in mp_actions:
            fail(f"multiplayer end-turn action missing readiness guard: {required_fragment}")

    rewards_state_match = re.search(
        r"private static Dictionary<string, object\?> BuildRewardsState\(.*?\n    private static RelicModel\? GetRelicRewardModel\(",
        state_builder,
        re.S,
    )
    if not rewards_state_match:
        fail("could not locate BuildRewardsState for rewards audit")
    rewards_state_body = rewards_state_match.group(0)
    rewards_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteClaimReward\(.*?\n    private static Dictionary<string, object\?> ExecuteSelectCardReward\(",
        actions,
        re.S,
    )
    if not rewards_action_match:
        fail("could not locate ExecuteClaimReward for rewards audit")
    rewards_action_body = rewards_action_match.group(0)
    for required_fragment in ["IsVisibleInTree", "is_visible", "can_claim"]:
        if required_fragment not in rewards_state_body:
            fail(f"rewards state missing visibility/claim metadata: {required_fragment}")
    if "can_proceed" not in rewards_state_body or "IsControlVisibleOrActionable" not in rewards_state_body:
        fail("rewards state must gate can_proceed on a visible enabled proceed button")
    for required_fragment in ["IsVisibleInTree", "IsEnabled"]:
        if required_fragment not in rewards_action_body:
            fail(f"claim_reward action missing visibility/enabled guard: {required_fragment}")

    map_state_match = re.search(
        r"private static Dictionary<string, object\?> BuildMapState\(.*?\n    private static Dictionary<string, object\?> BuildMapNode\(",
        state_builder,
        re.S,
    )
    if not map_state_match:
        fail("could not locate BuildMapState for map audit")
    map_state_body = map_state_match.group(0)
    map_action_match = re.search(
        r"private static Dictionary<string, object\?> ExecuteChooseMapNode\(.*?\n    private static Dictionary<string, object\?> ExecuteClaimReward\(",
        actions,
        re.S,
    )
    if not map_action_match:
        fail("could not locate ExecuteChooseMapNode for map audit")
    map_action_body = map_action_match.group(0)
    for required_fragment in ["IsVisibleInTree", "is_visible", "can_travel"]:
        if required_fragment not in map_state_body:
            fail(f"map next_options missing visibility/travel metadata: {required_fragment}")
    for required_fragment in ["IsVisibleInTree", "MapPointState.Travelable"]:
        if required_fragment not in map_action_body:
            fail(f"choose_map_node action missing visibility/travel guard: {required_fragment}")

    enemy_state_match = re.search(
        r"private static Dictionary<string, object\?> BuildEnemyState\(.*?\n    private static Dictionary<string, object\?> BuildEventState\(",
        state_builder,
        re.S,
    )
    if not enemy_state_match:
        fail("could not locate BuildEnemyState for enemy target audit")
    enemy_state_body = enemy_state_match.group(0)
    docs_raw_full = (repo / "docs" / "raw-full.md").read_text(encoding="utf-8")
    for required_fragment in ["is_alive", "is_visible", "can_target", "can_select"]:
        if required_fragment not in enemy_state_body:
            fail(f"enemy state missing targetability metadata: {required_fragment}")
        if required_fragment not in docs_raw_full:
            fail(f"docs missing enemy targetability metadata: {required_fragment}")

    print(f"states: {len(state_types)} documented, markdown coverage enforced")


def audit_live(base_url: str) -> None:
    root_status, root = load_json_url(base_url.rstrip("/") + "/")
    if root_status != 200 or not isinstance(root, dict):
        fail(f"root returned HTTP {root_status}: {root}")

    endpoint_rows = root.get("endpoints")
    if not isinstance(endpoint_rows, list):
        fail("root response does not include endpoints list")
    if root.get("status") != "ok" or root.get("kind") != "api_index":
        fail(f"root response expected status ok and kind api_index, got {root}")
    if not root.get("version"):
        fail(f"root response expected explicit version field, got {root}")
    if root.get("endpoint_count") != len(endpoint_rows):
        fail(f"root response endpoint_count mismatch, got {root.get('endpoint_count')} for {len(endpoint_rows)} endpoints")

    live_index = {
        (str(row.get("method")), str(row.get("path")))
        for row in endpoint_rows
        if isinstance(row, dict)
    }
    expected = set(EXPECTED_ENDPOINTS)
    if live_index != expected:
        fail(f"root endpoint index mismatch. missing={sorted(expected - live_index)} extra={sorted(live_index - expected)}")
    endpoint_descriptions = {
        str(row.get("path")): str(row.get("description"))
        for row in endpoint_rows
        if isinstance(row, dict)
    }
    for path in ["/api/v1/profile", "/api/v1/compendium"]:
        description = endpoint_descriptions.get(path, "")
        if "save/run context" not in description:
            fail(f"root endpoint index should advertise save/run context for {path}: {description}")
    print(f"root: {len(live_index)} endpoints advertised")

    glossary_payloads: dict[str, dict] = {}
    for method, path in EXPECTED_ENDPOINTS:
        if method != "GET":
            continue
        status, data = load_json_url(base_url.rstrip("/") + path)
        assert_error_body(path, status, data)

        if path in {"/api/v1/settings", "/api/v1/profile", "/api/v1/compendium", "/api/v1/bestiary", "/api/v1/profiles"} and status != 200:
            fail(f"{path} expected HTTP 200, got {status}: {data}")
        if path == "/api/v1/settings":
            if not isinstance(data, dict) or data.get("status") != "ok" or data.get("kind") != "settings":
                fail(f"{path} expected structured settings status/kind, got {data}")
            for required_field in ["display", "audio", "gameplay", "mods", "language", "skip_intro"]:
                if required_field not in data:
                    fail(f"{path} missing settings field: {required_field}")
        if path in {"/api/v1/profile", "/api/v1/compendium"}:
            if not isinstance(data, dict):
                fail(f"{path} expected structured profile context object, got {type(data).__name__}")
            assert_context_paths_normalized(path, data)
            expected_kind = path.rsplit("/", 1)[-1]
            if data.get("status") != "ok" or data.get("kind") != expected_kind:
                fail(f"{path} expected status ok and kind {expected_kind}, got {data}")
            for required_field in ["profile_id", "progress_path", "resolved_progress_path", "profile_root", "save_scope", "current_run"]:
                if required_field not in data:
                    fail(f"{path} missing profile/save context field: {required_field}")
            if path == "/api/v1/profile":
                for field in ["characters", "card_stats", "encounter_stats", "enemy_stats", "ancient_stats", "achievements", "epochs"]:
                    assert_sorted_objects(path, field, data.get(field), "id")
                for field in ["discovered_cards", "discovered_relics", "discovered_potions", "discovered_events", "discovered_acts"]:
                    assert_sorted_strings(path, field, data.get(field))
            if path == "/api/v1/compendium":
                sections = data.get("sections")
                if not isinstance(sections, dict):
                    fail(f"{path} expected sections object, got {sections}")
                card_library = sections.get("card_library")
                relic_collection = sections.get("relic_collection")
                potion_lab = sections.get("potion_lab")
                bestiary = sections.get("bestiary")
                character_stats = sections.get("character_stats")
                if not all(isinstance(section, dict) for section in [card_library, relic_collection, potion_lab, bestiary, character_stats]):
                    fail(f"{path} expected structured compendium sections, got {sections}")
                assert_sorted_strings(path, "card_library.discovered_ids", card_library.get("discovered_ids"))
                assert_sorted_objects(path, "card_library.stats", card_library.get("stats"), "id")
                assert_sorted_strings(path, "relic_collection.discovered_ids", relic_collection.get("discovered_ids"))
                assert_sorted_strings(path, "potion_lab.discovered_ids", potion_lab.get("discovered_ids"))
                assert_sorted_objects(path, "bestiary.encounter_stats", bestiary.get("encounter_stats"), "id")
                assert_sorted_objects(path, "bestiary.enemy_stats", bestiary.get("enemy_stats"), "id")
                assert_sorted_objects(path, "character_stats.characters", character_stats.get("characters"), "id")
        if path == "/api/v1/profiles":
            if not isinstance(data, dict) or data.get("status") != "ok" or data.get("kind") != "profiles":
                fail(f"{path} expected structured profiles status/kind, got {data}")
            profiles = data.get("profiles")
            if not isinstance(profiles, list) or data.get("count") != len(profiles):
                fail(f"{path} expected profiles list with matching count, got {data}")
            for profile_slot in profiles:
                if not isinstance(profile_slot, dict):
                    fail(f"{path} expected profile slot objects, got {profile_slot}")
                assert_context_paths_normalized(f"{path}.profiles[]", profile_slot)
                for required_field in ["id", "profile_id", "is_current", "has_data", "progress_path", "resolved_progress_path", "profile_root", "save_scope"]:
                    if required_field not in profile_slot:
                        fail(f"{path} profile slot missing field {required_field}: {profile_slot}")
        if path == "/api/v1/bestiary":
            if not isinstance(data, dict) or data.get("status") != "ok" or data.get("kind") != "bestiary":
                fail(f"{path} expected structured bestiary status/kind, got {data}")
            monsters = data.get("monsters")
            encounters = data.get("encounters")
            if not isinstance(monsters, list) or data.get("monster_count") != len(monsters):
                fail(f"{path} expected monsters list with matching monster_count, got {data}")
            if not isinstance(encounters, list) or data.get("encounter_count") != len(encounters):
                fail(f"{path} expected encounters list with matching encounter_count, got {data}")
            monster_ids = [str(item.get("id")) for item in monsters if isinstance(item, dict)]
            encounter_ids = [str(item.get("id")) for item in encounters if isinstance(item, dict)]
            if monster_ids != sorted(monster_ids) or encounter_ids != sorted(encounter_ids):
                fail(f"{path} expected deterministic id ordering")

        if path.startswith("/api/v1/glossary/") and status not in {200, 409}:
            fail(f"{path} expected HTTP 200 or 409, got {status}: {data}")
        if path.startswith("/api/v1/glossary/") and status == 409:
            if not isinstance(data, dict) or data.get("error_code") != "run_not_in_progress":
                fail(f"{path} expected run_not_in_progress glossary error, got {data}")
            assert_context_paths_normalized(path, data)
            for required_field in ["kind", "profile_id", "progress_path", "resolved_progress_path", "profile_root", "save_scope"]:
                if required_field not in data:
                    fail(f"{path} glossary error missing profile/save context field: {required_field}")
        if path.startswith("/api/v1/glossary/") and status == 200:
            expected_kind = path.rsplit("/", 1)[-1]
            if not isinstance(data, dict):
                fail(f"{path} expected structured glossary object, got {type(data).__name__}")
            assert_context_paths_normalized(path, data)
            if data.get("status") != "ok" or data.get("kind") != expected_kind:
                fail(f"{path} expected status ok and kind {expected_kind}, got {data}")
            if not isinstance(data.get("items"), list) or data.get("count") != len(data["items"]):
                fail(f"{path} expected items list with matching count, got {data}")
            for required_field in ["profile_id", "progress_path", "resolved_progress_path", "profile_root", "save_scope"]:
                if required_field not in data:
                    fail(f"{path} missing profile/save context field: {required_field}")
            current_run = data.get("current_run")
            if not isinstance(current_run, dict) or not current_run.get("run_id") or not current_run.get("seed"):
                fail(f"{path} expected current_run run_id and seed, got {current_run}")
            for required_field in ["progress_path", "resolved_progress_path", "profile_root", "save_scope"]:
                if required_field not in current_run:
                    fail(f"{path} current_run missing profile/save context field: {required_field}")
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
    assert_context_paths_normalized("/api/v1/singleplayer?format=json", data)
    if data.get("status") != "ok" or data.get("kind") != "singleplayer_state":
        fail(f"/api/v1/singleplayer?format=json expected status ok and kind singleplayer_state, got {data}")

    markdown_status, markdown = load_text_url(base_url.rstrip("/") + "/api/v1/singleplayer?format=markdown")
    if markdown_status != 200 or "# Game State:" not in markdown:
        fail(f"/api/v1/singleplayer?format=markdown expected markdown state, got HTTP {markdown_status}: {markdown[:120]}")

    status, data = load_json_url(base_url.rstrip("/") + "/api/v1/singleplayer?format=xml")
    assert_error_body("/api/v1/singleplayer?format=xml", status, data)
    if status != 400 or not isinstance(data, dict) or data.get("error_code") != "invalid_format":
        fail(f"/api/v1/singleplayer?format=xml expected invalid_format HTTP 400, got HTTP {status}: {data}")

    post_validation_checks = [
        ("/api/v1/singleplayer", b"{", 400),
        ("/api/v1/singleplayer", b"{}", 400),
        ("/api/v1/singleplayer", b'{"action": 1}', 400),
        ("/api/v1/singleplayer", b'{"action": "menu_select"}', 400),
        ("/api/v1/singleplayer", b'{"action": "menu_select", "option": 1}', 400),
        ("/api/v1/profiles", b"{", 400),
        ("/api/v1/profiles", b"{}", 400),
        ("/api/v1/profiles", b'{"action": 1}', 400),
        ("/api/v1/profiles", b'{"action": "switch"}', 400),
        ("/api/v1/profiles", b'{"action": "switch", "profile_id": "1"}', 400),
        ("/api/v1/profiles", b'{"action": "switch", "profile_id": 1.5}', 400),
        ("/api/v1/profiles", b'{"action": "switch", "profile_id": 999999999999}', 400),
        ("/api/v1/profiles", b'{"action": "switch", "profile_id": 4}', 400),
        ("/api/v1/profiles", b'{"action": "unknown", "profile_id": 1}', 400),
    ]
    for path, body, expected_status in post_validation_checks:
        status, data = load_json_url(base_url.rstrip("/") + path, "POST", body)
        assert_error_body(path, status, data)
        if status != expected_status:
            fail(f"{path} expected HTTP {expected_status} for validation check, got {status}: {data}")

    status, data = load_json_url(
        base_url.rstrip("/") + "/api/v1/singleplayer",
        "POST",
        b'{"action": "menu_select", "option": "definitely_not_real"}',
    )
    assert_error_body("/api/v1/singleplayer", status, data)
    if (status, data.get("error_code")) not in {
        (400, "unknown_menu_option"),
        (409, "not_on_menu"),
    }:
        fail(f"/api/v1/singleplayer expected structured invalid menu option error, got HTTP {status}: {data}")

    status, data = load_json_url(
        base_url.rstrip("/") + "/api/v1/singleplayer",
        "POST",
        b'{"action": "unknown_action"}',
    )
    assert_error_body("/api/v1/singleplayer", status, data)
    if status not in {400, 409} or data.get("error_code") not in {"unknown_action", "run_not_in_progress", "local_player_unavailable"}:
        fail(f"/api/v1/singleplayer expected structured unknown/no-run action error, got HTTP {status}: {data}")

    status, profiles_data = load_json_url(base_url.rstrip("/") + "/api/v1/profiles")
    if status != 200 or not isinstance(profiles_data, dict):
        fail(f"/api/v1/profiles expected profile data before active-delete validation, got HTTP {status}: {profiles_data}")
    current_profile_id = profiles_data.get("current_profile_id")
    if isinstance(current_profile_id, int):
        status, data = load_json_url(
            base_url.rstrip("/") + "/api/v1/profiles",
            "POST",
            json.dumps({"action": "delete", "profile_id": current_profile_id}).encode("utf-8"),
        )
        assert_error_body("/api/v1/profiles", status, data)
        if status != 409 or data.get("error_code") != "active_profile_delete":
            fail(f"/api/v1/profiles expected HTTP 409 active_profile_delete, got HTTP {status}: {data}")

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
