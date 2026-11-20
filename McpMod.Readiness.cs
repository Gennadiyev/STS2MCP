using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private sealed record ActionBlock(string Reason, string Message, bool Retryable);

    private static ActionBlock? GetCombatActionBlock(Player? player)
    {
        var manager = CombatManager.Instance;
        if (!manager.IsInProgress)
            return new("not_in_combat", "Combat is no longer in progress.", false);
        if (player?.PlayerCombatState is not { } playerState)
            return new("player_not_ready", "The local combat player is still loading.", true);
        if (!player.Creature.IsAlive)
            return new("player_dead", "The local player is dead.", false);
        var hand = NCombatRoom.Instance?.Ui?.Hand;
        if (hand?.IsInCardSelection == true)
            return new("hand_selection", "Resolve the current hand selection before taking another combat action.", false);
        if (!IsPlayerSideTurn())
            return new("enemy_turn", "Wait for the enemy turn to finish.", true);
        if (playerState.Phase != PlayerTurnPhase.Play ||
            (manager.PlayersTakingExtraTurn.Count > 0 && !manager.PlayersTakingExtraTurn.Contains(player)))
            return new("turn_transition", "Wait for the local player's play phase.", true);
        if (manager.IsPlayerReadyToEndTurn(player))
            return new("turn_already_ended", "The local player has already ended the turn.", false);
        if (manager.PlayerActionsDisabled)
            return new("player_actions_disabled", "The game is temporarily blocking combat actions.", true);

        // Native turn setup can finish for this player before the shared executor is unpaused.
        var executor = RunManager.Instance.ActionExecutor;
        if (executor.IsPaused)
            return new("turn_transition", "Wait for the combat action queue to resume.", true);
        if (executor.IsRunning)
            return new("action_resolving", "Wait for the current combat actions to resolve.", true);
        if (hand == null || !IsLiveNode(hand))
            return new("hand_not_ready", "The combat hand is still loading.", true);
        if (hand.PeekButton.IsPeeking)
            return new("hand_peeking", "Close the hand peek before taking a combat action.", false);
        // The model can change before the hand finishes adding or removing its card holders.
        if (hand.InCardPlay || hand.CurrentMode != NPlayerHand.Mode.Play ||
            hand.ActiveHolders.Count != playerState.Hand.Cards.Count)
            return new("hand_not_ready", "Wait for the combat hand to finish updating.", true);

        return null;
    }

    private static void AddCombatReadiness(Dictionary<string, object?> battle, Player? player)
    {
        var block = GetCombatActionBlock(player);
        battle["can_act"] = block == null;
        battle["action_blocked_reason"] = block?.Reason;
        battle["action_blocked_message"] = block?.Message;
    }

    private static Dictionary<string, object?> BlockedAction(ActionBlock block)
    {
        var result = Error(block.Message);
        result["reason"] = block.Reason;
        result["retryable"] = block.Retryable;
        return result;
    }

    private static NTreasureRoom? GetVisibleTreasureRoom()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        return FindAll<NTreasureRoom>(tree.Root).FirstOrDefault(room =>
            IsNodeVisible(room) && ActiveScreenContext.Instance.IsCurrent(room));
    }

    private static bool TryOpenTreasureChest(NTreasureRoom treasure)
    {
        var chest = treasure.GetNodeOrNull<NClickableControl>("Chest");
        if (chest == null || !IsControlVisibleOrActionable(chest))
            return false;

        chest.ForceClick();
        return true;
    }

    private static NProceedButton? GetAvailableTreasureProceedButton(NTreasureRoom treasure)
    {
        var button = treasure.ProceedButton;
        return IsControlVisibleOrActionable(button) ? button : null;
    }

    private static List<NTreasureRoomRelicHolder> GetAvailableTreasureRelicHolders(NTreasureRoom treasure)
    {
        var collection = treasure.GetNodeOrNull<NTreasureRoomRelicCollection>("%RelicCollection");
        if (collection == null || !IsNodeVisible(collection))
            return new();

        return FindAll<NTreasureRoomRelicHolder>(collection)
            .Where(holder => IsControlVisibleOrActionable(holder) && holder.Relic?.Model != null)
            .ToList();
    }
}
