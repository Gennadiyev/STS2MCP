using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace STS2_MCP;

public static partial class McpMod
{
    private const string RunNotInProgressErrorCode = "run_not_in_progress";
    private const string RunStateUnavailableErrorCode = "run_state_unavailable";

    private static void HandleGetSettings(HttpListenerResponse response)
    {
        try
        {
            var dataTask = RunOnMainThread(() =>
            {
                var sm = SaveManager.Instance;
                var settings = sm.SettingsSave;
                var prefs = sm.PrefsSave;

                return new Dictionary<string, object?>
                {
                    ["display"] = new Dictionary<string, object?>
                    {
                        ["fullscreen"] = settings.Fullscreen,
                        ["resolution"] = $"{settings.WindowSize.X}x{settings.WindowSize.Y}",
                        ["fps_limit"] = settings.FpsLimit,
                        ["vsync"] = settings.VSync.ToString(),
                        ["msaa"] = settings.Msaa,
                        ["aspect_ratio"] = settings.AspectRatioSetting.ToString(),
                        ["target_display"] = settings.TargetDisplay,
                        ["limit_fps_background"] = settings.LimitFpsInBackground,
                    },
                    ["audio"] = new Dictionary<string, object?>
                    {
                        ["master"] = settings.VolumeMaster,
                        ["bgm"] = settings.VolumeBgm,
                        ["sfx"] = settings.VolumeSfx,
                        ["ambience"] = settings.VolumeAmbience,
                    },
                    ["gameplay"] = new Dictionary<string, object?>
                    {
                        ["fast_mode"] = prefs.FastMode.ToString(),
                        ["screen_shake"] = prefs.ScreenShakeOptionIndex,
                        ["show_run_timer"] = prefs.ShowRunTimer,
                        ["show_card_indices"] = prefs.ShowCardIndices,
                        ["text_effects"] = prefs.TextEffectsEnabled,
                        ["long_press"] = prefs.IsLongPressEnabled,
                    },
                    ["mods"] = new Dictionary<string, object?>
                    {
                        ["enabled"] = settings.ModSettings?.PlayerAgreedToModLoading ?? false,
                    },
                    ["language"] = settings.Language,
                    ["skip_intro"] = settings.SkipIntroLogo,
                };
            });

            SendJson(response, dataTask.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            SendError(response, 500, $"Failed to read settings: {ex.Message}");
        }
    }

    private static void HandleGetBestiary(HttpListenerResponse response)
    {
        try
        {
            var dataTask = RunOnMainThread(BuildBestiary);
            SendJson(response, dataTask.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            SendError(response, 500, $"Failed to build bestiary: {ex.Message}");
        }
    }

    private static void HandleGetGlossaryCards(HttpListenerResponse response)
    {
        try
        {
            var dataTask = RunOnMainThread(() => BuildGlossaryPayload("cards", BuildGlossaryCards));
            SendGlossaryJson(response, dataTask.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            SendError(response, 500, $"Failed to build glossary: {ex.Message}");
        }
    }

    private static void HandleGetGlossaryKeywords(HttpListenerResponse response)
    {
        try
        {
            var dataTask = RunOnMainThread(() => BuildGlossaryPayload("keywords", BuildGlossaryKeywords));
            SendGlossaryJson(response, dataTask.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            SendError(response, 500, $"Failed to build glossary: {ex.Message}");
        }
    }

    private static void HandleGetGlossaryPotions(HttpListenerResponse response)
    {
        try
        {
            var dataTask = RunOnMainThread(() => BuildGlossaryPayload("potions", BuildGlossaryPotions));
            SendGlossaryJson(response, dataTask.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            SendError(response, 500, $"Failed to build glossary: {ex.Message}");
        }
    }

    private static void HandleGetGlossaryRelics(HttpListenerResponse response)
    {
        try
        {
            var dataTask = RunOnMainThread(() => BuildGlossaryPayload("relics", BuildGlossaryRelics));
            SendGlossaryJson(response, dataTask.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            SendError(response, 500, $"Failed to build glossary: {ex.Message}");
        }
    }

    private sealed class GlossaryPayload
    {
        public string Kind { get; init; } = "";
        public object Data { get; init; } = new();
        public int ProfileId { get; init; }
        public string? ProgressPath { get; init; }
        public string ProfileRoot { get; init; } = "";
        public string SaveScope { get; init; } = "vanilla";
        public string NetType { get; init; } = "";
        public List<Dictionary<string, object?>> Players { get; init; } = new();
    }

    private static GlossaryPayload BuildGlossaryPayload(string kind, Func<object> buildData)
    {
        var data = buildData();
        if (data is Dictionary<string, object?> error && error.TryGetValue("error_code", out _))
            return new GlossaryPayload { Kind = kind, Data = error };

        var profileId = SaveManager.Instance?.CurrentProfileId ?? 0;
        var progressPath = GetProfileProgressPath(profileId);
        var profileRoot = GetProfileRootFromProgressPath(progressPath, profileId);
        var runState = RunManager.Instance.DebugOnlyGetState();
        var players = runState?.Players.Select((player, index) => new Dictionary<string, object?>
        {
            ["index"] = index,
            ["character"] = player.Character?.Id.Entry,
            ["character_name"] = SafeGetText(() => player.Character?.Title)
        }).ToList() ?? new List<Dictionary<string, object?>>();

        return new GlossaryPayload
        {
            Kind = kind,
            Data = data,
            ProfileId = profileId,
            ProgressPath = progressPath,
            ProfileRoot = profileRoot,
            SaveScope = GetSaveScope(profileRoot),
            NetType = RunManager.Instance.NetService.Type.ToString(),
            Players = players
        };
    }

    private static void SendGlossaryJson(HttpListenerResponse response, GlossaryPayload payload)
    {
        var data = payload.Data;
        if (data is Dictionary<string, object?> error
            && error.TryGetValue("error_code", out var errorCode))
        {
            response.StatusCode = (errorCode as string) switch
            {
                RunNotInProgressErrorCode => 409,
                RunStateUnavailableErrorCode => 503,
                _ => 500
            };

            SendJson(response, data);
            return;
        }

        if (data is not List<Dictionary<string, object?>> items)
        {
            SendJson(response, data);
            return;
        }

        SendJson(response, new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["kind"] = payload.Kind,
            ["scope"] = "active_run",
            ["count"] = items.Count,
            ["current_run"] = BuildCurrentRunContext(
                isRunInProgress: true,
                profileId: payload.ProfileId,
                saveScope: payload.SaveScope,
                progressPath: payload.ProgressPath,
                profileRoot: payload.ProfileRoot),
            ["net_type"] = payload.NetType,
            ["players"] = payload.Players,
            ["items"] = items
        });
    }

    private static Dictionary<string, object?> GlossaryError(string message, string errorCode)
    {
        return new Dictionary<string, object?>
        {
            ["status"] = "error",
            ["error"] = message,
            ["error_code"] = errorCode
        };
    }

    internal static object BuildGlossaryCards()
    {
        if (!RunManager.Instance.IsInProgress)
            return GlossaryError("No run in progress.", RunNotInProgressErrorCode);

        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
            return GlossaryError("Could not read run state.", RunStateUnavailableErrorCode);

        var result = new List<Dictionary<string, object?>>();
        var seen = new HashSet<string>();

        foreach (var pool in ModelDb.AllSharedCardPools)
            AddCardsFromPool(pool, SafeGetText(() => pool.Title) ?? pool.Id.Entry, result, seen);

        foreach (var player in runState.Players)
        {
            var pool = player.Character?.CardPool;
            if (pool == null) continue;
            var poolName = SafeGetText(() => pool.Title) ?? "Unknown";
            AddCardsFromPool(pool, poolName, result, seen);
        }

        return result;
    }

    private static void AddCardsFromPool(
        CardPoolModel pool,
        string poolName,
        List<Dictionary<string, object?>> result,
        HashSet<string> seen)
    {
        foreach (var card in pool.AllCards)
        {
            var id = card.Id.Entry;
            if (seen.Contains(id)) continue;
            seen.Add(id);

            string costDisplay;
            if (card.EnergyCost.CostsX)
                costDisplay = "X";
            else
                costDisplay = card.EnergyCost.GetAmountToSpend().ToString();

            result.Add(new Dictionary<string, object?>
            {
                ["id"] = id,
                ["name"] = SafeGetText(() => card.Title),
                ["type"] = card.Type.ToString(),
                ["cost"] = costDisplay,
                ["description"] = SafeGetCardDescription(card),
                ["rarity"] = card.Rarity.ToString(),
                ["pool"] = poolName,
                ["is_upgradable"] = card.IsUpgradable,
                ["current_upgrade_level"] = card.CurrentUpgradeLevel,
                ["max_upgrade_level"] = card.MaxUpgradeLevel,
                ["upgrade_preview_type"] = card.UpgradePreviewType.ToString(),
                ["upgrade_preview_description"] = card.IsUpgradable
                    ? SafeGetText(() => card.GetDescriptionForUpgradePreview())
                    : null,
                ["keywords"] = BuildHoverTips(card.HoverTips)
            });
        }
    }

    internal static object BuildGlossaryRelics()
    {
        if (!RunManager.Instance.IsInProgress)
            return GlossaryError("No run in progress.", RunNotInProgressErrorCode);

        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
            return GlossaryError("Could not read run state.", RunStateUnavailableErrorCode);

        var result = new List<Dictionary<string, object?>>();
        var seen = new HashSet<string>();

        var sharedPool = ModelDb.RelicPool<SharedRelicPool>();
        AddRelicsFromPool(sharedPool, "Shared", result, seen);

        foreach (var player in runState.Players)
        {
            var character = player.Character;
            if (character == null || character.RelicPool == null) continue;
            var pool = character.RelicPool;
            var poolName = SafeGetText(() => character.Title) ?? "Unknown";
            AddRelicsFromPool(pool, poolName, result, seen);
        }

        return result;
    }

    private static void AddRelicsFromPool(
        RelicPoolModel pool,
        string poolName,
        List<Dictionary<string, object?>> result,
        HashSet<string> seen)
    {
        foreach (var relic in pool.AllRelics)
        {
            var id = relic.Id.Entry;
            if (seen.Contains(id)) continue;
            seen.Add(id);

            result.Add(new Dictionary<string, object?>
            {
                ["id"] = id,
                ["name"] = SafeGetText(() => relic.Title),
                ["description"] = SafeGetText(() => relic.DynamicDescription),
                ["rarity"] = relic.Rarity.ToString(),
                ["pool"] = poolName,
                ["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic)
            });
        }
    }

    internal static object BuildGlossaryPotions()
    {
        if (!RunManager.Instance.IsInProgress)
            return GlossaryError("No run in progress.", RunNotInProgressErrorCode);

        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
            return GlossaryError("Could not read run state.", RunStateUnavailableErrorCode);

        var result = new List<Dictionary<string, object?>>();
        var seen = new HashSet<string>();

        var sharedPool = ModelDb.PotionPool<SharedPotionPool>();
        AddPotionsFromPool(sharedPool, "Shared", result, seen);

        foreach (var player in runState.Players)
        {
            var character = player.Character;
            if (character == null || character.PotionPool == null) continue;
            var pool = character.PotionPool;
            var poolName = SafeGetText(() => character.Title) ?? "Unknown";
            AddPotionsFromPool(pool, poolName, result, seen);
        }

        return result;
    }

    private static void AddPotionsFromPool(
        PotionPoolModel pool,
        string poolName,
        List<Dictionary<string, object?>> result,
        HashSet<string> seen)
    {
        foreach (var potion in pool.AllPotions)
        {
            var id = potion.Id.Entry;
            if (seen.Contains(id)) continue;
            seen.Add(id);

            result.Add(new Dictionary<string, object?>
            {
                ["id"] = id,
                ["name"] = SafeGetText(() => potion.Title),
                ["description"] = SafeGetText(() => potion.DynamicDescription),
                ["rarity"] = potion.Rarity.ToString(),
                ["target_type"] = potion.TargetType.ToString(),
                ["usage"] = potion.Usage.ToString(),
                ["pool"] = poolName,
                ["keywords"] = BuildHoverTips(potion.ExtraHoverTips)
            });
        }
    }

    internal static object BuildGlossaryKeywords()
    {
        if (!RunManager.Instance.IsInProgress)
            return GlossaryError("No run in progress.", RunNotInProgressErrorCode);

        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
            return GlossaryError("Could not read run state.", RunStateUnavailableErrorCode);

        var keywords = new Dictionary<string, string>();

        foreach (var pool in ModelDb.AllSharedCardPools)
            foreach (var card in pool.AllCards)
                AddKeywordTips(card.HoverTips, keywords);
        foreach (var relic in ModelDb.RelicPool<SharedRelicPool>().AllRelics)
            AddKeywordTips(relic.HoverTipsExcludingRelic, keywords);
        foreach (var potion in ModelDb.PotionPool<SharedPotionPool>().AllPotions)
            AddKeywordTips(potion.ExtraHoverTips, keywords);

        foreach (var player in runState.Players)
        {
            var cardPool = player.Character?.CardPool;
            if (cardPool != null)
            {
                foreach (var card in cardPool.AllCards)
                    AddKeywordTips(card.HoverTips, keywords);
            }

            var relicPool = player.Character?.RelicPool;
            if (relicPool != null)
            {
                foreach (var relic in relicPool.AllRelics)
                    AddKeywordTips(relic.HoverTipsExcludingRelic, keywords);
            }

            var potionPool = player.Character?.PotionPool;
            if (potionPool != null)
            {
                foreach (var potion in potionPool.AllPotions)
                    AddKeywordTips(potion.ExtraHoverTips, keywords);
            }
        }

        var result = new List<Dictionary<string, object?>>();
        foreach (var kv in keywords.OrderBy(k => k.Key))
        {
            result.Add(new Dictionary<string, object?>
            {
                ["name"] = kv.Key,
                ["description"] = kv.Value
            });
        }

        return result;
    }

    private static void AddKeywordTips(IEnumerable<object> tips, Dictionary<string, string> keywords)
    {
        foreach (var tip in tips)
        {
            if (tip is not HoverTip ht)
                continue;

            var title = SafeGetText(() => ht.Title);
            if (!string.IsNullOrEmpty(title))
                keywords[title!] = SafeGetText(() => ht.Description) ?? "";
        }
    }

    internal static object BuildBestiary()
    {
        var result = new Dictionary<string, object?>();
        var bindFlags = System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance;

        var monsters = new List<Dictionary<string, object?>>();
        foreach (var type in typeof(MonsterModel).Assembly.GetTypes())
        {
            if (type.IsAbstract || !type.IsSubclassOf(typeof(MonsterModel)) || type.FullName!.Contains("+")) continue;

            var entry = new Dictionary<string, object?>
            {
                ["id"] = ModelId.SlugifyCategory(type.Name),
                ["class"] = type.Name,
            };

            try
            {
                var instance = RuntimeHelpers.GetUninitializedObject(type);
                var minHp = type.GetProperty("MinInitialHp")?.GetValue(instance);
                var maxHp = type.GetProperty("MaxInitialHp")?.GetValue(instance);
                if (minHp != null) entry["min_hp"] = minHp;
                if (maxHp != null) entry["max_hp"] = maxHp;
            }
            catch { }

            var moves = new List<string>();
            foreach (var method in type.GetMethods(bindFlags))
            {
                if (method.Name.EndsWith("Move")
                    && method.DeclaringType == type
                    && method.Name != "PerformMove"
                    && method.Name != "RollMove"
                    && method.Name != "SetMoveImmediate")
                {
                    moves.Add(method.Name.Replace("Move", ""));
                }
            }
            if (moves.Count > 0)
                entry["moves"] = moves;

            monsters.Add(entry);
        }
        result["monsters"] = monsters;

        var encounters = new List<Dictionary<string, object?>>();
        foreach (var type in typeof(EncounterModel).Assembly.GetTypes())
        {
            if (type.IsAbstract || !type.IsSubclassOf(typeof(EncounterModel)) || type.FullName!.Contains("+")) continue;

            var entry = new Dictionary<string, object?>
            {
                ["id"] = ModelId.SlugifyCategory(type.Name),
                ["class"] = type.Name,
            };

            try
            {
                var instance = RuntimeHelpers.GetUninitializedObject(type);
                var roomType = type.GetProperty("RoomType")?.GetValue(instance);
                var isWeak = type.GetProperty("IsWeak")?.GetValue(instance);
                var minGold = type.GetProperty("MinGoldReward")?.GetValue(instance);
                var maxGold = type.GetProperty("MaxGoldReward")?.GetValue(instance);
                if (roomType != null) entry["room_type"] = roomType.ToString();
                if (isWeak != null) entry["is_weak"] = isWeak;
                if (minGold != null) entry["min_gold"] = minGold;
                if (maxGold != null) entry["max_gold"] = maxGold;
            }
            catch { }

            try
            {
                var allMonstersMethod = type.GetProperty("AllPossibleMonsters");
                if (allMonstersMethod != null)
                {
                    foreach (var method in type.GetMethods(bindFlags))
                    {
                        if (method.Name == "GenerateMonsters" && method.DeclaringType == type)
                            break;
                    }
                }
            }
            catch { }

            var baseName = type.Name.Replace("Normal", "").Replace("Weak", "").Replace("Elite", "").Replace("Boss", "");
            var matchingMonsters = new List<string>();
            foreach (var monsterEntry in monsters)
            {
                var monsterClass = monsterEntry["class"] as string ?? "";
                if (baseName.Contains(monsterClass) || monsterClass.Contains(baseName.TrimEnd('s')))
                    matchingMonsters.Add(monsterClass);
            }
            if (matchingMonsters.Count > 0)
                entry["likely_monsters"] = matchingMonsters;

            encounters.Add(entry);
        }
        result["encounters"] = encounters;

        return result;
    }
}
