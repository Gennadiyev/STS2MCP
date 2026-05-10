# MCP Tools

## Singleplayer

| Tool | Scope | Description |
|---|---|---|
| `get_game_state(format?, wait_for_actionable?, wait_timeout?, poll_interval?)` | General | Get current game state (`markdown` or `json`), optionally waiting through transient non-actionable states |
| `log_agent_decision(summary, reasoning?, intended_action?, alternatives?, confidence?, tags?)` | General | Add a structured decision annotation to the run log |
| `log_external_token_usage(usage_json)` | General | Attach exact/external model token usage to a run log event or tool call |
| `log_model_message(role, content, source?, turn_id?, turn_index?, message_id?, related_tool_call_id?, related_event_id?, state_sha256?, content_preview?, exact_*_tokens?, token_source?, model?)` | General | Attach prompt/completion/message records to a stable conversation turn |
| `menu_select(option, seed?)` | General | Select a visible menu/game-over option |
| `get_profile()` | Profiles | Get active profile progress |
| `list_profiles()` | Profiles | List profile slots and active slot |
| `switch_profile(profile_id)` | Profiles | Switch to a profile slot through the game UI |
| `delete_profile(profile_id)` | Profiles | Delete an inactive profile slot |
| `use_potion(slot, target?)` | General | Use a potion (works in and out of combat) |
| `discard_potion(slot)` | General | Discard a potion to free up the slot |
| `proceed_to_map()` | General | Proceed from rewards/rest site/shop/treasure to the map |
| `combat_play_card(card_index, target?)` | Combat | Play a card from hand |
| `combat_end_turn()` | Combat | End the current turn |
| `combat_select_card(card_index)` | Combat Selection | Select a card from hand during exhaust/discard prompts |
| `combat_confirm_selection()` | Combat Selection | Confirm the in-combat card selection |
| `rewards_claim(reward_index)` | Rewards | Claim a reward from the post-combat screen |
| `rewards_pick_card(card_index)` | Rewards | Select a card from the card reward screen |
| `rewards_skip_card()` | Rewards | Skip the card reward |
| `map_choose_node(node_index)` | Map | Choose a map node to travel to |
| `rest_choose_option(option_index)` | Rest Site | Choose a rest site option (rest, smith, etc.) |
| `shop_purchase(item_index)` | Shop | Purchase an item from the shop |
| `event_choose_option(option_index)` | Event | Choose an event option (including Proceed) |
| `event_advance_dialogue()` | Event | Advance ancient event dialogue |
| `deck_select_card(card_index)` | Card Select | Pick/toggle a card in the selection screen |
| `deck_confirm_selection()` | Card Select | Confirm the current card selection |
| `deck_cancel_selection()` | Card Select | Cancel/skip card selection |
| `bundle_select(bundle_index)` | Bundle Select | Open a bundle preview |
| `bundle_confirm_selection()` | Bundle Select | Confirm the current bundle preview |
| `bundle_cancel_selection()` | Bundle Select | Cancel the current bundle preview |
| `relic_select(relic_index)` | Relic Select | Choose a relic from the selection screen |
| `relic_skip()` | Relic Select | Skip relic selection |
| `treasure_claim_relic(relic_index)` | Treasure | Claim a relic from the treasure chest |
| `crystal_sphere_set_tool(tool)` | Crystal Sphere | Switch the active divination tool |
| `crystal_sphere_click_cell(x, y)` | Crystal Sphere | Click a hidden cell in the grid |
| `crystal_sphere_proceed()` | Crystal Sphere | Continue after the minigame finishes |

## Multiplayer

All multiplayer tools are prefixed with `mp_`. They route through `/api/v1/multiplayer` and are only available during multiplayer (co-op) runs. The endpoints automatically guard against cross-mode calls.

