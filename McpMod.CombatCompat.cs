using MegaCrit.Sts2.Core.Combat;

namespace STS2_MCP;

public static partial class McpMod
{
    private static bool IsPlayPhase()
    {
        return IsPlayPhase(CombatManager.Instance.DebugOnlyGetState());
    }

    private static bool IsPlayPhase(CombatState? combatState)
    {
        var manager = CombatManager.Instance;
        return manager.IsInProgress
            && combatState?.CurrentSide == CombatSide.Player
            && !manager.PlayerActionsDisabled
            && !manager.IsEnemyTurnStarted
            && !manager.EndingPlayerTurnPhaseOne
            && !manager.EndingPlayerTurnPhaseTwo;
    }
}
