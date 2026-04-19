# API Quick Reference

HTTP API on `localhost:15526`. No authentication.

- `GET /api/v1/singleplayer` — read game state
- `POST /api/v1/singleplayer` — perform action
- `GET /api/v1/multiplayer` — read multiplayer state
- `POST /api/v1/multiplayer` — perform multiplayer action
- `GET /api/v1/profile` — read current profile progress
- `GET /api/v1/profiles` — list profile slots
- `POST /api/v1/profiles` — switch or delete profile slots

Singleplayer and multiplayer endpoints are mutually exclusive (HTTP 409 if mismatched).

## GET — Query Parameters

| Parameter | Values             | Default | Description     |
|-----------|--------------------|---------|-----------------|
| `format`  | `json`, `markdown` | `json`  | Response format |

## GET — State Types

Every JSON response includes:
- `state_type` — which screen the game is on (see below)
- `run` — `{ act, floor, ascension }` (absent for `menu`)
- `player` — full player state: character, HP, gold, relics, potions, and during combat: energy, hand, piles, orbs (absent for `menu`)

| `state_type` | Screen | Available Actions |
|---|---|---|
| `menu` | Main menu or menu submenu, no run in progress | `menu_select` |
| `unknown` | Unrecognized room or null state | None |
| `monster` / `elite` / `boss` | In combat | `play_card`, `use_potion`, `end_turn` |
| `hand_select` | In-combat card selection (exhaust, discard, upgrade) | `combat_select_card`, `combat_confirm_selection` |
| `rewards` | Rewards screen (post-combat or event-triggered) | `claim_reward`, `proceed` |
| `card_reward` | Pick a card to add to deck | `select_card_reward`, `skip_card_reward` |
| `map` | Map navigation | `choose_map_node` |
| `event` | Event or Ancient encounter | `choose_event_option`, `advance_dialogue` |
| `rest_site` | Rest site | `choose_rest_option`, `proceed` |
| `shop` | Shop (auto-opens inventory) | `shop_purchase`, `proceed` |
| `fake_merchant` | Fake Merchant event (relic-only shop) | `shop_purchase`, `proceed` |
| `treasure` | Treasure room (auto-opens chest) | `claim_treasure_relic`, `proceed` |
| `card_select` | Deck card selection overlay (transform, upgrade, remove, choose-a-card) | `select_card`, `confirm_selection`, `cancel_selection` |
| `bundle_select` | Card bundle choice overlay | `select_bundle`, `confirm_bundle_selection`, `cancel_bundle_selection` |
| `relic_select` | Relic choice overlay (boss relics) | `select_relic`, `skip_relic_selection` |
| `crystal_sphere` | Crystal Sphere minigame | `crystal_sphere_set_tool`, `crystal_sphere_click_cell`, `crystal_sphere_proceed` |
| `game_over` | Run ended | `menu_select` with `main_menu` |
| `overlay` | Unhandled overlay (catch-all, prevents soft-lock) | None (manual interaction needed) |

**Note:** `use_potion` and `discard_potion` work during any state where potions are accessible (combat, map, events, etc.).

## POST — Actions

All POST requests use JSON body with `"action"` field. All responses include `{ "status": "ok" | "error", "message": "..." }`.

### Menu / Game Over

| Action | Parameters | When to Use |
|---|---|---|
| `menu_select` | `option`: string, `seed`?: string | Choose an advertised menu option. Options are case-insensitive. Submenus include `back` where visible, including `profile_select` options `profile_1`, `profile_2`, `profile_3`, and `back`. `game_over` supports `main_menu` only; `continue` returns an error. Supplying `seed` in unsupported contexts such as standard singleplayer character select returns an error and does not start a run. |

### Profiles

`GET /api/v1/profile` returns persistent progress for the active profile, including character stats, discoveries, achievements, epochs, and global run totals.

`GET /api/v1/profiles` returns the three profile slots:

```json
{
  "current_profile_id": 1,
  "profiles": [
    { "id": 1, "is_current": true, "has_data": true },
    { "id": 2, "is_current": false, "has_data": false },
    { "id": 3, "is_current": false, "has_data": true }
  ]
}
```

