# Endpoint Contracts

This document summarizes the stable HTTP and MCP contracts introduced by the endpoint audit work. The raw references remain the source of detailed field-by-field examples:

- `docs/raw-full.md`
- `docs/raw-simplified.md`
- `mcp/README.md`

## Endpoint Index

`GET /` returns a structured API index instead of a plain greeting. Clients can use it as a capability discovery surface before choosing direct HTTP calls or MCP tools.

The index includes:

- `status: "ok"`
- `kind: "api_index"`
- `version`
- `message`
- `bound_prefixes`
- `endpoint_count`
- `endpoints`

Each endpoint row advertises a `method`, `path`, and description. The audit script checks that the index has no duplicate method/path pairs and that documented routes stay aligned with the live API surface.

## Response Envelopes

JSON read endpoints use explicit envelopes so clients can branch on `status` and `kind` without inferring response type from route names.

Examples:

- `singleplayer_state`
- `multiplayer_state`
- `settings`
- `profile`
- `profiles`
- `compendium`
- `bestiary`
- `glossary_cards`
- `glossary_relics`
- `glossary_potions`
- `glossary_keywords`
- `snapshots`
- `snapshot`
- `snapshot_resume`

Markdown state output is still available through `format=markdown` for the singleplayer and multiplayer state routes. JSON state output carries the same `state_type` and screen-specific payloads as before, now wrapped with `status: "ok"` and a route-specific `kind`.

## Current Run Context

Profile-aware and run-aware JSON responses expose shared save/run identity fields where available:

- `profile_id`
- `progress_path`
- `resolved_progress_path`
- `profile_root`
- `save_scope`
- `current_run`

`current_run` always identifies the active profile/save context. Save-backed fields such as `run_id`, `seed`, `start_time`, `save_time`, and `run_time` are present when the active `current_run.save` file exposes them.

`run_id` identifies one concrete attempt and follows this format:

```text
{save_scope}:profile{profile_id}:{start_time}
```

Use `seed` separately when grouping by generated content. Multiple attempts can share a seed.

All exposed path/context strings are normalized with forward slashes, including Windows absolute paths, profile roots, resolved progress paths, and Compendium history paths.

## Profile And Compendium

`GET /api/v1/profile` is the raw profile-progress view. It reports discovered content, achievements, epochs, character stats, global totals, and profile/save/current-run context.

`GET /api/v1/compendium` is the agent-friendly profile-progress view. It groups the same durable progress into Compendium-shaped sections:

- `card_library`
- `relic_collection`
- `potion_lab`
- `bestiary`
- `character_stats`
- `run_history`

Run history is read from the active profile save root and summarized from saved `.run` files. The response is bounded to the 20 most recent entries and includes path/count metadata so clients can tell when more local history exists.

The in-game Bestiary is currently marked future/locked. The Compendium `bestiary` section therefore exposes profile fight stats when available, while `GET /api/v1/bestiary` exposes deterministic model metadata.

## Glossary Endpoints

Glossary endpoints are active-run scoped:

- `GET /api/v1/glossary/cards`
- `GET /api/v1/glossary/relics`
- `GET /api/v1/glossary/potions`
- `GET /api/v1/glossary/keywords`

They expose the current character/run pool plus shared run pools where relevant. Successful responses include profile/save/current-run context, a route-specific `kind`, a `count`, and sorted `items`.

When no run is active, glossary endpoints return HTTP 409 with `error_code: "run_not_in_progress"` while preserving active profile/save context. When run state exists but cannot be read safely, they return HTTP 503 with `error_code: "run_state_unavailable"`.

Card glossary entries include upgrade metadata:

- current cost and star cost
- upgraded flag
- upgradeability
- current/max upgrade levels
- upgraded-preview cost and star cost
- upgraded-preview description

The same upgrade-preview fields are propagated into state card payloads where cards are visible.

## Settings And Bestiary

`GET /api/v1/settings` returns a null-safe settings envelope with display, audio, gameplay, mod, language, and skip-intro fields.

`GET /api/v1/bestiary` returns deterministic monster and encounter metadata:

