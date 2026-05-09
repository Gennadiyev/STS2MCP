# Full API Reference

HTTP API served by the STS2_MCP mod on `localhost:15526`. No authentication. Local use only.

**Endpoints:**
- `GET  /api/v1/singleplayer` — read current game state
- `POST /api/v1/singleplayer` — perform a game action
- `GET  /api/v1/multiplayer` — read multiplayer game state
- `POST /api/v1/multiplayer` — perform a multiplayer action
- `GET  /api/v1/settings` — read settings and preferences
- `GET  /api/v1/profile` — read current profile progress
- `GET  /api/v1/compendium` — read Compendium-shaped profile progress
- `GET  /api/v1/bestiary` — read monster and encounter metadata
- `GET  /api/v1/glossary/cards` — read active-run card pool metadata
- `GET  /api/v1/glossary/relics` — read active-run relic pool metadata
- `GET  /api/v1/glossary/potions` — read active-run potion pool metadata
- `GET  /api/v1/glossary/keywords` — read active-run keyword metadata
- `GET  /api/v1/profiles` — list profile slots
- `POST /api/v1/profiles` — switch or delete profile slots

HTTP error responses include `status: "error"` and `error`. Some route-specific errors also include `error_code`.

The singleplayer and multiplayer endpoints are mutually exclusive: calling singleplayer during a multiplayer run (or vice versa) returns HTTP 409. The mismatch response includes `error_code`: `multiplayer_run_active` when singleplayer is called during a multiplayer run, or `not_multiplayer_run` when multiplayer is called outside one.

---

## GET — Read Game State

### Query Parameters

| Parameter | Values             | Default | Description     |
|-----------|--------------------|---------|-----------------|
| `format`  | `json`, `markdown` | `json`  | Response format |

### Common Top-Level Fields

Every response (except `menu`) includes these top-level fields alongside the state-specific data:

```jsonc
{
  "state_type": "...",      // Screen identifier (see sections below)
  "run": {
    "act": 1,               // Current act (1-indexed)
    "floor": 3,             // Total floors visited
    "ascension": 0          // Ascension level
  },
  "player": { ... },        // Full player state (see Player Object below)
  // ... state-specific fields
}
```

### Player Object

Always present at the top level (except `menu`). Contains everything about the local player.

```jsonc
{
  "character": "The Ironclad",
  "hp": 72,
  "max_hp": 80,
  "block": 0,
  "gold": 99,

  // --- Combat-only fields (present only when CombatManager.IsInProgress) ---
  "energy": 3,
  "max_energy": 3,
  "stars": 2,               // Regent only; omitted if 0 and character doesn't always show stars
  "hand": [ /* Card Objects */ ],
  "draw_pile_count": 15,
  "discard_pile_count": 3,
  "exhaust_pile_count": 1,
  "draw_pile": [ /* Pile Card Objects (sorted by rarity, matching in-game display) */ ],
  "discard_pile": [ /* Pile Card Objects */ ],
  "exhaust_pile": [ /* Pile Card Objects */ ],
  "orbs": [ /* Orb Objects */ ],   // Defect only; omitted if orb capacity is 0
  "orb_slots": 3,
  "orb_empty_slots": 1,
  "pets": [                        // Necrobinder only; omitted if no pets
    {
      "id": "OSTY",
      "name": "Otsy",
      "alive": true,
      "hp": 12,
      "max_hp": 12,
      "block": 0,
      "status": [ /* Power Objects */ ]
    }
  ],

  // --- Always present ---
  "status": [ /* Power Objects */ ],
  "deck_count": 10,
  "deck": [ /* Pile Card Objects */ ],
  "relics": [
    {
      "id": "BURNING_BLOOD",
      "name": "Burning Blood",
      "description": "At the end of combat, heal 6 HP.",
      "rarity": "Starter",
      "counter": null,       // Number if relic shows a counter, null otherwise
      "keywords": [ /* Keyword Objects */ ]
    }
  ],
  "potions": [
    {
      "id": "SWIFT_POTION",
      "name": "Swift Potion",
      "description": "Draw 3 cards.",
      "rarity": "Common",
      "slot": 0,             // Potion slot index (use this for use_potion action)
      "can_use_in_combat": true,
      "can_use": true,       // true when use_potion can run now, assuming any required target is supplied
      "use_blocked_reason": null, // e.g. "CombatOnly", "Automatic", "AlreadyQueued", "PlayerDead", "CustomUsabilityCheckFailed", "NotInPlayPhase", "PlayerActionsDisabled", "NoValidTargets"
      "can_discard": true,
      "discard_blocked_reason": null, // e.g. "AlreadyQueued"
      "requires_target": false,
      "valid_targets": [],   // For AnyEnemy potions: [{ "entity_id": "JAW_WORM_0", "combat_id": 12, "name": "Jaw Worm" }]
      "target_type": "None", // None, Self, AnyEnemy, AnyAlly, AnyPlayer, etc.
      "usage": "Manual",
      "keywords": [ /* Keyword Objects */ ]
    }
  ],
  "max_potion_slots": 3      // Belt capacity. Default 3, grows with belt-expanding relics (e.g. Potion Belt: +2). Use to detect a full belt: len(potions) >= max_potion_slots.
}
```

### Card Object (in hand)

