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

## Game Listener Port Configuration

The C# game mod chooses its HTTP listener port during startup. Changing the port requires restarting the game; it does not rebind an already-running listener.

Port resolution order:

1. `STS2_PORT` from the game process environment.
2. `STS2_PORT` from `.env` next to the installed `STS2_MCP.dll`.
3. `STS2_PORT` from `.env` in the game working directory.
4. `STS2_MCP.conf` next to the installed mod DLL.
5. `DefaultPort`, currently `15526`.

The environment variable and `.env` value must parse as an integer from 1 through 65535. Invalid values are logged and the loader continues to the next configured source.

The game-side `.env` parser supports:

- blank lines
- full-line comments
- `KEY=value`
- `export KEY=value`
- single-quoted values
- double-quoted values
- inline comments for unquoted values

Examples:

```dotenv
STS2_PORT=15527
export STS2_PORT="15527" # local testing port
```

The game/mod listener and Python MCP bridge are configured separately. If the mod binds a custom port, the MCP bridge must use the same port through `--port`, `STS2_PORT`, or its own `mcp/.env` configuration.

Local `.env` files are ignored by git because they may contain machine-specific listener settings or bridge secrets.

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
- `invalid_format`

State and action conflict error codes include:

- `not_singleplayer_run`
- `not_multiplayer_run`
- `run_not_in_progress`
- `run_in_progress`
- `active_profile_delete`
- `blocking_popup_active`
- `timeline_manual_action_required`
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

`menu_select` retries through the multiplayer route when a singleplayer menu call is rejected because a multiplayer run is active. Static tests guard route-helper parity so multiplayer tools keep using multiplayer routes and non-multiplayer tools do not accidentally route to multiplayer helpers.

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

The audit suite checks endpoint/index/doc parity, response envelopes, structured errors, normalized save paths, profile/Compendium schemas, glossary schemas, Bestiary schemas, settings schemas, state format validation, action-readiness fields, and safe validation failures that should not mutate a run.
