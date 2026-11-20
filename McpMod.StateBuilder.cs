using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.ProfileScreen;
using Godot;

namespace STS2_MCP;

public static partial class McpMod
{
    private static Dictionary<string, object?> BuildGameState(bool compact = false)
    {
        var result = new Dictionary<string, object?>();
        var tree = (Godot.Engine.GetMainLoop()) as SceneTree;

        if (tree?.Root != null)
        {
            var ftueState = BuildVisibleFtueState(tree.Root);
            if (ftueState != null)
                return ftueState;
        }

        if (!RunManager.Instance.IsInProgress)
        {
            result["state_type"] = "menu";

            // Detect which menu screen is active
            if (tree?.Root != null)
            {
                if (!result.ContainsKey("menu_screen"))
                {
                // Check for singleplayer submenu (Standard / Daily / Custom)
                var spSubmenu = FindFirst<NSingleplayerSubmenu>(tree.Root);
                if (spSubmenu != null && IsNodeVisible(spSubmenu))
                {
                    result["menu_screen"] = "singleplayer";
                    result["message"] = "Select game mode.";

                    var modeOptions = new List<Dictionary<string, object?>>();
                    var modeFields = new[] { ("_standardButton", "standard"), ("_dailyButton", "daily"), ("_customButton", "custom") };
                    foreach (var (fieldName, label) in modeFields)
                    {
                        try
                        {
                            var btn = GetInstanceFieldValue(spSubmenu, fieldName);
                            if (btn is Control ctrl && IsNodeVisible(ctrl))
                            {
                                var isEnabled = btn.GetType().GetProperty("IsEnabled")?.GetValue(btn) as bool?;
                                modeOptions.Add(new Dictionary<string, object?>
                                {
                                    ["name"] = label,
                                    ["enabled"] = isEnabled ?? true
                                });
                            }
                        }
                        catch { }
                    }
                    AddMenuOptionIfVisible(modeOptions, spSubmenu, "_backButton", "back");
                    result["options"] = modeOptions;
                }
                // Check for multiplayer host submenu (Standard / Daily / Custom for multiplayer)
                else
                {
                    var mpHostSubmenu = FindFirst<NMultiplayerHostSubmenu>(tree.Root);
                    if (mpHostSubmenu != null && IsNodeVisible(mpHostSubmenu))
                    {
                        result["menu_screen"] = "multiplayer_host";
                        result["message"] = "Multiplayer host: select game mode.";

                        var modeOptions = new List<Dictionary<string, object?>>();
                        var modeFields = new[] { ("_standardButton", "standard"), ("_dailyButton", "daily"), ("_customButton", "custom") };
                        foreach (var (fieldName, label) in modeFields)
                        {
                            try
                            {
                                var btn = GetInstanceFieldValue(mpHostSubmenu, fieldName);
                                if (btn is Control ctrl && IsNodeVisible(ctrl))
                                {
                                    var isEnabled = btn.GetType().GetProperty("IsEnabled")?.GetValue(btn) as bool?;
                                    modeOptions.Add(new Dictionary<string, object?>
                                    {
                                        ["name"] = label,
                                        ["enabled"] = isEnabled ?? true
                                    });
                                }
                            }
                            catch { }
                        }
                        AddMenuOptionIfVisible(modeOptions, mpHostSubmenu, "_backButton", "back");
                        result["options"] = modeOptions;
                    }
                    else
                    {
                        // Check for multiplayer submenu (Host / Join / Load / Abandon)
                        var mpSubmenu = FindFirst<NMultiplayerSubmenu>(tree.Root);
                        if (mpSubmenu != null && IsNodeVisible(mpSubmenu))
                        {
                            result["menu_screen"] = "multiplayer";
                            result["message"] = "Multiplayer menu.";

                            var mpOptions = new List<Dictionary<string, object?>>();
                            var mpFields = new[] { ("_hostButton", "host"), ("_joinButton", "join"), ("_loadButton", "load"), ("_abandonButton", "abandon") };
                            foreach (var (fieldName, label) in mpFields)
                            {
                                try
                                {
                                    var btn = GetInstanceFieldValue(mpSubmenu, fieldName);
                                    if (btn is Control ctrl && IsNodeVisible(ctrl))
                                    {
                                        var isEnabled = btn.GetType().GetProperty("IsEnabled")?.GetValue(btn) as bool?;
                                        mpOptions.Add(new Dictionary<string, object?>
                                        {
                                            ["name"] = label,
                                            ["enabled"] = isEnabled ?? true
                                        });
                                    }
                                }
                                catch { }
                            }
                            AddMenuOptionIfVisible(mpOptions, mpSubmenu, "_backButton", "back");
                            result["options"] = mpOptions;
                        }
                    }
                }
                // Multiplayer Join Friend screen (lives in the same submenu stack as
                // NMultiplayerSubmenu; pushed when the user clicks "join")
                if (result.ContainsKey("menu_screen") == false)
                {
                    var joinScreen = FindFirst<NJoinFriendScreen>(tree.Root);
                    if (joinScreen != null && IsNodeVisible(joinScreen))
                    {
                        AddMultiplayerJoinMenuState(result, joinScreen);
                    }
                }

                // Multiplayer Load lobby — resume saved MP run. Pushed by NMultiplayerSubmenu
                // when "load" is clicked (host) or by JoinFlow when joining a save in progress (client).
                if (result.ContainsKey("menu_screen") == false)
                {
                    var loadLobby = FindFirst<NMultiplayerLoadGameScreen>(tree.Root);
                    if (loadLobby != null && IsNodeVisible(loadLobby))
                    {
                        AddMultiplayerLoadLobbyMenuState(result, loadLobby);
                    }
                }

                // Check for character select screen
                if (result.ContainsKey("menu_screen") == false)
                {
                    var charSelect = FindFirst<NCharacterSelectScreen>(tree.Root);
                    if (charSelect != null && IsNodeVisible(charSelect))
                    {
                        AddCharacterSelectMenuState(result, charSelect);
                    }
                    else
                    {
                        // Check for other screens
                        var timelineScreen = FindFirst<NTimelineScreen>(tree.Root);
                        var compendiumSubmenu = FindFirst<NCompendiumSubmenu>(tree.Root);
                        var settingsScreen = FindFirst<NSettingsScreen>(tree.Root);

                        if (timelineScreen != null && IsNodeVisible(timelineScreen))
                        {
                            result["menu_screen"] = "timeline";
                            result["message"] = "Timeline screen.";
                            result["options"] = new List<Dictionary<string, object?>>
                            {
                                new() { ["name"] = "advance", ["enabled"] = true },
                                new() { ["name"] = "back", ["enabled"] = true }
                            };

                            // Read epochs from ProgressState (stable, not hover-dependent)
                            try
                            {
                                var progress = SaveManager.Instance?.Progress;
                                if (progress != null)
                                {
                                    var epochList = new List<Dictionary<string, object?>>();
                                    var revealedCount = 0;
                                    var obtainedCount = 0;
                                    var lockedCount = 0;
                                    var noSlotCount = 0;
                                    foreach (var epoch in progress.Epochs)
                                    {
                                        var eraName = epoch.Id;
                                        var state = epoch.State.ToString();
                                        // Clean up ID to readable name
                                        var name = System.Text.RegularExpressions.Regex.Replace(eraName, @"(\d+)$", "");
                                        name = System.Text.RegularExpressions.Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])", " ");

                                        switch (epoch.State)
                                        {
                                            case EpochState.Revealed:
                                                revealedCount++;
                                                break;
                                            case EpochState.Obtained:
                                            case EpochState.ObtainedNoSlot:
                                                obtainedCount++;
                                                break;
                                            case EpochState.NotObtained:
                                                lockedCount++;
                                                break;
                                            case EpochState.NoSlot:
                                                noSlotCount++;
                                                break;
                                        }

                                        epochList.Add(new Dictionary<string, object?>
                                        {
                                            ["id"] = eraName,
                                            ["name"] = name,
                                            ["state"] = state,
                                            ["obtained"] = epoch.ObtainDate
                                        });
                                    }

                                    result["epochs"] = epochList;
                                    result["total_slots"] = epochList.Count;
                                    result["completed_count"] = revealedCount;
                                    result["revealed_count"] = revealedCount;
                                    result["obtained_unrevealed_count"] = obtainedCount;
                                    result["locked_count"] = lockedCount;
                                    result["no_slot_count"] = noSlotCount;
                                }
                            }
                            catch { }
                        }
                        else if (compendiumSubmenu != null && IsNodeVisible(compendiumSubmenu))
                        {
                            result["menu_screen"] = "compendium";
                            result["message"] = "Compendium screen.";
                            AddBackMenuOption(result, compendiumSubmenu);
                        }
                        else if (settingsScreen != null && IsNodeVisible(settingsScreen))
                        {
                            result["menu_screen"] = "settings";
                            result["message"] = "Settings screen.";
                            AddBackMenuOption(result, settingsScreen);
                        }
                        else
                        {
                            var profileScreen = FindFirst<NProfileScreen>(tree.Root);
                            if (profileScreen != null && IsNodeVisible(profileScreen))
                            {
                                result["menu_screen"] = "profile_select";
                                result["message"] = "Profile select screen.";
                                result["current_profile_id"] = SaveManager.Instance?.CurrentProfileId;

                                var options = new List<Dictionary<string, object?>>();
                                var buttons = GetInstanceFieldValue(profileScreen, "_profileButtons") as System.Collections.IEnumerable;
                                if (buttons != null)
                                {
                                    foreach (var btn in buttons)
                                    {
                                        var btnId = GetInstanceFieldValue(btn, "_profileId");
                                        if (btnId is int id)
                                        {
                                            var enabled = btn.GetType().GetProperty("IsEnabled")?.GetValue(btn) as bool?;
                                            options.Add(new Dictionary<string, object?>
                                            {
                                                ["name"] = $"profile_{id}",
                                                ["enabled"] = enabled ?? true
                                            });
                                        }
                                    }
                                }

                                var backBtn = GetInstanceFieldValue(profileScreen, "_backButton")
                                    ?? FindFirst<NBackButton>(profileScreen);
                                if (backBtn is NClickableControl backClickable && IsNodeVisible(backClickable))
                                {
                                    options.Add(new Dictionary<string, object?>
                                    {
                                        ["name"] = "back",
                                        ["enabled"] = backClickable.IsEnabled
                                    });
                                }

                                if (options.Count > 0)
                                    result["options"] = options;
                            }
                        }
                        if (!result.ContainsKey("menu_screen"))
                        {
                            result["menu_screen"] = "main";
                            result["message"] = "Main menu.";

                        var mainMenu = FindFirst<NMainMenu>(tree.Root);
                        if (mainMenu != null)
                        {
                            var options = new List<string>();
                            var blockedOptions = new List<Dictionary<string, object?>>();
                            var fields = new[] { "_continueButton", "_abandonRunButton", "_singleplayerButton", "_multiplayerButton", "_compendiumButton", "_timelineButton", "_settingsButton", "_quitButton" };
                            var labels = new[] { "continue", "abandon_run", "singleplayer", "multiplayer", "compendium", "timeline", "settings", "quit" };
                            var unrevealedEpochs = GetProgressEpochIdsByState("Obtained", "ObtainedNoSlot");
                            for (int i = 0; i < fields.Length; i++)
                            {
                                try
                                {
                                    var btn = GetInstanceFieldValue(mainMenu, fields[i]);
                                    if (btn is NClickableControl clickable &&
                                        clickable.IsEnabled &&
                                        clickable.Visible &&
                                        clickable.IsVisibleInTree())
                                    {
                                        if (labels[i] == "timeline" && unrevealedEpochs.Count > 0)
                                        {
                                            blockedOptions.Add(new Dictionary<string, object?>
                                            {
                                                ["name"] = "timeline",
                                                ["enabled"] = false,
                                                ["reason"] = "manual_epoch_reveal_required",
                                                ["pending_epoch_ids"] = unrevealedEpochs
                                            });
                                            continue;
                                        }

                                        options.Add(labels[i]);
                                    }
                                }
                                catch { }
                            }
                            if (options.Count > 0)
                                result["options"] = options;
                            if (blockedOptions.Count > 0)
                                result["blocked_options"] = blockedOptions;
                        }
                        }
                    }
                }
            }
            } // close if (!result.ContainsKey("menu_screen"))
            else
            {
                result["message"] = "No run in progress.";
            }