```jsonc
{
  "index": 0,
  "id": "STRIKE_R",
  "name": "Strike",
  "type": "Attack",          // Attack, Skill, Power, Status, Curse
  "cost": "1",               // Energy cost as string ("X" for X-cost)
  "star_cost": null,         // Regent star cost as string, null if N/A
  "description": "Deal 6 damage.",
  "target_type": "AnyEnemy", // None, Self, AnyEnemy, AllEnemies, etc.
  "can_play": true,
  "unplayable_reason": null, // e.g. "NotEnoughEnergy", "NotInPlayPhase", "PlayerActionsDisabled", null if playable
  "is_upgraded": false,
  "is_upgradable": true,
  "current_upgrade_level": 0,
  "max_upgrade_level": 1,
  "upgrade_preview_type": "None",
  "upgrade_preview_cost": "1",
  "upgrade_preview_star_cost": null,
  "upgrade_preview_description": "Deal 9 damage.",
  "keywords": [ /* Keyword Objects */ ]
}
```

### Pile Card Object (draw/discard/exhaust piles)

```jsonc
{
  "index": 0,
  "id": "STRIKE_R",
  "name": "Strike",
  "type": "Attack",
  "cost": "1",               // Energy cost as string ("X" for X-cost)
  "star_cost": null,          // Regent star cost as string, null if N/A
  "description": "Deal 6 damage.",
  "rarity": "Basic",
  "is_upgraded": false,
  "is_upgradable": true,
  "current_upgrade_level": 0,
  "max_upgrade_level": 1,
  "upgrade_preview_type": "None",
  "upgrade_preview_cost": "1",
  "upgrade_preview_star_cost": null,
  "upgrade_preview_description": "Deal 9 damage.",
  "keywords": [ /* Keyword Objects */ ]
}
```

### Orb Object

```jsonc
{
  "id": "LIGHTNING",
  "name": "Lightning",
  "description": "Passive: Deal 3 damage to a random enemy. Evoke: Deal 8 damage.",
  "passive_val": 3,
  "evoke_val": 8,
  "keywords": [ /* Keyword Objects */ ]
}
```

### Power Object (status effects)

Used for both player `status` and enemy `status`.

```jsonc
{
  "id": "STRENGTH",
  "name": "Strength",
  "amount": 3,
  "type": "Buff",            // "Buff" or "Debuff"
  "description": "Increases attack damage by 3.",
  "keywords": [ /* Keyword Objects */ ]
}
```

### Keyword Object

```jsonc
{
  "name": "Vulnerable",
  "description": "Takes 50% more damage from attacks."
}
```

---

## State Types

### `menu`

No run in progress.

```jsonc
{
  "state_type": "menu",
  "message": "No run in progress. Player is in the main menu.",
  "menu_screen": "main",
  "options": ["continue", "abandon_run", "singleplayer", "multiplayer", "compendium", "timeline", "settings", "quit"]
}
```

Use `menu_select` with one of the advertised options. Options are accepted case-insensitively.
If a visible option is intentionally withheld from automation, the state may also include `blocked_options` with a reason. For example, Timeline can be blocked while obtained epochs still need manual reveal because opening that game state through automation can trigger invalid unlock-state errors.
`abandon_run` opens a confirmation popup; the next state exposes the popup's `confirm` / `cancel` options.

Menu sub-screens expose their own options:

- `singleplayer`: `standard`, `daily`, `custom`, `back`
- `multiplayer`: `host`, `join`, `load`, `abandon`, `back`
- `multiplayer_host`: `standard`, `daily`, `custom`, `back`
- `multiplayer_join`: `refresh`, `back`, `join_<index>`, `join_<player_id>`
- `multiplayer_load_lobby`: `confirm` / `embark`, `unready`, `back`
- `profile_select`: `profile_1`, `profile_2`, `profile_3`, `back`
- `character_select`: character IDs/names, `back`, `confirm` / `embark`, `unready` (MP, after readying)
- `tutorial_prompt`: `no`, `yes`
- `popup`: advertised popup button labels, normalized to lowercase words such as `ignore` or `back`
- `timeline`: `advance`, `back`

The `multiplayer` submenu is gated on whether a multiplayer save exists: with no save, `host` is shown and `load` / `abandon` are hidden; with a save, those are shown and `host` is hidden. `abandon` opens a vertical popup — the next state turn surfaces the popup's `confirm` / `cancel` options, not a transition.

#### `multiplayer_join` — Friend list / FastMP

```jsonc
{
  "state_type": "menu",
  "menu_screen": "multiplayer_join",
  "message": "Pick a friend to join, or 'refresh' to update the list.",
  "fast_mp": false,             // true when --fastmp / Steam not initialized; auto-joins 127.0.0.1:33771
  "loading": false,             // true while refreshing or while a join handshake is in flight
  "no_friends": false,          // true when refresh returned an empty list
  "friends": [
    { "index": 0, "name": "Bob", "player_id": "76561198000000000", "enabled": true }
  ],
  "options": [
    { "name": "join_0",  "enabled": true },
    { "name": "refresh", "enabled": true },
    { "name": "back",    "enabled": true }
  ]
}
```

`menu_select` accepts either `join_<index>` (e.g. `join_0`) or `join_<player_id>` (e.g. `join_76561198000000000`) — both click the same button. With `fast_mp: true`, the screen auto-joins localhost on open; you typically won't need to issue a join action in that mode.

#### `multiplayer_load_lobby` — Resume saved MP run

Reached via the `multiplayer` submenu's `load` option (host) or by joining a host that already loaded a save (client). Mirrors the standard MP character-select ready/unready flow but on a fixed (saved) party.

```jsonc
{
  "state_type": "menu",
  "menu_screen": "multiplayer_load_lobby",
  "message": "Multiplayer load lobby. Confirm to ready up; ...",
  "lobby": {
    "type": "host",                // or "client"
    "game_mode": "standard",       // or "daily" / "custom"
    "ascension": 0,
    "act": 2,
    "floor": 13,
    "character_id": "REGENT",      // local player's saved character
    "current_hp": 64,
    "max_hp": 80,
    "gold": 240,
    "expected_player_count": 3,
    "connected_player_count": 2,
    "players": [
      {
        "id": "76561198000000000",
        "is_local": true,
        "character_id": "REGENT",
        "is_connected": true,
        "is_ready": false,
        "platform_name": "Alice"
      }
    ]
  },
  "options": [
    { "name": "confirm", "enabled": true },
    { "name": "embark",  "enabled": true },   // alias of confirm
    { "name": "back",    "enabled": true },
    { "name": "unready", "enabled": false }   // becomes enabled after confirm/embark
  ]
}
```