`POST /api/v1/profiles` supports:

| Action | Parameters | When to Use |
|---|---|---|
| `switch` | `profile_id`: 1-3 | Switch through the game profile UI. Empty slots can be used for fresh-profile testing. Cannot be used during a run. |
| `delete` | `profile_id`: 1-3 | Delete an inactive profile slot. The active profile is rejected. |

### Combat

| Action | Parameters | When to Use |
|---|---|---|
| `play_card` | `card_index`: int, `target`?: string | Play a card from hand. `target` is an `entity_id` (e.g. `"JAW_WORM_0"`), required for single-target cards. |
| `use_potion` | `slot`: int, `target`?: string | Use a potion. `target` required for enemy-targeting potions. Works outside combat for non-combat-only potions. |
| `discard_potion` | `slot`: int | Discard a potion to free up the slot. Use when slots are full and you need room for incoming potions. |
| `end_turn` | _(none)_ | End the player's turn. |

### In-Combat Hand Selection (`hand_select`)

| Action | Parameters | When to Use |
|---|---|---|
| `combat_select_card` | `card_index`: int | Select/deselect a card during "choose a card to exhaust/discard" prompts. |
| `combat_confirm_selection` | _(none)_ | Confirm the hand card selection. |

### Rewards (`rewards`)

| Action | Parameters | When to Use |
|---|---|---|
| `claim_reward` | `index`: int | Claim a reward. Card rewards open the `card_reward` screen. |
| `proceed` | _(none)_ | Leave the rewards screen. |

### Card Reward (`card_reward`)

| Action | Parameters | When to Use |
|---|---|---|
| `select_card_reward` | `card_index`: int | Pick a card to add to deck. |
| `skip_card_reward` | _(none)_ | Skip the card reward (if allowed). |

### Map (`map`)

| Action | Parameters | When to Use |
|---|---|---|
| `choose_map_node` | `index`: int | Travel to a node from `next_options`. |

### Event (`event`)

| Action | Parameters | When to Use |
|---|---|---|
| `choose_event_option` | `index`: int | Choose an event option by index from state. Locked options return an error. Also used for "Proceed" options. |
| `advance_dialogue` | _(none)_ | Click through Ancient dialogue until `in_dialogue` is false. |

### Rest Site (`rest_site`)

| Action | Parameters | When to Use |
|---|---|---|
| `choose_rest_option` | `index`: int | Choose rest, smith, or other option. |
| `proceed` | _(none)_ | Leave the rest site. |

### Shop (`shop`)

| Action | Parameters | When to Use |
|---|---|---|
| `shop_purchase` | `index`: int | Buy an item by its index. Must be stocked and affordable. |
| `proceed` | _(none)_ | Leave the shop. |

### Treasure (`treasure`)

| Action | Parameters | When to Use |
|---|---|---|
| `claim_treasure_relic` | `index`: int | Claim a relic from the opened chest. |
| `proceed` | _(none)_ | Leave the treasure room. |

### Card Selection Overlay (`card_select`)

| Action | Parameters | When to Use |
|---|---|---|
| `select_card` | `index`: int | Grid screens: toggle card selection. Choose-a-card: pick immediately. |
| `confirm_selection` | _(none)_ | Confirm (for grid screens with preview). Not needed for choose-a-card. |
| `cancel_selection` | _(none)_ | Cancel preview, skip (choose-a-card), or close screen. |

### Bundle Selection Overlay (`bundle_select`)

| Action | Parameters | When to Use |
|---|---|---|
| `select_bundle` | `index`: int | Open a bundle preview. |
| `confirm_bundle_selection` | _(none)_ | Confirm the previewed bundle. |
| `cancel_bundle_selection` | _(none)_ | Cancel the bundle preview. |

### Relic Selection Overlay (`relic_select`)

| Action | Parameters | When to Use |
|---|---|---|
| `select_relic` | `index`: int | Pick a relic (immediate). |
| `skip_relic_selection` | _(none)_ | Skip the relic choice. |

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
| `end_turn` | _(none)_ | Vote to end turn. Turn ends when all players vote. |
| `undo_end_turn` | _(none)_ | Retract end-turn vote (before all players committed). |