- `monster_count`
- `encounter_count`
- sorted `monsters`
- sorted `encounters`

Nested Bestiary metadata is sorted where the API can control ordering, including monster moves and likely encounter monster lists.

## Run Save Snapshots

Snapshot support is opt-in and disabled by default. Launch the game with:

```bash
STS2_MCP_SNAPSHOTS=1
```

to enable automatic snapshot capture whenever the game saves the active run. Use:

```bash
STS2_MCP_SNAPSHOT_DIR=/path/to/snapshot/root
```

to override the snapshot root. Without an override, snapshots are stored under the detected active account save root in `sts2_mcp_snapshots`.

The snapshot API has two routes:

- `GET /api/v1/snapshots`
- `POST /api/v1/snapshots`

`GET /api/v1/snapshots` returns:

- `status: "ok"`
- `kind: "snapshots"`
- `enabled`
- `enable_env_var`
- `snapshot_root_env_var`
- `snapshot_root`
- `count`
- `snapshots`

`POST /api/v1/snapshots` supports two actions:

- `{"action": "create"}`
- `{"action": "resume", "snapshot_id": "..."}`

Manual `create` copies the active profile's latest `current_run.save` or `current_run_mp.save` into a snapshot directory and writes `metadata.json`. Automatic snapshots use the same copy format but subscribe to the game's save event.

`resume` restores a selected snapshot into the active profile's current-run save slot. Restore is rejected while a run is in progress. After a successful restore, use the in-game Continue flow to load the restored run.

Before overwriting an existing current-run save, restore writes a `.pre_snapshot_resume_*.backup` copy next to the destination save.

### Snapshot Safety Rules

Snapshot restore does not trust absolute paths from snapshot metadata. It reconstructs the snapshot save path from the configured snapshot root, snapshot ID, and supported save filename, then derives the restore destination from the active profile/save root.

Only supported run-save filenames are accepted:

- `current_run.save`
- `current_run_mp.save`

Manual snapshot creation is rejected from map and shop-like screens:

- `map`
- `shop`
- `fake_merchant`

Those states are intentionally blocked because STS2 saves do not restore the idle map UI exactly, and shop saves do not persist the current merchant inventory. Snapshot after choosing a map node, before entering a shop, or after leaving it.

Snapshot IDs are sanitized path components derived from save scope, profile ID, save type, and timestamp. Snapshot enumeration only inspects immediate child directories of the snapshot root, not arbitrary recursive paths.

The MCP wrappers are:

- `list_snapshots`
- `create_snapshot`
- `resume_snapshot`

They preserve structured endpoint errors and `http_status` through the same bridge behavior as the other MCP read/action wrappers.

## MCP Bridge Configuration And Auth Headers

The Python MCP bridge can read connection settings from both the process environment and `mcp/.env`. Environment variables take precedence; `.env` only fills in keys that are not already set.

Supported bridge settings:

- `STS2_HOST`
- `STS2_PORT`
- `STS2_MCP_AUTH_TOKEN`

`STS2_HOST` and `STS2_PORT` choose the HTTP listener used by all MCP tool calls. They mirror the `--host` and `--port` command-line flags, with command-line parsing refreshing the bridge's base URL before tools are served.

`STS2_MCP_AUTH_TOKEN` is optional. When present, the bridge adds this header to every `httpx` request it sends to the STS2_MCP HTTP API:

```http
Authorization: Bearer <token>
```

This is client-side header injection only. It does not change the game/mod HTTP server's authentication behavior and does not make the listener require a token. It is intended for deployments where the MCP bridge talks through a local proxy, tunnel, or future authenticated listener that expects bearer tokens.

The `.env` parser intentionally stays small and predictable:

- blank lines and `#` comments are ignored
- `KEY=value` is supported
- `export KEY=value` is supported
- single-quoted and double-quoted values are supported
- inline comments are removed only when `#` is preceded by whitespace
- shell environment variables are never overwritten by `.env`

That means `STS2_MCP_AUTH_TOKEN=abc#123` keeps the `#123` suffix, while `STS2_MCP_AUTH_TOKEN=abc # local token` parses as `abc`.