#### `character_select` — extended for MP

The same screen drives SP, MP host, and MP client. In MP, an additional `lobby` block appears, and the `unready` option becomes available after the local player has hit `confirm`/`embark`:

```jsonc
{
  "state_type": "menu",
  "menu_screen": "character_select",
  "message": "Select a character.",
  "characters": [ /* same shape as SP */ ],
  "lobby": {                       // present only in MP
    "type": "host",                // "host" | "client" | "singleplayer"
    "game_mode": "standard",       // "standard" | "daily" | "custom"
    "max_players": 4,
    "ascension": 0,
    "max_ascension": 5,            // min across all lobby members' MaxMultiplayerAscension
    "all_ready": false,
    "is_about_to_begin": false,    // mirrors StartRunLobby.IsAboutToBeginGame()
    "is_local_ready": false,
    "local_player_id": "76561198000000000",
    "player_count": 2,
    "seed": "...",                 // optional, present only for daily/custom
    "players": [
      {
        "id": "76561198000000000",
        "slot_id": 0,
        "is_local": true,
        "is_host": true,           // only positively true for self when hosting; otherwise false
        "character": "The Regent",
        "character_id": "REGENT",
        "is_ready": false,
        "platform_name": "Alice"
      }
    ]
  },
  "options": [
    { "name": "REGENT",  "enabled": true },
    { "name": "IRONCLAD","enabled": true },
    /* ... other characters and lockable RANDOM ... */
    { "name": "confirm", "enabled": true },
    { "name": "embark",  "enabled": true },
    { "name": "back",    "enabled": true },
    { "name": "unready", "enabled": false }
  ]
}
```

In SP the `lobby` field is omitted, and `unready` does not appear (the unready button is only enabled after MP ready).

### `unknown`

Run state or room type not recognized.

```jsonc
{
  "state_type": "unknown",
  "room_type": "SomeUnknownRoom"  // May be present
}
```

### `monster` / `elite` / `boss` — Combat

```jsonc
{
  "state_type": "monster",  // or "elite", "boss"
  "battle": {
    "round": 1,
    "turn": "player",       // "player" or "enemy"
    "is_play_phase": true,
    "player_actions_disabled": false,
    "can_end_turn": true,
    "end_turn_blocked_reason": null, // e.g. "NotInPlayPhase", "PlayerActionsDisabled", "CardInPlay", "HandSelectionMode"
    "enemies": [
      {
        "entity_id": "JAW_WORM_0",    // Synthesized ID for targeting
        "combat_id": 42,               // Internal combat ID
        "name": "Jaw Worm",
        "hp": 44,
        "max_hp": 44,
        "block": 0,
        "status": [ /* Power Objects */ ],
        "intents": [
          {
            "type": "Attack",          // Attack, Defend, Buff, Debuff, Sleep, etc.
            "label": "11",             // Damage number or short label
            "title": "Attack",         // Hover tip title
            "description": "Deals 11 damage."  // Hover tip description
          }
        ],
        "is_visible": true,
        "can_select": true
      }
    ]
  },
  "run": { ... },
  "player": { ... }  // Includes hand, energy, piles, orbs during combat
}
```

### `hand_select` — In-Combat Card Selection

Appears when a card effect prompts "Select a card to exhaust/discard/upgrade". **Not** an overlay — happens within the combat hand.

```jsonc
{
  "state_type": "hand_select",
  "hand_select": {
    "mode": "simple_select",     // "simple_select" or "upgrade_select"
    "prompt": "Select a card to Exhaust.",
    "cards": [
      {
        "index": 0,
        "id": "STRIKE_R",
        "name": "Strike",
        "type": "Attack",
        "cost": "1",
        "star_cost": null,       // Regent star cost as string, null if N/A
        "description": "Deal 6 damage.",
        "is_upgraded": false,
        "keywords": [ /* Keyword Objects */ ]
      }
    ],
    "selected_cards": [          // Only present if cards have been selected
      {
        "index": 0,
        "id": "DEFEND_R",
        "name": "Defend",
        "type": "Skill",
        "cost": "1",
        "star_cost": null,
        "description": "Gain 5 Block.",
        "is_upgraded": false,
        "keywords": [ /* Keyword Objects */ ]
      }
    ],
    "can_confirm": false
  },
  "battle": { ... },  // Full battle state included for context
  "run": { ... },
  "player": { ... }
}
```

### `rewards` — Rewards Screen

Appears after combat ends or when triggered by events (e.g. TheFutureOfPotions, Draft).

```jsonc
{
  "state_type": "rewards",
  "rewards": {
    "items": [
      {
        "index": 0,
        "type": "gold",              // gold, potion, relic, card, special_card, card_removal
        "description": "Obtain 25 gold.",
        "is_visible": true,
        "can_claim": true,
        "gold_amount": 25            // Only for gold rewards
      },
      {
        "index": 1,
        "type": "potion",
        "description": "Obtain a potion.",
        "potion_id": "SWIFT_POTION", // Only for potion rewards
        "potion_name": "Swift Potion",
        "potion_description": "Draw 3 cards.",
        "potion_rarity": "Common",
        "potion_target_type": "None",
        "potion_usage": "Manual",
        "keywords": [ /* Keyword Objects */ ]
      },
      {
        "index": 2,
        "type": "relic",
        "description": "Obtain a relic.",
        "relic_id": "VAJRA",
        "relic_name": "Vajra",
        "relic_description": "At the start of each combat, gain 1 Strength.",
        "relic_rarity": "Common",
        "keywords": [ /* Keyword Objects */ ]
      },
      {
        "index": 3,
        "type": "card",
        "description": "Add a card to your deck."
        // Card rewards open the card_reward screen when claimed
      }
    ],
    "can_proceed": true
  },
  "run": { ... },
  "player": { ... }
}
```

