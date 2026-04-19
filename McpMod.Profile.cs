using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ProfileScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace STS2_MCP;

public static partial class McpMod
{
    private static void HandleGetProfile(HttpListenerResponse response)
    {
        try
        {
            var dataTask = RunOnMainThread(BuildProfile);
            SendJson(response, dataTask.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            SendError(response, 500, $"Failed to build profile: {ex.Message}");
        }
    }

    private static void HandleGetProfiles(HttpListenerResponse response)
    {
        try
        {
            var dataTask = RunOnMainThread(BuildProfilesSummary);
            SendJson(response, dataTask.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            SendError(response, 500, $"Failed to get profiles: {ex.Message}");
        }
    }

    private static void HandlePostProfiles(HttpListenerRequest request, HttpListenerResponse response)
    {
        string body;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            body = reader.ReadToEnd();

        Dictionary<string, JsonElement>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
        }
        catch
        {
            SendError(response, 400, "Invalid JSON");
            return;
        }

        if (parsed == null || !parsed.TryGetValue("action", out var actionElem))
        {
            SendError(response, 400, "Missing 'action' field. Use: switch, delete");
            return;
        }

        string action = actionElem.GetString() ?? "";
        int profileId = parsed.TryGetValue("profile_id", out var idElem) && idElem.ValueKind == JsonValueKind.Number
            ? idElem.GetInt32()
            : 0;

        try
        {
            var resultTask = RunOnMainThread(() => ExecuteProfileAction(action, profileId));
            SendJson(response, resultTask.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            SendError(response, 500, $"Profile action failed: {ex.Message}");
        }
    }

    private static Dictionary<string, object?> BuildProfilesSummary()
    {
        var sm = SaveManager.Instance;
        if (sm == null)
            return Error("Save manager is not available");

        var profiles = new List<Dictionary<string, object?>>();
        for (int i = 1; i <= 3; i++)
        {
            var profileData = new Dictionary<string, object?>
            {
                ["id"] = i,
                ["is_current"] = i == sm.CurrentProfileId,
            };

            try
            {
                var path = ProgressSaveManager.GetProgressPathForProfile(i);
                profileData["has_data"] = File.Exists(path);
                profileData["path"] = path;
            }
            catch
            {
                profileData["has_data"] = false;
            }

            profiles.Add(profileData);
        }

        return new Dictionary<string, object?>
        {
            ["current_profile_id"] = sm.CurrentProfileId,
            ["profiles"] = profiles
        };
    }

    private static Dictionary<string, object?> ExecuteProfileAction(string action, int profileId)
    {
        var sm = SaveManager.Instance;
        if (sm == null)
            return Error("Save manager is not available");
        if (profileId is < 1 or > 3)
            return Error("profile_id must be 1-3");

        var normalizedAction = action.Trim().ToLowerInvariant();

        if (normalizedAction == "switch")
        {
            if (RunManager.Instance?.IsInProgress == true)
                return Error("Cannot switch profiles during a run");

            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root != null)
            {
                if (TrySwitchProfileViaOpenScreen(tree.Root, profileId))
                {
                    return new Dictionary<string, object?>
                    {
                        ["status"] = "ok",
                        ["message"] = $"Switch requested for profile {profileId} (via UI)",
                        ["target_profile_id"] = profileId,
                        ["current_profile_id"] = sm.CurrentProfileId
                    };
                }

                var mainMenu = FindFirst<NMainMenu>(tree.Root);
                if (mainMenu != null && IsNodeVisible(mainMenu))
                {
                    mainMenu.OpenProfileScreen();
                    return new Dictionary<string, object?>
                    {
                        ["status"] = "ok",
                        ["message"] = "Opened profile screen. Send switch again to select profile."
                    };
                }
            }

            sm.SwitchProfileId(profileId);
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Switched to profile {profileId} (requires restart)",
                ["current_profile_id"] = sm.CurrentProfileId
            };
        }

        if (normalizedAction == "delete")
        {
            if (profileId == sm.CurrentProfileId)
                return Error("Cannot delete the active profile");

            sm.DeleteProfile(profileId);
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Deleted profile {profileId}"
            };
        }

