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
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.ProfileScreen;
using Godot;

namespace STS2_MCP;

public static partial class McpMod
{
    private static Dictionary<string, object?> BuildGameState()
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
                // Check for character select screen
                if (result.ContainsKey("menu_screen") == false)
                {
                    var charSelect = FindFirst<NCharacterSelectScreen>(tree.Root);
                    if (charSelect != null && IsNodeVisible(charSelect))
                    {
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

                                    // Starting relics
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

                                    // Starting deck summary
                                    var deckCards = new List<string>();
                                    foreach (var card in cm.StartingDeck)
                                        deckCards.Add(SafeGetText(() => card.Title) ?? "?");
                                    if (deckCards.Count > 0)
                                        charData["starting_deck"] = deckCards;

                                    // Known cards count from card pool
                                    try
                                    {
                                        var allCards = cm.CardPool?.AllCards;
                                        if (allCards != null)
                                            charData["total_cards"] = System.Linq.Enumerable.Count(allCards);
                                    }
                                    catch { }

                                    // Known relics count from relic pool
                                    try
                                    {
                                        var allRelics = cm.RelicPool?.AllRelics;
                                        if (allRelics != null)
                                            charData["total_relics"] = System.Linq.Enumerable.Count(allRelics);
                                    }
                                    catch { }

                                    // Known potions count from potion pool
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

                        var backBtn = GetInstanceFieldValue(charSelect, "_backButton")
                            ?? GetInstanceFieldValue(charSelect, "_unreadyButton");
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
                        }
                        else if (settingsScreen != null && IsNodeVisible(settingsScreen))
                        {
                            result["menu_screen"] = "settings";
                            result["message"] = "Settings screen.";
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
                            var fields = new[] { "_continueButton", "_singleplayerButton", "_multiplayerButton", "_compendiumButton", "_timelineButton", "_settingsButton", "_quitButton" };
                            var labels = new[] { "continue", "singleplayer", "multiplayer", "compendium", "timeline", "settings", "quit" };
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
                                        options.Add(labels[i]);
                                    }
                                }
                                catch { }
                            }
                            if (options.Count > 0)
                                result["options"] = options;
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
        else if (topOverlay is NCrystalSphereScreen crystalSphereScreen)
        {
            result["state_type"] = "crystal_sphere";
            result["crystal_sphere"] = BuildCrystalSphereState(crystalSphereScreen, runState);
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
            result["state_type"] = "game_over";
            result["game_over"] = new Dictionary<string, object?>
            {
                ["message"] = "Run ended.",
                ["options"] = new List<string> { "main_menu" }
            };
        }
        else if (topOverlay is IOverlayScreen
                 && topOverlay is not NRewardsScreen
                 && topOverlay is not NCardRewardSelectionScreen)
        {
            // Catch-all for unhandled overlays - prevents soft-locks
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
                // Auto-open the shopkeeper's inventory if not already open.
                // NMerchantRoom.Inventory (UI node) can be null before the scene is fully ready;
                // OpenInventory() itself accesses Inventory.IsOpen, so guard against null.
                var merchUI = NMerchantRoom.Instance;
                if (merchUI?.Inventory != null && !merchUI.Inventory.IsOpen)
                {
                    merchUI.OpenInventory();
                }
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
            ["ascension"] = runState.AscensionLevel
        };

        // Always include full player data (relics, potions, deck, etc.) on every screen
        var _player = LocalContext.GetMe(runState);
        if (_player != null)
        {
            result["player"] = BuildPlayerState(_player);
        }

        return result;
    }

    private static Dictionary<string, object?> BuildBattleState(RunState runState, CombatRoom combatRoom)
    {
        var combatState = CombatManager.Instance.DebugOnlyGetState();
        var battle = new Dictionary<string, object?>();

        if (combatState == null)
        {
            battle["error"] = "Combat state unavailable";
            return battle;
        }

        battle["round"] = combatState.RoundNumber;
        battle["turn"] = combatState.CurrentSide.ToString().ToLower();
        battle["is_play_phase"] = CombatManager.Instance.IsPlayPhase;

        // Enemies
        var enemies = new List<Dictionary<string, object?>>();
        var entityCounts = new Dictionary<string, int>();
        foreach (var creature in combatState.Enemies)
        {
            if (creature.IsAlive)
            {
                enemies.Add(BuildEnemyState(creature, entityCounts));
            }
        }
        battle["enemies"] = enemies;

        return battle;
    }

    private static Dictionary<string, object?> BuildPlayerState(Player player)
    {
        var state = new Dictionary<string, object?>();
        var creature = player.Creature;
        var combatState = player.PlayerCombatState;

        state["character"] = SafeGetText(() => player.Character.Title);
        state["hp"] = creature.CurrentHp;
        state["max_hp"] = creature.MaxHp;
        state["block"] = creature.Block;

        // PlayerCombatState can linger after combat while on map/rest/shop. Energy/MaxEnergy getters
        // run hooks (e.g. Hook.ModifyMaxEnergy) that null-ref without a live combat - only serialize
        // combat fields when a fight is actually in progress.
        if (combatState != null && CombatManager.Instance.IsInProgress)
        {
            state["energy"] = combatState.Energy;
            state["max_energy"] = combatState.MaxEnergy;

            // Stars (The Regent's resource, conditionally shown)
            if (player.Character.ShouldAlwaysShowStarCounter || combatState.Stars > 0)
            {
                state["stars"] = combatState.Stars;
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
            var drawCards = combatState.DrawPile.Cards.ToList();
            drawCards.Sort((c1, c2) => c1.Rarity != c2.Rarity
                ? c1.Rarity.CompareTo(c2.Rarity)
                : string.Compare(c1.Id.Entry, c2.Id.Entry, StringComparison.Ordinal));
            state["draw_pile"] = BuildPileCardList(drawCards, PileType.Draw);
            state["discard_pile"] = BuildPileCardList(combatState.DiscardPile.Cards, PileType.Discard);
            state["exhaust_pile"] = BuildPileCardList(combatState.ExhaustPile.Cards, PileType.Exhaust);

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

            // Pets (Osty for Necrobinder)
            var pets = BuildPetsState(player);
            if (pets.Count > 0)
            {
                state["pets"] = pets;
            }
        }

        state["gold"] = player.Gold;

        // Powers (status effects)
        state["status"] = BuildPowersState(creature);

        // Relics
        var relics = new List<Dictionary<string, object?>>();
        foreach (var relic in player.Relics)
        {
            relics.Add(new Dictionary<string, object?>
            {
                ["id"] = relic.Id.Entry,
                ["name"] = SafeGetText(() => relic.Title),
                ["description"] = SafeGetText(() => relic.DynamicDescription),
                ["counter"] = relic.ShowCounter ? relic.DisplayAmount : null,
                ["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic)
            });
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

        return state;
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
        state["description"] = SafeGetCardDescription(card); // hand cards use default pile
        state["target_type"] = card.TargetType.ToString();
        state["can_play"] = unplayableReason == UnplayableReason.None;
        state["unplayable_reason"] = unplayableReason != UnplayableReason.None ? unplayableReason.ToString() : null;
        return state;
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

    private static Dictionary<string, object?> BuildEnemyState(Creature creature, Dictionary<string, int> entityCounts)
    {
        var monster = creature.Monster;
        string baseId = monster?.Id.Entry ?? "unknown";

        // Generate entity_id like "jaw_worm_0"
        if (!entityCounts.TryGetValue(baseId, out int count))
            count = 0;
        entityCounts[baseId] = count + 1;
        string entityId = $"{baseId}_{count}";

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

        var eventModel = eventRoom.CanonicalEvent;
        bool isAncient = eventModel is AncientEventModel;
        state["event_id"] = eventModel.Id.Entry;
        state["event_name"] = SafeGetText(() => eventModel.Title);
        state["is_ancient"] = isAncient;

        // Check dialogue state for ancients
        bool inDialogue = false;
        var uiRoom = NEventRoom.Instance;
        if (isAncient && uiRoom != null)
        {
            var ancientLayout = FindFirst<NAncientEventLayout>(uiRoom);
            if (ancientLayout != null)
            {
                var hitbox = ancientLayout.GetNodeOrNull<NClickableControl>("%DialogueHitbox");
                inDialogue = hitbox != null && hitbox.Visible && hitbox.IsEnabled;
            }
        }
        state["in_dialogue"] = inDialogue;

        // Event body text
        state["body"] = SafeGetText(() => eventModel.Description);

        // Options from UI
        var options = new List<Dictionary<string, object?>>();
        if (uiRoom != null)
        {
            var buttons = FindAll<NEventOptionButton>(uiRoom);
            int index = 0;
            foreach (var button in buttons)
            {
                var opt = button.Option;
                var optData = new Dictionary<string, object?>
                {
                    ["index"] = index,
                    ["title"] = SafeGetText(() => opt.Title),
                    ["description"] = SafeGetText(() => opt.Description),
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
                index++;
            }
        }
        state["options"] = options;

        return state;
    }

    private static Dictionary<string, object?> BuildFakeMerchantState(EventRoom eventRoom, RunState runState)
    {
        var state = new Dictionary<string, object?>();
        // LocalMutableEvent holds the per-player mutable copy with populated inventory;
        // CanonicalEvent is the shared template which may not have it.
        var fakeMerchant = (FakeMerchant)(eventRoom.LocalMutableEvent ?? eventRoom.CanonicalEvent);

        state["event_id"] = fakeMerchant.Id.Entry;
        state["event_name"] = SafeGetText(() => fakeMerchant.Title);
        state["started_fight"] = fakeMerchant.StartedFight;

        // Find the NFakeMerchant UI node
        var uiRoom = NEventRoom.Instance;
        NFakeMerchant? fakeMerchantNode = null;
        if (uiRoom != null)
            fakeMerchantNode = FindFirst<NFakeMerchant>(uiRoom);

        if (fakeMerchant.StartedFight)
        {
            // After the foul potion fight, merchant is gone - just show proceed
            state["shop"] = new Dictionary<string, object?>
            {
                ["items"] = new List<Dictionary<string, object?>>(),
                ["can_proceed"] = true
            };
            state["message"] = "The fake merchant has been defeated. Proceed to map.";
            return state;
        }

        // Auto-open the inventory if the merchant button is still available
        if (fakeMerchantNode != null)
        {
            var inventoryUI = FindFirst<NMerchantInventory>(fakeMerchantNode);
            if (inventoryUI != null && !inventoryUI.IsOpen)
            {
                // ForceClick the merchant button to go through the proper signal chain
                // (disables proceed button, wires InventoryClosed callback, etc.)
                var merchantButton = fakeMerchantNode.MerchantButton;
                if (merchantButton != null && merchantButton.Visible && merchantButton.IsEnabled)
                    merchantButton.ForceClick();
            }
        }

        // Build shop inventory from the FakeMerchant model
        var shopState = BuildFakeMerchantShopItems(fakeMerchant.Inventory);

        // Proceed button
        if (fakeMerchantNode != null)
        {
            var proceedButton = FindFirst<NProceedButton>(fakeMerchantNode);
            shopState["can_proceed"] = proceedButton?.IsEnabled ?? false;
        }
        else
        {
            shopState["can_proceed"] = false;
        }

        state["shop"] = shopState;
        return state;
    }

    private static Dictionary<string, object?> BuildFakeMerchantShopItems(MerchantInventory? inventory)
    {
        var state = new Dictionary<string, object?>();

        if (inventory == null)
        {
            state["items"] = new List<Dictionary<string, object?>>();
            state["error"] = "Fake merchant inventory is not ready yet; retry in a moment.";
            return state;
        }

        var items = new List<Dictionary<string, object?>>();
        int index = 0;

        // FakeMerchant only sells relics (no cards, potions, or card removal)
        foreach (var entry in inventory.RelicEntries)
        {
            var item = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["category"] = "relic",
                ["price"] = entry.Cost,
                ["is_stocked"] = entry.IsStocked,
                ["can_afford"] = entry.EnoughGold
            };
            if (entry.Model is { } relic)
            {
                item["relic_id"] = relic.Id.Entry;
                item["relic_name"] = SafeGetText(() => relic.Title);
                item["relic_description"] = SafeGetText(() => relic.DynamicDescription);
                item["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic);
            }
            items.Add(item);
            index++;
        }

        state["items"] = items;
        return state;
    }

    private static Dictionary<string, object?> BuildRestSiteState(RestSiteRoom restSiteRoom, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        var options = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var opt in restSiteRoom.Options)
        {
            options.Add(new Dictionary<string, object?>
            {
                ["index"] = index,
                ["id"] = opt.OptionId,
                ["name"] = SafeGetText(() => opt.Title),
                ["description"] = SafeGetText(() => opt.Description),
                ["is_enabled"] = opt.IsEnabled
            });
            index++;
        }
        state["options"] = options;

        var proceedButton = NRestSiteRoom.Instance?.ProceedButton;
        state["can_proceed"] = proceedButton?.IsEnabled ?? false;

        return state;
    }

    private static Dictionary<string, object?> BuildShopState(MerchantRoom merchantRoom, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        var inventory = merchantRoom.Inventory;
        if (inventory == null)
        {
            state["items"] = new List<Dictionary<string, object?>>();
            state["can_proceed"] = NMerchantRoom.Instance?.ProceedButton?.IsEnabled ?? false;
            state["error"] =
                "Shop inventory is not ready yet (null). Often happens right after entering the merchant from the map; retry in a moment.";
            return state;
        }

        var items = new List<Dictionary<string, object?>>();
        int index = 0;

        // Cards
        foreach (var entry in inventory.CardEntries)
        {
            var item = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["category"] = "card",
                ["price"] = entry.Cost,
                ["is_stocked"] = entry.IsStocked,
                ["can_afford"] = entry.EnoughGold,
                ["on_sale"] = entry.IsOnSale
            };
            if (entry.CreationResult?.Card is { } card)
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
            items.Add(item);
            index++;
        }

        // Relics
        foreach (var entry in inventory.RelicEntries)
        {
            var item = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["category"] = "relic",
                ["price"] = entry.Cost,
                ["is_stocked"] = entry.IsStocked,
                ["can_afford"] = entry.EnoughGold
            };
            if (entry.Model is { } relic)
            {
                item["relic_id"] = relic.Id.Entry;
                item["relic_name"] = SafeGetText(() => relic.Title);
                item["relic_description"] = SafeGetText(() => relic.DynamicDescription);
                item["keywords"] = BuildHoverTips(relic.HoverTipsExcludingRelic);
            }
            items.Add(item);
            index++;
        }

        // Potions
        foreach (var entry in inventory.PotionEntries)
        {
            var item = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["category"] = "potion",
                ["price"] = entry.Cost,
                ["is_stocked"] = entry.IsStocked,
                ["can_afford"] = entry.EnoughGold
            };
            if (entry.Model is { } potion)
            {
                item["potion_id"] = potion.Id.Entry;
                item["potion_name"] = SafeGetText(() => potion.Title);
                item["potion_description"] = SafeGetText(() => potion.DynamicDescription);
                item["keywords"] = BuildHoverTips(potion.ExtraHoverTips);
            }
            items.Add(item);
            index++;
        }

        // Card removal
        if (inventory.CardRemovalEntry is { } removal)
        {
            items.Add(new Dictionary<string, object?>
            {
                ["index"] = index,
                ["category"] = "card_removal",
                ["price"] = removal.Cost,
                ["is_stocked"] = removal.IsStocked,
                ["can_afford"] = removal.EnoughGold
            });
        }

        state["items"] = items;

        var proceedButton = NMerchantRoom.Instance?.ProceedButton;
        state["can_proceed"] = proceedButton?.IsEnabled ?? false;

        return state;
    }

    private static Dictionary<string, object?> BuildMapState(RunState runState)
    {
        var state = new Dictionary<string, object?>();

        var map = runState.Map;
        var visitedCoords = runState.VisitedMapCoords;

        // Current position
        if (visitedCoords.Count > 0)
        {
            var cur = visitedCoords[visitedCoords.Count - 1];
            state["current_position"] = new Dictionary<string, object?>
            {
                ["col"] = cur.col, ["row"] = cur.row,
                ["type"] = map.GetPoint(cur)?.PointType.ToString()
            };
        }

        // Visited path
        var visited = new List<Dictionary<string, object?>>();
        foreach (var coord in visitedCoords)
        {
            visited.Add(new Dictionary<string, object?>
            {
                ["col"] = coord.col, ["row"] = coord.row,
                ["type"] = map.GetPoint(coord)?.PointType.ToString()
            });
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
                var option = new Dictionary<string, object?>
                {
                    ["index"] = index,
                    ["col"] = pt.coord.col,
                    ["row"] = pt.coord.row,
                    ["type"] = pt.PointType.ToString()
                };

                // 1-level lookahead
                var children = pt.Children
                    .OrderBy(c => c.coord.col)
                    .Select(c => new Dictionary<string, object?>
                    {
                        ["col"] = c.coord.col, ["row"] = c.coord.row,
                        ["type"] = c.PointType.ToString()
                    }).ToList();
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

        // Boss
        nodes.Add(BuildMapNode(map.BossMapPoint));
        if (map.SecondBossMapPoint != null)
            nodes.Add(BuildMapNode(map.SecondBossMapPoint));

        state["nodes"] = nodes;
        state["boss"] = new Dictionary<string, object?>
        {
            ["col"] = map.BossMapPoint.coord.col,
            ["row"] = map.BossMapPoint.coord.row
        };

        return state;
    }

    private static Dictionary<string, object?> BuildMapNode(MapPoint pt)
    {
        return new Dictionary<string, object?>
        {
            ["col"] = pt.coord.col,
            ["row"] = pt.coord.row,
            ["type"] = pt.PointType.ToString(),
            ["children"] = pt.Children
                .OrderBy(c => c.coord.col)
                .Select(c => new List<int> { c.coord.col, c.coord.row })
                .ToList()
        };
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

        var cardHolders = FindAllSortedByPosition<NCardHolder>(cardScreen);
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

        var altButtons = FindAll<NCardRewardAlternativeButton>(cardScreen);
        state["can_skip"] = altButtons.Count > 0;

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
        var cardHolders = FindAllSortedByPosition<NGridCardHolder>(screen);
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

        // Preview container showing? (selection complete, awaiting confirm)
        // Upgrade screens use UpgradeSinglePreviewContainer / UpgradeMultiPreviewContainer
        var previewSingle = screen.GetNodeOrNull<Godot.Control>("%UpgradeSinglePreviewContainer");
        var previewMulti = screen.GetNodeOrNull<Godot.Control>("%UpgradeMultiPreviewContainer");
        var previewGeneric = screen.GetNodeOrNull<Godot.Control>("%PreviewContainer");
        bool previewShowing = (previewSingle?.Visible ?? false)
                            || (previewMulti?.Visible ?? false)
                            || (previewGeneric?.Visible ?? false);
        state["preview_showing"] = previewShowing;

        // Button states - when a preview is open, cancel goes through the
        // preview container's Cancel / PreviewCancel button (same path as
        // the action handler), not the top-level %Close button.
        bool canCancel = false;
        if (previewShowing)
        {
            foreach (var container in new[] { previewSingle, previewMulti, previewGeneric })
            {
                if (container?.Visible == true)
                {
                    var cancelBtn = container.GetNodeOrNull<NBackButton>("Cancel")
                                    ?? container.GetNodeOrNull<NBackButton>("%PreviewCancel");
                    if (cancelBtn?.IsEnabled == true) { canCancel = true; break; }
                }
            }
        }
        if (!canCancel)
        {
            var closeButton = screen.GetNodeOrNull<NBackButton>("%Close");
            canCancel = closeButton?.IsEnabled ?? false;
        }
        state["can_cancel"] = canCancel;

        // Confirm button - search all preview containers and main screen
        bool canConfirm = false;
        foreach (var container in new[] { previewSingle, previewMulti, previewGeneric })
        {
            if (container?.Visible == true)
            {
                var confirm = container.GetNodeOrNull<NConfirmButton>("Confirm")
                              ?? container.GetNodeOrNull<NConfirmButton>("%PreviewConfirm");
                if (confirm?.IsEnabled == true) { canConfirm = true; break; }
            }
        }
        if (!canConfirm)
        {
            var mainConfirm = screen.GetNodeOrNull<NConfirmButton>("Confirm")
                              ?? screen.GetNodeOrNull<NConfirmButton>("%Confirm");
            if (mainConfirm?.IsEnabled == true) canConfirm = true;
        }
        // Fallback: search entire screen tree for any enabled confirm button
        // (covers subclasses like NDeckEnchantSelectScreen)
        if (!canConfirm)
        {
            canConfirm = FindAll<NConfirmButton>(screen).Any(b => b.IsEnabled && b.IsVisibleInTree());
        }
        state["can_confirm"] = canConfirm;

        return state;
    }

    private static Dictionary<string, object?> BuildChooseCardState(NChooseACardSelectionScreen screen, RunState runState)
    {
        var state = new Dictionary<string, object?>();
        state["screen_type"] = "choose";

        state["prompt"] = "Choose a card.";

        var cardHolders = FindAllSortedByPosition<NGridCardHolder>(screen);
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

        var skipButton = screen.GetNodeOrNull<NClickableControl>("SkipButton");
        state["can_skip"] = skipButton?.IsEnabled == true && skipButton.Visible;
        state["preview_showing"] = false;
        state["can_confirm"] = false;
        state["can_cancel"] = state["can_skip"];

        return state;
    }

    private static Dictionary<string, object?> BuildBundleSelectState(NChooseABundleSelectionScreen screen, RunState runState)
    {
        var state = new Dictionary<string, object?>();
        state["screen_type"] = "bundle";

        state["prompt"] = "Choose a bundle.";

        var bundles = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var bundle in FindAll<NCardBundle>(screen))
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

            bundles.Add(new Dictionary<string, object?>
            {
                ["index"] = index,
                ["card_count"] = cards.Count,
                ["cards"] = cards
            });
            index++;
        }
        state["bundles"] = bundles;

        var previewContainer = screen.GetNodeOrNull<Godot.Control>("%BundlePreviewContainer");
        bool previewShowing = previewContainer?.Visible == true;
        state["preview_showing"] = previewShowing;

        var previewCards = new List<Dictionary<string, object?>>();
        var previewCardsContainer = screen.GetNodeOrNull<Godot.Control>("%Cards");
        if (previewCardsContainer != null)
        {
            int previewIndex = 0;
            foreach (var holder in FindAll<NPreviewCardHolder>(previewCardsContainer))
            {
                var card = holder.CardModel;
                if (card == null) continue;

                var cardInfo = BuildCardInfo(card);
                cardInfo["index"] = previewIndex;
                previewCards.Add(cardInfo);
                previewIndex++;
            }
        }
        state["preview_cards"] = previewCards;

        var cancelButton = screen.GetNodeOrNull<NBackButton>("%Cancel");
        var confirmButton = screen.GetNodeOrNull<NConfirmButton>("%Confirm");
        state["can_cancel"] = cancelButton?.IsEnabled == true;
        state["can_confirm"] = confirmButton?.IsEnabled == true;

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
        int index = 0;
        foreach (var holder in hand.ActiveHolders)
        {
            var card = holder.CardModel;
            if (card == null) continue;

            var cardInfo = BuildCardInfo(card);
            cardInfo["index"] = index;
            cardInfo["description"] = SafeGetCardDescription(card); // hand cards use default pile
            selectableCards.Add(cardInfo);
            index++;
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
        var confirmBtn = hand.GetNodeOrNull<NConfirmButton>("%SelectModeConfirmButton");
        state["can_confirm"] = confirmBtn?.IsEnabled ?? false;

        return state;
    }

    private static Dictionary<string, object?> BuildRelicSelectState(NChooseARelicSelection screen, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        state["prompt"] = "Choose a relic.";

        var relicHolders = FindAll<NRelicBasicHolder>(screen);
        var relics = new List<Dictionary<string, object?>>();
        int index = 0;
        foreach (var holder in relicHolders)
        {
            var relic = holder.Relic?.Model;
            if (relic == null) continue;

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

    private static Dictionary<string, object?> BuildCrystalSphereState(NCrystalSphereScreen screen, RunState runState)
    {
        var state = new Dictionary<string, object?>();

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
        state["grid_width"] = cells.Count > 0 ? cells.Max(c => c.Entity.X) + 1 : 0;
        state["grid_height"] = cells.Count > 0 ? cells.Max(c => c.Entity.Y) + 1 : 0;

        var cellStates = new List<Dictionary<string, object?>>();
        var clickableCells = new List<Dictionary<string, object?>>();
        foreach (var cell in cells.OrderBy(c => c.Entity.Y).ThenBy(c => c.Entity.X))
        {
            var cellState = new Dictionary<string, object?>
            {
                ["x"] = cell.Entity.X,
                ["y"] = cell.Entity.Y,
                ["is_hidden"] = cell.Entity.IsHidden,
                ["is_clickable"] = cell.Entity.IsHidden && cell.Visible,
                ["is_highlighted"] = cell.Entity.IsHighlighted,
                ["is_hovered"] = cell.Entity.IsHovered
            };

            if (!cell.Entity.IsHidden && cell.Entity.Item != null)
            {
                cellState["item_type"] = cell.Entity.Item.GetType().Name;
                cellState["is_good"] = cell.Entity.Item.IsGood;
            }

            cellStates.Add(cellState);
            if (cell.Entity.IsHidden && cell.Visible)
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
        foreach (var item in cells
                     .Where(c => !c.Entity.IsHidden && c.Entity.Item != null)
                     .Select(c => c.Entity.Item!)
                     .Distinct())
        {
            revealedItems.Add(new Dictionary<string, object?>
            {
                ["item_type"] = item.GetType().Name,
                ["x"] = item.Position.X,
                ["y"] = item.Position.Y,
                ["width"] = item.Size.X,
                ["height"] = item.Size.Y,
                ["is_good"] = item.IsGood
            });
        }
        state["revealed_items"] = revealedItems;

        var bigButton = screen.GetNodeOrNull<Godot.Control>("%BigDivinationButton");
        var smallButton = screen.GetNodeOrNull<Godot.Control>("%SmallDivinationButton");
        bool bigVisible = bigButton?.Visible == true;
        bool smallVisible = smallButton?.Visible == true;
        bool bigActive = bigButton?.GetNodeOrNull<Godot.Control>("%Outline")?.Visible == true;
        bool smallActive = smallButton?.GetNodeOrNull<Godot.Control>("%Outline")?.Visible == true;

        state["tool"] = bigActive ? "big" : smallActive ? "small" : "none";
        state["can_use_big_tool"] = bigVisible;
        state["can_use_small_tool"] = smallVisible;

        var divinationsLeft = screen.GetNodeOrNull<Godot.Control>("%DivinationsLeft");
        if (divinationsLeft != null)
        {
            var textVariant = divinationsLeft.Get("text");
            if (textVariant.VariantType != Godot.Variant.Type.Nil)
                state["divinations_left_text"] = StripRichTextTags(textVariant.AsString());
        }

        var proceedButton = screen.GetNodeOrNull<NProceedButton>("%ProceedButton");
        state["can_proceed"] = proceedButton?.IsEnabled == true;

        return state;
    }

    private static Dictionary<string, object?> BuildTreasureState(TreasureRoom treasureRoom, RunState runState)
    {
        var state = new Dictionary<string, object?>();

        var treasureUI = FindFirst<NTreasureRoom>(
            ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root);

        if (treasureUI == null)
        {
            state["message"] = "Treasure room loading...";
            return state;
        }

        // Auto-open chest if not yet opened
        var chestButton = treasureUI.GetNodeOrNull<NClickableControl>("Chest");
        if (chestButton is { IsEnabled: true })
        {
            chestButton.ForceClick();
            state["message"] = "Opening chest...";
            return state;
        }

        // Show relics available for picking
        var relicCollection = treasureUI.GetNodeOrNull<NTreasureRoomRelicCollection>("%RelicCollection");
        if (relicCollection?.Visible == true)
        {
            var holders = FindAll<NTreasureRoomRelicHolder>(relicCollection)
                .Where(h => h.IsEnabled && h.Visible)
                .ToList();

            var relics = new List<Dictionary<string, object?>>();
            int index = 0;
            foreach (var holder in holders)
            {
                var relic = holder.Relic?.Model;
                if (relic == null) continue;
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
        }

        state["can_proceed"] = treasureUI.ProceedButton?.IsEnabled ?? false;

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

        // Check Osty specifically (Byrdpip/PaelsLegion are cosmetic with no real combat state)
        var osty = combatState.GetPet<Osty>();
        if (osty != null)
        {
            pets.Add(new Dictionary<string, object?>
            {
                ["id"] = osty.Monster?.Id.Entry ?? "OSTY",
                ["name"] = SafeGetText(() => osty.Monster?.Title) ?? "Otsy",
                ["alive"] = osty.IsAlive,
                ["hp"] = osty.CurrentHp,
                ["max_hp"] = osty.MaxHp,
                ["block"] = osty.Block,
                ["status"] = BuildPowersState(osty)
            });
        }

        return pets;
    }
}