### `card_reward` — Card Reward Selection

Pick one card to add to your deck. Appears after claiming a card reward, or directly during events (e.g. Draft modifier at Neow).

```jsonc
{
  "state_type": "card_reward",
  "card_reward": {
    "cards": [
      {
        "index": 0,
        "id": "UPPERCUT",
        "name": "Uppercut",
        "type": "Attack",
        "cost": "2",
        "star_cost": null,
        "description": "Deal 13 damage. Apply 1 Weak. Apply 1 Vulnerable.",
        "rarity": "Uncommon",
        "is_upgraded": false,
        "is_selected": false,
        "is_visible": true,
        "can_select": true,
        "keywords": [ /* Keyword Objects */ ]
      }
    ],
    "can_skip": true       // true only when the skip/alternate button is enabled and visible
  },
  "run": { ... },
  "player": { ... }
}
```

### `map` — Map Navigation

```jsonc
{
  "state_type": "map",
  "map": {
    "current_position": {
      "col": 3, "row": 2, "type": "Monster"
    },
    "visited": [
      { "col": 3, "row": 0, "type": "Start" },
      { "col": 3, "row": 1, "type": "Monster" },
      { "col": 3, "row": 2, "type": "Monster" }
    ],
    "next_options": [
      {
        "index": 0,
        "col": 2, "row": 3,
        "type": "RestSite",
        "is_visible": true,
        "can_travel": true,
        "leads_to": [            // 1-level lookahead (children)
          { "col": 1, "row": 4, "type": "Elite" },
          { "col": 3, "row": 4, "type": "Shop" }
        ]
      }
    ],
    "nodes": [                   // Full map DAG
      {
        "col": 3, "row": 0,
        "type": "Start",
        "children": [ [2, 1], [3, 1], [4, 1] ]  // [col, row] pairs
      }
    ],
    "boss": { "col": 3, "row": 15 }
  },
  "run": { ... },
  "player": { ... }
}
```

### `event` — Event / Ancient Encounter

```jsonc
{
  "state_type": "event",
  "event": {
    "event_id": "NEOW",
    "event_name": "Neow",
    "is_ancient": true,
    "in_dialogue": false,        // true = click to advance dialogue first
    "body": "Event description text...",
    "options": [
      {
        "index": 0,
        "title": "Draft",
        "description": "Choose 10 card rewards to replace your starting deck.",
        "is_locked": false,
        "is_enabled": true,
        "is_visible": true,
        "can_choose": true,
        "is_proceed": false,
        "was_chosen": false,
        "relic_id": "RELIC_ID",             // Only if option has a relic
        "relic_name": "Relic Name",         // Only if option has a relic
        "relic_description": "Relic desc.",  // Only if option has a relic
        "relic_rarity": "Common",           // Only if option has a relic
        "relic_keywords": [ /* Keyword Objects */ ],
        "keywords": [ /* Keyword Objects */ ]
      }
    ]
  },
  "run": { ... },
  "player": { ... }
}
```

### `rest_site` — Rest Site

```jsonc
{
  "state_type": "rest_site",
  "rest_site": {
    "options": [
      {
        "index": 0,
        "id": "rest",
        "name": "Rest",
        "description": "Heal 30% of max HP.",
        "is_enabled": true,
        "is_visible": true,
        "can_choose": true
      }
    ],
    "can_proceed": false
  },
  "run": { ... },
  "player": { ... }
}
```

### `shop` — Shop

Shop inventory is auto-opened when state is queried. `can_proceed` mirrors the `proceed` action: it is true if the proceed button is already enabled, or if the inventory can be closed and then proceeded through in one action.

```jsonc
{
  "state_type": "shop",
  "shop": {
    "items": [
      // Card entry
      {
        "index": 0,
        "category": "card",
        "price": 75,               // Gold price in the shop
        "is_stocked": true,
        "can_afford": true,
        "can_purchase": true,
        "on_sale": false,
        "card_id": "OFFERING",
        "card_name": "Offering",
        "card_type": "Skill",
        "card_cost": "1",          // Energy cost as string ("X" for X-cost)
        "card_star_cost": null,    // Regent star cost as string, null if N/A
        "card_rarity": "Rare",
        "card_description": "Lose 6 HP. Gain 2 Energy. Draw 3 cards.",
        "card_is_upgraded": false,
        "card_is_upgradable": true,
        "card_current_upgrade_level": 0,
        "card_max_upgrade_level": 1,
        "card_upgrade_preview_type": "None",
        "card_upgrade_preview_cost": "0",
        "card_upgrade_preview_star_cost": null,
        "card_upgrade_preview_description": "Lose 6 HP. Gain 2 Energy. Draw 4 cards.",
        "keywords": [ /* Keyword Objects */ ]
      },
      // Relic entry
      {
        "index": 5,
        "category": "relic",
        "price": 150,
        "is_stocked": true,
        "can_afford": false,
        "can_purchase": false,
        "relic_id": "VAJRA",
        "relic_name": "Vajra",
        "relic_description": "At the start of each combat, gain 1 Strength.",
        "relic_rarity": "Common",
        "keywords": [ /* Keyword Objects */ ]
      },
      // Potion entry
      {
        "index": 8,
        "category": "potion",
        "price": 50,
        "is_stocked": true,
        "can_afford": true,
        "can_purchase": true,
        "potion_id": "FIRE_POTION",
        "potion_name": "Fire Potion",
        "potion_description": "Deal 20 damage to target enemy.",
        "potion_rarity": "Common",
        "potion_target_type": "Enemy",
        "potion_usage": "Manual",
        "keywords": [ /* Keyword Objects */ ]
      },
      // Card removal entry
      {
        "index": 10,
        "category": "card_removal",
        "price": 75,
        "is_stocked": true,
        "can_afford": true,
        "can_purchase": true
      }
    ],
    "inventory_open": true,
    "can_close_inventory": true,
    "can_proceed": true,
    "error": "..."             // Only present if inventory isn't ready; retry in a moment
  },
  "run": { ... },
  "player": { ... }
}
```

