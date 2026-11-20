using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void CollectPlayerCombatWarnings(ICombatState combat, Creature player, List<CombatWarning> warnings)
    {
        if (!player.IsAlive)
            return;

        foreach (var power in player.Powers)
        {
            if (!power.IsVisible || power.Amount <= 0)
                continue;

            string id = power.Id.Entry;
            (string Name, string Message)? guidance = id switch
            {
                "RINGING_POWER" => ("Ringing", GetRingingWarning(combat, player)),
                "SMOGGY_POWER" => ("Smoggy", "You can play one Skill per turn. Plan which Skill to use."),
                "SLOTH_POWER" => ("Sloth", combat.CurrentSide == CombatSide.Player
                    ? $"Your per-turn card limit is {power.Amount}. You have {Math.Max(0, power.Amount - power.DisplayAmount)} plays remaining this turn."
                    : $"You can play at most {power.Amount} cards per turn."),
                "CHAINS_OF_BINDING_POWER" => ("Chains of Binding",
                    $"The first {power.Amount} cards drawn on your turn can become Bound. You can play one Bound card per turn. Bound clears at the end of your turn."),
                "HEX_POWER" => ("Hex", "Your cards are Ethereal while Spectral Knight is alive. Ethereal cards left in your hand exhaust at the end of your turn."),
                "SURROUNDED_POWER" => ("Surrounded",
                    "Attacks from behind deal 50% more damage. Target an enemy with a card or potion to face it."),
                "TENDER_POWER" => ("Tender",
                    "After each card you play, lose 1 Strength and 1 Dexterity for the rest of this turn."),
                "TANGLED_POWER" => ("Tangled", $"Entangled Attacks cost {power.Amount} extra energy this turn."),
                "ENCORE_POWER" => ("Encore", $"You have {power.DisplayAmount} more card plays this turn."),
                _ => null
            };

            if (guidance is { } planning)
                warnings.Add(new(id, planning.Name, "player", planning.Message, Amount: power.Amount));
        }
    }

    private static string GetRingingWarning(ICombatState combat, Creature player)
    {
        if (combat.CurrentSide == CombatSide.Player && CombatManager.Instance.History.CardPlaysStarted.Any(entry =>
                ReferenceEquals(entry.CardPlay.Player, player.Player) && entry.HappenedThisTurn(combat)))
            return "Ringing allows one card this turn. You have already used that play.";

        return "Ringing limits the affected turn to one card. Choose that card before playing.";
    }
}
