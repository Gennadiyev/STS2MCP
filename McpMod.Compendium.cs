using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace STS2_MCP;

public static partial class McpMod
{
    private static readonly object _runHistoryMembersLock = new();
    private static Type? _runHistoryMembersType;
    private static MemberInfo[]? _runHistoryMembers;

    private static void HandleGetCompendium(HttpListenerResponse response)
    {
        try
        {
            var snapshotTask = RunOnMainThread(BuildCompendiumSnapshot);
            var snapshot = snapshotTask.GetAwaiter().GetResult();
            SendJson(response, BuildCompendiumResponse(snapshot));
        }
        catch (Exception ex)
        {
            SendError(response, 500, $"Failed to build compendium: {ex.Message}");
        }
    }

    internal static object BuildCompendium()
    {
        return BuildCompendiumResponse(BuildCompendiumSnapshot());
    }

    private static CompendiumSnapshot BuildCompendiumSnapshot()
    {
        var progress = SaveManager.Instance?.Progress;
        var saveManager = SaveManager.Instance;
        if (progress == null || saveManager == null)
            return new CompendiumSnapshot { Error = "No profile data available." };

        var profileId = saveManager.CurrentProfileId;
        var progressPath = GetProfileProgressPath(profileId);
        var resolvedProgressPath = ResolveProfileProgressPath(profileId);
        var profileRoot = GetProfileRootFromProgressPath(progressPath, profileId);

        var cardStats = progress.CardStats.Select(kv => new Dictionary<string, object?>
        {
            ["id"] = kv.Key.Entry,
            ["times_picked"] = kv.Value.TimesPicked,
            ["times_skipped"] = kv.Value.TimesSkipped,
            ["times_won"] = kv.Value.TimesWon,
            ["times_lost"] = kv.Value.TimesLost
        }).ToList();

        var characterStats = progress.CharacterStats.Select(kv => new Dictionary<string, object?>
        {
            ["id"] = kv.Key.Entry,
            ["max_ascension"] = kv.Value.MaxAscension,
            ["preferred_ascension"] = kv.Value.PreferredAscension,
            ["total_wins"] = kv.Value.TotalWins,
            ["total_losses"] = kv.Value.TotalLosses,
            ["fastest_win_time"] = kv.Value.FastestWinTime,
            ["best_win_streak"] = kv.Value.BestWinStreak,
            ["current_win_streak"] = kv.Value.CurrentWinStreak,
            ["playtime"] = kv.Value.Playtime
        }).ToList();

        return new CompendiumSnapshot
        {
            ProfileId = profileId,
            ProgressPath = progressPath,
            ResolvedProgressPath = resolvedProgressPath,
            ProfileRoot = profileRoot,
            SaveScope = GetSaveScope(profileRoot),
            IsRunInProgress = RunManager.Instance?.IsInProgress == true,
            DiscoveredCards = progress.DiscoveredCards.Select(id => id.Entry).ToList(),
            DiscoveredRelics = progress.DiscoveredRelics.Select(id => id.Entry).ToList(),
            DiscoveredPotions = progress.DiscoveredPotions.Select(id => id.Entry).ToList(),
            CardStats = cardStats,
            CharacterStats = characterStats,
            EncounterStats = BuildEncounterStats(progress),
            EnemyStats = BuildEnemyStats(progress),
            RunHistoryProgressMembers = BuildRunHistoryProgressMembers(progress),
            GlobalStats = new Dictionary<string, object?>
            {
                ["total_playtime"] = progress.TotalPlaytime,
                ["total_unlocks"] = progress.TotalUnlocks,
                ["current_score"] = progress.CurrentScore,
                ["floors_climbed"] = progress.FloorsClimbed,
                ["architect_damage"] = progress.ArchitectDamage,
                ["total_wins"] = progress.Wins,
                ["total_losses"] = progress.Losses,
                ["fastest_victory"] = progress.FastestVictory,
                ["best_win_streak"] = progress.BestWinStreak,
                ["number_of_runs"] = progress.NumberOfRuns
            }
        };
    }

