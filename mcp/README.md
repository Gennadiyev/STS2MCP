# MCP Tools

## Singleplayer

| Tool | Scope | Description |
| --- | --- | --- |
| `get_game_state(format?)` | General | Get current game state (`markdown` or `json`) |
| `use_potion(slot, target?)` | General | Use a potion (works in and out of combat) |
| `proceed_to_map()` | General | Proceed from rewards/rest site/shop/treasure to the map |
| `sts2_wiki_search(query, limit?)` | General | Search STS2 wiki.gg pages to avoid STS1 knowledge pollution |
| `sts2_wiki_get_summary(title)` | General | Get intro summary for a specific STS2 wiki page |
| `sts2_wiki_get_page(title, section_chars?)` | General | Get richer plain-text extract from a STS2 wiki page |
| `sts2_decision_workflow(question, action_plan?, terms?, top_k?, include_wiki_summary?)` | General | Preferred single-entry flow: state pull + STS2 gate + heuristic CoT + optional guard |
| `sts2_knowledge_gate(question, terms?, per_term_limit?)` | General | STS2-only knowledge gate: verify terms against wiki and flag uncertain/conflicting rules |
| `sts2_heuristic_think(game_state_markdown, top_k?)` | General | Generate a fixed 6-step heuristic reasoning scaffold with Top-K candidate slots |
| `sts2_guard_action_plan(game_state_markdown, action_plan, reread_hand_before_each_play?)` | General | Rule-based safety guard for high-risk turns and index-shift errors |
| `sts2_replay_log_turn(entry_json)` | General | Store one structured replay turn entry into local case memory |
| `sts2_replay_search(query, limit?)` | General | Search local replay memory for similar historical cases |
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
| `relic_select(relic_index)` | Relic Select | Choose a relic from the selection screen |
| `relic_skip()` | Relic Select | Skip relic selection |
| `treasure_claim_relic(relic_index)` | Treasure | Claim a relic from the treasure chest |

## Multiplayer

All multiplayer tools are prefixed with `mp_`. They route through `/api/v1/multiplayer` and are only available during multiplayer (co-op) runs. The endpoints automatically guard against cross-mode calls.

| Tool | Scope | Description |
| --- | --- | --- |
| `mp_get_game_state(format?)` | General | Get multiplayer game state (all players, votes, bids) |
| `mp_combat_play_card(card_index, target?)` | Combat | Play a card from the local player's hand |
| `mp_combat_end_turn()` | Combat | Submit end-turn vote (turn ends when all players submit) |
| `mp_combat_undo_end_turn()` | Combat | Retract end-turn vote |
| `mp_use_potion(slot, target?)` | General | Use a potion from the local player's slots |
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
| `mp_combat_select_card(card_index)` | Combat Selection | Select a card during in-combat selection prompts |
| `mp_combat_confirm_selection()` | Combat Selection | Confirm in-combat card selection |
| `mp_relic_select(relic_index)` | Relic Select | Choose a relic from the selection screen |
| `mp_relic_skip()` | Relic Select | Skip relic selection |
| `mp_treasure_claim_relic(relic_index)` | Treasure | Bid on a relic (relic fight if contested) |

## Recommended STS2 Decision Flow

For stable non-training gameplay:

1. `sts2_decision_workflow(...)` (preferred first call)
2. Execute gameplay actions (`combat_play_card`, etc.)
3. `sts2_replay_log_turn(...)` and later `sts2_replay_search(...)`

Manual equivalent (if you do not use the wrapper):

1. `get_game_state(format="markdown")`
2. `sts2_knowledge_gate(...)`
3. (If needed) `sts2_wiki_search/get_summary/get_page`
4. `sts2_heuristic_think(...)`
5. `sts2_guard_action_plan(...)`
6. Execute gameplay actions (`combat_play_card`, etc.)
7. `sts2_replay_log_turn(...)` and later `sts2_replay_search(...)`

## New Policy/Schema Files

- `decision_policy.md`: standardized 6-step reasoning policy
- `replay_schema.json`: structured replay record schema
- `sts2_conflict_lexicon.json`: STS1 contamination warning terms