### `fake_merchant` — Fake Merchant Event

A relic-only shop disguised as an event. Uses `shop_purchase` and `proceed` actions (same as regular shop). If the player triggers a fight, the merchant disappears.

```jsonc
{
  "state_type": "fake_merchant",
  "fake_merchant": {
    "event_id": "FAKE_MERCHANT",
    "event_name": "Fake Merchant",
    "started_fight": false,         // true after triggering the foul potion fight
    "shop": {
      "items": [
        {
          "index": 0,
          "category": "relic",
          "cost": 150,
          "is_stocked": true,
          "can_afford": true,
          "can_purchase": true,
          "relic_id": "VAJRA",
          "relic_name": "Vajra",
          "relic_description": "At the start of each combat, gain 1 Strength.",
          "relic_rarity": "Common",
          "keywords": [ /* Keyword Objects */ ]
        }
      ],
      "inventory_open": true,
      "can_close_inventory": true,
      "can_proceed": true
    },
    // After fight:
    // "started_fight": true,
    // "shop": { "items": [], "can_proceed": true },
    // "message": "The fake merchant has been defeated. Proceed to map."
  },
  "run": { ... },
  "player": { ... }
}
```

**Note:** The fake merchant only sells relics — no cards, potions, or card removal. The `shop_purchase` action works the same as for a regular shop.

### `treasure` — Treasure Room

Chest is auto-opened on first state query.

```jsonc
{
  "state_type": "treasure",
  "treasure": {
    "message": "Opening chest...",  // Transitional state; query again
    // OR, once opened:
    "relics": [
      {
        "index": 0,
        "id": "LANTERN",
        "name": "Lantern",
        "description": "Gain 1 Energy on the first turn of each combat.",
        "rarity": "Uncommon",
        "is_visible": true,
        "can_claim": true,
        "keywords": [ /* Keyword Objects */ ]
      }
    ],
    "can_proceed": true
  },
  "run": { ... },
  "player": { ... }
}
```

### `card_select` — Card Selection Overlay

Covers deck transforms, upgrades, removals, and choose-a-card effects. Appears on top of any room.

```jsonc
{
  "state_type": "card_select",
  "card_select": {
    "screen_type": "transform",  // transform, upgrade, select, simple_select, choose
    "prompt": "Choose 2 cards to Transform.",
    "cards": [
      {
        "index": 0,
        "id": "STRIKE_R",
        "name": "Strike",
        "type": "Attack",
        "cost": "1",
        "star_cost": null,       // Regent star cost as string, null if N/A
        "description": "Deal 6 damage.",
        "rarity": "Common",
        "is_upgraded": false,
        "is_selected": false,
        "is_visible": true,
        "can_select": true,
        "keywords": [ /* Keyword Objects */ ]
      }
    ],
    "selected_cards": [],
    "selected_count": 0,
    "min_select": 1,
    "max_select": 1,
    "preview_showing": false,    // true when selection is complete and preview is displayed
    "preview_cards": [],         // cards shown by an upgrade/transform/select preview
    "can_confirm": false,        // true when confirm button is available
    "can_cancel": true           // true when close/cancel button is available

    // For "choose" type: picking is immediate (no confirm needed).
    // can_skip indicates if a skip button exists.
  },
  "run": { ... },
  "player": { ... }
}
```

### `bundle_select` — Card Bundle Selection Overlay

Choose between bundles of cards (e.g. from Scroll Boxes relic).

```jsonc
{
  "state_type": "bundle_select",
  "bundle_select": {
    "screen_type": "bundle",
    "prompt": "Choose a bundle.",
    "bundles": [
      {
        "index": 0,
        "card_count": 3,
        "cards": [
          {
            "index": 0,
            "id": "UPPERCUT",
            "name": "Uppercut",
            "type": "Attack",
            "cost": "2",
            "star_cost": null,   // Regent star cost as string, null if N/A
            "description": "Deal 13 damage. Apply 1 Weak. Apply 1 Vulnerable.",
            "rarity": "Uncommon",
            "is_upgraded": false,
            "keywords": [ /* Keyword Objects */ ]
          }
        ]
      }
    ],
    "preview_showing": false,
    "preview_cards": [ /* Card Objects — shown when a bundle is selected */ ],
    "can_cancel": false,
    "can_confirm": false
  },
  "run": { ... },
  "player": { ... }
}
```

### `relic_select` — Relic Choice Overlay

Boss relic selection. Pick is immediate.

```jsonc
{
  "state_type": "relic_select",
  "relic_select": {
    "prompt": "Choose a relic.",
    "relics": [
      {
        "index": 0,
        "id": "BLACK_STAR",
        "name": "Black Star",
        "description": "Elites drop an additional relic.",
        "rarity": "Rare",
        "is_visible": true,
        "can_select": true,
        "keywords": [ /* Keyword Objects */ ]
      }
    ],
    "can_skip": true
  },
  "run": { ... },
  "player": { ... }
}
```

