using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;

namespace STS2_MCP;

public static partial class McpMod
{
    private static NFastModeTickbox? _instantModeTickbox;
    private static NFastModeTickbox? _originalFastModeTickbox;

    private static bool IsInInstantModeLine(Node node)
    {
        var current = node;
        while (current != null)
        {
            if (current.Name == "InstantMode") return true;
            current = current.GetParent();
        }
        return false;
    }

    private static T? FindChildOfType<T>(Node parent) where T : class
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is T typed) return typed;
            if (child is Node childNode)
            {
                var found = FindChildOfType<T>(childNode);
                if (found != null) return found;
            }
        }
        return null;
    }

    private static void InjectInstantModeCheckbox(NSettingsScreen settingsScreen)
    {
        _instantModeTickbox = null;
        _originalFastModeTickbox = null;

        var content = settingsScreen.GetNode<NSettingsPanel>("%GeneralSettings").Content;
        var fastModeLine = content.GetNodeOrNull("FastMode");
        if (fastModeLine == null)
        {
            GD.PrintErr("[STS2 MCP] Could not find FastMode settings line");
            return;
        }

        _originalFastModeTickbox = FindChildOfType<NFastModeTickbox>(fastModeLine);

        // Duplicate the entire FastMode line and rename
        var instantLine = (Node)fastModeLine.Duplicate();
        instantLine.Name = "InstantMode";

        // Insert right after FastMode in the VBoxContainer
        int idx = fastModeLine.GetIndex();
        content.AddChild(instantLine);
        content.MoveChild(instantLine, idx + 1);

        // Update label text
        var label = instantLine.GetNodeOrNull<MegaRichTextLabel>("Label");
        if (label != null) label.Text = "Instant Mode";

        // Store reference and correct initial tick state
        // (NFastModeTickbox._Ready() already ran via AddChild, but used original SetFromSettings logic)
        _instantModeTickbox = FindChildOfType<NFastModeTickbox>(instantLine);
        if (_instantModeTickbox != null)
            _instantModeTickbox.IsTicked = SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant;

        GD.Print("[STS2 MCP] Instant Mode checkbox injected into settings");
    }

    // --- Harmony Patches ---

    [HarmonyPatch(typeof(NSettingsScreen), "_Ready")]
    static class SettingsScreenReadyPatch
    {
        static void Postfix(NSettingsScreen __instance)
        {
            try { InjectInstantModeCheckbox(__instance); }
            catch (Exception ex) { GD.PrintErr($"[STS2 MCP] Settings UI injection failed: {ex}"); }
        }
    }

    [HarmonyPatch(typeof(NFastModeTickbox), nameof(NFastModeTickbox.SetFromSettings))]
    static class FastModeSetFromSettingsPatch
    {
        static bool Prefix(NFastModeTickbox __instance)
        {
            if (!IsInInstantModeLine(__instance)) return true;
            __instance.IsTicked = SaveManager.Instance.PrefsSave.FastMode == FastModeType.Instant;
            return false;
        }
    }

    [HarmonyPatch(typeof(NFastModeTickbox), "OnTick")]
    static class FastModeOnTickPatch
    {
        static bool Prefix(NFastModeTickbox __instance)
        {
            if (!IsInInstantModeLine(__instance)) return true;
            // Instant Mode turned on
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant;
            // Ensure the original Fast Mode checkbox also shows ticked
            if (_originalFastModeTickbox != null && !_originalFastModeTickbox.IsTicked)
                _originalFastModeTickbox.IsTicked = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(NFastModeTickbox), "OnUntick")]
    static class FastModeOnUntickPatch
    {
        static bool Prefix(NFastModeTickbox __instance)
        {
            if (!IsInInstantModeLine(__instance)) return true;
            // Instant Mode turned off - fall back to Fast
            SaveManager.Instance.PrefsSave.FastMode = FastModeType.Fast;
            return false;
        }

        static void Postfix(NFastModeTickbox __instance)
        {
            // Only for the ORIGINAL Fast Mode tickbox being unticked (→ Normal)
            if (IsInInstantModeLine(__instance)) return;
            if (_instantModeTickbox != null && _instantModeTickbox.IsTicked)
                _instantModeTickbox.IsTicked = false;
        }
    }

    [HarmonyPatch(typeof(NFastModeHoverTip), "_Ready")]
    static class InstantModeHoverTipPatch
    {
        static void Postfix(NFastModeHoverTip __instance)
        {
            if (!IsInInstantModeLine(__instance)) return;

            // Build a HoverTip with custom strings via reflection
            // (HoverTip is a record struct; Title/Description have private setters)
            object boxed = (object)default(HoverTip);
            var ht = typeof(HoverTip);
            ht.GetProperty("Id")!.SetValue(boxed, "sts2mcp_instant_mode");
            ht.GetProperty("Title")!.GetSetMethod(true)!
                .Invoke(boxed, ["Instant Mode"]);
            ht.GetProperty("Description")!.GetSetMethod(true)!
                .Invoke(boxed, [
                    "This option is added by the STS2MCP mod.\n" +
                    "When enabled, most in-game animations will become instant.\n" +
                    "Can reduce token usage as get-state calls will no longer receive an intermediate state."
                ]);

            // Replace the _hoverTip field set by the original _Ready
            typeof(NFastModeHoverTip)
                .GetField("_hoverTip", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(__instance, (IHoverTip)boxed);
        }
    }
}
