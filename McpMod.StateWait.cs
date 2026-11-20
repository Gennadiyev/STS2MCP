using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private sealed class CombatStateWait
    {
        internal Task? CardCompletion { get; set; }
        internal int? EndedTurnNumber { get; set; }
        internal MapTravelWait? MapTravel { get; set; }
        internal HandConfirmationWait? HandConfirmation { get; set; }
    }

    private sealed record MapTravelWait(RunState Run, AbstractRoom? Origin, int ActIndex, MapCoord Destination);
    private sealed record HandConfirmationWait(NPlayerHand Hand, object? Prompt);

    private static object? GetHandSelectionPrompt(NPlayerHand hand) =>
        GetInstanceFieldValue(hand, "_selectionCompletionSource");

    private static async Task<(Dictionary<string, object?> State, bool TimedOut)> ReadReadyState(
        bool compact, CombatStateWait? action = null)
    {
        var started = Stopwatch.GetTimestamp();
        var deadline = TimeSpan.FromSeconds(7);
        while (true)
        {
            var result = await RunOnMainThread(() =>
            {
                bool waiting = IsWaitingForPlayableState(action);
                bool timedOut = waiting && Stopwatch.GetElapsedTime(started) >= deadline;
                var state = !waiting || timedOut ? BuildGameState(compact) : null;
                return (State: state, TimedOut: timedOut);
            }).ConfigureAwait(false);
            if (result.State != null)
                return (result.State, result.TimedOut);

            // Probe native controls without rebuilding the hand, piles, and scene payload each time.
            await Task.Delay(25).ConfigureAwait(false);
        }
    }

    private static bool IsWaitingForPlayableState(CombatStateWait? action)
    {
        var run = RunManager.Instance;
        if (!run.IsInProgress || HasActiveMultiplayerRun())
            return false;

        var state = run.DebugOnlyGetState();
        bool mapIsOpen = IsMapScreenOpenOrVisible();
        bool overlayIsOpen = NOverlayStack.Instance?.Peek() != null;
        bool modalIsOpen = GetOpenModalNode() != null;
        if (action?.MapTravel is { } travel)
        {
            if (state == null || !ReferenceEquals(state, travel.Run) || state.CurrentActIndex != travel.ActIndex)
                return false; // The original run or act is gone.

            var room = state.CurrentRoom;
            bool reached = state.CurrentMapCoord == travel.Destination &&
                room != null && !ReferenceEquals(room, travel.Origin);
            if (modalIsOpen || (reached && !mapIsOpen &&
                (overlayIsOpen || NPlayerHand.Instance?.IsInCardSelection == true)))
                return false; // Return an input screen even if room entry is waiting on it.

            if (!reached || run.IsChangingRooms || NMapScreen.Instance?.IsTraveling == true ||
                (mapIsOpen && room is not MapRoom))
                return true;
        }

        if (mapIsOpen || overlayIsOpen || modalIsOpen)
            return false;

        if (state?.CurrentRoom is TreasureRoom)
        {
            var treasure = GetVisibleTreasureRoom();
            return treasure == null || TryOpenTreasureChest(treasure) ||
                (GetAvailableTreasureProceedButton(treasure) == null && GetAvailableTreasureRelicHolders(treasure).Count == 0);
        }

        if (state?.CurrentRoom is not CombatRoom)
            return false;
        if (!CombatManager.Instance.IsInProgress)
            return true; // The map, rewards, or game-over screen has yet to appear.

        var player = LocalContext.GetMe(state);
        var block = GetCombatActionBlock(player);
        if (block is { Reason: "hand_selection" } && action?.HandConfirmation is { } confirmation)
        {
            // Confirm can complete the prompt's source before the hand has left selection mode.
            // A different source represents a new prompt, even on the same hand.
            return ReferenceEquals(NPlayerHand.Instance, confirmation.Hand) &&
                ReferenceEquals(GetHandSelectionPrompt(confirmation.Hand), confirmation.Prompt);
        }
        if (block is { Retryable: false } && block.Reason != "turn_already_ended")
            return false; // A hand selection or other input can interrupt the pending action.

        return block != null || action?.CardCompletion is { IsCompleted: false } ||
            (action?.EndedTurnNumber is int turn && player?.PlayerCombatState?.TurnNumber == turn) ||
            (action?.HandConfirmation != null && !run.ActionQueueSet.IsEmpty);
    }
}