### `crystal_sphere` — Crystal Sphere Minigame

```jsonc
{
  "state_type": "crystal_sphere",
  "crystal_sphere": {
    "instructions_title": "Crystal Sphere",
    "instructions_description": "Use divination tools to reveal cells...",
    "grid_width": 9,
    "grid_height": 11,
    "cells": [
      {
        "x": 0, "y": 0,
        "is_hidden": true,       // true = unrevealed
        "is_clickable": true,    // true = can be clicked to reveal
        "is_highlighted": false,
        "is_hovered": false,
        "item_type": "CrystalSphereGold",  // Only on revealed cells
        "is_good": true                     // Only on revealed cells
      }
    ],
    "clickable_cells": [
      { "x": 4, "y": 7 }       // Convenience list of clickable cells
    ],
    "revealed_items": [
      {
        "item_type": "CrystalSphereGold",
        "x": 2, "y": 3,
        "width": 1, "height": 1,
        "is_good": true
      }
    ],
    "tool": "big",               // "big", "small", or "none"
    "can_use_big_tool": true,
    "can_use_small_tool": true,
    "divinations_left_text": "3 divinations remaining",
    "can_proceed": false
  },
  "run": { ... },
  "player": { ... }
}
```

### `game_over`

Run has ended.

```jsonc
{
  "state_type": "game_over",
  "game_over": {
    "message": "Run ended.",
    "options": ["main_menu"]
  },
  "run": { ... },
  "player": { ... }
}
```

Use `menu_select` with `main_menu` to return to the main menu. `continue` is not advertised because it is not an actionable game-over option.

### `overlay` — Unhandled Overlay (catch-all)

Prevents soft-locks when an unrecognized overlay is active.

```jsonc
{
  "state_type": "overlay",
  "overlay": {
    "screen_type": "NSomeUnknownScreen",
    "message": "An overlay (NSomeUnknownScreen) is active. It may require manual interaction in-game."
  },
  "run": { ... },
  "player": { ... }
}
```

---

## Profiles

Profile endpoints are independent of the singleplayer and multiplayer run endpoints.

### `GET /api/v1/settings`

Returns current settings and preferences, including display, audio, gameplay, language, skip-intro, and mod-loading status.

### `GET /api/v1/profile`

Returns the active profile's persistent progress summary, including character stats, card stats, encounter stats, discovered content, achievements, epochs, and global totals.

### `GET /api/v1/compendium`

Returns the active profile's progress grouped by the in-game Compendium cards:

- `current_run`: present while a run is active. Includes a derived `run_id` in `{save_scope}:profile{profile_id}:{start_time}` format, plus `start_time`, `seed`, `save_time`, `run_time`, and run metadata read from `current_run.save`.
- `card_library`: discovered card IDs and card pick/skip/win/loss stats. Detailed card metadata lives at `/api/v1/glossary/cards`, which currently requires a run context.
- `relic_collection`: discovered relic IDs. Detailed relic metadata lives at `/api/v1/glossary/relics`, which currently requires a run context.
- `potion_lab`: discovered potion IDs. Detailed potion metadata lives at `/api/v1/glossary/potions`, which currently requires a run context.
- `bestiary`: profile encounter/enemy fight stats plus a pointer to `/api/v1/bestiary` for reflected model metadata. The in-game Bestiary card is currently marked future/locked.
- `character_stats`: per-character and global profile totals.
- `run_history`: summaries of the active profile's saved `saves/history/*.run` files. If more than 20 files exist, the response includes the 20 most recent entries and the local `history_path`.

```jsonc
{
  "profile_id": 1,
  "current_run": {
    "is_in_progress": true,
    "profile_id": 1,
    "save_scope": "modded",
    "run_id": "modded:profile1:1778295706",
    "start_time": 1778295706,
    "seed": "2450ZAR9EF",
    "run_time": 137
  },
  "sections": {
    "card_library": { "status": "exposed", "discovered_ids": ["BASH"], "stats": [] },
    "relic_collection": { "status": "partially_exposed", "discovered_ids": ["BURNING_BLOOD"] },
    "potion_lab": { "status": "partially_exposed", "discovered_ids": [] },
    "bestiary": { "status": "locked_in_ui", "detail_endpoint": "/api/v1/bestiary" },
    "character_stats": { "status": "exposed", "characters": [], "global": {} },
    "run_history": {
      "status": "exposed",
      "entry_count": 10,
      "entries": [
        {
          "id": "1774869148",
          "run_id": "modded:profile1:1774869148",
          "players": [{ "id": 1, "character": "CHARACTER.IRONCLAD" }],
          "ascension": 0,
          "win": false,
          "run_time": 6541
        }
      ]
    }
  }
}
```

### `GET /api/v1/bestiary`

Returns reflected monster and encounter metadata. Profile-specific encounter and enemy fight stats are also summarized under `/api/v1/compendium`.

### `GET /api/v1/glossary/*`

The glossary endpoints expose active-run pool metadata. They require a run in progress and are scoped to the current run/character context plus shared run pools such as Colorless cards, shared relics, and shared potions, not profile-wide discovered content. Successful responses are structured objects with `status`, `kind`, `scope`, `count`, `current_run`, `players`, and `items`.

- `GET /api/v1/glossary/cards`: active-run card pool metadata, including upgrade availability plus upgraded-preview cost and description.
- `GET /api/v1/glossary/relics`: active-run relic pool metadata.
- `GET /api/v1/glossary/potions`: active-run potion pool metadata.
- `GET /api/v1/glossary/keywords`: keyword metadata collected from active-run cards, relics, and potions.

