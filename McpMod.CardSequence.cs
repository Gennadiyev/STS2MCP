using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private const int MaxSequencePlays = 6;
    private sealed record PlannedPlay(int? CardIndex, string? CardId, string? Target);
    private sealed record SequencePlay(CardModel Card, string CardId, string? Target);
    private sealed record SequencePlan(Player Player, ICombatState Combat, int Turn, HashSet<Creature> Enemies,
        List<SequencePlay> Plays);

    private static async Task<Dictionary<string, object?>> ExecuteCardSequence(Dictionary<string, JsonElement> data)
    {
        if (!data.TryGetValue("plays", out var entries) || entries.ValueKind != JsonValueKind.Array ||
            entries.GetArrayLength() is < 1 or > MaxSequencePlays)
            return Error($"Provide between one and {MaxSequencePlays} card plays.");

        PlannedPlay?[]? plays;
        try { plays = entries.Deserialize<PlannedPlay?[]>(_jsonOptions); }
        catch (JsonException) { return Error("Each play must include card_index, card_id, and a target when required."); }
        if (plays == null || plays.Any(play => play?.CardIndex is not >= 0 || string.IsNullOrWhiteSpace(play.CardId)))
            return Error("Each play must include a non-negative card_index and a card_id from the starting hand.");

        if (data.TryGetValue("end_turn", out var endTurnValue) &&
            endTurnValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return Error("end_turn must be a boolean.");
        bool endTurn = endTurnValue.ValueKind == JsonValueKind.True;

        var submitted = new List<string>(plays.Length);
        var clock = Stopwatch.StartNew();
        ActionBlock? stop;
        try
        {
            var bound = await RunOnMainThread(() => BindSequence(plays)).ConfigureAwait(false);
            var plan = bound.Plan;
            stop = bound.Block;
            if (plan != null)
            {
                foreach (var play in plan.Plays)
                {
                    if (clock.Elapsed >= TimeSpan.FromSeconds(7))
                    {
                        stop = new("state_wait_timed_out", "The card sequence reached its time limit. Refresh game state.", false);
                        break;
                    }

                    var wait = new CombatStateWait();
                    stop = await RunOnMainThread(() => SubmitSequenceCard(plan, play, wait)).ConfigureAwait(false);
                    if (stop != null)
                        break;

                    submitted.Add(play.CardId);
                    stop = await WaitForSequenceCard(plan, wait, clock).ConfigureAwait(false);
                    if (stop != null)
                        break;
                }

                if (stop == null && endTurn)
                {
                    var wait = new CombatStateWait();
                    stop = await RunOnMainThread(() => GetSequenceBlock(plan.Player, plan) ??
                        GetSequenceRejection(ExecuteEndTurn(plan.Player, wait), "end_turn_rejected")).ConfigureAwait(false);
                    if (stop == null)
                        return await BuildSequenceResult(submitted, true, null, wait).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Card sequence interrupted: {ex}");
            stop = new("sequence_interrupted", "The next result is uncertain. Refresh state before continuing.", false);
        }

        return await BuildSequenceResult(submitted, false, stop).ConfigureAwait(false);
    }

    private static (SequencePlan? Plan, ActionBlock? Block) BindSequence(PlannedPlay?[] plays)
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        var player = run == null ? null : LocalContext.GetMe(run);
        if (GetSequenceBlock(player) is { } block)
            return (null, block);
        if (player?.PlayerCombatState is not { } playerState || player.Creature.CombatState is not { } combat)
            return (null, new("not_in_combat", "No active player combat state.", false));

        var hand = playerState.Hand.Cards;
        var bound = new List<SequencePlay>(plays.Length);
        var usedIndices = new HashSet<int>();
        foreach (var requested in plays)
        {
            var play = requested!;
            int index = play.CardIndex!.Value;
            if (index >= hand.Count || !string.Equals(hand[index].Id.Entry, play.CardId, StringComparison.Ordinal) ||
                !usedIndices.Add(index))
                return (null, new("card_mismatch", "Each play must match a distinct card in the starting hand.", false));

            var card = hand[index];
            if (card.TargetType == TargetType.AnyEnemy &&
                (play.Target == null || ResolveTarget(combat, play.Target) == null))
                return (null, new("target_mismatch", "A planned target is missing or is no longer alive.", false));
            bound.Add(new SequencePlay(card, card.Id.Entry, play.Target));
        }

        return (new(player, combat, playerState.TurnNumber,
            new HashSet<Creature>(GetLivingEnemies(combat).Select(entry => entry.Creature), ReferenceEqualityComparer.Instance),
            bound), null);
    }

    private static ActionBlock? GetSequenceBlock(Player? player, SequencePlan? plan = null)
    {
        if (IsMultiplayerRun())
            return new("multiplayer", "Card sequences are for single-player combat.", false);
        if (IsMapScreenOpenOrVisible() || NOverlayStack.Instance?.Peek() != null || GetOpenModalNode() != null ||
            Engine.GetMainLoop() is SceneTree tree && IsAnyFtueVisible(tree.Root))
            return new("screen_changed", "A different screen is active.", false);
        if (GetCombatActionBlock(player) is { } block)
            return block;
        if (plan != null && (!ReferenceEquals(player?.Creature.CombatState, plan.Combat) ||
                            player?.PlayerCombatState?.TurnNumber != plan.Turn))
            return new("turn_changed", "The original combat turn ended.", false);
        if (HasNewInterruptingCombatWarning())
            return new("combat_warning", "A new danger warning needs attention.", false);
        if (plan != null && !plan.Enemies.SetEquals(GetLivingEnemies(plan.Combat).Select(entry => entry.Creature)))
            return new("enemies_changed", "The enemies changed. Check the new state before continuing.", false);
        return null;
    }

    private static ActionBlock? SubmitSequenceCard(SequencePlan plan, SequencePlay play, CombatStateWait wait)
    {
        if (GetSequenceBlock(plan.Player, plan) is { } block)
            return block;

        var hand = plan.Player.PlayerCombatState!.Hand.Cards;
        for (int i = 0; i < hand.Count; i++)
        {
            if (ReferenceEquals(hand[i], play.Card) && hand[i].Id.Entry == play.CardId)
                return GetSequenceRejection(ExecutePlayableCard(plan.Player, i, play.Target, wait), "card_rejected");
        }

        return new("card_missing", "The planned card is no longer in the hand.", false);
    }

    private static ActionBlock? GetSequenceRejection(Dictionary<string, object?> result, string fallback) =>
        result.GetValueOrDefault("status") is "ok" ? null : new ActionBlock(
            result.GetValueOrDefault("reason") as string ?? fallback,
            result.GetValueOrDefault("error") as string ?? "The game rejected the action.", false);

    private static async Task<ActionBlock?> WaitForSequenceCard(SequencePlan plan, CombatStateWait wait, Stopwatch clock)
    {
        while (true)
        {
            var (pending, stop) = await RunOnMainThread(() =>
            {
                if (IsWaitingForPlayableState(wait))
                    return (true, (ActionBlock?)null);
                if (wait.CardCompletion is { IsFaulted: true } or { IsCanceled: true })
                    return (false, new ActionBlock("card_interrupted", "The game interrupted the submitted card.", false));
                return (false, GetSequenceBlock(plan.Player, plan));
            }).ConfigureAwait(false);
            if (!pending)
                return stop;
            if (clock.Elapsed >= TimeSpan.FromSeconds(7))
                return new("state_wait_timed_out", "A submitted card may still be resolving. Refresh game state.", false);
            await Task.Delay(25).ConfigureAwait(false);
        }
    }

    private static async Task<Dictionary<string, object?>> BuildSequenceResult(List<string> submitted, bool endTurn,
        ActionBlock? stop, CombatStateWait? wait = null)
    {
        var result = new Dictionary<string, object?>
        {
            ["status"] = stop == null ? "ok" : "stopped",
            ["submitted"] = submitted,
            ["end_turn_submitted"] = endTurn,
            ["stop_reason"] = stop?.Reason,
            ["message"] = stop?.Message,
        };
        try
        {
            if (wait == null)
            {
                result["state"] = await RunOnMainThread(() => BuildGameState(compact: true)).ConfigureAwait(false);
            }
            else
            {
                var ready = await ReadReadyState(compact: true, action: wait).ConfigureAwait(false);
                result["state"] = ready.State;
                if (ready.TimedOut)
                    result["state_wait_timed_out"] = true;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Sequence state failed: {ex}");
            result["state_error"] = "Refresh game state before continuing.";
        }
        return result;
    }
}
