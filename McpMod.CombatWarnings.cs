using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace STS2_MCP;

public static partial class McpMod
{
    private const string SandpitPowerId = "SANDPIT_POWER";
    private const string ThornsPowerId = "THORNS_POWER";

    private readonly record struct CombatWarningKey(string Side, string? EntityId, string EffectId);

    private sealed record CombatWarning(string EffectId, string Name, string Side, string Message,
        decimal? Amount = null, bool InterruptSequence = false, string? EntityId = null)
    {
        internal CombatWarningKey Key => new(Side, EntityId, EffectId);
    }

    private static readonly ConditionalWeakTable<ICombatState, HashSet<CombatWarningKey>> ReportedWarningsByEncounter = new();

    private static HashSet<CombatWarningKey> CaptureInterruptingCombatWarnings(ICombatState combat, Creature player) =>
        GetActiveCombatWarnings(combat, player).Where(warning => warning.InterruptSequence)
            .Select(warning => warning.Key).ToHashSet();

    private static CombatWarning? FindNewInterruptingCombatWarning(ICombatState combat, Creature player,
        HashSet<CombatWarningKey> startingWarnings) =>
        GetActiveCombatWarnings(combat, player).FirstOrDefault(warning =>
            warning.InterruptSequence && !startingWarnings.Contains(warning.Key));

    // Attach warnings after the full response state has been built, never during a readiness probe.
    private static void AddCombatWarnings(Dictionary<string, object?> state, Creature? player)
    {
        if (player is not { IsAlive: true }
            || state.GetValueOrDefault("battle") is not Dictionary<string, object?> battle
            || CombatManager.Instance.DebugOnlyGetState() is not { } combatState)
            return;

        var active = GetActiveCombatWarnings(combatState, player);
        if (active.Count == 0)
            return;

        var reported = ReportedWarningsByEncounter.GetOrCreateValue(combatState);
        var warnings = new List<Dictionary<string, object?>>();
        foreach (var warning in active.OrderByDescending(candidate => candidate.InterruptSequence))
        {
            if (!reported.Add(warning.Key))
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
        if (warnings.Count > 0)
            battle["warnings"] = warnings;
    }

    private static List<CombatWarning> GetActiveCombatWarnings(ICombatState combatState, Creature player)
    {
        var warnings = new List<CombatWarning>();
        if (FindCombatWarningPower(player, SandpitPowerId) is { } sandpit)
            warnings.Add(SandpitWarning(sandpit));

        CollectEnemyCombatWarnings(combatState, player, warnings);
        CollectPlayerCombatWarnings(combatState, player, warnings);
        CollectLethalCombatWarnings(combatState, player, warnings);
        return warnings;
    }

    private static CombatWarning SandpitWarning(PowerModel sandpit, string? entityId = null)
    {
        string countdown = sandpit.Amount == 1
            ? "The Insatiable will eat you when it takes its next turn."
            : $"The Insatiable will eat you in {sandpit.Amount} turns.";
        return new(SandpitPowerId, "Sandpit", "player",
            countdown + " Defeat it before the countdown expires or play Frantic Escape to buy one turn.",
            sandpit.Amount, InterruptSequence: true, EntityId: entityId);
    }

    private static PowerModel? FindCombatWarningPower(Creature? creature, string powerId) =>
        creature is { IsAlive: true }
            ? creature.Powers.FirstOrDefault(power => IsCombatWarningPower(power, powerId))
            : null;

    private static bool IsCombatWarningPower(PowerModel power, string powerId) =>
        power.Amount > 0 && power.Id.Entry == powerId && power.IsVisible;
}
