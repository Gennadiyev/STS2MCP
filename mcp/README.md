# MCP Tools

## Singleplayer

| Tool | Scope | Description |
|---|---|---|
| `get_game_state(format?, compact?, wait_for_ready?)` | General | Get current game state with optional transition waiting and compact output |
| `menu_select(option, seed?)` | General | Select a visible menu/game-over option |
| `get_profile()` | Profiles | Get active profile progress |
| `get_compendium()` | Profiles | Get active profile progress grouped like the in-game Compendium |
| `search_wiki(query, item_type?, limit?)` | Profiles | Look up discovered cards and relics, or unlocked characters |
| `list_profiles()` | Profiles | List profile slots and active slot |
| `switch_profile(profile_id)` | Profiles | Switch to a profile slot through the game UI |
| `delete_profile(profile_id)` | Profiles | Delete an inactive profile slot when `allow_profile_delete` is enabled in the mod config |
| `use_potion(slot, target?)` | General | Use a potion (works in and out of combat) |
| `discard_potion(slot)` | General | Discard a potion to free up the slot |
| `proceed_to_map(leave_shop?)` | General | Proceed to the map, or leave an open shop in one call with `leave_shop=true` |
| `combat_play_card(card_index, target?, return_state?)` | Combat | Play a card from hand and optionally return the next state |
| `combat_play_sequence(plays, end_turn?)` | Combat | Resolve up to six planned cards and return the resulting state |
| `combat_end_turn(return_state?)` | Combat | End the current turn and optionally return the next state |
| `combat_select_card(card_index)` | Combat Selection | Select a card from hand during exhaust/discard prompts |
| `combat_confirm_selection(return_state?)` | Combat Selection | Confirm the in-combat card selection and optionally wait for a prompt or ready state |
| `rewards_claim(reward_index)` | Rewards | Claim a reward from the post-combat screen |
| `rewards_pick_card(card_index)` | Rewards | Select a card from the card reward screen |
| `rewards_skip_card()` | Rewards | Skip the card reward |
| `map_choose_node(node_index, return_state?)` | Map | Choose a map node and optionally wait for the destination |
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

### Action Readiness

For single-player runs, use `get_game_state(compact=true, wait_for_ready=true)` to wait through combat and treasure transitions in one call. Compact state keeps the hand, enemy intents, resources, and pile counts. Request full state when a decision depends on individual pile contents.

The mod also exposes the game's Instant Mode under **Settings → General**. Enable it on the profile you're using to shorten in-game animations during MCP play.

Use `return_state=true` on `combat_play_card` and `combat_end_turn` to get a compact JSON `state` alongside the action receipt. That state can be the next playable turn, a selection prompt, or a different screen. A wait lasts up to seven seconds. If the receipt includes `state_wait_timed_out` or `state_error`, refresh state before continuing. The receipt's `status: ok` still means the action was accepted.

`map_choose_node` and `combat_confirm_selection` also accept `return_state=true`. The former waits for the destination, and the latter waits for the confirmed choice to settle or a new prompt to open.

`combat_play_sequence` accepts plays in order, each with a `card_index` and `card_id` from the **starting hand**, and an enemy `target` when required. It tracks those cards as indices shift. A stop returns `stop_reason`, the `submitted` prefix of accepted card IDs, and the current state. A submitted card may still be resolving or waiting for your input. Set `end_turn=true` only when you intend to end the original turn after the complete sequence. Check `end_turn_submitted` and the resulting state before acting again.

`battle.warnings` flags each hazard the first time its warning condition is met in an encounter. This includes delayed damage after a kill, card and gold theft, limits on card plays, damage caps, and potentially lethal damage outside the normal attack intents. Read the live statuses for current amounts on subsequent turns. A warning with `interrupt_sequence: true` can stop a planned sequence when first discovered. Planning notices also appear in the response and leave the sequence running.

In combat, check `battle.can_act` before playing a card, using a potion, or ending the turn. When it is false, `battle.action_blocked_reason` explains what is pending. Use the selection tools during a card-selection prompt. After a retryable error, refresh state and use the latest card index.

In treasure rooms, wait for `treasure.can_claim`, `treasure.can_skip`, or `treasure.can_proceed` to match the intended action. With a shop inventory open, the default Proceed closes it. Set `leave_shop=true` to close it and leave in one call.

### Profile Tools

`get_profile()` returns the raw active-profile progress summary: character totals, global totals, discoveries, achievements, epochs, and aggregate stats.

`get_compendium()` presents the same profile data in the shape agents usually need when reasoning about long-term progress. It groups the response into `card_library`, `relic_collection`, `potion_lab`, `bestiary`, `character_stats`, and `run_history`, matching the high-level Compendium cards in the game UI. It also includes `current_run` while a run is active, with a derived `run_id` in `{save_scope}:profile{profile_id}:{start_time}` format.

The Compendium response is profile-scoped, not run-state-scoped. It works from the main menu, does not require navigating the in-game Compendium UI, and summarizes only the 20 most recent saved run-history files to keep the MCP response bounded.

`search_wiki(query, item_type="all", limit=10)` fuzzy-matches names and IDs across the active profile’s discovered cards and relics. Card matches include `base` and `upgraded` blocks with rules text, costs and keywords. Request `item_type="character"` and a name or ID like `REGENT` for an unlocked character’s localized description, starting HP, gold, energy, deck and relics. Use the query `character` with that type to list the unlocked roster. Results default to 10 and are capped at 50.

## Multiplayer

All multiplayer tools are prefixed with `mp_`. They route through `/api/v1/multiplayer` and are only available during multiplayer (co-op) runs. The endpoints automatically guard against cross-mode calls.

| Tool | Scope | Description |
|---|---|---|
| `mp_get_game_state(format?)` | General | Get multiplayer game state (all players, votes, bids) |
| `mp_combat_play_card(card_index, target?)` | Combat | Play a card from the local player's hand |
| `mp_combat_end_turn()` | Combat | Submit end-turn vote (turn ends when all players submit) |
| `mp_combat_undo_end_turn()` | Combat | Retract end-turn vote |
| `mp_use_potion(slot, target?)` | General | Use a potion from the local player's slots |
| `mp_discard_potion(slot)` | General | Discard a potion from the local player's slots |
| `mp_proceed_to_map(leave_shop?)` | General | Proceed to the map, or leave an open shop in one call with `leave_shop=true` |
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