| Tool | Scope | Description |
|---|---|---|
| `mp_get_game_state(format?, wait_for_actionable?, wait_timeout?, poll_interval?)` | General | Get multiplayer game state (all players, votes, bids), optionally waiting through transient non-actionable states |
| `mp_combat_play_card(card_index, target?)` | Combat | Play a card from the local player's hand |
| `mp_combat_end_turn()` | Combat | Submit end-turn vote (turn ends when all players submit) |
| `mp_combat_undo_end_turn()` | Combat | Retract end-turn vote |
| `mp_use_potion(slot, target?)` | General | Use a potion from the local player's slots |
| `mp_discard_potion(slot)` | General | Discard a potion from the local player's slots |
| `mp_proceed_to_map()` | General | Proceed from current screen to the map |
| `mp_map_vote(node_index)` | Map | Vote for a map node (travel when all agree) |
| `mp_event_choose_option(option_index)` | Event | Vote for / choose an event option |
| `mp_event_advance_dialogue()` | Event | Advance ancient event dialogue |
| `mp_rest_choose_option(option_index)` | Rest Site | Choose a rest site option (per-player, no vote) |
| `mp_shop_purchase(item_index)` | Shop | Purchase an item (per-player inventory) |
| `mp_rewards_claim(reward_index)` | Rewards | Claim a post-combat reward |
| `mp_rewards_pick_card(card_index)` | Rewards | Select a card from the card reward screen |
| `mp_rewards_skip_card()` | Rewards | Skip the card reward |
| `mp_deck_select_card(card_index)` | Card Select | Pick/toggle a card in the selection screen |
| `mp_deck_confirm_selection()` | Card Select | Confirm the current card selection |
| `mp_deck_cancel_selection()` | Card Select | Cancel/skip card selection |
| `mp_bundle_select(bundle_index)` | Bundle Select | Open a bundle preview |
| `mp_bundle_confirm_selection()` | Bundle Select | Confirm the current bundle preview |
| `mp_bundle_cancel_selection()` | Bundle Select | Cancel the current bundle preview |
| `mp_combat_select_card(card_index)` | Combat Selection | Select a card during in-combat selection prompts |
| `mp_combat_confirm_selection()` | Combat Selection | Confirm in-combat card selection |
| `mp_relic_select(relic_index)` | Relic Select | Choose a relic from the selection screen |
| `mp_relic_skip()` | Relic Select | Skip relic selection |
| `mp_treasure_claim_relic(relic_index)` | Treasure | Bid on a relic (relic fight if contested) |
| `mp_crystal_sphere_set_tool(tool)` | Crystal Sphere | Switch the active divination tool |
| `mp_crystal_sphere_click_cell(x, y)` | Crystal Sphere | Click a hidden cell in the grid |
| `mp_crystal_sphere_proceed()` | Crystal Sphere | Continue after the minigame finishes |

## Run Logging

The MCP bridge writes structured JSONL logs by default under `logs/run_<timestamp>-<id>.jsonl` relative to the server working directory. It also writes `logs/run_<timestamp>-<id>.summary.json` with run-level token rollups. Each JSONL line has a stable envelope with `schema_version`, `token_schema_version`, `run_id`, `event_id`, `sequence`, UTC `timestamp`, `monotonic_ms`, `event_type`, token fields, and when applicable `tool_call_id` / `tool_name`.

Logged events include:

- `session_start` with bridge configuration and runtime metadata.
- `tool_call_start`, `tool_call_result`, and `tool_call_error` for every MCP tool.
- `http_request`, `http_response`, and `http_error` for calls to the STS2_MCP REST API.
- `state_poll` and `state_poll_final_format` for smart polling decisions.
- `agent_decision` entries from `log_agent_decision`.
- `external_token_usage` entries from `log_external_token_usage`.
- `model_message` entries from `log_model_message`.

Tool and HTTP results include length, byte count, SHA-256, preview text, truncation status, and estimated token count. Keys containing `authorization`, `cookie`, `password`, `secret`, `token`, `api_key`, or `apikey` are redacted before writing.