If no run is active, glossary endpoints return HTTP 409 with `error_code: "run_not_in_progress"`.

Example success shape:

```json
{
  "status": "ok",
  "kind": "cards",
  "scope": "active_run",
  "count": 87,
  "current_run": {
    "run_id": "modded:profile1:1778295706",
    "seed": "VQY2JBY38L"
  },
  "items": [
    {
      "id": "STRIKE_RED",
      "name": "Strike",
      "type": "Attack",
      "rarity": "Basic",
      "is_upgradable": true,
      "current_upgrade_level": 0,
      "max_upgrade_level": 1,
      "upgrade_preview_type": "None",
      "upgrade_preview_cost": "1",
      "upgrade_preview_star_cost": null,
      "upgrade_preview_description": "Deal 9 damage.",
      "keywords": []
    }
  ]
}
```

### `GET /api/v1/profiles`

Lists the three profile slots and identifies the active slot.

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

### `POST /api/v1/profiles`

Switch to a profile slot through the game's profile UI:

```json
{ "action": "switch", "profile_id": 2 }
```

Delete an inactive profile slot:

```json
{ "action": "delete", "profile_id": 2 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `action` | string | Yes | `switch` or `delete` |
| `profile_id` | int | Yes | Profile slot, from 1 to 3 |

Switching is rejected during a run. Deleting the active profile is rejected; switch away first if you need to remove a slot.

---

## POST — Perform Actions

All POST requests use a JSON body with an `"action"` field and action-specific parameters.

### Success Response

```jsonc
{ "status": "ok", "message": "Playing 'Strike' targeting Jaw Worm" }
```

### Error Response

```jsonc
{ "status": "error", "error": "Card requires a target. Provide 'target' with an entity_id." }
```

---

### `menu_select`

Select an option from the main menu, a menu submenu, profile select, character select, tutorial prompt, blocking popup, timeline screen, or game-over screen.

```json
{ "action": "menu_select", "option": "singleplayer" }
```

```json
{ "action": "menu_select", "option": "main_menu" }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `option` | string | Yes | One of the current state's advertised menu options. Matching is case-insensitive. |
| `seed` | string | No | Only supported in menu contexts that expose a real seeded flow. Standard singleplayer character select currently returns an error without starting a run when `seed` is supplied. |

`game_over` advertises only `main_menu`. `continue` is not actionable on that screen and returns an error.
If `timeline` is blocked by pending obtained epochs, `menu_select` returns an error with `manual_action_required: true` and `pending_epoch_ids` instead of opening Timeline.

---

### `play_card`

Play a card from hand during combat.

```json
{ "action": "play_card", "card_index": 0, "target": "JAW_WORM_0" }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `card_index` | int | Yes | 0-based index in hand |
| `target` | string | For `AnyEnemy` cards | `entity_id` of the target enemy |

**Errors:** Not in combat, not play phase, card unplayable, invalid index, missing target.

**Warning:** Playing a card removes it from hand — all higher indices shift left. Play from highest index first, or re-query state between plays.

### `use_potion`

Use a potion from the potion belt.

```json
{ "action": "use_potion", "slot": 0, "target": "JAW_WORM_0" }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `slot` | int | Yes | Potion slot index |
| `target` | string | For `AnyEnemy` potions | `entity_id` of the target enemy |

**Errors:** Empty slot, combat-only potion used outside combat, automatic potion, already queued, player dead, custom usability blocked, missing/invalid target, enemy-targeted potion outside combat.

### `discard_potion`

Discard a potion to free up the slot. Use when potion slots are full and you need room for incoming potions (rewards, Cauldron, etc.).

