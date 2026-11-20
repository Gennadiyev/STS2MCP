using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private const string SandpitPowerId = "SANDPIT_POWER";
    private const string ThornsPowerId = "THORNS_POWER";

    private sealed record CombatWarning(string EffectId, string Name, string Side, string Message,
        decimal? Amount = null, bool InterruptSequence = false, string? EntityId = null);

    private static readonly ConditionalWeakTable<ICombatState, HashSet<string>> CombatWarningsByEncounter = new();

    // Sequence checks run on the game thread and do not consume warnings.
    private static bool HasNewInterruptingCombatWarning()
    {
        var manager = CombatManager.Instance;
        if (!manager.IsInProgress || manager.DebugOnlyGetState() is not { } combatState)
            return false;

        var runState = RunManager.Instance.DebugOnlyGetState();
        var player = runState == null ? null : LocalContext.GetMe(runState);
        return player?.Creature is { IsAlive: true } creature &&
            GetUnreportedCombatWarnings(combatState, creature).Any(warning => warning.InterruptSequence);
    }

    // Attach warnings after the full response state has been built, never during a readiness probe.
    private static void AddCombatWarnings(Dictionary<string, object?> state, Creature? player)
    {
        if (player is not { IsAlive: true }
            || state.GetValueOrDefault("battle") is not Dictionary<string, object?> battle
            || CombatManager.Instance.DebugOnlyGetState() is not { } combatState)
            return;

        var pending = GetUnreportedCombatWarnings(combatState, player);
        if (pending.Count == 0)
            return;

        var reported = CombatWarningsByEncounter.GetOrCreateValue(combatState);
        var warnings = new List<Dictionary<string, object?>>(pending.Count);
        foreach (var warning in pending.OrderByDescending(candidate => candidate.InterruptSequence))
        {
            if (!reported.Add(warning.EffectId))
                continue;
            var item = new Dictionary<string, object?>
            {
                ["effect_id"] = warning.EffectId,
                ["name"] = warning.Name,
                ["side"] = warning.Side,
                ["message"] = warning.Message,
                ["interrupt_sequence"] = warning.InterruptSequence
            };
            if (warning.Amount is { } amount)
                item["amount"] = amount;
            if (warning.EntityId is { } entityId)
                item["entity_id"] = entityId;
            warnings.Add(item);
        }
        battle["warnings"] = warnings;
    }

    private static List<CombatWarning> GetUnreportedCombatWarnings(ICombatState combatState, Creature player)
    {
        var warnings = new List<CombatWarning>();
        if (FindSandpitWarning(combatState, player) is { } sandpit)
        {
            string countdown = sandpit.Amount == 1
                ? "The Insatiable will eat you when it takes its next turn."
                : $"The Insatiable will eat you in {sandpit.Amount} turns.";
            warnings.Add(new(SandpitPowerId, "Sandpit", "player",
                countdown + " Defeat it before the countdown expires or play Frantic Escape to buy one turn.",
                sandpit.Amount, InterruptSequence: true));
        }
        if (combatState.Enemies.Any(enemy => FindCombatWarningPower(enemy, ThornsPowerId) != null))
        {
            warnings.Add(new(ThornsPowerId, "Thorns", "enemy",
                "Each Attack hit on an enemy with Thorns can deal damage back, even through its Block. Check each target's Thorns amount and budget Block per hit before multi-hit or area Attacks.",
                InterruptSequence: true));
        }

        CollectEnemyCombatWarnings(combatState, player, warnings);
        CollectPlayerCombatWarnings(combatState, player, warnings);
        CollectLethalCombatWarnings(combatState, player, warnings);
        if (CombatWarningsByEncounter.TryGetValue(combatState, out var reported))
            warnings.RemoveAll(warning => reported.Contains(warning.EffectId));
        return warnings;
    }

    private static PowerModel? FindSandpitWarning(ICombatState combatState, Creature? player)
    {
        if (player is not { IsAlive: true })
            return null;

        // The Insatiable owns an instanced Sandpit power targeting each affected player.
        return FindCombatWarningPower(player, SandpitPowerId) ??
            combatState.Enemies.Where(enemy => enemy.IsAlive)
                .SelectMany(enemy => enemy.Powers)
                .FirstOrDefault(power => IsCombatWarningPower(power, SandpitPowerId) &&
                    ReferenceEquals(power.Target, player));
    }

    private static PowerModel? FindCombatWarningPower(Creature? creature, string powerId) =>
        creature is { IsAlive: true }
            ? creature.Powers.FirstOrDefault(power => IsCombatWarningPower(power, powerId))
            : null;

    private static bool IsCombatWarningPower(PowerModel power, string powerId) =>
        power.Amount > 0 && power.Id.Entry == powerId && power.IsVisible;
}
