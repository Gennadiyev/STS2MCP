using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private static async Task<Dictionary<string, object?>> ExecuteMultiplayerAction(string action, Dictionary<string, JsonElement> data)
    {
        if (!RunManager.Instance.IsInProgress)
            return Error("No run in progress");

        if (!RunManager.Instance.NetService.Type.IsMultiplayer())
            return BlockedAction(RunModeConflict(isMultiplayer: false));

        var runState = RunManager.Instance.DebugOnlyGetState()!;
        var player = LocalContext.GetMe(runState);
        if (player == null)
            return Error("Could not find local player");

        if (action is "coordination_state" or "coordination_prompt" or "coordination_reply" or
            "coordination_message" or "coordination_cancel")
            return ExecuteCoordination(action, data);

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root != null && IsAnyFtueVisible(tree.Root))
            return Error("Blocking popup active. Use menu_select with one of the advertised popup options before gameplay actions.");

        if (action == "choose_rest_option")
        {
            var result = ExecuteChooseRestOption(data, out var completion);
            return completion == null ? result : await completion;
        }

        if (action is "play_card" or "use_potion" or "end_turn" &&
            data.TryGetValue("coordination_prompt_id", out var promptId) &&
            ValidateCoordinationAction(data, promptId) is { } blocked)
            return blocked;

        return action switch
        {
            // Delegated to existing sync-safe handlers
            "play_card" => ExecutePlayCard(player, data),
            "use_potion" => ExecuteUsePotion(player, data),
            "discard_potion" => ExecuteDiscardPotion(player, data),
            "choose_map_node" => ExecuteChooseMapNode(data, mapVoter: player),
            "choose_event_option" => ExecuteChooseEventOption(data),
            "advance_dialogue" => ExecuteAdvanceDialogue(),
            "shop_purchase" => ExecuteShopPurchase(player, data),
            "claim_reward" => ExecuteClaimReward(data),
            "select_card_reward" => ExecuteSelectCardReward(data),
            "select_card_reward_alternative" => ExecuteSelectCardRewardAlternative(data),
            "skip_card_reward" => ExecuteSkipCardReward(),
            "proceed" => ExecuteProceed(player, data),
            "select_card" => ExecuteSelectCard(data),
            "confirm_selection" => ExecuteConfirmSelection(),
            "cancel_selection" => ExecuteCancelSelection(),
            "select_bundle" => ExecuteSelectBundle(data),
            "confirm_bundle_selection" => ExecuteConfirmBundleSelection(),
            "cancel_bundle_selection" => ExecuteCancelBundleSelection(),
            "combat_select_card" => ExecuteCombatSelectCard(data),
            "combat_confirm_selection" => ExecuteCombatConfirmSelection(),
            "select_relic" => ExecuteSelectRelic(data),
            "skip_relic_selection" => ExecuteSkipRelicSelection(),
            "claim_treasure_relic" => ExecuteClaimTreasureRelic(data),
            "crystal_sphere_set_tool" => ExecuteCrystalSphereSetTool(data),
            "crystal_sphere_click_cell" => ExecuteCrystalSphereClickCell(data),
            "crystal_sphere_proceed" => ExecuteCrystalSphereProceed(),

            // Multiplayer-specific actions
            "end_turn" => ExecuteMultiplayerEndTurn(player, data),
            "undo_end_turn" => ExecuteUndoEndTurn(player),

            _ => Error($"Unknown multiplayer action: {action}")
        };
    }

    private static Dictionary<string, object?> ExecuteMultiplayerEndTurn(Player player,
        Dictionary<string, JsonElement> data)
    {
        if (GetCombatActionBlock(player) is { } block)
        {
            if (block.Reason == "turn_already_ended")
                block = block with { Message = "Already submitted end turn. Use 'undo_end_turn' to retract." };
            return BlockedAction(block);
        }

        bool allowPlayableCards = data.TryGetValue("allow_playable_cards", out var allow) &&
                                  allow.ValueKind == JsonValueKind.True;
        var playerState = player.PlayerCombatState!;
        if (!allowPlayableCards)
        {
            var playableIndices = playerState.Hand.Cards
                .Select((card, index) => (card, index))
                .Where(entry => entry.card.CanPlay(out _, out _))
                .Select(entry => entry.index)
                .ToArray();
            if (playableIndices.Length > 0)
            {
                var blocked = BlockedAction(new("playable_cards_remain",
                    "You still have playable cards. Play them or pass allow_playable_cards=true to end deliberately.", false));
                blocked["playable_card_indices"] = playableIndices;
                return blocked;
            }
        }

        int turnNumber = playerState.TurnNumber;
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
            new EndPlayerTurnAction(player, turnNumber));

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["turn_status"] = "end_submitted",
            ["message"] = "End turn submitted. Check multiplayer state for readiness or the next turn."
        };
    }

    private static ActionBlock? GetUndoEndTurnBlock(Player? player)
    {
        var manager = CombatManager.Instance;
        if (!manager.IsInProgress || player?.PlayerCombatState == null)
            return new("not_in_combat", "There is no local combat turn to reopen.", false);
        if (!manager.IsPlayerReadyToEndTurn(player))
            return new("turn_not_ended", "The local player has no end-turn vote to retract.", false);
        // PlayerActionsDisabled also covers the normal wait after a local end-turn vote.
        if (!IsPlayerSideTurn() || !manager.CanUnEndTurn(player))
            return new("turn_locked", "The game has locked this turn. Wait for the next turn.", false);
        return null;
    }

    private static Dictionary<string, object?> ExecuteUndoEndTurn(Player player)
    {
        if (GetUndoEndTurnBlock(player) is { } block)
            return BlockedAction(block);

        int turnNumber = player.PlayerCombatState!.TurnNumber;
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
            new UndoEndPlayerTurnAction(player, turnNumber));

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["turn_status"] = "undo_submitted",
            ["message"] = "Undo submitted. Wait for battle.can_act before playing more cards."
        };
    }
}