        return Error($"Unknown action: {action}. Use: switch, delete");
    }

    private static bool TrySwitchProfileViaOpenScreen(Node root, int profileId)
    {
        var profileScreen = FindFirst<NProfileScreen>(root);
        if (profileScreen == null || !IsNodeVisible(profileScreen))
            return false;

        var buttons = GetInstanceFieldValue(profileScreen, "_profileButtons") as System.Collections.IEnumerable;
        if (buttons == null)
            return false;

        foreach (var btn in buttons)
        {
            var btnId = GetInstanceFieldValue(btn, "_profileId");
            if (btnId is not int id || id != profileId)
                continue;

            var switchMethod = btn.GetType().GetMethod(
                "SwitchToThisProfile",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            switchMethod?.Invoke(btn, null);
            return switchMethod != null;
        }

        return false;
    }

    internal static object BuildProfile()
    {
        var progress = SaveManager.Instance?.Progress;
        if (progress == null)
            return new Dictionary<string, object?> { ["error"] = "No profile data available." };

        var result = new Dictionary<string, object?>();

        var characters = new List<Dictionary<string, object?>>();
        foreach (var kv in progress.CharacterStats)
        {
            var stats = kv.Value;
            characters.Add(new Dictionary<string, object?>
            {
                ["id"] = kv.Key.Entry,
                ["max_ascension"] = stats.MaxAscension,
                ["preferred_ascension"] = stats.PreferredAscension,
                ["total_wins"] = stats.TotalWins,
                ["total_losses"] = stats.TotalLosses,
                ["fastest_win_time"] = stats.FastestWinTime,
                ["best_win_streak"] = stats.BestWinStreak,
                ["current_win_streak"] = stats.CurrentWinStreak,
                ["playtime"] = stats.Playtime
            });
        }
        result["characters"] = characters;

        var cards = new List<Dictionary<string, object?>>();
        foreach (var kv in progress.CardStats)
        {
            var stats = kv.Value;
            cards.Add(new Dictionary<string, object?>
            {
                ["id"] = kv.Key.Entry,
                ["times_picked"] = stats.TimesPicked,
                ["times_skipped"] = stats.TimesSkipped,
                ["times_won"] = stats.TimesWon,
                ["times_lost"] = stats.TimesLost
            });
        }
        result["card_stats"] = cards;

        var encounters = new List<Dictionary<string, object?>>();
        foreach (var kv in progress.EncounterStats)
        {
            var enc = new Dictionary<string, object?>
            {
                ["id"] = kv.Key.Entry,
                ["total_wins"] = kv.Value.TotalWins,
                ["total_losses"] = kv.Value.TotalLosses
            };
            var fightStats = new List<Dictionary<string, object?>>();
            foreach (var fs in kv.Value.FightStats)
            {
                fightStats.Add(new Dictionary<string, object?>
                {
                    ["character"] = fs.Character.Entry,
                    ["wins"] = fs.Wins,
                    ["losses"] = fs.Losses
                });
            }
            if (fightStats.Count > 0)
                enc["by_character"] = fightStats;
            encounters.Add(enc);
        }
        result["encounter_stats"] = encounters;

        var enemies = new List<Dictionary<string, object?>>();
        foreach (var kv in progress.EnemyStats)
        {
            var enemy = new Dictionary<string, object?>
            {
                ["id"] = kv.Key.Entry,
                ["total_wins"] = kv.Value.TotalWins,
                ["total_losses"] = kv.Value.TotalLosses
            };
            var fightStats = new List<Dictionary<string, object?>>();
            foreach (var fs in kv.Value.FightStats)
            {
                fightStats.Add(new Dictionary<string, object?>
                {
                    ["character"] = fs.Character.Entry,
                    ["wins"] = fs.Wins,
                    ["losses"] = fs.Losses
                });
            }
            if (fightStats.Count > 0)
                enemy["by_character"] = fightStats;
            enemies.Add(enemy);
        }
        result["enemy_stats"] = enemies;

        var ancients = new List<Dictionary<string, object?>>();
        foreach (var kv in progress.AncientStats)
        {
            var anc = new Dictionary<string, object?>
            {
                ["id"] = kv.Key.Entry,
                ["total_visits"] = kv.Value.TotalVisits,
                ["total_wins"] = kv.Value.TotalWins,
                ["total_losses"] = kv.Value.TotalLosses
            };
            var charStats = new List<Dictionary<string, object?>>();
            foreach (var cs in kv.Value.CharStats)
            {
                charStats.Add(new Dictionary<string, object?>
                {
                    ["character"] = cs.Character.Entry,
                    ["wins"] = cs.Wins,
                    ["losses"] = cs.Losses
                });
            }
            if (charStats.Count > 0)
                anc["by_character"] = charStats;
            ancients.Add(anc);
        }
        result["ancient_stats"] = ancients;

        result["discovered_cards"] = progress.DiscoveredCards.Select(id => id.Entry).ToList();
        result["discovered_relics"] = progress.DiscoveredRelics.Select(id => id.Entry).ToList();
        result["discovered_potions"] = progress.DiscoveredPotions.Select(id => id.Entry).ToList();
        result["discovered_events"] = progress.DiscoveredEvents.Select(id => id.Entry).ToList();
        result["discovered_acts"] = progress.DiscoveredActs.Select(id => id.Entry).ToList();

        var achievements = new List<Dictionary<string, object?>>();
        foreach (var kv in progress.UnlockedAchievements)
        {
            achievements.Add(new Dictionary<string, object?>
            {
                ["id"] = kv.Key,
                ["unlocked_at"] = kv.Value
            });
        }
        result["achievements"] = achievements;

        result["epochs"] = progress.Epochs.Select(e => new Dictionary<string, object?>
        {
            ["id"] = e.Id,
            ["state"] = e.State.ToString(),
            ["obtained"] = e.ObtainDate
        }).ToList();

        result["total_playtime"] = progress.TotalPlaytime;
        result["total_unlocks"] = progress.TotalUnlocks;
        result["current_score"] = progress.CurrentScore;
        result["floors_climbed"] = progress.FloorsClimbed;
        result["architect_damage"] = progress.ArchitectDamage;
        result["total_wins"] = progress.Wins;
        result["total_losses"] = progress.Losses;
        result["fastest_victory"] = progress.FastestVictory;
        result["best_win_streak"] = progress.BestWinStreak;
        result["number_of_runs"] = progress.NumberOfRuns;

        return result;
    }
}