Every record includes:

- `input_tokens`
- `output_tokens`
- `tool_response_tokens`
- `hidden_poll_tokens`
- `total_tokens`
- `token_source`
- `tokenizer_name`
- `tokenizer_version`
- `model_family`
- `estimation_method`

By default these counts use a deterministic regex estimator (`generic_regex_v1`) so runs remain comparable without installing model-specific tokenizers. Treat these as estimates, not provider-billed tokens. Use `log_external_token_usage` to attach exact model API usage when a client has it:

```json
{
  "related_tool_call_id": "9f...",
  "model": "claude-sonnet-4.6",
  "input_tokens": 1200,
  "output_tokens": 400,
  "token_source": "exact"
}
```

Use `log_model_message` to attach first-class prompt and turn structure:

```json
{
  "role": "user",
  "content": "Choose the best card to play.",
  "source": "agent-wrapper",
  "turn_id": "run-1-turn-7",
  "turn_index": 7,
  "message_id": "msg-007-user",
  "state_sha256": "..."
}
```

Message roles are `system`, `user`, `assistant`, `tool`, `developer`, and `external`. Each `model_message` record stores `message_id`, `turn_id`, `turn_index`, role/source, content hash, bounded preview metadata, estimated tokens, optional exact token usage, privacy flags, and related `tool_call_id` / `event_id` links when supplied. If `turn_id` or `message_id` is omitted, the bridge generates stable IDs for the current run. Set `content_preview=false` to keep hashes and counts while omitting prompt preview text.

The summary artifact rolls up totals by tool name, event type, state type, game mode, action category, run phase, floor, token source, turn, role, and message source where available. It also tracks total prompts, total turns, average tokens per turn, tool-result token share, hidden polling token share, hidden polling cost, largest prompts, largest payloads, repeated-state cost, invalid-action cost, replay artifact size/token metadata, and externally supplied usage records.

Logging options:

```bash
python server.py --log-dir logs
python server.py --disable-run-log
python server.py --log-preview-chars 8000
python server.py --log-full-text
python server.py --tokenizer-profile openai_cl100k_proxy
python server.py --token-model-family anthropic --tokenizer-name claude_proxy_regex
```

`STS2_MCP_LOG_DIR` can also set the default log directory. Use `--log-full-text` when you need complete replayable tool/API text for evaluation; otherwise previews plus hashes keep the log smaller while preserving integrity checks. Token estimates are computed from the full text before truncation, even when only previews are written.

Validate a log and its summary:

```bash
python validate_run_log.py --self-test
python validate_run_log.py logs/run_<timestamp>-<id>.jsonl
```

The validator checks JSONL parseability, strictly increasing sequence numbers, unique `event_id` values, non-decreasing monotonic time, token field consistency, summary rollup consistency, model-message roles, monotonic first-seen turn ordering, message-to-turn linkage, external-usage-to-message reconciliation, and a deterministic tokenizer fixture.

Privacy notes:

- Token counts are computed after recursive redaction for structured arguments and metadata.
- Response text is counted before preview truncation, but full response text is stored only with `--log-full-text`.
- `log_model_message` stores prompt previews by default. Pass `content_preview=false` when clients need hash/token accounting without prompt text snippets.
- External usage records may reveal provider/model information supplied by the client; redact those fields client-side if needed.

## Smart State Polling

`get_game_state` and `mp_get_game_state` default to `wait_for_actionable=true`. The bridge polls JSON state until one of these conditions is met, then returns the requested format:

- combat reaches the player's play phase;
- event dialogue/options are available;
- reward, rest, shop, treasure, or Crystal Sphere controls are actionable;
- another non-transient state is reached;
- `wait_timeout` expires.

Set `wait_for_actionable=false` to get the immediate raw state. `wait_timeout` is capped at 60 seconds and `poll_interval` is capped between 0.1 and 5 seconds.
