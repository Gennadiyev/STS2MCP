# API Quick Reference

HTTP API on `localhost:15526`. No authentication.

- `GET /api/v1/singleplayer` — read game state
- `POST /api/v1/singleplayer` — perform action
- `GET /api/v1/multiplayer` — read multiplayer state
- `POST /api/v1/multiplayer` — perform multiplayer action
- `GET /api/v1/settings` — read settings and preferences
- `GET /api/v1/profile` — read current profile progress
- `GET /api/v1/compendium` — read Compendium-shaped profile progress
- `GET /api/v1/bestiary` — read monster and encounter metadata
- `GET /api/v1/glossary/cards` — read active-run card pool metadata
- `GET /api/v1/glossary/relics` — read active-run relic pool metadata
- `GET /api/v1/glossary/potions` — read active-run potion pool metadata
- `GET /api/v1/glossary/keywords` — read active-run keyword metadata
- `GET /api/v1/profiles` — list profile slots
- `POST /api/v1/profiles` — switch or delete profile slots

HTTP error responses include `status: "error"` and `error`. Some route-specific errors also include `error_code`.

Singleplayer and multiplayer endpoints are mutually exclusive (HTTP 409 if mismatched). The mismatch response includes `error_code`: `multiplayer_run_active` when singleplayer is called during a multiplayer run, or `not_multiplayer_run` when multiplayer is called outside one.

## GET — Query Parameters

| Parameter | Values             | Default | Description     |
|-----------|--------------------|---------|-----------------|
| `format`  | `json`, `markdown` | `json`  | Response format |

## GET — State Types

Every JSON response includes:
- `state_type` — which screen the game is on (see below)
- `run` — `{ act, floor, ascension }` (absent for `menu`)
- `current_run` — run identity and save context when a run is active, including `profile_id`, `progress_path`, `resolved_progress_path`, `profile_root`, `save_scope`, `run_id`, and `seed` when the save file exposes it
- `player` — full player state: character, HP, gold, deck, relics, potions, `max_potion_slots` (belt capacity, grows with relics), and during combat: energy, hand, piles, orbs (absent for `menu`)

Serialized card objects in hand, deck, piles, rewards, card selections, bundles, and glossary card items include energy/star costs plus upgrade fields: `is_upgraded`, `is_upgradable`, `current_upgrade_level`, `max_upgrade_level`, `upgrade_preview_type`, `upgrade_preview_cost`, `upgrade_preview_star_cost`, and `upgrade_preview_description`. Hand cards also include `requires_target` and `valid_targets` for enemy-targeted cards. Shop card items expose the same fields with a `card_` prefix, for example `card_upgrade_preview_description`.

| `state_type` | Screen | Available Actions |
|---|---|---|
| `menu` | Main menu, menu submenu (incl. multiplayer host/join/load lobby), character select, or a blocking FTUE/tutorial/popup that can also appear mid-run | `menu_select` |
| `unknown` | Unrecognized room or null state | None |
| `monster` / `elite` / `boss` | In combat | `play_card`, `use_potion`, `end_turn` |
| `hand_select` | In-combat card selection (exhaust, discard, upgrade) | `combat_select_card`, `combat_confirm_selection` |
| `rewards` | Rewards screen (post-combat or event-triggered) | `claim_reward`, `proceed` |
| `card_reward` | Pick a card to add to deck | `select_card_reward`, `skip_card_reward` |
| `map` | Map navigation | `choose_map_node` |
| `event` | Event or Ancient encounter | `choose_event_option`, `advance_dialogue` |
| `rest_site` | Rest site | `choose_rest_option`, `proceed` |
| `shop` | Shop (auto-opens inventory; `can_proceed` is true when `proceed` can close inventory and leave) | `shop_purchase`, `proceed` |
| `fake_merchant` | Fake Merchant event (relic-only shop; `can_proceed` follows the same close-inventory behavior) | `shop_purchase`, `proceed` |
| `treasure` | Treasure room (auto-opens chest) | `claim_treasure_relic`, `proceed` |
| `card_select` | Deck card selection overlay (transform, upgrade, remove, choose-a-card) | `select_card`, `confirm_selection`, `cancel_selection` |
| `bundle_select` | Card bundle choice overlay | `select_bundle`, `confirm_bundle_selection`, `cancel_bundle_selection` |
| `relic_select` | Relic choice overlay (boss relics) | `select_relic`, `skip_relic_selection` |
| `crystal_sphere` | Crystal Sphere minigame | `crystal_sphere_set_tool`, `crystal_sphere_click_cell`, `crystal_sphere_proceed` |
| `game_over` | Run ended | `menu_select` with `main_menu` |
| `overlay` | Unhandled overlay (catch-all, prevents soft-lock) | None (manual interaction needed) |