            return result;
        }

        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            if (tree?.Root != null)
            {
                var activeCharSelect = FindFirst<NCharacterSelectScreen>(tree.Root);
                if (activeCharSelect != null && IsNodeVisible(activeCharSelect))
                {
                    AddCharacterSelectMenuState(result, activeCharSelect);
                    return result;
                }
            }

            result["state_type"] = "unknown";
            return result;
        }

        // Overlays can appear on top of any room (events, rest sites, combat).
        // Rewards/card-reward overlays defer to the map - they may linger on the
        // overlay stack while the map opens after the player clicks proceed.
        var topOverlay = NOverlayStack.Instance?.Peek();
        var currentRoom = runState.CurrentRoom;
        bool mapIsOpen = IsMapScreenOpenOrVisible();
        if (topOverlay is NCardGridSelectionScreen cardSelectScreen)
        {
            result["state_type"] = "card_select";
            result["card_select"] = BuildCardSelectState(cardSelectScreen, runState);
        }
        else if (topOverlay is NChooseACardSelectionScreen chooseCardScreen)
        {
            result["state_type"] = "card_select";
            result["card_select"] = BuildChooseCardState(chooseCardScreen, runState);
        }
        else if (topOverlay is NChooseABundleSelectionScreen bundleScreen)
        {
            result["state_type"] = "bundle_select";
            result["bundle_select"] = BuildBundleSelectState(bundleScreen, runState);
        }
        else if (topOverlay is NChooseARelicSelection relicSelectScreen)
        {
            result["state_type"] = "relic_select";
            result["relic_select"] = BuildRelicSelectState(relicSelectScreen, runState);
        }
        else if (!mapIsOpen && topOverlay is NCrystalSphereScreen crystalSphereScreen)
        {
            // Mirror the NRewardsScreen guard below: the Crystal Sphere overlay
            // lingers on the stack after proceed is clicked (only ClearScreens
            // on the next room transition pops it). Once the map opens on top,
            // report "map" so state matches the screen the player can interact
            // with. See issue #73.
            result["state_type"] = "crystal_sphere";
            result["crystal_sphere"] = BuildCrystalSphereState(crystalSphereScreen);
        }
        else if (!mapIsOpen && topOverlay is NCardRewardSelectionScreen cardRewardScreen)
        {
            result["state_type"] = "card_reward";
            result["card_reward"] = BuildCardRewardState(cardRewardScreen);
        }
        else if (!mapIsOpen && topOverlay is NRewardsScreen rewardsScreen)
        {
            result["state_type"] = "rewards";
            result["rewards"] = BuildRewardsState(rewardsScreen, runState);
        }
        else if (topOverlay is NGameOverScreen gameOverScreen)
        {
            SetGameOverState(result, gameOverScreen);
        }
        else if (topOverlay is IOverlayScreen
                 && topOverlay is not NRewardsScreen
                 && topOverlay is not NCardRewardSelectionScreen
                 && topOverlay is not NCrystalSphereScreen)
        {
            // Catch-all for unhandled overlays - prevents soft-locks.
            // Overlays that linger on the stack while the map takes over
            // (rewards, card reward, Crystal Sphere) are excluded so the
            // fallback below reports "map" once mapIsOpen is true.
            result["state_type"] = "overlay";
            result["overlay"] = new Dictionary<string, object?>
            {
                ["screen_type"] = topOverlay.GetType().Name,
                ["message"] = $"An overlay ({topOverlay.GetType().Name}) is active. It may require manual interaction in-game."
            };
        }
        else if (mapIsOpen)
        {
            result["state_type"] = "map";
            result["map"] = BuildMapState(runState);
        }
        else if (currentRoom is CombatRoom combatRoom)
        {
            if (CombatManager.Instance.IsInProgress)
            {
                // Check for in-combat hand card selection (e.g., "Select a card to exhaust")
                var playerHand = NPlayerHand.Instance;
                if (playerHand != null && playerHand.IsInCardSelection)
                {
                    result["state_type"] = "hand_select";
                    result["hand_select"] = BuildHandSelectState(playerHand, runState);
                    result["battle"] = BuildBattleState(runState, combatRoom);
                }
                else
                {
                    result["state_type"] = combatRoom.RoomType.ToString().ToLower(); // monster, elite, boss
                    result["battle"] = BuildBattleState(runState, combatRoom);
                }
            }
            else
            {
                // After combat ends - reward/card overlays are caught by top-level checks above.
                // Only handle map and the brief transition before rewards appear.
                if (IsMapScreenOpenOrVisible())
                {
                    result["state_type"] = "map";
                    result["map"] = BuildMapState(runState);
                }
                else
                {
                    result["state_type"] = combatRoom.RoomType.ToString().ToLower();
                    result["message"] = "Combat ended. Waiting for rewards...";
                }
            }
        }
        else if (currentRoom is EventRoom eventRoom)
        {
            if (IsMapScreenOpenOrVisible())
            {
                result["state_type"] = "map";
                result["map"] = BuildMapState(runState);
            }
            else if (eventRoom.CanonicalEvent is FakeMerchant)
            {
                result["state_type"] = "fake_merchant";
                result["fake_merchant"] = BuildFakeMerchantState(eventRoom, runState);
            }
            else
            {
                result["state_type"] = "event";
                result["event"] = BuildEventState(eventRoom, runState);
            }
        }
        else if (currentRoom is MapRoom)
        {
            result["state_type"] = "map";
            result["map"] = BuildMapState(runState);
        }
        else if (currentRoom is MerchantRoom merchantRoom)
        {
            if (IsMapScreenOpenOrVisible())
            {
                result["state_type"] = "map";
                result["map"] = BuildMapState(runState);
            }
            else
            {
                result["state_type"] = "shop";
                result["shop"] = BuildShopState(merchantRoom, runState);
            }
        }
        else if (currentRoom is RestSiteRoom restSiteRoom)
        {
            if (IsMapScreenOpenOrVisible())
            {
                result["state_type"] = "map";
                result["map"] = BuildMapState(runState);
            }
            else
            {
                result["state_type"] = "rest_site";
                result["rest_site"] = BuildRestSiteState(restSiteRoom, runState);
            }
        }
        else if (currentRoom is TreasureRoom treasureRoom)
        {
            if (IsMapScreenOpenOrVisible())
            {
                result["state_type"] = "map";
                result["map"] = BuildMapState(runState);
            }
            else
            {
                result["state_type"] = "treasure";
                result["treasure"] = BuildTreasureState(treasureRoom, runState);
            }
        }
        else if (currentRoom == null && tree?.Root != null)
        {
            var activeCharSelect = FindFirst<NCharacterSelectScreen>(tree.Root);
            if (activeCharSelect != null && IsNodeVisible(activeCharSelect))
            {
                AddCharacterSelectMenuState(result, activeCharSelect);
            }
            else
            {
                result["state_type"] = "unknown";
                result["room_type"] = currentRoom?.GetType().Name;
            }
        }
        else
        {
            result["state_type"] = "unknown";
            result["room_type"] = currentRoom?.GetType().Name;
        }

        // Common run info
        result["run"] = new Dictionary<string, object?>
        {
            ["act"] = runState.CurrentActIndex + 1,
            ["floor"] = runState.TotalFloor,
            ["ascension"] = runState.AscensionLevel,
            ["freed_repy"] = runState.ExtraFields.FreedRepy
        };

        // Always include full player data (relics, potions, deck, etc.) on every screen
        var _player = LocalContext.GetMe(runState);
        if (_player != null)
        {
            result["player"] = BuildPlayerState(_player, compact);
        }

        AddCombatWarnings(result, _player?.Creature);
        return result;
    }

    private static void SetGameOverState(Dictionary<string, object?> state, NGameOverScreen screen)
    {
        // The native game-over banner reads RunHistory.Win from this screen's history.
        // In co-op the local player can be dead even when the party won.
        var history = GetInstanceFieldValue(screen, "_history");
        var win = history?.GetType().GetProperty("Win")?.GetValue(history) as bool?;
        var (outcome, message) = win switch
        {
            true => ("victory", "Victory!"),
            false => ("defeat", "Defeat."),
            null => ("unknown", "Run ended.")
        };

        state["state_type"] = "game_over";
        state["game_over"] = new Dictionary<string, object?>
        {
            ["outcome"] = outcome,
            ["message"] = message,
            ["options"] = new List<string> { "main_menu" }
        };
    }

    private static void AddCharacterSelectMenuState(
        Dictionary<string, object?> result,
        NCharacterSelectScreen charSelect)
    {
        result["state_type"] = "menu";
        result["menu_screen"] = "character_select";
        result["message"] = "Select a character.";

        var buttons = FindAll<NCharacterSelectButton>(charSelect);
        var characters = new List<Dictionary<string, object?>>();
        var options = new List<Dictionary<string, object?>>();
        foreach (var btn in buttons)
        {
            try
            {
                if (btn.Character is { } cm && IsNodeVisible(btn))
                {
                    var characterId = cm.Id.Entry;
                    var characterName = SafeGetText(() => cm.Title);
                    options.Add(new Dictionary<string, object?>
                    {
                        ["name"] = characterId,
                        ["enabled"] = !btn.IsLocked
                    });

                    var charData = new Dictionary<string, object?>
                    {
                        ["name"] = characterName,
                        ["id"] = characterId,
                        ["locked"] = btn.IsLocked,
                        ["hp"] = cm.StartingHp,
                        ["gold"] = cm.StartingGold,
                        ["energy"] = cm.MaxEnergy,
                        ["description"] = SafeGetText(() => cm.CardsModifierDescription),
                    };

                    var startRelics = new List<Dictionary<string, object?>>();
                    foreach (var relic in cm.StartingRelics)
                    {
                        startRelics.Add(new Dictionary<string, object?>
                        {
                            ["name"] = SafeGetText(() => relic.Title),
                            ["description"] = SafeGetText(() => relic.DynamicDescription)
                        });
                    }
                    if (startRelics.Count > 0)
                        charData["starting_relics"] = startRelics;

                    var deckCards = new List<string>();
                    foreach (var card in cm.StartingDeck)
                        deckCards.Add(SafeGetText(() => card.Title) ?? "?");
                    if (deckCards.Count > 0)
                        charData["starting_deck"] = deckCards;

                    try
                    {
                        var allCards = cm.CardPool?.AllCards;
                        if (allCards != null)
                            charData["total_cards"] = System.Linq.Enumerable.Count(allCards);
                    }
                    catch { }

                    try
                    {
                        var allRelics = cm.RelicPool?.AllRelics;
                        if (allRelics != null)
                            charData["total_relics"] = System.Linq.Enumerable.Count(allRelics);
                    }
                    catch { }

                    try
                    {
                        var allPotions = cm.PotionPool?.AllPotions;
                        if (allPotions != null)
                            charData["total_potions"] = System.Linq.Enumerable.Count(allPotions);
                    }
                    catch { }

                    characters.Add(charData);
                }
            }
            catch { }
        }
        if (characters.Count > 0)
            result["characters"] = characters;

        var embarkBtn = GetInstanceFieldValue(charSelect, "_embarkButton");
        if (embarkBtn is NClickableControl embarkClickable && IsNodeVisible(embarkClickable))
        {
            options.Add(new Dictionary<string, object?>
            {
                ["name"] = "confirm",
                ["enabled"] = embarkClickable.IsEnabled
            });
            options.Add(new Dictionary<string, object?>
            {
                ["name"] = "embark",
                ["enabled"] = embarkClickable.IsEnabled
            });
        }

        // _backButton and _unreadyButton are surfaced as distinct options so MP callers
        // can distinguish "leave the lobby" from "retract my ready vote". In SP, only
        // _backButton ever becomes enabled. See NCharacterSelectScreen.OnEmbarkPressed /
        // OnUnreadyPressed for the toggle logic.
        var backBtn = GetInstanceFieldValue(charSelect, "_backButton");
        if (backBtn is NClickableControl backClickable && IsNodeVisible(backClickable))
        {
            options.Add(new Dictionary<string, object?>
            {
                ["name"] = "back",
                ["enabled"] = backClickable.IsEnabled
            });
        }

        // MP lobby block — surfaces roster / ready state / ascension when this character
        // select is part of a host or client lobby. SP runs leave the field absent.
        bool isMpCharSelect = false;
        try
        {
            var lobby = charSelect.Lobby;
            if (lobby != null && lobby.NetService != null && lobby.NetService.Type.IsMultiplayer())
            {
                isMpCharSelect = true;
                result["lobby"] = BuildStartRunLobbyState(lobby);
            }
        }
        catch { }

        // _unreadyButton is part of the scene in SP too but never becomes enabled there.
        // Only surface it as an option in MP, where it has a real role.
        if (isMpCharSelect)
        {
            var unreadyBtn = GetInstanceFieldValue(charSelect, "_unreadyButton");
            if (unreadyBtn is NClickableControl unreadyClickable && IsNodeVisible(unreadyClickable))
            {
                options.Add(new Dictionary<string, object?>
                {
                    ["name"] = "unready",
                    ["enabled"] = unreadyClickable.IsEnabled
                });
            }
        }

        if (options.Count > 0)
            result["options"] = options;
    }

    // v0.111: StartRunLobby no longer exposes a public MaxPlayers; read the private
    // backing field (set from the ctor's maxPlayers arg). Falls back to current count.
    private static int GetStartRunLobbyMaxPlayers(StartRunLobby lobby)
    {
        try
        {
            var f = typeof(StartRunLobby).GetField("_maxPlayers",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (f != null && f.GetValue(lobby) is int n)
                return n;
        }
        catch { }
        return lobby.Players.Count;
    }

    private static Dictionary<string, object?> BuildStartRunLobbyState(StartRunLobby lobby)
    {
        var lobbyState = new Dictionary<string, object?>
        {
            ["type"] = lobby.NetService.Type switch
            {
                NetGameType.Host => "host",
                NetGameType.Client => "client",
                NetGameType.Singleplayer => "singleplayer",
                _ => lobby.NetService.Type.ToString().ToLowerInvariant()
            },
            ["game_mode"] = lobby.GameMode.ToString().ToLowerInvariant(),
            ["max_players"] = GetStartRunLobbyMaxPlayers(lobby),
            ["ascension"] = lobby.Ascension,
            ["max_ascension"] = lobby.MaxAscension,
            ["all_ready"] = lobby.Players.Count > 0 && lobby.Players.All(p => p.isReady),
            ["is_about_to_begin"] = SafeIsAboutToBeginGame(lobby)
        };

        // is_local_ready === local player has hit Embark in MP and is now waiting.
        // Mirrors NCharacterSelectScreen._readyAndWaitingContainer.Visible.
        try
        {
            var local = lobby.LocalPlayer;
            lobbyState["is_local_ready"] = local.isReady;
            lobbyState["local_player_id"] = local.id.ToString();
        }
        catch { }

        var players = new List<Dictionary<string, object?>>();
        ulong localId;
        try { localId = lobby.LocalPlayer.id; } catch { localId = 0; }
        ulong hostId = lobby.NetService.Type == NetGameType.Host ? localId : 0;

        foreach (var p in lobby.Players)
        {
            var entry = new Dictionary<string, object?>
            {
                ["id"] = p.id.ToString(),
                ["slot_id"] = p.slotId,
                ["is_local"] = p.id == localId,
                // We can only positively identify the host as "us" when we ARE the host;
                // a client doesn't know which remote id is the host without inspecting
                // the net service. Keep it simple and only flag is_host=true for self
                // when hosting — clients can infer host-ness by player_id when needed.
                ["is_host"] = p.id == hostId && hostId != 0,
                ["character"] = SafeGetText(() => p.character?.Title)
                                ?? p.character?.Id.Entry,
                ["character_id"] = p.character?.Id.Entry,
                ["is_ready"] = p.isReady,
                ["platform_name"] = SafeGetPlayerName(lobby.NetService.Platform, p.id)
            };
            players.Add(entry);
        }
        lobbyState["players"] = players;
        lobbyState["player_count"] = players.Count;

        if (!string.IsNullOrEmpty(lobby.Seed))
            lobbyState["seed"] = lobby.Seed;

        return lobbyState;
    }

    private static bool SafeIsAboutToBeginGame(StartRunLobby lobby)
    {
        try { return lobby.IsAboutToBeginGame(); }
        catch { return false; }
    }

    private static string? SafeGetPlayerName(PlatformType platform, ulong playerId)
    {
        try { return PlatformUtil.GetPlayerName(platform, playerId); }
        catch { return null; }
    }

    private static void AddMultiplayerJoinMenuState(
        Dictionary<string, object?> result,
        NJoinFriendScreen joinScreen)
    {
        result["state_type"] = "menu";
        result["menu_screen"] = "multiplayer_join";

        // FastMP: when Steam isn't initialized OR --fastmp is set, OnSubmenuOpened auto-
        // joins localhost:33771 instead of presenting friends. This is a debug/local-dev
        // path. We surface it so callers don't try to hit "refresh" expecting a list.
        bool fastMp = !SteamInitializer.Initialized || CommandLineHelper.HasArg("fastmp");
        result["fast_mp"] = fastMp;

        var loadingFriends = GetInstanceFieldValue(joinScreen, "_loadingFriendsIndicator") as Control;
        var loadingOverlay = GetInstanceFieldValue(joinScreen, "_loadingOverlay") as Control;
        bool loading = (loadingFriends != null && loadingFriends.Visible)
                       || (loadingOverlay != null && loadingOverlay.Visible);
        result["loading"] = loading;

        var noFriendsLabel = GetInstanceFieldValue(joinScreen, "_noFriendsLabel") as Control;
        bool noFriends = noFriendsLabel != null && noFriendsLabel.Visible;
        result["no_friends"] = noFriends;

        var friends = new List<Dictionary<string, object?>>();
        var options = new List<Dictionary<string, object?>>();

        var buttonContainer = GetInstanceFieldValue(joinScreen, "_buttonContainer") as Control;
        if (buttonContainer != null)
        {
            int index = 0;
            foreach (var child in buttonContainer.GetChildren())
            {
                if (child is NJoinFriendButton friendBtn)
                {
                    string? name = null;
                    try { name = PlatformUtil.GetPlayerName(PlatformUtil.PrimaryPlatform, friendBtn.PlayerId); }
                    catch { }

                    friends.Add(new Dictionary<string, object?>
                    {
                        ["index"] = index,
                        ["name"] = name,
                        ["player_id"] = friendBtn.PlayerId.ToString(),
                        ["enabled"] = friendBtn.IsEnabled
                    });
                    options.Add(new Dictionary<string, object?>
                    {
                        ["name"] = $"join_{index}",
                        ["enabled"] = friendBtn.IsEnabled
                    });
                    index++;
                }
            }
        }
        result["friends"] = friends;

        var refreshBtn = GetInstanceFieldValue(joinScreen, "_refreshButton") as NClickableControl;
        if (refreshBtn != null && IsNodeVisible(refreshBtn))
        {
            options.Add(new Dictionary<string, object?>
            {
                ["name"] = "refresh",
                ["enabled"] = refreshBtn.IsEnabled && !loading
            });
        }
        AddMenuOptionIfVisible(options, joinScreen, "_backButton", "back");

        if (fastMp)
        {
            result["message"] = loading
                ? "FastMP join flow is connecting to localhost:33771..."
                : "FastMP mode (no Steam): join screen auto-connects to localhost:33771.";
        }
        else if (loading)
        {
            result["message"] = "Refreshing friend list...";
        }
        else if (noFriends)
        {
            result["message"] = "No friends with open lobbies. Use 'refresh' to retry, or 'back' to return.";
        }
        else
        {
            result["message"] = "Pick a friend to join, or 'refresh' to update the list.";
        }

        result["options"] = options;
    }

    private static void AddMultiplayerLoadLobbyMenuState(
        Dictionary<string, object?> result,
        NMultiplayerLoadGameScreen loadLobby)
    {
        result["state_type"] = "menu";
        result["menu_screen"] = "multiplayer_load_lobby";

        var lobby = GetInstanceFieldValue(loadLobby, "_runLobby") as LoadRunLobby;
        if (lobby != null)
        {
            var info = new Dictionary<string, object?>
            {
                ["type"] = lobby.NetService.Type switch
                {
                    NetGameType.Host => "host",
                    NetGameType.Client => "client",
                    _ => lobby.NetService.Type.ToString().ToLowerInvariant()
                },
                ["game_mode"] = lobby.GameMode.ToString().ToLowerInvariant(),
                ["ascension"] = lobby.Run?.Ascension ?? 0,
                ["act"] = (lobby.Run?.CurrentActIndex ?? 0) + 1,
                ["floor"] = lobby.Run?.VisitedMapCoords?.Count ?? 0
            };

            try
            {
                var localPlayer = lobby.Run?.Players?.FirstOrDefault(p => p.NetId == lobby.NetService.NetId);
                if (localPlayer != null)
                {
                    info["character_id"] = localPlayer.CharacterId?.Entry;
                    info["current_hp"] = localPlayer.CurrentHp;
                    info["max_hp"] = localPlayer.MaxHp;
                    info["gold"] = localPlayer.Gold;
                }
            }
            catch { }

            info["expected_player_count"] = lobby.Run?.Players?.Count ?? 0;
            // v0.111: connected players are the lobby's Players (PlayerCount/PlayerIds);
            // the old ConnectedPlayerIds property was removed.
            info["connected_player_count"] = lobby.PlayerCount;

            // v0.111 restores LoadRunLobby.IsAboutToBeginGame(); use it directly.
            bool aboutToBegin = false;
            try { aboutToBegin = lobby.IsAboutToBeginGame(); }
            catch { }
            info["all_ready"] = aboutToBegin;
            info["is_about_to_begin"] = aboutToBegin;

            // Per-player ready/connected breakdown
            var players = new List<Dictionary<string, object?>>();
            try
            {
                if (lobby.Run?.Players != null)
                {
                    foreach (var sp in lobby.Run.Players)
                    {
                        bool isConnected = lobby.PlayerIds.Contains(sp.NetId);
                        bool isReady = false;
                        try { isReady = lobby.IsPlayerReady(sp.NetId); } catch { }
                        players.Add(new Dictionary<string, object?>
                        {
                            ["id"] = sp.NetId.ToString(),
                            ["is_local"] = sp.NetId == lobby.NetService.NetId,
                            ["character_id"] = sp.CharacterId?.Entry,
                            ["is_connected"] = isConnected,
                            ["is_ready"] = isReady,
                            ["platform_name"] = SafeGetPlayerName(lobby.NetService.Platform, sp.NetId)
                        });
                    }
                }
            }
            catch { }
            info["players"] = players;

            result["lobby"] = info;
        }

        var options = new List<Dictionary<string, object?>>();

        var confirmBtn = GetInstanceFieldValue(loadLobby, "_confirmButton") as NClickableControl;
        if (confirmBtn != null && IsNodeVisible(confirmBtn))
        {
            options.Add(new Dictionary<string, object?>
            {
                ["name"] = "confirm",
                ["enabled"] = confirmBtn.IsEnabled
            });
            options.Add(new Dictionary<string, object?>
            {
                ["name"] = "embark",
                ["enabled"] = confirmBtn.IsEnabled
            });
        }

        var backBtn2 = GetInstanceFieldValue(loadLobby, "_backButton") as NClickableControl;
        if (backBtn2 != null && IsNodeVisible(backBtn2))
        {
            options.Add(new Dictionary<string, object?>
            {
                ["name"] = "back",
                ["enabled"] = backBtn2.IsEnabled
            });
        }

        var unreadyBtn2 = GetInstanceFieldValue(loadLobby, "_unreadyButton") as NClickableControl;
        if (unreadyBtn2 != null && IsNodeVisible(unreadyBtn2))
        {
            options.Add(new Dictionary<string, object?>
            {
                ["name"] = "unready",
                ["enabled"] = unreadyBtn2.IsEnabled
            });
        }

        result["options"] = options;
        result["message"] = "Multiplayer load lobby. Confirm to ready up; once everyone is connected and ready, the run resumes.";
    }

    private static void AddBackMenuOption(Dictionary<string, object?> state, Control screen)
    {
        if (GetVisibleMenuBackButton(screen) is { } back)
            state["options"] = new List<Dictionary<string, object?>>
            {
                new() { ["name"] = "back", ["enabled"] = back.IsEnabled }
            };
    }

    private static Dictionary<string, object?> BuildLoadingBattleState() => new()
    {
        ["error"] = "Combat state unavailable",
        ["can_act"] = false,
        ["action_blocked_reason"] = "combat_loading",
        ["action_blocked_message"] = "Wait for combat to finish loading."
    };

    private static Dictionary<string, object?> BuildBattleState(RunState runState, CombatRoom combatRoom)
    {
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null)
            return BuildLoadingBattleState();

        var battle = new Dictionary<string, object?>();
        battle["round"] = combatState.RoundNumber;
        battle["turn"] = combatState.CurrentSide.ToString().ToLower();
        battle["is_play_phase"] = IsPlayPhase(combatState);
        AddCombatReadiness(battle, LocalContext.GetMe(runState));

        battle["enemies"] = BuildEnemiesState(combatState);

        return battle;
    }

    private static Dictionary<string, object?> BuildPlayerState(Player player, bool compact = false)
    {
        var state = new Dictionary<string, object?>();
        var creature = player.Creature;
        var combatState = player.PlayerCombatState;
        bool inCombat = CombatManager.Instance.IsInProgress;

        state["character"] = SafeGetText(() => player.Character.Title);
        state["hp"] = creature.CurrentHp;
        state["max_hp"] = creature.MaxHp;
        state["block"] = creature.Block;

        // PlayerCombatState can linger after combat while on map/rest/shop. Energy/MaxEnergy getters
        // run hooks (e.g. Hook.ModifyMaxEnergy) that null-ref without a live combat - only serialize
        // combat fields when a fight is actually in progress.
        if (combatState != null && inCombat)
        {
            state["energy"] = combatState.Energy;
            state["max_energy"] = combatState.MaxEnergy;

            // Stars (The Regent's resource, conditionally shown)
            if (player.Character.ShouldAlwaysShowStarCounter || combatState.Stars > 0)
            {
                state["stars"] = combatState.Stars;
            }
            if (player.Character.BaseArrowSlotCount > 0 || combatState.Arrows.Count > 0)
            {
                state["arrow_count"] = combatState.AvailableArrowCount;
                state["arrow_slots"] = combatState.Arrows.Count;
            }

            // Hand
            var hand = new List<Dictionary<string, object?>>();
            int cardIndex = 0;
            foreach (var card in combatState.Hand.Cards)
            {
                hand.Add(BuildCardState(card, cardIndex));
                cardIndex++;
            }
            state["hand"] = hand;

            // Pile counts
            state["draw_pile_count"] = combatState.DrawPile.Cards.Count;
            state["discard_pile_count"] = combatState.DiscardPile.Cards.Count;
            state["exhaust_pile_count"] = combatState.ExhaustPile.Cards.Count;

            // Pile contents (draw pile sorted by rarity then card ID, matching in-game display)
            if (!compact)
            {
                var drawCards = combatState.DrawPile.Cards.ToList();
                drawCards.Sort((c1, c2) => c1.Rarity != c2.Rarity
                    ? c1.Rarity.CompareTo(c2.Rarity)
                    : string.Compare(c1.Id.Entry, c2.Id.Entry, StringComparison.Ordinal));
                state["draw_pile"] = BuildPileCardList(drawCards, PileType.Draw);
                state["discard_pile"] = BuildPileCardList(combatState.DiscardPile.Cards, PileType.Discard);
                state["exhaust_pile"] = BuildPileCardList(combatState.ExhaustPile.Cards, PileType.Exhaust);
            }

            // Orbs
            var orbQueue = combatState.OrbQueue;
            if (orbQueue != null && orbQueue.Capacity > 0)
            {
                var orbs = new List<Dictionary<string, object?>>();
                foreach (var orb in orbQueue.Orbs)
                {
                    // Populate SmartDescription placeholders with Focus-modified values,
                    // mirroring OrbModel.HoverTips getter (OrbModel.cs:92-94)
                    string? description = SafeGetText(() =>
                    {
                        var desc = orb.SmartDescription;
                        desc.Add("energyPrefix", orb.Owner.Character.CardPool.Title);
                        desc.Add("Passive", orb.PassiveVal);
                        desc.Add("Evoke", orb.EvokeVal);
                        return desc;
                    });
                    orbs.Add(new Dictionary<string, object?>
                    {
                        ["id"] = orb.Id.Entry,
                        ["name"] = SafeGetText(() => orb.Title),
                        ["description"] = description,
                        ["passive_val"] = orb.PassiveVal,
                        ["evoke_val"] = orb.EvokeVal,
                        ["keywords"] = BuildHoverTips(orb.HoverTips)
                    });
                }
                state["orbs"] = orbs;
                state["orb_slots"] = orbQueue.Capacity;
                state["orb_empty_slots"] = orbQueue.Capacity - orbQueue.Orbs.Count;
            }

            var pets = BuildPetsState(player);
            if (pets.Count > 0)
            {
                state["pets"] = pets;
            }
        }

        state["gold"] = player.Gold;
        state["deck_count"] = player.Deck.Cards.Count;
        if (!compact && !inCombat)
            state["deck"] = player.Deck.Cards.Select(BuildDeckCardIdentity).ToList();

        var questCards = player.Deck.Cards.Where(card => card.Type == CardType.Quest)
            .Select(BuildQuestCardInfo)
            .ToList();
        if (questCards.Count > 0)
            state["quest_cards"] = questCards;

        if (player.ExtraFields.WongoPoints > 0)
            state["wongo_points"] = player.ExtraFields.WongoPoints;

        // Powers (status effects)
        state["status"] = BuildPowersState(creature);

        // Relics
        var relics = new List<Dictionary<string, object?>>();
        foreach (var relic in player.Relics)
        {
            var info = new Dictionary<string, object?>
            {
                ["id"] = relic.Id.Entry,
                ["name"] = SafeGetText(() => relic.Title),
                ["description"] = SafeGetText(() => relic.DynamicDescription),
                ["counter"] = relic.ShowCounter ? relic.DisplayAmount : null,
                ["status"] = relic.Status.ToString(),
                ["is_used_up"] = relic.IsUsedUp,
                ["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic)
            };
            if (relic is SilverCrucible crucible)
            {
                info["card_rewards_remaining"] = Math.Max(0, crucible.DisplayAmount);
                info["empty_chest_pending"] = crucible.TreasureRoomsEntered == 0;
            }
            relics.Add(info);
        }
        state["relics"] = relics;

        // Potions
        var potions = new List<Dictionary<string, object?>>();
        int slotIndex = 0;
        foreach (var potion in player.PotionSlots)
        {
            if (potion != null)
            {
                potions.Add(new Dictionary<string, object?>
                {
                    ["id"] = potion.Id.Entry,
                    ["name"] = SafeGetText(() => potion.Title),
                    ["description"] = SafeGetText(() => potion.DynamicDescription),
                    ["slot"] = slotIndex,
                    ["can_use_in_combat"] = potion.Usage == PotionUsage.CombatOnly || potion.Usage == PotionUsage.AnyTime,
                    ["target_type"] = potion.TargetType.ToString(),
                    ["keywords"] = BuildHoverTips(potion.ExtraHoverTips)
                });
            }
            slotIndex++;
        }
        state["potions"] = potions;
        state["max_potion_slots"] = player.MaxPotionCount;
        state["can_use_or_remove_potions"] = player.CanUseOrRemovePotions;

        return state;
    }

    private static Dictionary<string, object?> BuildDeckCardIdentity(CardModel card)
    {
        var info = new Dictionary<string, object?>
        {
            ["id"] = card.Id.Entry,
            ["name"] = SafeGetText(() => card.Title),
            ["type"] = card.Type.ToString(),
            ["upgrade_level"] = card.CurrentUpgradeLevel
        };
        if (card.Enchantment is { } enchantment)
            info["enchantment_id"] = enchantment.Id.Entry;
        if (card.Affliction is { } affliction)
            info["affliction_id"] = affliction.Id.Entry;
        return info;
    }

    private const int LanternKeyTargetActIndex = 2;

    private static Dictionary<string, object?> BuildQuestCardInfo(CardModel card)
    {
        var info = BuildDeckCardIdentity(card);
        info["description"] = SafeGetCardDescription(card, PileType.Deck);

        if (card is SpoilsMap spoils)
        {
            info["target_act"] = spoils.SpoilsActIndex + 1;
            if (spoils.SpoilsCoord is { } coord)
                info["target_node"] = new Dictionary<string, object?> { ["col"] = coord.col, ["row"] = coord.row };
        }
        else if (card is LanternKey)
        {
            info["target_act"] = LanternKeyTargetActIndex + 1;
        }
        return info;
    }

    private static string GetCostDisplay(CardModel card)
        => card.EnergyCost.CostsX ? "X" : card.EnergyCost.GetAmountToSpend().ToString();

    private static string? GetStarCostDisplay(CardModel card)
    {
        if (card.HasStarCostX) return "X";
        if (card.CurrentStarCost >= 0) return card.GetStarCostWithModifiers().ToString();
        return null;
    }

    /// <summary>
    /// Builds the common card display fields shared across all card serialization contexts.
    /// Callers merge context-specific fields (e.g. index, can_play, target_type) on top.
    /// </summary>
    private static Dictionary<string, object?> BuildCardInfo(CardModel card, PileType pile = PileType.None)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = card.Id.Entry,
            ["name"] = SafeGetText(() => card.Title),
            ["type"] = card.Type.ToString(),
            ["cost"] = GetCostDisplay(card),
            ["star_cost"] = GetStarCostDisplay(card),
            ["description"] = SafeGetCardDescription(card, pile),
            ["rarity"] = card.Rarity.ToString(),
            ["is_upgraded"] = card.IsUpgraded,
            ["keywords"] = BuildHoverTips(card.HoverTips)
        };
    }

    private static Dictionary<string, object?> BuildCardState(CardModel card, int index)
    {
        card.CanPlay(out var unplayableReason, out _);

        var state = BuildCardInfo(card);
        state["index"] = index;
        state["target_type"] = card.TargetType.ToString();
        state["can_play"] = unplayableReason == UnplayableReason.None;
        state["unplayable_reason"] = unplayableReason != UnplayableReason.None ? unplayableReason.ToString() : null;
        return state;
    }

    private static void AddPreviewCardsFromContainer(
        Godot.Control? container,
        List<Dictionary<string, object?>> previewCards,
        Func<CardModel, CardModel>? projectCard = null)
    {
        if (container?.Visible != true)
            return;

        var cardHolders = FindAllSortedByPosition<NCardHolder>(container);
        if (cardHolders.Count > 0)
        {
            foreach (var holder in cardHolders)
            {
                var card = holder.CardModel;
                if (card == null) continue;

                var cardInfo = BuildCardInfo(projectCard?.Invoke(card) ?? card);
                cardInfo["index"] = previewCards.Count;
                previewCards.Add(cardInfo);
            }
            return;
        }

        foreach (var holder in FindAll<NPreviewCardHolder>(container))
        {
            var card = holder.CardModel;
            if (card == null) continue;

            var cardInfo = BuildCardInfo(projectCard?.Invoke(card) ?? card);
            cardInfo["index"] = previewCards.Count;
            previewCards.Add(cardInfo);
        }
    }

    private static CardModel ProjectEnchantedCard(CardModel card, EnchantmentModel enchantment, decimal amount)
    {
        // Multi-card previews show the original models. Keep projections off the run's card scope,
        // and avoid cloning existing modifiers that could notify subscribers on the original card.
        if (card.Enchantment != null || card.Affliction != null ||
            card.GetType().Assembly != typeof(CardModel).Assembly ||
            enchantment.GetType().Assembly != typeof(EnchantmentModel).Assembly)
            return card;

        try
        {
            var projected = (CardModel)card.MutableClone();
            projected.EnchantInternal(enchantment.ToMutable(), amount);
            projected.IsEnchantmentPreview = true;
            var applied = projected.Enchantment!;
            applied.ModifyCard();

            // Match the visual preview's calculations before formatting either description.
            projected.DynamicVars.ClearPreview();
            projected.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, projected.DynamicVars);
            applied.DynamicVars.ClearPreview();
            projected.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, applied.DynamicVars);
            return projected;
        }
        catch
        {
            return card;
        }
    }

    private static List<Dictionary<string, object?>> BuildPileCardList(IEnumerable<CardModel> cards, PileType pile)
    {
        var list = new List<Dictionary<string, object?>>();
        foreach (var card in cards)
        {
            // Pile cards only need a subset - keep it lightweight
            list.Add(new Dictionary<string, object?>
            {
                ["name"] = SafeGetText(() => card.Title),
                ["cost"] = GetCostDisplay(card),
                ["star_cost"] = GetStarCostDisplay(card),
                ["description"] = SafeGetCardDescription(card, pile)
            });
        }
        return list;
    }

    private static List<Dictionary<string, object?>> BuildEnemiesState(ICombatState combatState) =>
        GetLivingEnemies(combatState)
            .Select(enemy => BuildEnemyState(enemy.Creature, enemy.EntityId))
            .ToList();

    private static Dictionary<string, object?> BuildEnemyState(Creature creature, string entityId)
    {
        var monster = creature.Monster;

        var state = new Dictionary<string, object?>
        {
            ["entity_id"] = entityId,
            ["combat_id"] = creature.CombatId,
            ["name"] = SafeGetText(() => monster?.Title),
            ["hp"] = creature.CurrentHp,
            ["max_hp"] = creature.MaxHp,
            ["block"] = creature.Block,
            ["status"] = BuildPowersState(creature)
        };

        // Intents
        if (monster?.NextMove is MoveState moveState)
        {
            var intents = new List<Dictionary<string, object?>>();
            foreach (var intent in moveState.Intents)
            {
                var intentData = new Dictionary<string, object?>
                {
                    ["type"] = intent.IntentType.ToString()
                };
                try
                {
                    var targets = creature.CombatState?.PlayerCreatures;
                    if (targets != null)
                    {
                        string label = intent.GetIntentLabel(targets, creature).GetFormattedText();
                        intentData["label"] = StripRichTextTags(label);

                        var hoverTip = intent.GetHoverTip(targets, creature);
                        if (hoverTip.Title != null)
                            intentData["title"] = StripRichTextTags(hoverTip.Title);
                        if (hoverTip.Description != null)
                            intentData["description"] = StripRichTextTags(hoverTip.Description);
                    }
                }
                catch { /* intent label may fail for some types */ }
                intents.Add(intentData);
            }
            state["intents"] = intents;
        }

        return state;
    }

    private static Dictionary<string, object?> BuildEventState(EventRoom eventRoom, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        var liveEvent = GetReadyLocalEvent(eventRoom);
        var eventModel = liveEvent ?? eventRoom.CanonicalEvent;
        bool isAncient = eventModel is AncientEventModel;
        state["event_id"] = eventModel.Id.Entry;
        state["event_name"] = SafeGetText(() => eventModel.Title);
        state["is_ancient"] = isAncient;
        state["is_finished"] = liveEvent?.IsFinished;

        var description = liveEvent?.Description;
        var bodyKey = description?.LocEntryKey;
        state["body"] = SafeGetText(() => description);
        state["body_key"] = string.IsNullOrWhiteSpace(bodyKey) ? null : bodyKey;

        var layout = NEventRoom.Instance?.Layout;
        bool canUseEventControls = CanUseEventControls(layout);
        var hitbox = GetEventDialogueHitbox(layout);
        bool inDialogue = IsControlVisibleInTree(hitbox);
        state["in_dialogue"] = inDialogue;
        state["can_advance_dialogue"] = canUseEventControls && IsControlVisibleOrActionable(hitbox);
        if (layout is NAncientEventLayout ancientLayout &&
            GetInstanceFieldValue(ancientLayout, "_currentDialogueLine") is int lineIndex &&
            lineIndex >= 0 &&
            GetInstanceFieldValue(ancientLayout, "_dialogueContainer") is Node lines &&
            lines.GetChildOrNull<NAncientDialogueLine>(lineIndex) is { } line &&
            IsNodeVisible(line))
        {
            AddEventDialogueText(state, line, lineIndex);
        }
        else if (liveEvent is TheArchitect &&
                 GetInstanceFieldValue(liveEvent, "_speechBubble") is Control speechBubble &&
                 IsNodeVisible(speechBubble) &&
                 GetInstanceFieldValue(liveEvent, "_currentLineIndex") is int architectLineIndex)
        {
            AddEventDialogueText(state, speechBubble, architectLineIndex);
        }

        var options = new List<Dictionary<string, object?>>();
        foreach (var button in GetActiveEventOptionButtons(liveEvent, layout))
        {
            var opt = button.Option;
            bool willKillPlayer = EventOptionWillKillPlayer(button);
            bool isEnabled = IsEventOptionButtonEnabled(button);
            var block = GetEventOptionBlock(button, canUseEventControls && !inDialogue, isEnabled, willKillPlayer);
            var optData = new Dictionary<string, object?>
            {
                ["index"] = button.GetIndex(),
                ["option_key"] = opt.TextKey,
                ["title"] = SafeGetText(() => opt.Title),
                ["description"] = SafeGetText(() => opt.Description),
                ["is_enabled"] = isEnabled,
                ["can_choose"] = block == null,
                ["blocked_reason"] = block?.Reason,
                ["will_kill_player"] = willKillPlayer,
                ["is_locked"] = opt.IsLocked,
                ["is_proceed"] = opt.IsProceed,
                ["was_chosen"] = opt.WasChosen
            };
            if (opt.Relic != null)
            {
                optData["relic_name"] = SafeGetText(() => opt.Relic.Title);
                optData["relic_description"] = SafeGetText(() => opt.Relic.DynamicDescription);
            }
            optData["keywords"] = BuildHoverTips(opt.HoverTips);
            options.Add(optData);
        }
        state["options"] = options;
        state["page_key"] = GetEventPageKey(bodyKey, options);

        return state;
    }

    private static void AddEventDialogueText(Dictionary<string, object?> state, Control owner, int lineIndex)
    {
        var textNode = owner.GetNodeOrNull<Control>("%Text");
        if (GetNodeText(textNode) is { } text)
        {
            state["dialogue_text"] = StripRichTextTags(text);
            state["dialogue_line_index"] = lineIndex;
        }
    }

    private static string? GetEventPageKey(string? bodyKey, List<Dictionary<string, object?>> options)
    {
        const string DescriptionSuffix = ".description";
        if (bodyKey != "PROCEED.description" && bodyKey?.EndsWith(DescriptionSuffix, StringComparison.Ordinal) == true)
            return bodyKey[..^DescriptionSuffix.Length];

        foreach (var option in options)
        {
            if (option["option_key"] is not string key)
                continue;

            int marker = key.IndexOf(".options.", StringComparison.Ordinal);
            if (marker > 0)
                return key[..marker];

            if (key.StartsWith("THE_ARCHITECT.dialogue.", StringComparison.Ordinal))
                return key;
        }

        return null;
    }

    private static Dictionary<string, object?> BuildFakeMerchantState(EventRoom eventRoom, RunState runState)
    {
        var state = new Dictionary<string, object?>();
        // LocalMutableEvent holds the per-player mutable copy with populated inventory;
        // CanonicalEvent is the shared template which may not have it.
        var fakeMerchant = (FakeMerchant)(GetReadyLocalEvent(eventRoom) ?? eventRoom.CanonicalEvent);

        state["event_id"] = fakeMerchant.Id.Entry;
        state["event_name"] = SafeGetText(() => fakeMerchant.Title);
        state["started_fight"] = fakeMerchant.StartedFight;

        var fakeMerchantNode = NEventRoom.Instance is { } uiRoom ? FindFirst<NFakeMerchant>(uiRoom) : null;
        var proceedButton = fakeMerchantNode == null ? null : FindFirst<NProceedButton>(fakeMerchantNode);

        if (fakeMerchant.StartedFight)
        {
            state["shop"] = BuildMerchantShopState(null, null, proceedButton);
            state["message"] = "The fake merchant has been defeated.";
            return state;
        }

        state["shop"] = BuildMerchantShopState(
            GetLocalMerchantInventory(fakeMerchant),
            fakeMerchantNode?.Inventory,
            proceedButton,
            "Fake merchant inventory is not ready yet. Check again in a moment.");
        return state;
    }

    private static Dictionary<string, object?> BuildRestSiteState(RestSiteRoom restSiteRoom, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        var options = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var opt in restSiteRoom.Options)
        {
            var option = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["id"] = opt.OptionId,
                ["name"] = SafeGetText(() => opt.Title),
                ["description"] = SafeGetText(() => opt.Description),
                ["is_enabled"] = opt.IsEnabled
            };
            if (opt is MendRestSiteOption)
                option["target_player_slots"] = GetMendTargetSlots(runState);
            options.Add(option);
            index++;
        }
        state["options"] = options;

        var proceedButton = NRestSiteRoom.Instance?.ProceedButton;
        state["can_proceed"] = proceedButton?.IsEnabled ?? false;

        return state;
    }

    private static Dictionary<string, object?> BuildShopState(MerchantRoom merchantRoom, RunState runState)
    {
        var room = NMerchantRoom.Instance;
        return BuildMerchantShopState(
            GetLocalMerchantInventory(merchantRoom),
            room?.Inventory,
            room?.ProceedButton,
            "Shop inventory is not ready yet. Check again in a moment.");
    }

    private static Dictionary<string, object?> BuildMerchantShopState(
        MerchantInventory? inventory,
        NMerchantInventory? inventoryUi,
        NProceedButton? proceedButton,
        string? unavailableMessage = null)
    {
        var state = new Dictionary<string, object?>();
        var entries = inventory == null ? Array.Empty<MerchantEntry>() : GetMerchantEntries(inventory);
        var items = new List<Dictionary<string, object?>>();
        for (int index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var item = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["price"] = entry.Cost,
                ["is_stocked"] = entry.IsStocked,
                ["can_afford"] = entry.EnoughGold
            };
            switch (entry)
            {
                case MerchantCardEntry cardEntry:
                    item["category"] = "card";
                    item["on_sale"] = cardEntry.IsOnSale;
                    if (cardEntry.CreationResult?.Card is { } card)
                    {
                        var cardInfo = BuildCardInfo(card);
                        item["card_id"] = cardInfo["id"];
                        item["card_name"] = cardInfo["name"];
                        item["card_type"] = cardInfo["type"];
                        item["card_cost"] = cardInfo["cost"];
                        item["card_star_cost"] = cardInfo["star_cost"];
                        item["card_rarity"] = cardInfo["rarity"];
                        item["card_description"] = cardInfo["description"];
                        item["keywords"] = cardInfo["keywords"];
                    }
                    break;
                case MerchantRelicEntry relicEntry:
                    item["category"] = "relic";
                    if (relicEntry.Model is { } relic)
                    {
                        item["relic_id"] = relic.Id.Entry;
                        item["relic_name"] = SafeGetText(() => relic.Title);
                        item["relic_description"] = SafeGetText(() => relic.DynamicDescription);
                        item["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic);
                    }
                    break;
                case MerchantPotionEntry potionEntry:
                    item["category"] = "potion";
                    if (potionEntry.Model is { } potion)
                    {
                        item["potion_id"] = potion.Id.Entry;
                        item["potion_name"] = SafeGetText(() => potion.Title);
                        item["potion_description"] = SafeGetText(() => potion.DynamicDescription);
                        item["keywords"] = BuildHoverTips(potion.ExtraHoverTips);
                    }
                    break;
                case MerchantCardRemovalEntry:
                    item["category"] = "card_removal";
                    break;
                default:
                    item["category"] = "other";
                    item["entry_type"] = entry.GetType().Name;
                    break;
            }
            items.Add(item);
        }

        state["items"] = items;
        if (inventory == null && unavailableMessage != null)
            state["error"] = unavailableMessage;
        var purchase = inventory != null ? GetLastMerchantPurchase(inventory) : null;
        var receipt = purchase != null ? BuildMerchantPurchaseResult(purchase) : null;
        if (receipt != null)
            state["last_purchase"] = receipt;
        AddShopActions(state, inventoryUi, proceedButton, entries, receipt?.GetValueOrDefault("status") is "pending");
        return state;
    }

    private static void AddShopActions(
        Dictionary<string, object?> shop,
        NMerchantInventory? inventoryUi,
        NProceedButton? proceedButton,
        IReadOnlyList<MerchantEntry> entries,
        bool purchasePending)
    {
        bool isOpen = inventoryUi?.IsOpen == true;
        bool canUseControls = CanUseShopControls();
        bool canClose = canUseControls && inventoryUi is { IsOpen: true } && GetAvailableShopBackButton(inventoryUi) != null;
        bool canLeave = canUseControls && !isOpen && IsControlVisibleOrActionable(proceedButton);
        shop["is_open"] = isOpen;
        shop["can_proceed"] = canLeave;

        var actions = new List<Dictionary<string, object?>>();
        if (canUseControls && !purchasePending && entries.Any(entry => entry.IsStocked && entry.EnoughGold))
        {
            actions.Add(new Dictionary<string, object?>
            {
                ["action"] = "shop_purchase",
                ["parameter"] = "item_index",
                ["description"] = "Buy a stocked, affordable item."
            });
        }
        if (canClose || canLeave)
        {
            actions.Add(new Dictionary<string, object?>
            {
                ["action"] = "proceed",
                ["description"] = canClose
                    ? "Close the inventory, then check the shop state before leaving."
                    : "Leave the shop and open the map."
            });
        }
        if (canClose && IsLiveNode(proceedButton))
        {
            actions.Add(new Dictionary<string, object?>
            {
                ["action"] = "proceed",
                ["parameter"] = "leave_shop",
                ["arguments"] = new Dictionary<string, object?> { ["leave_shop"] = true },
                ["description"] = "Close the inventory and leave the shop in one call."
            });
        }
        shop["available_actions"] = actions;
    }

    private static Dictionary<string, object?> BuildMapState(RunState runState)
    {
        var state = new Dictionary<string, object?>();

        var map = runState.Map;
        var visitedCoords = runState.VisitedMapCoords;
        var furCoatMarks = runState.Players.SelectMany(player => player.Relics).OfType<FurCoat>()
            .Where(coat => coat.FurCoatActIndex == runState.CurrentActIndex)
            .SelectMany(coat => coat.GetMarkedCoords() ?? [])
            .ToHashSet();
        bool goldenCompassActive = runState.Players.SelectMany(player => player.Relics).OfType<GoldenCompass>()
            .Any(compass => compass.GoldenPathAct == runState.CurrentActIndex);
        bool lanternKeyActive = runState.CurrentActIndex == LanternKeyTargetActIndex
            && runState.Players.Any(player => player.Deck.Cards.Any(card => card is LanternKey));
        // The first-run tutorial overrides the outcomes of the first three Unknown rooms.
        bool unknownsAreEvents = (goldenCompassActive || lanternKeyActive)
            && (runState.UnlockState.NumberOfRuns > 0 || runState.MapPointHistory
                .SelectMany(act => act).Count(entry => entry.MapPointType == MapPointType.Unknown) >= 3);
        int? currentRow = visitedCoords.Count > 0 ? visitedCoords[^1].row : null;

        if (lanternKeyActive)
        {
            var repy = ModelDb.Event<WarHistorianRepy>();
            state["next_event"] = new Dictionary<string, object?>
            {
                ["id"] = repy.Id.Entry,
                ["name"] = SafeGetText(() => repy.Title),
                ["source"] = "LANTERN_KEY"
            };
        }

        Dictionary<string, object?> BuildFuturePointInfo(MapPoint point) =>
            BuildMapPointInfo(point.coord, point, furCoatMarks,
                unknownsAreEvents && (currentRow == null || point.coord.row > currentRow));

        Dictionary<string, object?> BuildMapNode(MapPoint point)
        {
            var node = BuildFuturePointInfo(point);
            node["children"] = point.Children
                .OrderBy(child => child.coord.col)
                .Select(child => new List<int> { child.coord.col, child.coord.row })
                .ToList();
            return node;
        }

        // Current position
        if (visitedCoords.Count > 0)
        {
            var cur = visitedCoords[visitedCoords.Count - 1];
            state["current_position"] = BuildMapPointInfo(cur, map.GetPoint(cur), furCoatMarks);
        }

        // Visited path
        var visited = new List<Dictionary<string, object?>>();
        foreach (var coord in visitedCoords)
        {
            visited.Add(BuildMapPointInfo(coord, map.GetPoint(coord), furCoatMarks));
        }
        state["visited"] = visited;

        // Next options - read travelable state from UI nodes
        var nextOptions = new List<Dictionary<string, object?>>();
        var mapScreen = NMapScreen.Instance;
        if (mapScreen != null)
        {
            var travelable = FindAll<NMapPoint>(mapScreen)
                .Where(mp => mp.State == MapPointState.Travelable && mp.Point != null)
                .OrderBy(mp => mp.Point!.coord.col)
                .ToList();

            int index = 0;
            foreach (var nmp in travelable)
            {
                var pt = nmp.Point;
                var option = BuildFuturePointInfo(pt);
                option["index"] = index;

                // 1-level lookahead
                var children = pt.Children
                    .OrderBy(c => c.coord.col)
                    .Select(BuildFuturePointInfo)
                    .ToList();
                if (children.Count > 0)
                    option["leads_to"] = children;

                nextOptions.Add(option);
                index++;
            }
        }
        state["next_options"] = nextOptions;

        // Full map - all nodes organized for planning
        var nodes = new List<Dictionary<string, object?>>();

        // Starting point
        var start = map.StartingMapPoint;
        nodes.Add(BuildMapNode(start));

        // Grid nodes
        foreach (var pt in map.GetAllMapPoints())
            nodes.Add(BuildMapNode(pt));

        // Boss identity comes from the live act's EncounterModel — BossEncounter
        // throws if the act hasn't finished setup yet, so guard the access.
        EncounterModel? bossEncounter = null;
        try { bossEncounter = runState.Act.BossEncounter; } catch { }
        var secondBossEncounter = runState.Act.SecondBossEncounter;

        var primaryBossId = bossEncounter?.Id?.Entry;
        var primaryBossName = SafeGetText(() => bossEncounter?.Title);
        var bossNode = BuildMapNode(map.BossMapPoint);
        AddBossIdentity(bossNode, primaryBossId, primaryBossName);
        nodes.Add(bossNode);

        Dictionary<string, object?>? secondBoss = null;
        if (map.SecondBossMapPoint != null)
        {
            var secondBossId = secondBossEncounter?.Id?.Entry;
            var secondBossName = SafeGetText(() => secondBossEncounter?.Title);
            var secondBossNode = BuildMapNode(map.SecondBossMapPoint);
            AddBossIdentity(secondBossNode, secondBossId, secondBossName);
            nodes.Add(secondBossNode);
            secondBoss = BuildBossInfo(map.SecondBossMapPoint, secondBossId, secondBossName);
        }

        state["nodes"] = nodes;
        var primaryBoss = BuildBossInfo(map.BossMapPoint, primaryBossId, primaryBossName);
        state["boss"] = primaryBoss;
        state["bosses"] = secondBoss != null
            ? new List<Dictionary<string, object?>> { primaryBoss, secondBoss }
            : new List<Dictionary<string, object?>> { primaryBoss };

        return state;
    }

    private static Dictionary<string, object?> BuildBossInfo(MapPoint pt, string? bossId, string? bossName)
    {
        var boss = new Dictionary<string, object?>
        {
            ["col"] = pt.coord.col,
            ["row"] = pt.coord.row
        };
        AddBossIdentity(boss, bossId, bossName);
        return boss;
    }

    private static void AddBossIdentity(Dictionary<string, object?> target, string? bossId, string? bossName)
    {
        if (string.IsNullOrWhiteSpace(bossId))
            return;

        target["id"] = bossId;
        if (!string.IsNullOrWhiteSpace(bossName))
            target["name"] = bossName;
    }

    private static Dictionary<string, object?> BuildMapPointInfo(
        MapCoord coord, MapPoint? point, HashSet<MapCoord> furCoatMarks, bool unknownsAreEvents = false)
    {
        var info = new Dictionary<string, object?>
        {
            ["col"] = coord.col,
            ["row"] = coord.row,
            ["type"] = point?.PointType.ToString()
        };
        if (furCoatMarks.Contains(coord))
            info["enemy_starting_hp"] = 1;
        if (point?.PointType == MapPointType.Unknown && unknownsAreEvents)
            info["guaranteed_room_type"] = RoomType.Event.ToString();
        if (point?.Quests.Count > 0)
        {
            var quests = point.Quests.OfType<CardModel>()
                .Select(card => new Dictionary<string, object?>
                {
                    ["id"] = card.Id.Entry,
                    ["name"] = SafeGetText(() => card.Title)
                })
                .ToList();
            if (quests.Count > 0)
                info["quests"] = quests;
        }
        return info;
    }

    private static Dictionary<string, object?> BuildRewardsState(NRewardsScreen rewardsScreen, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        // Reward items
        var rewardButtons = FindAll<NRewardButton>(rewardsScreen);
        var items = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var button in rewardButtons)
        {
            if (button.Reward == null || !button.IsEnabled) continue;
            var reward = button.Reward;

            var item = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["type"] = GetRewardTypeName(reward),
                ["description"] = SafeGetText(() => reward.Description)
            };

            // Type-specific details
            if (reward is GoldReward goldReward)
                item["gold_amount"] = goldReward.Amount;
            else if (reward is PotionReward potionReward && potionReward.Potion != null)
            {
                item["potion_id"] = potionReward.Potion.Id.Entry;
                item["potion_name"] = SafeGetText(() => potionReward.Potion.Title);
                item["potion_description"] = SafeGetText(() => potionReward.Potion.DynamicDescription);
            }
            else if (reward is SpecialCardReward && reward.HoverTips.OfType<CardHoverTip>().FirstOrDefault() is { } cardTip)
            {
                item["card"] = BuildCardInfo(cardTip.Card);
            }

            items.Add(item);
            index++;
        }
        state["items"] = items;

        // Proceed button
        var proceedButton = FindFirst<NProceedButton>(rewardsScreen);
        state["can_proceed"] = proceedButton?.IsEnabled ?? false;

        return state;
    }

    private static Dictionary<string, object?> BuildCardRewardState(NCardRewardSelectionScreen cardScreen)
    {
        var state = new Dictionary<string, object?>();

        var cardHolders = GetCardRewardHolders(cardScreen);
        var cards = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var holder in cardHolders)
        {
            var cardInfo = BuildCardInfo(holder.CardModel!);
            cardInfo["index"] = index;
            cards.Add(cardInfo);
            index++;
        }
        state["cards"] = cards;

        bool canSkip = false;
        bool isAwaitingChoice = GetPendingCardRewardChoice(cardScreen) != null;
        var alternatives = new List<Dictionary<string, object?>>();
        foreach (var (option, button) in GetCardRewardAlternatives(cardScreen))
        {
            if (!IsControlVisibleInTree(button))
                continue;

            bool isEnabled = isAwaitingChoice && IsControlVisibleOrActionable(button);
            if (option.OptionId == "Skip" && isEnabled)
                canSkip = true;

            alternatives.Add(new Dictionary<string, object?>
            {
                ["id"] = option.OptionId,
                ["title"] = SafeGetText(() => option.Title),
                ["is_enabled"] = isEnabled,
                ["after_selected"] = option.AfterSelected.ToString()
            });
        }
        state["alternatives"] = alternatives;
        state["can_skip"] = canSkip;

        return state;
    }

    private static Dictionary<string, object?> BuildCardSelectState(NCardGridSelectionScreen screen, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        // Screen type
        state["screen_type"] = screen switch
        {
            NDeckTransformSelectScreen => "transform",
            NDeckUpgradeSelectScreen => "upgrade",
            NDeckCardSelectScreen => "select",
            NSimpleCardSelectScreen => "simple_select",
            _ => screen.GetType().Name
        };

        // Player summary
        // Prompt text from UI label
        var bottomLabel = screen.GetNodeOrNull("%BottomLabel");
        if (bottomLabel != null)
        {
            var textVariant = bottomLabel.Get("text");
            string? prompt = textVariant.VariantType != Godot.Variant.Type.Nil ? StripRichTextTags(textVariant.AsString()) : null;
            state["prompt"] = prompt;
        }

        // Cards in the grid (sorted by visual position - MoveToFront can reorder children)
        var cardHolders = GetSelectableGridCards(screen);
        var cards = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var holder in cardHolders)
        {
            var card = holder.CardModel;
            if (card == null) continue;

            var cardInfo = BuildCardInfo(card);
            cardInfo["index"] = index;
            cards.Add(cardInfo);
            index++;
        }
        state["cards"] = cards;

        if (GetInstanceFieldValue(screen, "_selectedCards") is IEnumerable<CardModel> selectedModels)
        {
            // Read the native selection collection so partial multi-card picks remain visible.
            var selectedCards = new List<Dictionary<string, object?>>();
            foreach (var card in selectedModels)
            {
                int selectedIndex = cardHolders.FindIndex(holder => ReferenceEquals(holder.CardModel, card));
                selectedCards.Add(new Dictionary<string, object?>
                {
                    ["index"] = selectedIndex >= 0 ? selectedIndex : null,
                    ["name"] = SafeGetText(() => card.Title)
                });
            }

            state["selected_cards"] = selectedCards;
            state["selected_count"] = selectedCards.Count;
        }

        var (preview, confirm, cancel) = GetCardSelectionControls(screen);
        state["preview_showing"] = preview != null;
        state["can_select"] = preview == null && cards.Count > 0;
        state["can_confirm"] = confirm != null;
        state["can_cancel"] = cancel != null;
        if (preview != null)
        {
            var previewCards = new List<Dictionary<string, object?>>();
            if (screen is NDeckEnchantSelectScreen &&
                ReferenceEquals(preview, screen.GetNodeOrNull<Godot.Control>("%EnchantMultiPreviewContainer")) &&
                GetInstanceFieldValue(screen, "_enchantment") is EnchantmentModel enchantment &&
                GetInstanceFieldValue(screen, "_enchantmentAmount") is int amount)
            {
                state["preview_enchantment"] = new Dictionary<string, object?>
                {
                    ["id"] = enchantment.Id.Entry,
                    ["name"] = SafeGetText(() => enchantment.Title),
                    ["amount"] = amount
                };
                AddPreviewCardsFromContainer(preview, previewCards, card => ProjectEnchantedCard(card, enchantment, amount));
            }
            else
            {
                AddPreviewCardsFromContainer(preview, previewCards);
            }
            state["preview_cards"] = previewCards;
        }

        return state;
    }

    private static Dictionary<string, object?> BuildChooseCardState(NChooseACardSelectionScreen screen, RunState runState)
    {
        var state = new Dictionary<string, object?>();
        state["screen_type"] = "choose";

        state["prompt"] = "Choose a card.";

        var cardHolders = GetSelectableGridCards(screen);
        var cards = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var holder in cardHolders)
        {
            var card = holder.CardModel;
            if (card == null) continue;

            var cardInfo = BuildCardInfo(card);
            cardInfo["index"] = index;
            cards.Add(cardInfo);
            index++;
        }
        state["cards"] = cards;

        state["can_skip"] = GetEnabledSelectionButton<NClickableControl>(screen, "SkipButton") != null;
        state["preview_showing"] = false;
        state["can_select"] = cards.Count > 0;
        state["can_confirm"] = false;
        state["can_cancel"] = state["can_skip"];

        return state;
    }

    private static Dictionary<string, object?> BuildBundleSelectState(NChooseABundleSelectionScreen screen, RunState runState)
    {
        var state = new Dictionary<string, object?>();
        state["screen_type"] = "bundle";

        state["prompt"] = "Choose a bundle.";

        bool previewShowing = IsBundlePreviewShowing(screen);
        var selectedBundle = previewShowing ? GetInstanceFieldValue(screen, "_selectedBundle") as NCardBundle : null;
        var bundles = new List<Dictionary<string, object?>>();
        var previewCards = new List<Dictionary<string, object?>>();
        var bundleNodes = FindAll<NCardBundle>(screen);
        int index = 0;
        foreach (var bundle in bundleNodes)
        {
            var cards = new List<Dictionary<string, object?>>();
            int cardIndex = 0;
            foreach (var card in bundle.Bundle)
            {
                var cardInfo = BuildCardInfo(card);
                cardInfo["index"] = cardIndex;
                cards.Add(cardInfo);
                cardIndex++;
            }
            if (ReferenceEquals(bundle, selectedBundle))
                previewCards = cards;

            bundles.Add(new Dictionary<string, object?>
            {
                ["index"] = index,
                ["card_count"] = cards.Count,
                ["cards"] = cards
            });
            index++;
        }
        state["bundles"] = bundles;

        state["preview_showing"] = previewShowing;
        state["can_select"] = !previewShowing && bundleNodes.Any(bundle => IsControlVisibleOrActionable(bundle.Hitbox));
        state["preview_cards"] = previewCards;

        state["can_cancel"] = previewShowing && GetEnabledSelectionButton<NBackButton>(screen, "%Cancel") != null;
        state["can_confirm"] = previewShowing && GetEnabledSelectionButton<NConfirmButton>(screen, "%Confirm") != null;

        return state;
    }

    private static Dictionary<string, object?> BuildHandSelectState(NPlayerHand hand, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        // Mode
        state["mode"] = hand.CurrentMode switch
        {
            NPlayerHand.Mode.SimpleSelect => "simple_select",
            NPlayerHand.Mode.UpgradeSelect => "upgrade_select",
            _ => hand.CurrentMode.ToString()
        };

        // Prompt text from %SelectionHeader
        var headerLabel = hand.GetNodeOrNull<Godot.Control>("%SelectionHeader");
        if (headerLabel != null)
        {
            var textVariant = headerLabel.Get("text");
            string? prompt = textVariant.VariantType != Godot.Variant.Type.Nil
                ? StripRichTextTags(textVariant.AsString())
                : null;
            state["prompt"] = prompt;
        }

        // Selectable cards (visible holders in the hand)
        var selectableCards = new List<Dictionary<string, object?>>();
        var holders = hand.ActiveHolders;
        for (int index = 0; index < holders.Count; index++)
        {
            var card = holders[index].CardModel;
            if (card == null) continue;

            var cardInfo = BuildCardInfo(card);
            cardInfo["index"] = index;
            cardInfo["description"] = SafeGetCardDescription(card); // hand cards use default pile
            selectableCards.Add(cardInfo);
        }
        state["cards"] = selectableCards;

        // Already-selected cards (in the SelectedHandCardContainer)
        var selectedContainer = hand.GetNodeOrNull<Godot.Control>("%SelectedHandCardContainer");
        if (selectedContainer != null)
        {
            var selectedCards = new List<Dictionary<string, object?>>();
            var selectedHolders = FindAll<NSelectedHandCardHolder>(selectedContainer);
            int selIdx = 0;
            foreach (var holder in selectedHolders)
            {
                var card = holder.CardModel;
                if (card == null) continue;
                selectedCards.Add(new Dictionary<string, object?>
                {
                    ["index"] = selIdx,
                    ["name"] = SafeGetText(() => card.Title)
                });
                selIdx++;
            }
            if (selectedCards.Count > 0)
                state["selected_cards"] = selectedCards;
        }

        // Confirm button state
        state["can_confirm"] = GetEnabledSelectionButton<NConfirmButton>(hand, "%SelectModeConfirmButton") != null;

        return state;
    }

    private static Dictionary<string, object?> BuildRelicSelectState(NChooseARelicSelection screen, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        state["prompt"] = "Choose a relic.";

        var relicHolders = GetRelicSelectionHolders(screen);
        var relics = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var holder in relicHolders)
        {
            var relic = holder.Relic!.Model!;

            relics.Add(new Dictionary<string, object?>
            {
                ["index"] = index,
                ["id"] = relic.Id.Entry,
                ["name"] = SafeGetText(() => relic.Title),
                ["description"] = SafeGetText(() => relic.DynamicDescription),
                ["rarity"] = relic.Rarity.ToString(),
                ["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic)
            });
            index++;
        }
        state["relics"] = relics;

        var skipButton = screen.GetNodeOrNull<NClickableControl>("SkipButton");
        state["can_skip"] = skipButton?.IsEnabled == true && skipButton.Visible;

        return state;
    }

    private static Dictionary<string, object?> BuildCrystalSphereState(NCrystalSphereScreen screen)
    {
        var minigame = GetCrystalSphereMinigame(screen);
        bool canDivine = CanDivineAtCrystalSphere(minigame);
        var state = new Dictionary<string, object?>
        {
            ["divinations_remaining"] = minigame?.DivinationCount,
            ["tool"] = GetCrystalSphereTool(minigame),
            ["can_use_big_tool"] = canDivine && IsControlVisibleOrActionable(GetCrystalSphereToolButton(screen, "big")),
            ["can_use_small_tool"] = canDivine && IsControlVisibleOrActionable(GetCrystalSphereToolButton(screen, "small"))
        };

        var instructionsTitle = screen.GetNodeOrNull<Godot.Control>("%InstructionsTitle");
        if (instructionsTitle != null)
        {
            var textVariant = instructionsTitle.Get("text");
            if (textVariant.VariantType != Godot.Variant.Type.Nil)
                state["instructions_title"] = StripRichTextTags(textVariant.AsString());
        }

        var instructionsDescription = screen.GetNodeOrNull<Godot.Control>("%InstructionsDescription");
        if (instructionsDescription != null)
        {
            var textVariant = instructionsDescription.Get("text");
            if (textVariant.VariantType != Godot.Variant.Type.Nil)
                state["instructions_description"] = StripRichTextTags(textVariant.AsString());
        }

        var cells = FindAll<NCrystalSphereCell>(screen);
        state["grid_width"] = minigame?.GridSize.X ?? 0;
        state["grid_height"] = minigame?.GridSize.Y ?? 0;

        var revealed = minigame == null
            ? new HashSet<CrystalSphereItem>()
            : (GetInstanceFieldValue(minigame, "_revealed") as IEnumerable<CrystalSphereItem> ?? [])
                .ToHashSet();

        var cellStates = new List<Dictionary<string, object?>>();
        var clickableCells = new List<Dictionary<string, object?>>();
        foreach (var cell in cells.OrderBy(c => c.Entity.Y).ThenBy(c => c.Entity.X))
        {
            bool isClickable = canDivine && cell.Entity.IsHidden && IsControlVisibleOrActionable(cell);
            var cellState = new Dictionary<string, object?>
            {
                ["x"] = cell.Entity.X,
                ["y"] = cell.Entity.Y,
                ["is_hidden"] = cell.Entity.IsHidden,
                ["is_clickable"] = isClickable,
                ["is_highlighted"] = cell.Entity.IsHighlighted,
                ["is_hovered"] = cell.Entity.IsHovered
            };

            if (!cell.Entity.IsHidden && cell.Entity.Item is { } item && revealed.Contains(item))
            {
                cellState["item_type"] = item.GetType().Name;
                cellState["is_good"] = item.IsGood;
            }

            cellStates.Add(cellState);
            if (isClickable)
            {
                clickableCells.Add(new Dictionary<string, object?>
                {
                    ["x"] = cell.Entity.X,
                    ["y"] = cell.Entity.Y
                });
            }
        }
        state["cells"] = cellStates;
        state["clickable_cells"] = clickableCells;

        var revealedItems = new List<Dictionary<string, object?>>();
        foreach (var item in revealed.OrderBy(item => item.Position.Y).ThenBy(item => item.Position.X))
        {
            var itemState = new Dictionary<string, object?>
            {
                ["item_type"] = item.GetType().Name,
                ["x"] = item.Position.X,
                ["y"] = item.Position.Y,
                ["width"] = item.Size.X,
                ["height"] = item.Size.Y,
                ["is_good"] = item.IsGood,
                ["is_fully_revealed"] = true
            };
            if (item is CrystalSphereGold &&
                item.GetType().GetProperty("Amount", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)?.GetValue(item) is int amount)
                itemState["gold_amount"] = amount;
            if (item is CrystalSphereCardReward or CrystalSpherePotion &&
                GetInstanceFieldValue(item, "_rarity") is { } rarity)
                itemState["rarity"] = rarity.ToString();

            revealedItems.Add(itemState);
        }
        state["revealed_items"] = revealedItems;

        var divinationsLeft = screen.GetNodeOrNull<Godot.Control>("%DivinationsLeft");
        if (divinationsLeft != null)
        {
            var textVariant = divinationsLeft.Get("text");
            if (textVariant.VariantType != Godot.Variant.Type.Nil)
                state["divinations_left_text"] = StripRichTextTags(textVariant.AsString());
        }

        state["can_proceed"] = FindCrystalSphereProceedButton(screen) != null;

        return state;
    }

    private static Dictionary<string, object?> BuildTreasureState(TreasureRoom treasureRoom, RunState runState)
    {
        var state = new Dictionary<string, object?>
        {
            ["can_claim"] = false,
            ["can_skip"] = false,
            ["can_proceed"] = false
        };

        if (GetVisibleTreasureRoom() is not { } treasure)
        {
            state["status"] = "loading";
            state["message"] = "Treasure room is loading.";
            return state;
        }

        // Preserve the existing auto-open behavior, using the live native control only once.
        if (TryOpenTreasureChest(treasure))
        {
            state["status"] = "opening";
            state["message"] = "Opening chest. Check the treasure state again after it opens.";
            return state;
        }

        var button = GetAvailableTreasureProceedButton(treasure);
        var holders = GetAvailableTreasureRelicHolders(treasure);
        state["can_claim"] = holders.Count > 0;
        state["can_skip"] = button?.IsSkip == true;
        state["can_proceed"] = button is { IsSkip: false };

        var relics = new List<Dictionary<string, object?>>();
        for (int index = 0; index < holders.Count; index++)
        {
            var relic = holders[index].Relic!.Model!;
            relics.Add(new Dictionary<string, object?>
            {
                ["index"] = index,
                ["id"] = relic.Id.Entry,
                ["name"] = SafeGetText(() => relic.Title),
                ["description"] = SafeGetText(() => relic.DynamicDescription),
                ["rarity"] = relic.Rarity.ToString(),
                ["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic)
            });
        }
        state["relics"] = relics;

        if (button is { IsSkip: false })
        {
            state["status"] = "ready_to_leave";
            state["message"] = "The map is available through Proceed.";
        }
        else if (holders.Count > 0)
        {
            state["status"] = "relics_available";
            state["message"] = "Choose a relic.";
        }
        else if (GetInstanceFieldValue(treasure, "_isRelicCollectionOpen") is true)
        {
            state["status"] = "waiting_for_relics";
            state["message"] = "Waiting for treasure relic selection to finish.";
        }
        else
        {
            state["status"] = "opening";
            state["message"] = "Opening chest. Check the treasure state again after it opens.";
        }

        return state;
    }

    private static string GetRewardTypeName(Reward reward) => reward switch
    {
        GoldReward => "gold",
        PotionReward => "potion",
        RelicReward => "relic",
        CardReward => "card",
        SpecialCardReward => "special_card",
        CardRemovalReward => "card_removal",
        _ => reward.GetType().Name.ToLower()
    };

    private static List<Dictionary<string, object?>> BuildPowersState(Creature creature)
    {
        var powers = new List<Dictionary<string, object?>>();
        foreach (var power in creature.Powers)
        {
            if (!power.IsVisible) continue;

            // Per-power try/catch: HoverTips getter calls into game engine code
            // (LocString resolution, DynamicVars, virtual ExtraHoverTips) that can
            // throw during state transitions. Skip the power rather than fail the
            // entire state query.
            try
            {
                var allTips = power.HoverTips.ToList();
                string? resolvedDesc = null;
                var extraTips = new List<IHoverTip>();
                foreach (var tip in allTips)
                {
                    if (tip.Id == power.Id.ToString())
                    {
                        if (tip is HoverTip ht && ht.Description != null)
                            resolvedDesc = StripRichTextTags(ht.Description);
                    }
                    else
                    {
                        extraTips.Add(tip);
                    }
                }
                resolvedDesc ??= SafeGetText(() => power.SmartDescription);

                powers.Add(new Dictionary<string, object?>
                {
                    ["id"] = power.Id.Entry,
                    ["name"] = SafeGetText(() => power.Title),
                    ["amount"] = power.DisplayAmount,
                    ["type"] = power.Type.ToString(),
                    ["description"] = resolvedDesc,
                    ["keywords"] = BuildHoverTips(extraTips)
                });
            }
            catch { /* skip this power - game engine state may be inconsistent */ }
        }
        return powers;
    }

    private static List<Dictionary<string, object?>> BuildPetsState(Player player)
    {
        var pets = new List<Dictionary<string, object?>>();
        var combatState = player.PlayerCombatState;
        if (combatState == null) return pets;

        foreach (var pet in combatState.Pets)
        {
            pets.Add(new Dictionary<string, object?>
            {
                ["id"] = pet.Monster?.Id.Entry,
                ["name"] = SafeGetText(() => pet.Monster?.Title) ?? pet.Name,
                ["alive"] = pet.IsAlive,
                ["is_interactable"] = pet.Monster?.IsInteractable ?? true,
                ["hp"] = pet.CurrentHp,
                ["max_hp"] = pet.MaxHp,
                ["block"] = pet.Block,
                ["status"] = BuildPowersState(pet)
            });
        }

        return pets;
    }
}