    private static object BuildCompendiumResponse(CompendiumSnapshot snapshot)
    {
        if (snapshot.Error != null)
            return Error(snapshot.Error);

        var runHistory = BuildRunHistorySection(snapshot);

        return new Dictionary<string, object?>
        {
            ["profile_id"] = snapshot.ProfileId,
            ["progress_path"] = snapshot.ProgressPath,
            ["resolved_progress_path"] = snapshot.ResolvedProgressPath,
            ["profile_root"] = snapshot.ProfileRoot,
            ["save_scope"] = snapshot.SaveScope,
            ["current_run"] = BuildCurrentRunContext(snapshot),
            ["source"] = "SaveManager.Progress plus model metadata endpoints",
            ["sections"] = new Dictionary<string, object?>
            {
                ["card_library"] = new Dictionary<string, object?>
                {
                    ["ui_label"] = "Card Library",
                    ["status"] = "exposed",
                    ["source"] = "/api/v1/profile card_stats and discovered_cards",
                    ["detail_endpoint"] = "/api/v1/glossary/cards",
                    ["detail_endpoint_requires_run"] = true,
                    ["discovered_ids"] = snapshot.DiscoveredCards,
                    ["stats"] = snapshot.CardStats
                },
                ["relic_collection"] = new Dictionary<string, object?>
                {
                    ["ui_label"] = "Relic Collection",
                    ["status"] = "partially_exposed",
                    ["source"] = "/api/v1/profile discovered_relics",
                    ["detail_endpoint"] = "/api/v1/glossary/relics",
                    ["detail_endpoint_requires_run"] = true,
                    ["discovered_ids"] = snapshot.DiscoveredRelics,
                    ["limitation"] = "Profile exposes discovered relic IDs; per-relic obtained counts are not exposed by a typed API here."
                },
                ["potion_lab"] = new Dictionary<string, object?>
                {
                    ["ui_label"] = "Potion Lab",
                    ["status"] = "partially_exposed",
                    ["source"] = "/api/v1/profile discovered_potions",
                    ["detail_endpoint"] = "/api/v1/glossary/potions",
                    ["detail_endpoint_requires_run"] = true,
                    ["discovered_ids"] = snapshot.DiscoveredPotions,
                    ["limitation"] = "Profile exposes discovered potion IDs; per-potion lab UI metadata is not exposed by a typed API here."
                },
                ["bestiary"] = new Dictionary<string, object?>
                {
                    ["ui_label"] = "Bestiary",
                    ["status"] = "locked_in_ui",
                    ["source"] = "/api/v1/bestiary model metadata",
                    ["detail_endpoint"] = "/api/v1/bestiary",
                    ["encounter_stats"] = snapshot.EncounterStats,
                    ["enemy_stats"] = snapshot.EnemyStats,
                    ["limitation"] = "The current game UI labels Bestiary as future/locked; the endpoint exposes reflected model metadata and profile fight stats when available."
                },
                ["character_stats"] = new Dictionary<string, object?>
                {
                    ["ui_label"] = "Character Stats",
                    ["status"] = "exposed",
                    ["source"] = "/api/v1/profile characters and global totals",
                    ["characters"] = snapshot.CharacterStats,
                    ["global"] = snapshot.GlobalStats
                },
                ["run_history"] = runHistory
            }
        };
    }

    private sealed class CompendiumSnapshot
    {
        public string? Error { get; init; }
        public int ProfileId { get; init; }
        public string? ProgressPath { get; init; }
        public string? ResolvedProgressPath { get; init; }
        public string ProfileRoot { get; init; } = "";
        public string SaveScope { get; init; } = "vanilla";
        public bool IsRunInProgress { get; init; }
        public List<string> DiscoveredCards { get; init; } = new();
        public List<string> DiscoveredRelics { get; init; } = new();
        public List<string> DiscoveredPotions { get; init; } = new();
        public List<Dictionary<string, object?>> CardStats { get; init; } = new();
        public List<Dictionary<string, object?>> CharacterStats { get; init; } = new();
        public List<Dictionary<string, object?>> EncounterStats { get; init; } = new();
        public List<Dictionary<string, object?>> EnemyStats { get; init; } = new();
        public Dictionary<string, object?> RunHistoryProgressMembers { get; init; } = new();
        public Dictionary<string, object?> GlobalStats { get; init; } = new();
    }