**Note:** `use_potion` and `discard_potion` work during any state where potions are accessible (combat, map, events, etc.).

## POST — Actions

All POST requests use JSON body with `"action"` field. Action responses include `{ "status": "ok" | "error" }`; successful actions usually include `message`, and failed actions include `error`.

### Menu / Game Over

| Action | Parameters | When to Use |
|---|---|---|
| `menu_select` | `option`: string, `seed`?: string | Choose an advertised menu option. Options are case-insensitive. Main menu may include `continue`, `abandon_run`, `singleplayer`, `multiplayer`, `compendium`, `timeline`, `settings`, and `quit`; `abandon_run` opens a confirmation popup. Submenus include `back` where visible, including `profile_select` options `profile_1`, `profile_2`, `profile_3`, and `back`. Blocking popups expose normalized button labels such as `ignore` or `back`. `game_over` supports `main_menu` only; `continue` returns an error. Supplying `seed` in unsupported contexts such as standard singleplayer character select returns an error and does not start a run. If Timeline has pending obtained epochs that require manual reveal, it may appear in `blocked_options`; selecting `timeline` returns `manual_action_required: true` with `pending_epoch_ids` instead of opening Timeline. Multiplayer flow: on `multiplayer_join` use `refresh` / `back` / `join_<index>` / `join_<player_id>`. On `multiplayer_load_lobby` use `confirm` (or `embark`) to ready up, `unready` to retract, `back` to leave. On `character_select` while in MP, an additional `unready` option becomes available after readying, plus a `lobby` block in state lists ascension, all_ready, and per-player roster. |

### Profiles

`GET /api/v1/profile` returns persistent progress for the active profile, including character stats, discoveries, achievements, epochs, and global run totals.

`GET /api/v1/settings` returns `status: "ok"`, `kind: "settings"`, display, audio, gameplay, language, and mod-loading preferences.

`GET /api/v1/bestiary` returns deterministic reflected monster and encounter metadata with `status`, `kind`, `monster_count`, `encounter_count`, `monsters`, and `encounters`. Profile-specific fight stats are also summarized under `/api/v1/compendium`.

The `/api/v1/glossary/*` endpoints expose active-run pool metadata. They require a run in progress and are scoped to the current run/character context plus shared run pools such as Colorless cards, shared relics, and shared potions, not profile-wide discovered content. Card glossary items include upgrade availability plus upgraded-preview cost and description. Successful responses include `profile_id`, `progress_path`, `resolved_progress_path`, `profile_root`, `save_scope`, `current_run.run_id`, `current_run.seed`, `kind`, `count`, and `items`. If no run is active, they return HTTP 409 with `error_code: "run_not_in_progress"` and the same profile/save context fields.

`GET /api/v1/compendium` returns the active profile grouped like the in-game Compendium:

The top level includes `profile_id`, `progress_path`, `resolved_progress_path`, `profile_root`, and `save_scope`, matching `/api/v1/profile`.

When a run is active, the response includes `current_run.run_id` in `{save_scope}:profile{profile_id}:{start_time}` format. This identifies the specific run attempt, while `seed` identifies the generated run content.

