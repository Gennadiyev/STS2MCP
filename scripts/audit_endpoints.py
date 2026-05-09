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

    status, data = load_json_url(base_url.rstrip("/") + "/api/v1/settings", "POST", b"{}")
    assert_error_body("/api/v1/settings", status, data)
    if status != 405:
        fail(f"/api/v1/settings expected HTTP 405 for POST, got {status}: {data}")

    print("live: GET endpoint smoke checks passed")
    print("live: safe POST validation checks passed")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://localhost:15526", help="STS2_MCP HTTP base URL")
    parser.add_argument("--skip-live", action="store_true", help="Only check checked-in docs")
    args = parser.parse_args()

    repo = Path(__file__).resolve().parents[1]
    audit_docs(repo)
    if not args.skip_live:
        audit_live(args.base_url)


if __name__ == "__main__":
    main()