    private static List<Dictionary<string, object?>> BuildEncounterStats(dynamic progress)
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (var kv in progress.EncounterStats)
            result.Add(BuildFightStatsEntry(kv.Key.Entry, kv.Value));
        return result;
    }

    private static List<Dictionary<string, object?>> BuildEnemyStats(dynamic progress)
    {
        var result = new List<Dictionary<string, object?>>();
        foreach (var kv in progress.EnemyStats)
            result.Add(BuildFightStatsEntry(kv.Key.Entry, kv.Value));
        return result;
    }

    private static Dictionary<string, object?> BuildFightStatsEntry(string id, dynamic stats)
    {
        var entry = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["total_wins"] = stats.TotalWins,
            ["total_losses"] = stats.TotalLosses
        };

        var byCharacter = new List<Dictionary<string, object?>>();
        foreach (var fs in stats.FightStats)
        {
            byCharacter.Add(new Dictionary<string, object?>
            {
                ["character"] = fs.Character.Entry,
                ["wins"] = fs.Wins,
                ["losses"] = fs.Losses
            });
        }
        if (byCharacter.Count > 0)
            entry["by_character"] = byCharacter;

        return entry;
    }

    private static Dictionary<string, object?> BuildRunHistoryProgressMembers(object progress)
    {
        var values = new Dictionary<string, object?>();
        foreach (var member in GetRunHistoryMembers(progress.GetType()))
        {
            try
            {
                object? value = member switch
                {
                    PropertyInfo property when property.GetIndexParameters().Length == 0 => property.GetValue(progress),
                    FieldInfo field => field.GetValue(progress),
                    _ => null
                };
                if (value != null)
                    values[member.Name] = ToJsonSafe(value, 0, 50);
            }
            catch { }
        }
        return values;
    }

    private static MemberInfo[] GetRunHistoryMembers(Type progressType)
    {
        lock (_runHistoryMembersLock)
        {
            if (_runHistoryMembersType == progressType && _runHistoryMembers != null)
                return _runHistoryMembers;

            _runHistoryMembersType = progressType;
            _runHistoryMembers = progressType
                .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(member => member.Name.Contains("RunHistory", StringComparison.OrdinalIgnoreCase)
                    || member.Name.Contains("RunHistories", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return _runHistoryMembers;
        }
    }

    private static Dictionary<string, object?> BuildRunHistorySection(CompendiumSnapshot snapshot)
    {
        var values = snapshot.RunHistoryProgressMembers;
        var historyDirectory = FindRunHistoryDirectory(snapshot);
        if (historyDirectory != null)
        {
            var files = Directory.GetFiles(historyDirectory, "*.run")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();

            return new Dictionary<string, object?>
            {
                ["ui_label"] = "Run History",
                ["status"] = files.Count > 0 ? "exposed" : "exposed_empty",
                ["source"] = "Profile save history files",
                ["history_path"] = historyDirectory,
                ["entry_count"] = files.Count,
                ["entries"] = files.Take(20).Select(file => BuildRunHistoryEntry(file, snapshot.ProfileId, snapshot.SaveScope)).ToList(),
                ["progress_members"] = values.Count > 0 ? values : null,
                ["limitation"] = files.Count > 20
                    ? "Only the 20 most recent run files are summarized; use history_path to inspect older local run files."
                    : "Run history is summarized from the active profile's saved .run files."
            };
        }

        return new Dictionary<string, object?>
        {
            ["ui_label"] = "Run History",
            ["status"] = values.Count > 0 ? "partially_exposed" : "exposed_empty",
            ["source"] = "SaveManager.Progress reflected run-history members",
            ["entries"] = values,
            ["limitation"] = values.Count > 0
                ? "Run history is serialized from discovered progress members and capped for response size."
                : "No run-history files or typed run-history members were found for the active profile."
        };
    }

    private static Dictionary<string, object?> BuildRunHistoryEntry(FileInfo file, int profileId, string saveScope)
    {
        var entry = new Dictionary<string, object?>
        {
            ["id"] = Path.GetFileNameWithoutExtension(file.Name),
            ["run_id"] = $"{saveScope}:profile{profileId}:{Path.GetFileNameWithoutExtension(file.Name)}",
            ["file_name"] = file.Name,
            ["size_bytes"] = file.Length,
            ["last_write_time_utc"] = file.LastWriteTimeUtc
        };

        try
        {
            using var stream = file.OpenRead();
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            CopyJsonScalar(root, entry, "start_time");
            CopyJsonScalar(root, entry, "run_time");
            CopyJsonScalar(root, entry, "game_mode");
            CopyJsonScalar(root, entry, "ascension");
            CopyJsonScalar(root, entry, "win");
            CopyJsonScalar(root, entry, "was_abandoned");
            CopyJsonScalar(root, entry, "killed_by_encounter");
            CopyJsonScalar(root, entry, "killed_by_event");
            CopyJsonScalar(root, entry, "seed");
            CopyJsonScalar(root, entry, "build_id");

            if (root.TryGetProperty("acts", out var acts) && acts.ValueKind == JsonValueKind.Array)
                entry["acts"] = acts.EnumerateArray().Select(GetJsonValue).ToList();

            if (root.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array)
            {
                entry["players"] = players.EnumerateArray().Select(player =>
                {
                    var playerEntry = new Dictionary<string, object?>();
                    CopyJsonScalar(player, playerEntry, "id");
                    CopyJsonScalar(player, playerEntry, "character");
                    if (player.TryGetProperty("deck", out var deck) && deck.ValueKind == JsonValueKind.Array)
                        playerEntry["deck_count"] = deck.GetArrayLength();
                    if (player.TryGetProperty("relics", out var relics) && relics.ValueKind == JsonValueKind.Array)
                        playerEntry["relic_count"] = relics.GetArrayLength();
                    if (player.TryGetProperty("potions", out var potions) && potions.ValueKind == JsonValueKind.Array)
                        playerEntry["potion_count"] = potions.GetArrayLength();
                    return playerEntry;
                }).ToList();
            }

            entry["map_point_count"] = CountMapPoints(root);
        }
        catch (Exception ex)
        {
            entry["parse_error"] = ex.Message;
        }

        return entry;
    }

    private static Dictionary<string, object?>? BuildCurrentRunContext(CompendiumSnapshot snapshot)
        => BuildCurrentRunContext(snapshot.IsRunInProgress, snapshot.ProfileId, snapshot.SaveScope, snapshot.ProgressPath, snapshot.ProfileRoot);

    private static Dictionary<string, object?>? BuildActiveRunContext()
    {
        var saveManager = SaveManager.Instance;
        if (saveManager == null)
            return null;

        var profileId = saveManager.CurrentProfileId;
        var progressPath = GetProfileProgressPath(profileId);
        var profileRoot = GetProfileRootFromProgressPath(progressPath, profileId);
        return BuildCurrentRunContext(
            RunManager.Instance?.IsInProgress == true,
            profileId,
            GetSaveScope(profileRoot),
            progressPath,
            profileRoot);
    }

    private static Dictionary<string, object?>? BuildCurrentRunContext(
        bool isRunInProgress,
        int profileId,
        string saveScope,
        string? progressPath,
        string profileRoot)
    {
        if (!isRunInProgress)
            return null;

        var result = new Dictionary<string, object?>
        {
            ["is_in_progress"] = true,
            ["profile_id"] = profileId,
            ["progress_path"] = progressPath,
            ["resolved_progress_path"] = ResolveProfileProgressPath(profileId),
            ["profile_root"] = profileRoot,
            ["save_scope"] = saveScope,
            ["id_format"] = "{save_scope}:profile{profile_id}:{start_time}"
        };

        var currentRunPath = ResolveCurrentRunPath(progressPath, profileRoot);
        if (currentRunPath == null || !File.Exists(currentRunPath))
        {
            result["limitation"] = "Run is in progress, but current_run.save was not found yet.";
            return result;
        }

        result["save_path"] = currentRunPath;

        try
        {
            using var stream = File.OpenRead(currentRunPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            CopyJsonScalar(root, result, "start_time");
            CopyJsonScalar(root, result, "save_time");
            CopyJsonScalar(root, result, "run_time");
            CopyJsonScalar(root, result, "game_mode");
            CopyJsonScalar(root, result, "ascension");
            CopyJsonScalar(root, result, "current_act_index");
            CopyJsonScalar(root, result, "schema_version");
            CopyJsonScalar(root, result, "platform_type");

            if (root.TryGetProperty("rng", out var rng)
                && rng.ValueKind == JsonValueKind.Object
                && rng.TryGetProperty("seed", out var seed))
                result["seed"] = GetJsonValue(seed);

            if (result.TryGetValue("start_time", out var startTime) && startTime != null)
                result["run_id"] = $"{saveScope}:profile{profileId}:{startTime}";
        }
        catch (Exception ex)
        {
            result["parse_error"] = ex.Message;
        }

        return result;
    }

    private static int CountMapPoints(JsonElement root)
    {
        if (!root.TryGetProperty("map_point_history", out var acts) || acts.ValueKind != JsonValueKind.Array)
            return 0;

        var count = 0;
        foreach (var act in acts.EnumerateArray())
        {
            if (act.ValueKind == JsonValueKind.Array)
                count += act.GetArrayLength();
        }
        return count;
    }

    private static void CopyJsonScalar(JsonElement source, Dictionary<string, object?> target, string propertyName)
    {
        if (source.TryGetProperty(propertyName, out var property))
            target[propertyName] = GetJsonValue(property);
    }

    private static object? GetJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private static string? FindRunHistoryDirectory(CompendiumSnapshot snapshot)
    {
        var saveDirectory = GetSaveDirectoryFromProgressPath(snapshot.ProgressPath);
        if (saveDirectory != null)
        {
            var historyPath = Path.Combine(saveDirectory, "history");
            if (Directory.Exists(historyPath))
                return historyPath;
        }

        foreach (var saveRoot in EnumerateSaveRoots())
        {
            var historyPath = Path.Combine(saveRoot, snapshot.ProfileRoot, "saves", "history");
            if (Directory.Exists(historyPath))
                return historyPath;
        }

        return null;
    }

    private static string GetProfileRootFromProgressPath(string? progressPath, int profileId)
    {
        var normalized = progressPath?.Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            var moddedProfile = $"modded/profile{profileId}";
            if (normalized.Contains($"{moddedProfile}/", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(moddedProfile, StringComparison.OrdinalIgnoreCase))
                return $"modded/profile{profileId}";

            var profile = $"profile{profileId}";
            if (normalized.Contains($"/{profile}/", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith($"{profile}/", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals(profile, StringComparison.OrdinalIgnoreCase))
                return $"profile{profileId}";
        }

        return $"profile{profileId}";
    }

    private static string GetSaveScope(string profileRoot)
    {
        return profileRoot.StartsWith("modded/", StringComparison.OrdinalIgnoreCase)
            ? "modded"
            : "vanilla";
    }

    private static IEnumerable<string> EnumerateSaveRoots()
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steamRoots = EnumerateSteamDataRoots().ToList();

        var activeSteamId = GetActiveSteamId();
        if (!string.IsNullOrWhiteSpace(activeSteamId))
        {
            foreach (var steamRoot in steamRoots)
            {
                var accountRoot = Path.Combine(steamRoot, activeSteamId);
                if (Directory.Exists(accountRoot) && yielded.Add(accountRoot))
                    yield return accountRoot;
            }
        }

        foreach (var steamRoot in steamRoots)
        {
            foreach (var accountRoot in Directory.GetDirectories(steamRoot).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (yielded.Add(accountRoot))
                    yield return accountRoot;
            }
        }
    }

    private static IEnumerable<string> EnumerateSteamDataRoots()
    {
        var candidates = new List<string?>();

        candidates.Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        candidates.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        candidates.Add(Environment.GetEnvironmentVariable("XDG_DATA_HOME"));

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            candidates.Add(Path.Combine(home, ".local", "share"));

        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in candidates)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var steamRoot = Path.Combine(root, "SlayTheSpire2", "steam");
            if (Directory.Exists(steamRoot) && yielded.Add(steamRoot))
                yield return steamRoot;
        }
    }

    private static string? GetActiveSteamId()
    {
        try
        {
            if (!SteamInitializer.Initialized)
                return null;

            var steamUtil = typeof(SteamInitializer).Assembly.GetType("MegaCrit.Sts2.Core.Platform.Steam.SteamUtil");
            var getSteamId = steamUtil?.GetMethod("GetSteamID64", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var steamId = getSteamId?.Invoke(null, null)?.ToString();
            if (string.IsNullOrWhiteSpace(steamId) || steamId == "0")
                return null;

            return steamId;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetProfileProgressPath(int profileId)
    {
        try { return ProgressSaveManager.GetProgressPathForProfile(profileId); }
        catch { return null; }
    }

    private static string? ResolveProfileProgressPath(int profileId)
    {
        var progressPath = GetProfileProgressPath(profileId);
        if (!string.IsNullOrWhiteSpace(progressPath) && Path.IsPathRooted(progressPath))
            return progressPath;

        var profileRoot = GetProfileRootFromProgressPath(progressPath, profileId);

        foreach (var saveRoot in EnumerateSaveRoots())
        {
            var absolutePath = Path.Combine(saveRoot, profileRoot, "saves", "progress.save");
            if (File.Exists(absolutePath))
                return absolutePath;
        }

        return progressPath;
    }

    private static string? ResolveCurrentRunPath(CompendiumSnapshot snapshot)
        => ResolveCurrentRunPath(snapshot.ProgressPath, snapshot.ProfileRoot);

    private static string? ResolveCurrentRunPath(string? progressPath, string profileRoot)
    {
        var saveDirectory = GetSaveDirectoryFromProgressPath(progressPath);
        if (saveDirectory != null)
        {
            var currentRunPath = Path.Combine(saveDirectory, "current_run.save");
            if (File.Exists(currentRunPath))
                return currentRunPath;
        }

        foreach (var saveRoot in EnumerateSaveRoots())
        {
            var absolutePath = Path.Combine(saveRoot, profileRoot, "saves", "current_run.save");
            if (File.Exists(absolutePath))
                return absolutePath;
        }

        return null;
    }

    private static string? GetSaveDirectoryFromProgressPath(string? progressPath)
    {
        if (string.IsNullOrWhiteSpace(progressPath) || !Path.IsPathRooted(progressPath))
            return null;

        var saveDirectory = Path.GetDirectoryName(progressPath);
        return !string.IsNullOrWhiteSpace(saveDirectory) && Directory.Exists(saveDirectory)
            ? saveDirectory
            : null;
    }

    private static object? ToJsonSafe(object? value, int depth, int maxItems)
    {
        if (value == null || depth > 4) return value?.ToString();
        if (value is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            return value;
        if (value is Enum) return value.ToString();

        var type = value.GetType();
        var entryProperty = type.GetProperty("Entry", BindingFlags.Instance | BindingFlags.Public);
        if (entryProperty?.GetValue(value) is string entry)
            return entry;

        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();
            var count = 0;
            foreach (DictionaryEntry item in dictionary)
            {
                if (count++ >= maxItems) break;
                result[item.Key?.ToString() ?? "null"] = ToJsonSafe(item.Value, depth + 1, maxItems);
            }
            return result;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            var result = new List<object?>();
            var count = 0;
            foreach (var item in enumerable)
            {
                if (count++ >= maxItems) break;
                result.Add(ToJsonSafe(item, depth + 1, maxItems));
            }
            return result;
        }

        var obj = new Dictionary<string, object?>();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length != 0)
                continue;
            try { obj[property.Name] = ToJsonSafe(property.GetValue(value), depth + 1, maxItems); }
            catch { }
        }
        return obj.Count > 0 ? obj : value.ToString();
    }
}
