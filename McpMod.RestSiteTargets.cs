using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private static readonly MethodInfo? NativeRestSiteSelect = AccessTools.DeclaredMethod(
        typeof(NRestSiteButton), "SelectOption", [typeof(RestSiteOption)]);

    private static NRestSiteCharacter? _mendTarget;

    private static int[] GetMendTargetSlots(RunState run) =>
        Enumerable.Range(0, run.Players.Count)
            .Where(slot => !LocalContext.IsMe(run.Players[slot]))
            .ToArray();

    private static Dictionary<string, object?> SubmitMend(NRestSiteRoom room, NRestSiteButton button,
        MendRestSiteOption mend, Dictionary<string, JsonElement> data,
        out Task<Dictionary<string, object?>>? completion)
    {
        completion = null;
        var runManager = RunManager.Instance;
        if (!IsMultiplayerRun() || runManager.DebugOnlyGetState() is not { CurrentRoom: RestSiteRoom } run ||
            LocalContext.GetMe(run) is not { } localPlayer)
            return Error("Mend requires the multiplayer rest site.");
        if (!data.TryGetValue("target_player_slot", out var input))
            return BlockedAction(new("rest_target_required",
                "Mend requires target_player_slot from the rest option's target_player_slots.", false));
        if (input.ValueKind != JsonValueKind.Number || !input.TryGetInt32(out int slot) ||
            slot < 0 || slot >= run.Players.Count || ReferenceEquals(run.Players[slot], localPlayer))
            return Error("Select a teammate from the rest option's target_player_slots.");
        if (!button.IsEnabled || IsMapScreenOpenOrVisible() || NTargetManager.Instance?.IsInSelection != false)
            return BlockedAction(new("rest_target_not_ready", "Wait for the rest site and close any active target picker.", true));
        if (room.GetCharacterForPlayer(run.Players[slot]) is not { } target || !IsLiveNode(target))
            return BlockedAction(new("rest_target_not_ready", "The teammate is still loading at the rest site.", true));
        if (NativeRestSiteSelect == null ||
            Harmony.GetPatchInfo(MendStartTargetingPatch.TargetMethod())?.Owners.Contains("com.sts2mcp") != true ||
            Harmony.GetPatchInfo(MendSelectionFinishedPatch.TargetMethod())?.Owners.Contains("com.sts2mcp") != true)
            return Error("Programmatic Mend targeting is unavailable on this game build.");

        completion = CompleteMend(button, mend, target, slot, localPlayer.NetId);
        return new Dictionary<string, object?> { ["status"] = "pending" };
    }

    private static async Task<Dictionary<string, object?>> CompleteMend(NRestSiteButton button,
        MendRestSiteOption mend, NRestSiteCharacter target, int slot, ulong localPlayerId)
    {
        var synchronizer = RunManager.Instance.RestSiteSynchronizer;
        bool healed = false;
        void OnRestChoice(RestSiteOption option, bool successful, ulong playerId)
        {
            if (ReferenceEquals(option, mend) && playerId == localPlayerId)
                healed = successful;
        }

        synchronizer.AfterPlayerOptionChosen += OnRestChoice;
        try
        {
            // The native button starts the choice synchronously before awaiting its result.
            Task selection;
            _mendTarget = target;
            try
            {
                selection = (Task)NativeRestSiteSelect!.Invoke(button, [mend])!;
            }
            finally
            {
                _mendTarget = null;
            }
            await selection;
        }
        finally
        {
            synchronizer.AfterPlayerOptionChosen -= OnRestChoice;
        }

        if (!healed)
            return Error("Mend did not complete. Check the rest site before choosing again.");
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Mend completed.",
            ["target_player_slot"] = slot
        };
    }

    [HarmonyPatch]
    private static class MendStartTargetingPatch
    {
        internal static MethodBase TargetMethod() => AccessTools.DeclaredMethod(typeof(NTargetManager),
            nameof(NTargetManager.StartTargeting),
            [typeof(TargetType), typeof(Vector2), typeof(TargetMode), typeof(Func<bool>), typeof(Func<Node, bool>)]);

        private static bool Prefix() => _mendTarget == null;
    }

    [HarmonyPatch]
    private static class MendSelectionFinishedPatch
    {
        internal static MethodBase TargetMethod() =>
            AccessTools.DeclaredMethod(typeof(NTargetManager), nameof(NTargetManager.SelectionFinished));

        private static bool Prefix(ref Task<Node> __result)
        {
            if (_mendTarget == null)
                return true;
            __result = Task.FromResult<Node>(_mendTarget);
            return false;
        }
    }
}
