using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace STS2_MCP;

public static partial class McpMod
{
    private const string StolenCardRewardTip =
        "Claim the card from rewards if you want it back. Leave a Curse or another unwanted card unclaimed to keep it out of your deck.";

    private static void CollectEnemyCombatWarnings(ICombatState combat, Creature player, List<CombatWarning> warnings)
    {
        foreach (var (enemy, entityId) in GetLivingEnemies(combat))
        {
            foreach (var power in enemy.Powers)
            {
                if (power.Amount > 0 && power.IsVisible &&
                    EnemyWarningForPower(power, player, entityId) is { } warning)
                    warnings.Add(warning);
            }
        }
    }

    private static CombatWarning? EnemyWarningForPower(PowerModel power, Creature player, string entityId)
    {
        string id = power.Id.Entry;
        int amount = power.Amount;
        return id switch
        {
            "STEAM_ERUPTION_POWER" => new(id, "Steam Eruption", "enemy",
                $"Lethal damage triggers a final Death Blow at the end of your next turn. It currently deals {amount} damage and can grow. Check the updated intent and save Block for it.",
                amount, InterruptSequence: true, EntityId: entityId),
            "GALVANIC_POWER" => new(id, "Galvanic", "enemy",
                $"Power cards are afflicted with Galvanized. Playing a Galvanized card deals {amount} damage to the player who played it. Budget Block before playing it.",
                amount, InterruptSequence: true, EntityId: entityId),
            "PAPER_CUTS_POWER" => new(id, "Paper Cuts", "enemy",
                $"Each unblocked Attack hit from this enemy removes {amount} Max HP. Fully block its attacks, including multi-hit attacks, or defeat it first.",
                amount, EntityId: entityId),
            "CRAB_RAGE_POWER" => new(id, "Crab Rage", "enemy",
                $"When an ally dies, the surviving enemy gains {power.DynamicVars.Strength.IntValue} Strength and {power.DynamicVars.Block.IntValue} Block. Recheck its Block and intent before committing follow-up attacks.",
                EntityId: entityId),
            "ADAPTABLE_POWER" => new(id, "Adaptable", "enemy",
                "This enemy can revive stronger after lethal damage. Combat continues until its final phase is defeated, even if the enemy list is briefly empty.",
                EntityId: entityId),
            "REATTACH_POWER" => new(id, "Reattach", "enemy",
                $"A defeated segment revives in two turns with {amount} HP if another segment is still alive. Defeat all segments before they reattach.",
                amount, EntityId: entityId),
            "ILLUSION_POWER" => new(id, "Illusion", "enemy",
                "This enemy revives next turn at full HP. Plan to defeat the remaining encounter before it returns.",
                EntityId: entityId),
            "SWIPE_POWER" when power is SwipePower swipe && ReferenceEquals(power.Target, player) =>
                EnemyWarningForSwipe(swipe, entityId),
            "ESCAPE_ARTIST_POWER" => new(id, "Escape Artist", "enemy",
                (amount == 1 ? "This enemy tries to escape this turn." : $"This enemy tries to escape in {amount} turns.") +
                " Defeat it before it leaves to make any stolen card available as a reward. " + StolenCardRewardTip,
                amount, EntityId: entityId),
            "THIEVERY_POWER" when ReferenceEquals(power.Target, player) && player.Player is { Gold: > 0 } localPlayer =>
                EnemyWarningForThievery(id, Math.Min(amount, localPlayer.Gold), entityId),
            "HEIST_POWER" when ReferenceEquals(power.Target, player) => new(id, "Heist", "enemy",
                $"This enemy is carrying {amount} of your stolen gold. Defeat it before it escapes, then claim the gold reward.",
                amount, EntityId: entityId),
            "HARDENED_SHELL_POWER" => new(id, "Hardened Shell", "enemy",
                $"This enemy can lose at most {amount} HP per turn and can lose {power.DisplayAmount} more this turn. Save damage for later once it reaches the cap.",
                power.DisplayAmount, EntityId: entityId),
            "HARD_TO_KILL_POWER" => new(id, "Hard to Kill", "enemy",
                $"Each hit or other instance of HP loss is capped at {amount} against this enemy. Multiple hits can get more damage through.",
                amount, EntityId: entityId),
            "SLIPPERY_POWER" => new(id, "Slippery", "enemy",
                amount == 1
                    ? "This enemy's next HP loss is capped at 1. Use a small hit before spending a large attack."
                    : $"This enemy's next {amount} instances of HP loss are capped at 1 each. Use small hits before spending a large attack.",
                amount, EntityId: entityId),
            "ENRAGE_POWER" => new(id, "Enrage", "enemy",
                $"Whenever a player plays a Skill, this enemy gains {amount} Strength. Check its updated Attack intent after playing Skills.",
                amount, EntityId: entityId),
            "EXPLOSIVE_POWER" => new(id, "Explosive", "enemy",
                $"This enemy deals {amount} damage to all living players when it dies. Build Block before lethal and account for each explosion when defeating multiple enemies.",
                amount, InterruptSequence: true, EntityId: entityId),
            _ => null
        };
    }

    private static CombatWarning EnemyWarningForSwipe(SwipePower power, string entityId)
    {
        var title = SafeGetText(() => power.StolenCard?.Title);
        string card = string.IsNullOrWhiteSpace(title) ? "a card from your deck" : $"\"{title}\" from your deck";
        return new(power.Id.Entry, "Swipe", "enemy",
            $"This enemy stole {card}. Defeat it before it escapes to make the card available as a reward. {StolenCardRewardTip}",
            EntityId: entityId);
    }

    private static CombatWarning EnemyWarningForThievery(string id, int gold, string entityId) =>
        new(id, "Thievery", "enemy",
            $"This enemy can steal {gold} of your gold with its stealing moves, even if you fully block the attack. Defeat the carrier with Heist before it escapes, then claim the stolen gold from rewards.",
            gold, EntityId: entityId);
}
