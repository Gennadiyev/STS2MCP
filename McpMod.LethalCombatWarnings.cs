using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void CollectLethalCombatWarnings(ICombatState combat, Creature player, List<CombatWarning> warnings)
    {
        if (!player.IsAlive)
            return;

        int hp = player.CurrentHp;
        if (FindCombatWarningPower(player, "DOOM_POWER") is { } doom && doom.Amount >= hp)
        {
            warnings.Add(new CombatWarning("DOOM_POWER", "Doom", "player",
                $"Ending your turn with {hp} HP and {doom.Amount} Doom can kill you through Block. Raise your HP or remove Doom before ending your turn.",
                doom.Amount, InterruptSequence: true));
        }

        if (FindCombatWarningPower(player, "POISON_POWER") is PoisonPower poison)
        {
            int nextTick = poison.CalculateTotalDamageNextTurn();
            if (nextTick >= hp)
            {
                warnings.Add(new CombatWarning("POISON_POWER", "Poison", "player",
                    $"Poison is projected to deal {nextTick} damage through Block at the start of your next turn. That can be lethal at your current {hp} HP.",
                    nextTick, InterruptSequence: true));
            }
        }

        if (FindCombatWarningPower(player, "THE_GAMBIT_POWER") != null)
        {
            warnings.Add(new CombatWarning("THE_GAMBIT_POWER", "The Gambit", "player",
                "Any unblocked Attack damage is fatal while The Gambit is active. Fully block incoming Attacks or defeat the attacker before it acts.",
                InterruptSequence: true));
        }

        // Current Block is relevant to these effects when it is the player's side's turn.
        if (combat.CurrentSide != player.Side)
            return;

        var hand = player.Player?.PlayerCombatState?.Hand.Cards;
        int witherDamage = hand?.Where(card => card.Id.Entry == "WITHER")
            .Sum(card => card.DynamicVars.Damage.IntValue) ?? 0;
        (string EffectId, string Name, int Damage)[] ticks =
        [
            ("CONSTRICT_POWER", "Constrict", FindCombatWarningPower(player, "CONSTRICT_POWER")?.Amount ?? 0),
            ("DISINTEGRATION_POWER", "Disintegration", FindCombatWarningPower(player, "DISINTEGRATION_POWER")?.Amount ?? 0),
            ("INSATIABLE_DOMINION_POWER", "Insatiable Dominion", FindCombatWarningPower(player, "INSATIABLE_DOMINION_POWER")?.DisplayAmount ?? 0),
            ("WITHER", "Wither cards in hand", witherDamage)
        ];
        var pending = ticks.Where(tick => tick.Damage > 0).ToArray();
        decimal totalDamage = pending.Sum(tick => (decimal)tick.Damage);
        int block = player.Block;
        if (totalDamage < (decimal)hp + block)
            return;

        string sources = string.Join(", ", pending.Select(tick => tick.Name));
        var (effectId, name) = pending.Length == 1
            ? (pending[0].EffectId, pending[0].Name)
            : ("END_TURN_DAMAGE", "End-turn damage");
        warnings.Add(new CombatWarning(effectId, name, "player",
            $"Ending your turn at {hp} HP and {block} Block could be lethal. Pending end-turn damage from {sources} totals {totalDamage}.",
            totalDamage, InterruptSequence: true));
    }
}
