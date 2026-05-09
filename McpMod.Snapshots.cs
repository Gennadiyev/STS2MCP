using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace STS2_MCP;

public static partial class McpMod
{
    private const string SnapshotsEnabledEnvVar = "STS2_MCP_SNAPSHOTS";
    private const string SnapshotRootEnvVar = "STS2_MCP_SNAPSHOT_DIR";
    private const string SnapshotDirectoryName = "sts2_mcp_snapshots";
    private const string SingleplayerRunSaveFileName = "current_run.save";
    private const string MultiplayerRunSaveFileName = "current_run_mp.save";
    private static bool _snapshotSubscriptionInitialized;

    private static void InitializeSnapshotSupport()
    {
        if (!AreSnapshotsEnabled())
            return;

        if (_snapshotSubscriptionInitialized)
            return;

        try
        {
            SaveManager.Instance.Saved += HandleGameSavedSnapshot;
            _snapshotSubscriptionInitialized = true;
            GD.Print($"[STS2 MCP] Save snapshots enabled via {SnapshotsEnabledEnvVar}; root: {GetSnapshotRoot()}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to initialize save snapshots: {ex}");
        }
    }

    private static void HandleGetSnapshots(HttpListenerResponse response)
    {
        try
        {
            SendJson(response, BuildSnapshotsList());
        }
        catch (Exception ex)
        {
            SendError(response, 500, $"Failed to list snapshots: {ex.Message}", "snapshots_read_failed");
        }
    }