| Section | Status |
|---|---|
| `card_library` | Discovered cards and card stats; `/api/v1/glossary/cards` adds metadata when a run context exists. |
| `relic_collection` | Discovered relic IDs; `/api/v1/glossary/relics` adds metadata when a run context exists. |
| `potion_lab` | Discovered potion IDs; `/api/v1/glossary/potions` adds metadata when a run context exists. |
| `bestiary` | Encounter/enemy profile stats plus `/api/v1/bestiary` model metadata. The game UI marks Bestiary as future/locked. |
| `character_stats` | Per-character and global totals. |
| `run_history` | Summaries of the active profile's saved `saves/history/*.run` files, capped to the 20 most recent entries. |

`GET /api/v1/profiles` returns the three profile slots with `status`, `kind`, `count`, and per-slot save context fields (`progress_path`, `resolved_progress_path`, `profile_root`, `save_scope`):

`GET /api/v1/profile` returns the active profile's progress with `status`, `kind: "profile"`, `profile_id`, save scope/path context, `resolved_progress_path`, and `current_run` when a run is active.

`GET /api/v1/compendium` returns the active profile's Compendium-shaped progress with `status`, `kind: "compendium"`, the same profile/save context, and grouped Compendium sections.

```json
{
  "status": "ok",
  "kind": "profiles",
  "current_profile_id": 1,
  "count": 3,
  "profiles": [
    { "id": 1, "profile_id": 1, "is_current": true, "has_data": true, "progress_path": "modded/profile1/saves/progress.save", "resolved_progress_path": "C:/.../progress.save", "profile_root": "modded/profile1", "save_scope": "modded" },
    { "id": 2, "profile_id": 2, "is_current": false, "has_data": false, "progress_path": "modded/profile2/saves/progress.save", "resolved_progress_path": "modded/profile2/saves/progress.save", "profile_root": "modded/profile2", "save_scope": "modded" },
    { "id": 3, "profile_id": 3, "is_current": false, "has_data": true, "progress_path": "modded/profile3/saves/progress.save", "resolved_progress_path": "C:/.../progress.save", "profile_root": "modded/profile3", "save_scope": "modded" }
  ]
}
```

`POST /api/v1/profiles` supports:

| Action | Parameters | When to Use |
|---|---|---|
| `switch` | `profile_id`: 1-3 | Switch through the game profile UI. Empty slots can be used for fresh-profile testing. Cannot be used during a run. |
| `delete` | `profile_id`: 1-3 | Delete an inactive profile slot. The active profile is rejected. |

Profile action validation uses non-2xx HTTP status codes: invalid profile IDs and unknown actions return HTTP 400; deleting the active profile and switching during a run return HTTP 409 with an `error_code`.

### Combat

| Action | Parameters | When to Use |
|---|---|---|
| `play_card` | `card_index`: int, `target`?: string | Play a card from hand when `can_play` is true. `target` is required when the card has `requires_target`; use one of `valid_targets`. |
| `use_potion` | `slot`: int, `target`?: string | Use a potion when its state says `can_use`. `target` required for enemy-targeting potions; use one of `valid_targets`. Works outside combat for non-combat-only, non-enemy-targeting potions. |
| `discard_potion` | `slot`: int | Discard a potion when its state says `can_discard`. Use when slots are full and you need room for incoming potions. |
| `end_turn` | _(none)_ | End the player's turn when battle state says `can_end_turn`. |

### In-Combat Hand Selection (`hand_select`)

| Action | Parameters | When to Use |
|---|---|---|
| `combat_select_card` | `card_index`: int | Select/deselect a visible hand card whose `can_select` is true during selection prompts. |
| `combat_confirm_selection` | _(none)_ | Confirm the hand card selection when the visible confirm button is enabled. |

### Rewards (`rewards`)