Local `.env` files are ignored by git so bridge secrets and machine-specific host/port settings are not committed.

## Structured Errors

Endpoint failures return non-2xx structured JSON instead of HTTP 200 error payloads whenever the failure is a route, validation, action, read, or state conflict.

Common route/validation error codes include:

- `method_not_allowed`
- `not_found`
- `internal_error`
- `invalid_json`
- `missing_action`
- `invalid_action_type`
- `invalid_action_payload`
- `missing_profile_id`
- `invalid_profile_id`
- `invalid_profile_id_type`
- `unknown_profile_action`
- `missing_snapshot_id`
- `unknown_snapshot_action`
- `invalid_format`

State and action conflict error codes include:

- `not_singleplayer_run`
- `not_multiplayer_run`
- `run_not_in_progress`
- `run_in_progress`
- `active_profile_delete`
- `blocking_popup_active`
- `timeline_manual_action_required`
- `snapshots_disabled`
- `snapshot_not_found`
- `current_run_save_not_found`
- `snapshot_state_not_supported`
- `snapshot_restore_path_unavailable`
- `action_error`

Read endpoint availability/failure codes include:

- `save_manager_unavailable`
- `settings_data_unavailable`
- `profile_data_unavailable`
- `settings_read_failed`
- `profile_build_failed`
- `profiles_read_failed`
- `compendium_build_failed`
- `bestiary_build_failed`
- `glossary_build_failed`
- `snapshots_read_failed`
- `snapshot_create_failed`
- `snapshot_resume_failed`
- `singleplayer_state_read_failed`
- `multiplayer_state_read_failed`

The MCP bridge preserves structured endpoint error bodies and adds `http_status`, so MCP clients can branch on the same error fields that HTTP clients receive.

## Action Readiness Fields

State payloads expose UI/action readiness so agents can avoid clicking hidden, disabled, or stale UI elements.

Covered surfaces include:

- card selection
- hand selection
- card rewards
- event options
- rest options
- relic selection
- bundle selection
- shop and fake merchant purchases
- treasure relic claims
- Crystal Sphere choices
- rewards
- map travel
- combat and multiplayer end-turn controls
- potion use and discard controls
- enemy targeting

Actions that depend on mounted UI controls now check visibility/enabled state before clicking. Proceed actions are similarly gated on visible enabled controls, while preserving the shop inventory close-then-proceed behavior.

## MCP Coverage

The MCP server adds wrappers for read-only metadata and profile endpoints:

- `get_api_index`
- `get_settings`
- `get_profile`
- `get_compendium`
- `get_bestiary`
- `get_glossary_cards`
- `get_glossary_relics`
- `get_glossary_potions`
- `get_glossary_keywords`
- `list_profiles`
- `switch_profile`
- `delete_profile`
- `list_snapshots`
- `create_snapshot`
- `resume_snapshot`

`menu_select` retries through the multiplayer route when a singleplayer menu call is rejected because a multiplayer run is active. Static tests guard route-helper parity so multiplayer tools keep using multiplayer routes and non-multiplayer tools do not accidentally route to multiplayer helpers.

The MCP bridge also supports optional bearer-token injection through `STS2_MCP_AUTH_TOKEN`; this is covered by focused bridge tests and does not alter the HTTP server contract.

## Audit Coverage

`scripts/audit_endpoints.py` provides static and live checks for the endpoint contract. The static mode is suitable for CI and documentation reviews:

```bash
python3 scripts/audit_endpoints.py --skip-live
```

Live mode validates the running mod:

```bash
python3 scripts/audit_endpoints.py --base-url http://127.0.0.1:15526
```

`scripts/test_mcp_server.py` covers focused MCP bridge behavior:

```bash
uv run --project mcp python scripts/test_mcp_server.py
```

The audit suite checks endpoint/index/doc parity, response envelopes, structured errors, normalized save paths, profile/Compendium schemas, glossary schemas, Bestiary schemas, settings schemas, snapshot endpoint contracts, state format validation, action-readiness fields, and safe validation failures that should not mutate a run. The MCP bridge tests cover `.env` precedence and auth-header injection in addition to structured endpoint error propagation.