    private static void HandlePostSnapshots(HttpListenerRequest request, HttpListenerResponse response)
    {
        Dictionary<string, JsonElement>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.InputStream, _jsonOptions);
        }
        catch
        {
            SendError(response, 400, "Invalid JSON", "invalid_json");
            return;
        }

        if (parsed == null || !parsed.TryGetValue("action", out var actionElem))
        {
            SendError(response, 400, "Missing 'action' field. Use: create, resume", "missing_action");
            return;
        }
        if (actionElem.ValueKind != JsonValueKind.String)
        {
            SendError(response, 400, "'action' field must be a string", "invalid_action_type");
            return;
        }

        var action = actionElem.GetString()?.Trim().ToLowerInvariant();
        Dictionary<string, object?> result = action switch
        {
            "create" => CreateSnapshotFromCurrentSave(manual: true, validateLiveState: true),
            "resume" => ResumeSnapshot(GetRequiredString(parsed, "snapshot_id")),
            _ => Error($"Unknown snapshot action: {actionElem.GetString()}. Use: create, resume", "unknown_snapshot_action")
        };

        SendSnapshotActionJson(response, result);
    }

    private static string? GetRequiredString(Dictionary<string, JsonElement> parsed, string propertyName)
    {
        if (!parsed.TryGetValue(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
            return null;
        return element.GetString();
    }

    private static void SendSnapshotActionJson(HttpListenerResponse response, Dictionary<string, object?> result)
    {
        if (result.TryGetValue("status", out var status) && status as string == "error")
        {
            response.StatusCode = result.TryGetValue("error_code", out var errorCode)
                ? (errorCode as string) switch
                {
                    "snapshots_disabled" or "missing_snapshot_id" or "unknown_snapshot_action" => 400,
                    "snapshot_not_found" or "current_run_save_not_found" => 404,
                    "run_in_progress" or "snapshot_state_not_supported" => 409,
                    "save_manager_unavailable" or "snapshot_restore_path_unavailable" => 503,
                    _ => 500
                }
                : 500;
        }

        SendJson(response, result);
    }

    private static Dictionary<string, object?> BuildSnapshotsList()
    {
        var snapshots = EnumerateSnapshots()
            .OrderByDescending(snapshot => snapshot.CreatedAtUtc)
            .Select(snapshot => snapshot.ToResponse())
            .ToList();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["kind"] = "snapshots",
            ["enabled"] = AreSnapshotsEnabled(),
            ["enable_env_var"] = SnapshotsEnabledEnvVar,
            ["snapshot_root_env_var"] = SnapshotRootEnvVar,
            ["snapshot_root"] = NormalizePathForJson(GetSnapshotRoot()),
            ["count"] = snapshots.Count,
            ["snapshots"] = snapshots
        };
    }

    private static void HandleGameSavedSnapshot()
    {
        try
        {
            var result = CreateSnapshotFromCurrentSave(manual: false, validateLiveState: false);
            if (result.TryGetValue("status", out var status) && status as string == "ok")
            {
                GD.Print($"[STS2 MCP] Snapshot created: {result.GetValueOrDefault("snapshot_id")}");
            }
            else if (result.TryGetValue("error", out var error))
            {
                GD.PrintErr($"[STS2 MCP] Snapshot skipped: {error}");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Snapshot failed: {ex}");
        }
    }

    private static Dictionary<string, object?> CreateSnapshotFromCurrentSave(bool manual, bool validateLiveState)
    {
        if (!AreSnapshotsEnabled())
            return Error($"Snapshots are disabled. Set {SnapshotsEnabledEnvVar}=1 before launching the game.", "snapshots_disabled");

        var saveManager = SaveManager.Instance;
        if (saveManager == null)
            return Error("Save manager is not available", "save_manager_unavailable");

        if (validateLiveState && RunManager.Instance?.IsInProgress == true)
        {
            var liveStateType = GetCurrentSnapshotStateType();
            var unsupportedStateMessage = GetUnsupportedManualSnapshotStateMessage(liveStateType);
            if (unsupportedStateMessage != null)
                return Error(unsupportedStateMessage, "snapshot_state_not_supported");
        }

        var profileId = saveManager.CurrentProfileId;
        var progressPath = GetProfileProgressPath(profileId);
        var profileRoot = GetProfileRootFromProgressPath(progressPath, profileId);
        var saveScope = GetSaveScope(profileRoot);
        var fileName = TryGetActiveRunSaveFileName();
        var sourcePath = ResolveRunSavePath(profileId, profileRoot, fileName, requireExists: true);

        if (sourcePath == null && fileName != SingleplayerRunSaveFileName)
        {
            fileName = SingleplayerRunSaveFileName;
            sourcePath = ResolveRunSavePath(profileId, profileRoot, fileName, requireExists: true);
        }
        if (sourcePath == null)
            return Error("No current run save file was found to snapshot.", "current_run_save_not_found");

        RunSnapshotMetadata metadata;
        try
        {
            metadata = BuildSnapshotMetadata(profileId, profileRoot, saveScope, fileName, sourcePath, manual);
            var snapshotDirectory = Path.Combine(GetSnapshotRoot(), metadata.Id);
            Directory.CreateDirectory(snapshotDirectory);

            var snapshotSavePath = Path.Combine(snapshotDirectory, fileName);
            File.Copy(sourcePath, snapshotSavePath, overwrite: true);
            metadata.SnapshotPath = snapshotDirectory;
            metadata.SnapshotSavePath = snapshotSavePath;

            var metadataPath = Path.Combine(snapshotDirectory, "metadata.json");
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, _jsonOptions));
        }
        catch (Exception ex)
        {
            return Error($"Failed to create snapshot: {ex.Message}", "snapshot_create_failed");
        }

        var result = metadata.ToResponse();
        result["status"] = "ok";
        result["kind"] = "snapshot";
        result["message"] = "Snapshot created from current run save.";
        result["snapshot_id"] = metadata.Id;
        return result;
    }

    private static string? GetUnsupportedManualSnapshotStateMessage(string? stateType)
    {
        return stateType switch
        {
            "map" => "Manual snapshots from the map screen are not supported because the game save resumes the latest visited room instead of the idle map. Choose a map node, then snapshot inside the next room.",
            "shop" or "fake_merchant" => "Manual snapshots from shop screens are not supported because STS2 saves do not persist the current merchant inventory. Create a snapshot before entering the shop or after leaving it.",
            _ => null
        };
    }

    private static string? GetCurrentSnapshotStateType()
    {
        try
        {
            var state = RunOnMainThread(BuildGameState).GetAwaiter().GetResult();
            return state.TryGetValue("state_type", out var stateType) ? stateType as string : null;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to inspect state before snapshot: {ex}");
            return null;
        }
    }

    private static Dictionary<string, object?> ResumeSnapshot(string? snapshotId)
    {
        if (string.IsNullOrWhiteSpace(snapshotId))
            return Error("Missing 'snapshot_id' field.", "missing_snapshot_id");
        if (!AreSnapshotsEnabled())
            return Error($"Snapshots are disabled. Set {SnapshotsEnabledEnvVar}=1 before launching the game.", "snapshots_disabled");
        if (RunManager.Instance?.IsInProgress == true)
            return Error("Cannot resume a snapshot while a run is in progress. Return to the main menu or restart first.", "run_in_progress");

        var snapshot = FindSnapshot(snapshotId);
        if (snapshot == null)
            return Error($"Snapshot not found: {snapshotId}", "snapshot_not_found");
        if (string.IsNullOrWhiteSpace(snapshot.SnapshotSavePath) || !File.Exists(snapshot.SnapshotSavePath))
            return Error($"Snapshot save file is missing for {snapshot.Id}", "snapshot_not_found");

        var destination = ResolveSnapshotRestorePath(snapshot);
        if (destination == null)
            return Error("Could not resolve a destination current-run save path for this snapshot.", "snapshot_restore_path_unavailable");

        try
        {
            var destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            string? backupPath = null;
            if (File.Exists(destination))
            {
                backupPath = destination + $".pre_snapshot_resume_{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.backup";
                File.Copy(destination, backupPath, overwrite: false);
            }

            File.Copy(snapshot.SnapshotSavePath, destination, overwrite: true);

            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["kind"] = "snapshot_resume",
                ["message"] = "Snapshot restored to the active profile's current run save. Use the in-game Continue flow to resume it.",
                ["snapshot"] = snapshot.ToResponse(),
                ["restored_save_path"] = NormalizePathForJson(destination),
                ["previous_save_backup_path"] = NormalizePathForJson(backupPath)
            };
        }
        catch (Exception ex)
        {
            return Error($"Failed to resume snapshot: {ex.Message}", "snapshot_resume_failed");
        }
    }

    private static RunSnapshotMetadata BuildSnapshotMetadata(
        int profileId,
        string profileRoot,
        string saveScope,
        string fileName,
        string sourcePath,
        bool manual)
    {
        var createdAtUtc = DateTime.UtcNow;
        var metadata = new RunSnapshotMetadata
        {
            Id = BuildSnapshotId(saveScope, profileId, fileName, createdAtUtc),
            CreatedAtUtc = createdAtUtc,
            ProfileId = profileId,
            ProfileRoot = profileRoot,
            SaveScope = saveScope,
            GameMode = fileName == MultiplayerRunSaveFileName ? "multiplayer" : "singleplayer",
            SaveFileName = fileName,
            SourceSavePath = sourcePath,
            Manual = manual
        };

        try
        {
            using var stream = File.OpenRead(sourcePath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            metadata.StartTime = GetJsonInt64(root, "start_time");
            metadata.SaveTime = GetJsonInt64(root, "save_time");
            metadata.RunTime = GetJsonInt64(root, "run_time");
            metadata.Ascension = GetJsonInt64(root, "ascension");
            metadata.CurrentActIndex = GetJsonInt64(root, "current_act_index");
            if (root.TryGetProperty("game_mode", out var gameMode) && gameMode.ValueKind == JsonValueKind.String)
                metadata.GameMode = gameMode.GetString() ?? metadata.GameMode;
            if (root.TryGetProperty("rng", out var rng)
                && rng.ValueKind == JsonValueKind.Object
                && rng.TryGetProperty("seed", out var seed))
                metadata.Seed = GetJsonValue(seed);

            if (metadata.StartTime.HasValue)
                metadata.RunId = $"{saveScope}:profile{profileId}:{metadata.StartTime}";
        }
        catch (Exception ex)
        {
            metadata.ParseError = ex.Message;
        }

        return metadata;
    }

    private static string BuildSnapshotId(string saveScope, int profileId, string fileName, DateTime createdAtUtc)
    {
        var mode = fileName == MultiplayerRunSaveFileName ? "mp" : "sp";
        return $"{SanitizePathPart(saveScope)}_profile{profileId}_{mode}_{createdAtUtc:yyyyMMddTHHmmssfffZ}";
    }

    private static string SanitizePathPart(string value)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '/', '\\', ':' };
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        return builder.ToString();
    }

    private static long? GetJsonInt64(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Number)
            return null;
        return property.TryGetInt64(out var value) ? value : null;
    }

    private static string TryGetActiveRunSaveFileName()
    {
        try
        {
            return RunManager.Instance?.NetService?.Type.IsMultiplayer() == true
                ? MultiplayerRunSaveFileName
                : SingleplayerRunSaveFileName;
        }
        catch
        {
            return SingleplayerRunSaveFileName;
        }
    }

    private static RunSnapshotMetadata? FindSnapshot(string snapshotId)
    {
        return EnumerateSnapshots()
            .FirstOrDefault(snapshot => string.Equals(snapshot.Id, snapshotId, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<RunSnapshotMetadata> EnumerateSnapshots()
    {
        var root = GetSnapshotRoot();
        if (!Directory.Exists(root))
            yield break;

        foreach (var metadataPath in Directory.EnumerateDirectories(root)
            .Select(directory => Path.Combine(directory, "metadata.json"))
            .Where(File.Exists))
        {
            RunSnapshotMetadata? metadata = null;
            try
            {
                var json = File.ReadAllText(metadataPath);
                metadata = JsonSerializer.Deserialize<RunSnapshotMetadata>(json, _jsonOptions);
                if (metadata != null)
                {
                    var directory = Path.GetDirectoryName(metadataPath) ?? root;
                    if (!IsSupportedRunSaveFileName(metadata.SaveFileName))
                    {
                        GD.PrintErr($"[STS2 MCP] Ignoring snapshot metadata with unsupported save file name at {metadataPath}: {metadata.SaveFileName}");
                        metadata = null;
                    }
                    else
                    {
                        metadata.SnapshotPath = directory;
                        metadata.SnapshotSavePath = Path.Combine(directory, metadata.SaveFileName);
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[STS2 MCP] Failed to parse snapshot metadata at {metadataPath}: {ex}");
            }

            if (metadata != null)
                yield return metadata;
        }
    }

    private static string? ResolveSnapshotRestorePath(RunSnapshotMetadata snapshot)
    {
        if (!IsSupportedRunSaveFileName(snapshot.SaveFileName))
            return null;

        var saveManager = SaveManager.Instance;
        if (saveManager == null)
            return null;

        var activeProfileId = saveManager.CurrentProfileId;
        if (snapshot.ProfileId != activeProfileId)
            return null;

        var progressPath = GetProfileProgressPath(activeProfileId);
        var activeProfileRoot = GetProfileRootFromProgressPath(progressPath, activeProfileId);

        return ResolveRunSavePath(activeProfileId, activeProfileRoot, snapshot.SaveFileName, requireExists: false);
    }

    private static bool IsSupportedRunSaveFileName(string? fileName)
    {
        return string.Equals(fileName, SingleplayerRunSaveFileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, MultiplayerRunSaveFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveRunSavePath(int profileId, string profileRoot, string fileName, bool requireExists)
    {
        var progressPath = GetProfileProgressPath(profileId);
        var saveDirectory = GetSaveDirectoryFromProgressPath(progressPath);
        if (saveDirectory != null)
        {
            var path = Path.Combine(saveDirectory, fileName);
            if (!requireExists || File.Exists(path))
                return path;
        }

        foreach (var saveRoot in EnumerateSaveRoots())
        {
            var path = Path.Combine(saveRoot, profileRoot, "saves", fileName);
            if (!requireExists)
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    return path;
            }
            else if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string GetSnapshotRoot()
    {
        var configured = System.Environment.GetEnvironmentVariable(SnapshotRootEnvVar);
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(System.Environment.ExpandEnvironmentVariables(configured));

        var saveRoot = EnumerateSaveRoots().FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(saveRoot))
            return Path.Combine(saveRoot, SnapshotDirectoryName);

        var modDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        return Path.Combine(modDir ?? System.Environment.CurrentDirectory, SnapshotDirectoryName);
    }

    private static bool AreSnapshotsEnabled()
        => IsTruthy(System.Environment.GetEnvironmentVariable(SnapshotsEnabledEnvVar));

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on" or "enabled";
    }

    private sealed class RunSnapshotMetadata
    {
        public string Id { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public int ProfileId { get; set; }
        public string ProfileRoot { get; set; } = "";
        public string SaveScope { get; set; } = "vanilla";
        public string GameMode { get; set; } = "singleplayer";
        public string SaveFileName { get; set; } = SingleplayerRunSaveFileName;
        public string SourceSavePath { get; set; } = "";
        [JsonIgnore]
        public string SnapshotPath { get; set; } = "";
        [JsonIgnore]
        public string SnapshotSavePath { get; set; } = "";
        public bool Manual { get; set; }
        public string? RunId { get; set; }
        public long? StartTime { get; set; }
        public long? SaveTime { get; set; }
        public long? RunTime { get; set; }
        public long? Ascension { get; set; }
        public long? CurrentActIndex { get; set; }
        public object? Seed { get; set; }
        public string? ParseError { get; set; }

        public Dictionary<string, object?> ToResponse()
        {
            return new Dictionary<string, object?>
            {
                ["id"] = Id,
                ["created_at_utc"] = CreatedAtUtc,
                ["profile_id"] = ProfileId,
                ["profile_root"] = NormalizePathForJson(ProfileRoot),
                ["save_scope"] = SaveScope,
                ["game_mode"] = GameMode,
                ["save_file_name"] = SaveFileName,
                ["source_save_path"] = NormalizePathForJson(SourceSavePath),
                ["snapshot_path"] = NormalizePathForJson(SnapshotPath),
                ["snapshot_save_path"] = NormalizePathForJson(SnapshotSavePath),
                ["manual"] = Manual,
                ["run_id"] = RunId,
                ["start_time"] = StartTime,
                ["save_time"] = SaveTime,
                ["run_time"] = RunTime,
                ["ascension"] = Ascension,
                ["current_act_index"] = CurrentActIndex,
                ["seed"] = Seed,
                ["parse_error"] = ParseError
            };
        }
    }
}