| Action | Parameters | When to Use |
|---|---|---|
| `claim_reward` | `index`: int | Claim a visible enabled reward. Card rewards open the `card_reward` screen. |
| `proceed` | _(none)_ | Leave the rewards screen. |

### Card Reward (`card_reward`)

| Action | Parameters | When to Use |
|---|---|---|
| `select_card_reward` | `card_index`: int | Pick a card to add to deck. |
| `skip_card_reward` | _(none)_ | Skip the card reward (if allowed). |

### Map (`map`)

| Action | Parameters | When to Use |
|---|---|---|
| `choose_map_node` | `index`: int | Travel to a visible travelable node from `next_options`. |

### Event (`event`)

| Action | Parameters | When to Use |
|---|---|---|
| `choose_event_option` | `index`: int | Choose a visible event option by index from state. Locked or disabled options return an error. Also used for "Proceed" options. |
| `advance_dialogue` | _(none)_ | Click through Ancient dialogue until `in_dialogue` is false. |

### Rest Site (`rest_site`)

| Action | Parameters | When to Use |
|---|---|---|
| `choose_rest_option` | `index`: int | Choose a visible enabled rest, smith, or other option. |
| `proceed` | _(none)_ | Leave the rest site when the visible proceed button is enabled. |

### Shop (`shop`)

| Action | Parameters | When to Use |
|---|---|---|
| `shop_purchase` | `index`: int | Buy an item by its index. Must be stocked and affordable. |
| `proceed` | _(none)_ | Leave the shop when the visible proceed button is enabled, or close an open inventory first when its visible back button is enabled. |

### Treasure (`treasure`)

| Action | Parameters | When to Use |
|---|---|---|
| `claim_treasure_relic` | `index`: int | Claim a visible enabled relic from the opened chest. |
| `proceed` | _(none)_ | Leave the treasure room when the visible proceed button is enabled. |

### Card Selection Overlay (`card_select`)

| Action | Parameters | When to Use |
|---|---|---|
| `select_card` | `index`: int | Grid screens: toggle a visible card whose `can_select` is true unless a preview is open. Choose-a-card: pick immediately when `can_select` is true. |
| `confirm_selection` | _(none)_ | Confirm only when the visible confirm button is enabled. Not needed for choose-a-card. |
| `cancel_selection` | _(none)_ | Cancel preview, skip (choose-a-card), or close screen when the visible cancel/skip button is enabled. |

### Bundle Selection Overlay (`bundle_select`)

| Action | Parameters | When to Use |
|---|---|---|
| `select_bundle` | `index`: int | Open a visible enabled bundle preview. |
| `confirm_bundle_selection` | _(none)_ | Confirm the previewed bundle when the visible confirm button is enabled. |
| `cancel_bundle_selection` | _(none)_ | Cancel the bundle preview when the visible cancel button is enabled. |

### Relic Selection Overlay (`relic_select`)

| Action | Parameters | When to Use |
|---|---|---|
| `select_relic` | `index`: int | Pick a visible enabled relic (immediate). |
| `skip_relic_selection` | _(none)_ | Skip the relic choice when the skip button is visible and enabled. |

### Crystal Sphere (`crystal_sphere`)

| Action | Parameters | When to Use |
|---|---|---|
| `crystal_sphere_set_tool` | `tool`: `"big"` or `"small"` | Switch divination tool. |
| `crystal_sphere_click_cell` | `x`: int, `y`: int | Reveal a cell. |
| `crystal_sphere_proceed` | _(none)_ | Finish the minigame. |

## Multiplayer Additions

`POST /api/v1/multiplayer` supports all singleplayer actions plus:

| Action | Parameters | When to Use |
|---|---|---|
| `end_turn` | _(none)_ | Vote to end turn when battle state says `can_end_turn`. Turn ends when all players vote. |
| `undo_end_turn` | _(none)_ | Retract end-turn vote when battle state says `can_undo_end_turn` (before all players committed). |
