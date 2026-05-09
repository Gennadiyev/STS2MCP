#!/usr/bin/env python3
"""Audit the STS2_MCP HTTP endpoint surface.

This script intentionally uses only the Python standard library so it can run
from a checkout without installing the MCP package.
"""

from __future__ import annotations

import argparse
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

    for method, path in EXPECTED_ENDPOINTS:
        if method != "GET":
            continue
        status, data = load_json_url(base_url.rstrip("/") + path)
        assert_error_body(path, status, data)

        if path in {"/api/v1/settings", "/api/v1/profile", "/api/v1/compendium", "/api/v1/bestiary", "/api/v1/profiles"} and status != 200:
            fail(f"{path} expected HTTP 200, got {status}: {data}")

        if path.startswith("/api/v1/glossary/") and status not in {200, 409}:
            fail(f"{path} expected HTTP 200 or 409, got {status}: {data}")

    status, data = load_json_url(base_url.rstrip("/") + "/api/v1/singleplayer?format=json")
    if status != 200 or not isinstance(data, dict) or "state_type" not in data:
        fail(f"/api/v1/singleplayer?format=json expected JSON state, got HTTP {status}: {data}")

    markdown_status, markdown = load_text_url(base_url.rstrip("/") + "/api/v1/singleplayer?format=markdown")
    if markdown_status != 200 or "# Game State:" not in markdown:
        fail(f"/api/v1/singleplayer?format=markdown expected markdown state, got HTTP {markdown_status}: {markdown[:120]}")

    post_validation_checks = [
        ("/api/v1/singleplayer", b"{", 400),
        ("/api/v1/singleplayer", b"{}", 400),
        ("/api/v1/profiles", b"{", 400),
        ("/api/v1/profiles", b"{}", 400),
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

    status, data = load_json_url(base_url.rstrip("/") + "/api/v1/settings", "POST", b"{}")
    assert_error_body("/api/v1/settings", status, data)
    if status != 405:
        fail(f"/api/v1/settings expected HTTP 405 for POST, got {status}: {data}")

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
    audit_static_formatters(repo)
    if not args.skip_live:
        audit_live(args.base_url)


if __name__ == "__main__":
    main()
