#!/usr/bin/env python3
"""Focused tests for the Python MCP bridge.

The bridge normally imports the MCP package only to register tool decorators.
These tests stub that package so they can exercise the endpoint error handling
without starting an MCP server.
"""

from __future__ import annotations

import asyncio
import importlib.util
import json
import sys
import types
from pathlib import Path

import httpx


class _FakeFastMCP:
    def __init__(self, name: str) -> None:
        self.name = name

    def tool(self):
        def decorator(func):
            return func

        return decorator

    def run(self) -> None:
        raise AssertionError("test import should not run the MCP server")


def _load_server_module():
    fake_mcp = types.ModuleType("mcp")
    fake_mcp_server = types.ModuleType("mcp.server")
    fake_fastmcp = types.ModuleType("mcp.server.fastmcp")
    fake_fastmcp.FastMCP = _FakeFastMCP
    sys.modules.setdefault("mcp", fake_mcp)
    sys.modules.setdefault("mcp.server", fake_mcp_server)
    sys.modules["mcp.server.fastmcp"] = fake_fastmcp

    server_path = Path(__file__).resolve().parents[1] / "mcp" / "server.py"
    spec = importlib.util.spec_from_file_location("sts2_mcp_server_under_test", server_path)
    if spec is None or spec.loader is None:
        raise AssertionError(f"could not load {server_path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _json_response(status_code: int, payload: object) -> httpx.Response:
    request = httpx.Request("POST", "http://localhost:15526/api/v1/singleplayer")
    return httpx.Response(status_code, json=payload, request=request)


def _http_status_error(status_code: int, payload: object) -> httpx.HTTPStatusError:
    response = _json_response(status_code, payload)
    return httpx.HTTPStatusError("endpoint error", request=response.request, response=response)


def test_structured_http_error_is_preserved(server) -> None:
    error = _http_status_error(
        409,
        {
            "status": "error",
            "error": "No run in progress",
            "error_code": "run_not_in_progress",
        },
    )

    rendered = server._handle_error(error)
    payload = json.loads(rendered)
    assert payload["status"] == "error"
    assert payload["error_code"] == "run_not_in_progress"
    assert payload["http_status"] == 409


def test_non_endpoint_http_error_stays_text(server) -> None:
    error = _http_status_error(502, {"status": "ok", "message": "bad gateway"})
    rendered = server._handle_error(error)
    assert rendered.startswith("Error: HTTP 502")


async def test_read_tool_preserves_structured_endpoint_error(server) -> None:
    async def fake_glossary_get(kind: str) -> str:
        assert kind == "cards"
        raise _http_status_error(
            503,
            {
                "status": "error",
                "error": "Could not read run state.",
                "error_code": "run_state_unavailable",
                "kind": "cards",
            },
        )

    server._glossary_get = fake_glossary_get
    rendered = await server.get_glossary_cards()
    payload = json.loads(rendered)
    assert payload["status"] == "error"
    assert payload["error_code"] == "run_state_unavailable"
    assert payload["kind"] == "cards"
    assert payload["http_status"] == 503


async def test_menu_select_retries_multiplayer_conflict(server) -> None:
    calls: list[tuple[str, dict]] = []

    async def fake_post(body: dict) -> str:
        calls.append(("singleplayer", body))
        raise _http_status_error(
            409,
            {
                "status": "error",
                "error": "Multiplayer run is active.",
                "error_code": "multiplayer_run_active",
            },
        )

    async def fake_mp_post(body: dict) -> str:
        calls.append(("multiplayer", body))
        return '{"status":"ok"}'

    server._post = fake_post
    server._mp_post = fake_mp_post
    result = await server._menu_select_post({"action": "menu_select", "option": "continue"})

    assert result == '{"status":"ok"}'
    assert calls == [
        ("singleplayer", {"action": "menu_select", "option": "continue"}),
        ("multiplayer", {"action": "menu_select", "option": "continue"}),
    ]


async def test_menu_select_does_not_retry_other_409(server) -> None:
    calls: list[str] = []

    async def fake_post(body: dict) -> str:
        calls.append("singleplayer")
        raise _http_status_error(
            409,
            {
                "status": "error",
                "error": "No run in progress",
                "error_code": "run_not_in_progress",
            },
        )

    async def fake_mp_post(body: dict) -> str:
        calls.append("multiplayer")
        return '{"status":"ok"}'

    server._post = fake_post
    server._mp_post = fake_mp_post
    try:
        await server._menu_select_post({"action": "menu_select", "option": "continue"})
    except httpx.HTTPStatusError:
        pass
    else:
        raise AssertionError("expected non-multiplayer 409 to be re-raised")

    assert calls == ["singleplayer"]


async def test_switch_profile_reposts_from_profile_screen_and_waits(server) -> None:
    calls: list[tuple[str, dict | None]] = []

    async def fake_sleep(_seconds: float) -> None:
        return None

    async def fake_profiles_post(body: dict) -> str:
        calls.append(("profiles_post", body))
        if len([call for call in calls if call[0] == "profiles_post"]) == 1:
            return json.dumps({"status": "ok", "message": "Opened profile screen"})
        return json.dumps({"status": "ok", "message": "Switch requested"})

    async def fake_get(params: dict | None = None) -> str:
        calls.append(("get", params))
        return json.dumps({"status": "ok", "menu_screen": "profile_select"})

    async def fake_profiles_get() -> str:
        calls.append(("profiles_get", None))
        return json.dumps(
            {
                "status": "ok",
                "current_profile_id": 2,
                "profiles": [{"profile_id": 2, "is_current": True}],
            }
        )

    original_sleep = asyncio.sleep
    server.asyncio.sleep = fake_sleep
    server._profiles_post = fake_profiles_post
    server._get = fake_get
    server._profiles_get = fake_profiles_get

    try:
        rendered = await server.switch_profile(2)
        payload = json.loads(rendered)

        assert payload["status"] == "ok"
        assert payload["current_profile_id"] == 2
        assert calls == [
            ("profiles_post", {"action": "switch", "profile_id": 2}),
            ("get", {"format": "json"}),
            ("profiles_post", {"action": "switch", "profile_id": 2}),
            ("profiles_get", None),
        ]
    finally:
        server.asyncio.sleep = original_sleep


async def test_wait_for_profile_timeout_keeps_initial_response(server) -> None:
    async def fake_sleep(_seconds: float) -> None:
        return None

    async def fake_profiles_get() -> str:
        return json.dumps(
            {
                "status": "ok",
                "current_profile_id": 1,
                "profiles": [{"profile_id": 1, "is_current": True}],
            }
        )

    original_sleep = asyncio.sleep
    server.asyncio.sleep = fake_sleep
    server._profiles_get = fake_profiles_get

    try:
        rendered = await server._wait_for_profile(3, '{"status":"ok","message":"Switch requested"}')
        payload = json.loads(rendered)

        assert payload["status"] == "error"
        assert payload["current_profile_id"] == 1
        assert payload["profiles"] == [{"profile_id": 1, "is_current": True}]
        assert payload["initial_response"] == '{"status":"ok","message":"Switch requested"}'
    finally:
        server.asyncio.sleep = original_sleep


async def test_delete_profile_preserves_structured_endpoint_error(server) -> None:
    async def fake_profiles_post(body: dict) -> str:
        assert body == {"action": "delete", "profile_id": 1}
        raise _http_status_error(
            409,
            {
                "status": "error",
                "error": "Cannot delete the active profile",
                "error_code": "active_profile_delete",
            },
        )

    server._profiles_post = fake_profiles_post
    rendered = await server.delete_profile(1)
    payload = json.loads(rendered)

    assert payload["status"] == "error"
    assert payload["error_code"] == "active_profile_delete"
    assert payload["http_status"] == 409


async def test_create_snapshot_preserves_structured_endpoint_error(server) -> None:
    async def fake_snapshots_post(body: dict) -> str:
        assert body == {"action": "create"}
        raise _http_status_error(
            400,
            {
                "status": "error",
                "error": "Snapshots are disabled.",
                "error_code": "snapshots_disabled",
            },
        )

    server._snapshots_post = fake_snapshots_post
    rendered = await server.create_snapshot()
    payload = json.loads(rendered)

    assert payload["status"] == "error"
    assert payload["error_code"] == "snapshots_disabled"
    assert payload["http_status"] == 400


async def test_resume_snapshot_uses_snapshot_id(server) -> None:
    async def fake_snapshots_post(body: dict) -> str:
        assert body == {"action": "resume", "snapshot_id": "snapshot-1"}
        return '{"status":"ok","kind":"snapshot_resume"}'

    server._snapshots_post = fake_snapshots_post
    rendered = await server.resume_snapshot("snapshot-1")
    payload = json.loads(rendered)

    assert payload["status"] == "ok"
    assert payload["kind"] == "snapshot_resume"


def main() -> None:
    server = _load_server_module()
    test_structured_http_error_is_preserved(server)
    test_non_endpoint_http_error_stays_text(server)
    asyncio.run(test_read_tool_preserves_structured_endpoint_error(server))
    asyncio.run(test_menu_select_retries_multiplayer_conflict(server))
    asyncio.run(test_menu_select_does_not_retry_other_409(server))
    asyncio.run(test_switch_profile_reposts_from_profile_screen_and_waits(server))
    asyncio.run(test_wait_for_profile_timeout_keeps_initial_response(server))
    asyncio.run(test_delete_profile_preserves_structured_endpoint_error(server))
    asyncio.run(test_create_snapshot_preserves_structured_endpoint_error(server))
    asyncio.run(test_resume_snapshot_uses_snapshot_id(server))
    print("mcp server tests passed")


if __name__ == "__main__":
    main()