```json
{ "action": "discard_potion", "slot": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `slot` | int | Yes | Potion slot index |

**Errors:** Empty slot, out-of-range slot, potion already queued.

### `end_turn`

End the player's combat turn.

```json
{ "action": "end_turn" }
```

**Errors:** Not in combat, not play phase, actions disabled, card mid-play, hand in selection mode.

### `combat_select_card`

Select a card during in-combat hand selection (exhaust, discard, upgrade prompts).

```json
{ "action": "combat_select_card", "card_index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `card_index` | int | Yes | Index in the selectable hand cards |

### `combat_confirm_selection`

Confirm the in-combat hand card selection.

```json
{ "action": "combat_confirm_selection" }
```

**Errors:** No selection active, confirm button not enabled (select more cards first).

### `claim_reward`

Claim a reward from the rewards screen.

```json
{ "action": "claim_reward", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based reward index |

Gold, potion, and relic rewards are claimed immediately. Card rewards open the `card_reward` screen.

**Note:** Claiming a reward may change indices of remaining rewards.

### `select_card_reward`

Pick a card from the card reward selection screen.

```json
{ "action": "select_card_reward", "card_index": 1 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `card_index` | int | Yes | 0-based card index |

### `skip_card_reward`

Skip the card reward (if a skip/bowl option is available).

```json
{ "action": "skip_card_reward" }
```

### `proceed`

Leave the current screen and open the map. Works from: rewards, rest site, shop, fake merchant, and treasure room.

```json
{ "action": "proceed" }
```

**Note:** Does NOT work for events — use `choose_event_option` with the Proceed option's index instead.

### `choose_event_option`

Choose an event option.

```json
{ "action": "choose_event_option", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based index matching the option's `index` from state (locked options return an error) |

Works for both regular events and Ancients (after dialogue ends).

### `advance_dialogue`

Click through Ancient dialogue.

```json
{ "action": "advance_dialogue" }
```

Call repeatedly until `in_dialogue` becomes `false` and event options appear.

### `choose_rest_option`

Choose a rest site option (rest, smith, etc.).

```json
{ "action": "choose_rest_option", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based index matching the option's `index` from state (disabled options return an error) |

### `shop_purchase`

Purchase a shop item.

```json
{ "action": "shop_purchase", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based index in the flat items list |

**Errors:** Not in shop, item sold out, not enough gold, inventory not ready.

### `choose_map_node`

Travel to a map node.

```json
{ "action": "choose_map_node", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based index from `next_options` |

### `select_card`

Select a card in a card selection overlay.

```json
{ "action": "select_card", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based card index in the grid |

**Behavior varies by screen type:**
- Grid screens (transform, upgrade, select): toggles selection. When enough cards are selected, a preview may appear.
- Choose-a-card screens (potions, effects): picks the card immediately.

### `confirm_selection`

Confirm card selection (grid screens only).

```json
{ "action": "confirm_selection" }
```

Checks preview containers first, then main confirm button. Not needed for choose-a-card screens.

### `cancel_selection`

Cancel or close the card selection overlay.

```json
{ "action": "cancel_selection" }
```

**Behavior:**
- If a preview is showing: cancels back to the selection grid.
- For choose-a-card screens: clicks the skip button (if available).
- Otherwise: closes the selection screen (if cancellation is allowed).

### `select_bundle`

Open a bundle preview in the bundle selection screen.

```json
{ "action": "select_bundle", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based bundle index |

### `confirm_bundle_selection`

Confirm the currently previewed bundle.

```json
{ "action": "confirm_bundle_selection" }
```

### `cancel_bundle_selection`

Cancel the bundle preview.

```json
{ "action": "cancel_bundle_selection" }
```

### `select_relic`

Pick a relic from the relic choice screen. Selection is immediate.

```json
{ "action": "select_relic", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based relic index |

### `skip_relic_selection`

Skip the relic choice.

```json
{ "action": "skip_relic_selection" }
```

### `claim_treasure_relic`

Claim a relic from an opened treasure chest.

```json
{ "action": "claim_treasure_relic", "index": 0 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `index` | int | Yes | 0-based relic index |

### `crystal_sphere_set_tool`

Switch divination tool in the Crystal Sphere minigame.

```json
{ "action": "crystal_sphere_set_tool", "tool": "big" }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `tool` | string | Yes | `"big"` or `"small"` |

### `crystal_sphere_click_cell`

Reveal a cell in the Crystal Sphere.

```json
{ "action": "crystal_sphere_click_cell", "x": 4, "y": 7 }
```

| Parameter | Type | Required | Description |
|---|---|---|---|
| `x` | int | Yes | Cell x-coordinate |
| `y` | int | Yes | Cell y-coordinate |

### `crystal_sphere_proceed`

Finish the Crystal Sphere minigame.

```json
{ "action": "crystal_sphere_proceed" }
```

---

## Multiplayer Additions

### GET — Additional Top-Level Fields

```jsonc
{
  "game_mode": "multiplayer",
  "net_type": "SteamMultiplayer",
  "player_count": 2,
  "local_player_slot": 0,      // Index of local player in players array
  "players": [                  // Summary of all players
    {
      "character": "The Ironclad",
      "hp": 72, "max_hp": 80,
      "gold": 99,
      "is_alive": true,
      "is_local": true
    }
  ]
}
```

### Battle State Additions

```jsonc
{
  "battle": {
    "all_players_ready": false,
    "local_player_ready_to_end_turn": false,
    "can_end_turn": true,
    "end_turn_blocked_reason": null,
    "can_undo_end_turn": false,
    "undo_end_turn_blocked_reason": "NotReady"
    // Same shape as singleplayer (round, turn, is_play_phase, enemies).
    // Player state lives in top-level "player" (local) and "players" (all).
  },
  "players": [
    {
      "character": "The Ironclad",
      "hp": 72, "max_hp": 80,
      "gold": 99,
      "is_alive": true,
      "is_local": true,
      "is_ready_to_end_turn": false   // Only present during combat
    },
    {
      "character": "The Necrobinder",
      "hp": 60, "max_hp": 66,
      "gold": 80,
      "is_alive": true,
      "is_local": false,
      "is_ready_to_end_turn": false,
      "pets": [                       // Omitted for local player (pets are under top-level "player")
        {
          "id": "OSTY",
          "name": "Otsy",
          "alive": true,
          "hp": 12, "max_hp": 12,
          "block": 0,
          "status": [ /* Power Objects */ ]
        }
      ]
    }
  ]
}
```

### Map State Additions

```jsonc
{
  "map": {
    "votes": [
      {
        "player": "The Ironclad",
        "is_local": true,
        "voted": true,
        "vote_col": 2, "vote_row": 3
      }
    ],
    "all_voted": false
  }
}
```

### Event State Additions

```jsonc
{
  "event": {
    "is_shared": true,
    "votes": [
      {
        "player": "The Ironclad",
        "is_local": true,
        "voted": true,
        "vote_option": 0
      }
    ],
    "all_voted": false
  }
}
```

### Treasure State Additions

```jsonc
{
  "treasure": {
    "is_bidding_phase": true,
    "bids": [
      {
        "player": "The Ironclad",
        "is_local": true,
        "voted": true,
        "vote_relic_index": 0
      }
    ],
    "all_bid": false
  }
}
```

### Multiplayer-Only Actions

**End turn (vote):**
```json
{ "action": "end_turn" }
```
In multiplayer, this is a vote. The turn ends only when all players submit. Use `battle.can_end_turn` / `battle.end_turn_blocked_reason` to check local readiness.

**Undo end turn:**
```json
{ "action": "undo_end_turn" }
```
Retract the end-turn vote before all players have committed. Use `battle.can_undo_end_turn` / `battle.undo_end_turn_blocked_reason` to check local readiness.

All other actions work identically to singleplayer.
