# STS2 Decision Policy (Non-Training)

This document defines a stable turn-by-turn reasoning workflow for STS2 agents.

## STS2-only Gate

Before committing actions:

1. Run `sts2_knowledge_gate(question, terms?)`
2. If `status=needs_review`, mark uncertain mechanics and prefer conservative play.
3. For any ambiguous term, fetch evidence via:
   - `sts2_wiki_search`
   - `sts2_wiki_get_summary` or `sts2_wiki_get_page`

## 6-Step Heuristic Chain

Use the same sequence every turn:

1. `ThreatCheck`  
   Estimate incoming damage and lethal threshold.
2. `WinCondition`  
   Choose shortest survivable route to win or stabilize.
3. `ResourcePlan`  
   Plan energy, potion, power-card windows.
4. `TargetPriority`  
   Focus highest-pressure enemy (damage, scaling, revive pressure).
5. `ActionSequence`  
   Order cards safely with index-shift awareness.
6. `PostCheck`  
   Re-estimate risk after planned line.

Generate the template by calling `sts2_heuristic_think(game_state_markdown, top_k)`.

## Top-K Candidate Rule

- Produce at least 2 candidate lines per turn.
- Rank by:
  1. survivability
  2. long-term scaling value
  3. immediate damage efficiency

## Action Guard

Run `sts2_guard_action_plan` before execution.

Fail conditions:

- Potential STS1 contamination terms present in the plan
- High-risk turn without explicit defense
- Multiple `combat_play_card` intents without hand re-read commitment

If guard result is `unsafe`, revise line before acting.

## Replay Loop

After each turn:

1. Write structured entry through `sts2_replay_log_turn`
2. Include expected vs actual and one transferable lesson
3. During future similar fights, query `sts2_replay_search`

Use schema: `replay_schema.json`.
