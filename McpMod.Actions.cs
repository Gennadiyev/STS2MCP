using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using Godot;

namespace STS2_MCP;

public static partial class McpMod
{
    private static Dictionary<string, object?> ExecuteAction(string action, Dictionary<string, JsonElement> data)
    {
        if (!RunManager.Instance.IsInProgress)
            return Error("No run in progress");

        var runState = RunManager.Instance.DebugOnlyGetState()!;
        var player = LocalContext.GetMe(runState);
        if (player == null)
            return Error("Could not find local player");

        return action switch
        {
            "play_card" => ExecutePlayCard(player, data),
            "use_potion" => ExecuteUsePotion(player, data),
            "discard_potion" => ExecuteDiscardPotion(player, data),
            "end_turn" => ExecuteEndTurn(player),
            "choose_map_node" => ExecuteChooseMapNode(data),
            "choose_event_option" => ExecuteChooseEventOption(data),
            "advance_dialogue" => ExecuteAdvanceDialogue(),
            "choose_rest_option" => ExecuteChooseRestOption(data),
            "shop_purchase" => ExecuteShopPurchase(player, data),
            "claim_reward" => ExecuteClaimReward(data),
            "select_card_reward" => ExecuteSelectCardReward(data),
            "skip_card_reward" => ExecuteSkipCardReward(),
            "proceed" => ExecuteProceed(),
            "select_card" => ExecuteSelectCard(data),
            "confirm_selection" => ExecuteConfirmSelection(),
            "cancel_selection" => ExecuteCancelSelection(),
            "select_bundle" => ExecuteSelectBundle(data),
            "confirm_bundle_selection" => ExecuteConfirmBundleSelection(),
            "cancel_bundle_selection" => ExecuteCancelBundleSelection(),
            "combat_select_card" => ExecuteCombatSelectCard(data),
            "combat_confirm_selection" => ExecuteCombatConfirmSelection(),
            "select_relic" => ExecuteSelectRelic(data),
            "skip_relic_selection" => ExecuteSkipRelicSelection(),
            "claim_treasure_relic" => ExecuteClaimTreasureRelic(data),
            "crystal_sphere_set_tool" => ExecuteCrystalSphereSetTool(data),
            "crystal_sphere_click_cell" => ExecuteCrystalSphereClickCell(data),
            "crystal_sphere_proceed" => ExecuteCrystalSphereProceed(),
            _ => Error($"Unknown action: {action}")
        };
    }

    private static Dictionary<string, object?> ExecutePlayCard(Player player, Dictionary<string, JsonElement> data)
    {
        if (!CombatManager.Instance.IsInProgress)
            return Error("Not in combat");
        if (!CombatManager.Instance.IsPlayPhase)
            return Error("Not in play phase - cannot act during enemy turn");
        if (CombatManager.Instance.PlayerActionsDisabled)
            return Error("Player actions are currently disabled");
        if (!player.Creature.IsAlive)
            return Error("Player creature is dead - cannot play cards");

        var combatState = player.Creature.CombatState;
        if (combatState == null)
            return Error("No combat state");

        // Get card by index in hand
        if (!data.TryGetValue("card_index", out var indexElem))
            return Error("Missing 'card_index'");

        int cardIndex = indexElem.GetInt32();
        var hand = player.PlayerCombatState?.Hand;
        if (hand == null)
            return Error("No hand available");

        if (cardIndex < 0 || cardIndex >= hand.Cards.Count)
            return Error($"card_index {cardIndex} out of range (hand has {hand.Cards.Count} cards)");

        var card = hand.Cards[cardIndex];

        if (!card.CanPlay(out var reason, out _))
            return Error($"Card '{card.Title}' cannot be played: {reason}");

        // Resolve target
        Creature? target = null;
        if (card.TargetType == TargetType.AnyEnemy)
        {
            if (!data.TryGetValue("target", out var targetElem))
                return Error("Card requires a target. Provide 'target' with an entity_id.");

            string targetId = targetElem.GetString() ?? "";
            target = ResolveTarget(combatState, targetId);
            if (target == null)
                return Error($"Target '{targetId}' not found among alive enemies");
        }

        // Play the card via the action queue (same path as the game UI)
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, target));

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Playing '{card.Title}'" + (target != null ? $" targeting {SafeGetText(() => target.Monster?.Title) ?? "target"}" : "")
        };
    }

    private static Dictionary<string, object?> ExecuteEndTurn(Player player)
    {
        if (!CombatManager.Instance.IsInProgress)
            return Error("Not in combat");
        if (!CombatManager.Instance.IsPlayPhase)
            return Error("Not in play phase - cannot act during enemy turn");
        if (CombatManager.Instance.PlayerActionsDisabled)
            return Error("Player actions are currently disabled (turn may already be ending)");

        // Match the game's own CanTurnBeEnded guard (NEndTurnButton.cs:114-123)
        var hand = NCombatRoom.Instance?.Ui?.Hand;
        if (hand != null && (hand.InCardPlay || hand.CurrentMode != NPlayerHand.Mode.Play))
            return Error("Cannot end turn while a card is being played or hand is in selection mode");

        PlayerCmd.EndTurn(player, canBackOut: false);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Ending turn"
        };
    }

    private static Dictionary<string, object?> ExecuteUsePotion(Player player, Dictionary<string, JsonElement> data)
    {
        if (!data.TryGetValue("slot", out var slotElem))
            return Error("Missing 'slot' (potion slot index)");

        int slot = slotElem.GetInt32();
        if (slot < 0 || slot >= player.PotionSlots.Count)
            return Error($"Potion slot {slot} out of range (player has {player.PotionSlots.Count} slots)");

        var potion = player.GetPotionAtSlotIndex(slot);
        if (potion == null)
            return Error($"No potion in slot {slot}");
        if (potion.IsQueued)
            return Error($"Potion '{SafeGetText(() => potion.Title)}' is already queued for use");
        if (potion.Owner.Creature.IsDead)
            return Error("Cannot use potion - player creature is dead");
        if (!potion.PassesCustomUsabilityCheck)
            return Error($"Potion '{SafeGetText(() => potion.Title)}' cannot be used right now");

        bool inCombat = CombatManager.Instance.IsInProgress;
        if (potion.Usage == PotionUsage.CombatOnly)
        {
            if (!inCombat)
                return Error($"Potion '{SafeGetText(() => potion.Title)}' can only be used in combat");
            if (!CombatManager.Instance.IsPlayPhase)
                return Error("Cannot use potions outside of play phase");
        }
        else if (potion.Usage == PotionUsage.Automatic)
            return Error($"Potion '{SafeGetText(() => potion.Title)}' is automatic and cannot be manually used");

        if (inCombat && CombatManager.Instance.PlayerActionsDisabled)
            return Error("Player actions are currently disabled");

        // Resolve target
        Creature? target = null;
        var combatState = player.Creature.CombatState;

        switch (potion.TargetType)
        {
            case TargetType.AnyEnemy:
                if (!data.TryGetValue("target", out var targetElem))
                    return Error("Potion requires a target enemy. Provide 'target' with an entity_id.");
                string targetId = targetElem.GetString() ?? "";
                if (combatState == null)
                    return Error("No combat state for target resolution");
                target = ResolveTarget(combatState, targetId);
                if (target == null)
                    return Error($"Target '{targetId}' not found among alive enemies");
                break;
            case TargetType.Self:
            case TargetType.AnyAlly:
            case TargetType.AnyPlayer:
                target = player.Creature;
                break;
            default:
                target = null;
                break;
        }

        potion.EnqueueManualUse(target);

        string targetMsg = potion.TargetType switch
        {
            TargetType.AnyEnemy => $" targeting {SafeGetText(() => target?.Monster?.Title) ?? "enemy"}",
            TargetType.Self or TargetType.AnyPlayer or TargetType.AnyAlly => " on self",
            _ => ""
        };

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Using potion '{SafeGetText(() => potion.Title)}' from slot {slot}{targetMsg}"
        };
    }

    private static Dictionary<string, object?> ExecuteDiscardPotion(Player player, Dictionary<string, JsonElement> data)
    {
        if (!data.TryGetValue("slot", out var slotElem))
            return Error("Missing 'slot' (potion slot index)");

        int slot = slotElem.GetInt32();
        if (slot < 0 || slot >= player.PotionSlots.Count)
            return Error($"Potion slot {slot} out of range (player has {player.PotionSlots.Count} slots)");

        var potion = player.GetPotionAtSlotIndex(slot);
        if (potion == null)
            return Error($"No potion in slot {slot}");

        string potionName = SafeGetText(() => potion.Title) ?? "unknown";
        _ = PotionCmd.Discard(potion);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Discarded potion '{potionName}' from slot {slot}"
        };
    }

    private static Dictionary<string, object?> ExecuteChooseEventOption(Dictionary<string, JsonElement> data)
    {
        var uiRoom = NEventRoom.Instance;
        if (uiRoom == null)
            return Error("Event room is not open");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (event option index)");

        int index = indexElem.GetInt32();

        var buttons = FindAll<NEventOptionButton>(uiRoom);

        if (buttons.Count == 0)
            return Error("No event options available");
        if (index < 0 || index >= buttons.Count)
            return Error($"Event option index {index} out of range ({buttons.Count} options)");

        var button = buttons[index];
        if (button.Option.IsLocked)
            return Error($"Event option {index} is locked");
        string title = SafeGetText(() => button.Option.Title) ?? "option";
        button.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Choosing event option: {title}"
        };
    }

    private static Dictionary<string, object?> ExecuteAdvanceDialogue()
    {
        var uiRoom = NEventRoom.Instance;
        if (uiRoom == null)
            return Error("Event room is not open");

        var ancientLayout = FindFirst<NAncientEventLayout>(uiRoom);
        if (ancientLayout == null)
            return Error("No ancient dialogue active");

        var hitbox = ancientLayout.GetNodeOrNull<NClickableControl>("%DialogueHitbox");
        if (hitbox == null || !hitbox.Visible || !hitbox.IsEnabled)
            return Error("Dialogue hitbox not available - dialogue may have ended");

        hitbox.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Advancing dialogue"
        };
    }

    private static Dictionary<string, object?> ExecuteChooseRestOption(Dictionary<string, JsonElement> data)
    {
        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (rest site option index)");

        int index = indexElem.GetInt32();

        var restRoom = NRestSiteRoom.Instance;
        if (restRoom == null)
            return Error("Rest site room is not open");

        var buttons = FindAll<NRestSiteButton>(restRoom);

        if (buttons.Count == 0)
            return Error("No rest site options available");
        if (index < 0 || index >= buttons.Count)
            return Error($"Rest option index {index} out of range ({buttons.Count} options)");

        var button = buttons[index];
        if (!button.Option.IsEnabled)
            return Error($"Rest option {index} ({button.Option.OptionId}) is disabled");
        string optionName = SafeGetText(() => button.Option.Title) ?? button.Option.OptionId;
        button.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Selecting rest site option: {optionName}"
        };
    }

    private static Dictionary<string, object?> ExecuteShopPurchase(Player player, Dictionary<string, JsonElement> data)
    {
        MerchantInventory? inventory = null;

        if (player.RunState.CurrentRoom is MerchantRoom merchantRoom)
        {
            // Regular merchant - auto-open inventory if needed
            var merchUI = NMerchantRoom.Instance;
            if (merchUI?.Inventory != null && !merchUI.Inventory.IsOpen)
                merchUI.OpenInventory();
            inventory = merchantRoom.Inventory;
        }
        else if (player.RunState.CurrentRoom is EventRoom eventRoom
                 && eventRoom.CanonicalEvent is FakeMerchant
                 && (eventRoom.LocalMutableEvent ?? eventRoom.CanonicalEvent) is FakeMerchant fakeMerchant)
        {
            // Fake merchant event - auto-open via button if needed
            if (!fakeMerchant.StartedFight)
            {
                var uiRoom = NEventRoom.Instance;
                if (uiRoom != null)
                {
                    var fmNode = FindFirst<NFakeMerchant>(uiRoom);
                    if (fmNode != null)
                    {
                        var inventoryUI = FindFirst<NMerchantInventory>(fmNode);
                        if (inventoryUI != null && !inventoryUI.IsOpen)
                        {
                            var btn = fmNode.MerchantButton;
                            if (btn != null && btn.Visible && btn.IsEnabled)
                                btn.ForceClick();
                        }
                    }
                }
            }
            inventory = fakeMerchant.Inventory;
        }
        else
        {
            return Error("Not in a shop");
        }

        if (inventory == null)
            return Error("Shop inventory not ready yet; wait a moment and retry");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (shop item index)");

        int index = indexElem.GetInt32();

        var allEntries = inventory.AllEntries.ToList();
        if (index < 0 || index >= allEntries.Count)
            return Error($"Shop item index {index} out of range ({allEntries.Count} items)");

        var entry = allEntries[index];
        if (!entry.IsStocked)
            return Error("Item is sold out");
        if (!entry.EnoughGold)
            return Error($"Not enough gold (need {entry.Cost}, have {player.Gold})");

        // Fire-and-forget purchase (same path as AutoSlay)
        _ = entry.OnTryPurchaseWrapper(inventory);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Purchasing item for {entry.Cost} gold"
        };
    }

    private static Dictionary<string, object?> ExecuteChooseMapNode(Dictionary<string, JsonElement> data)
    {
        var mapScreen = NMapScreen.Instance;
        if (mapScreen == null || !mapScreen.IsOpen)
            return Error("Map screen is not open");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (map node index from next_options)");

        int index = indexElem.GetInt32();

        var travelable = FindAll<NMapPoint>(mapScreen)
            .Where(mp => mp.State == MapPointState.Travelable && mp.Point != null)
            .OrderBy(mp => mp.Point!.coord.col)
            .ToList();

        if (travelable.Count == 0)
            return Error("No travelable map nodes available");
        if (index < 0 || index >= travelable.Count)
            return Error($"Map node index {index} out of range ({travelable.Count} options available)");

        var target = travelable[index];
        var pt = target.Point!;
        mapScreen.OnMapPointSelectedLocally(target);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Traveling to {pt.PointType} at ({pt.coord.col},{pt.coord.row})"
        };
    }

    private static Dictionary<string, object?> ExecuteClaimReward(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NRewardsScreen rewardsScreen)
            return Error("Rewards screen is not open");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (reward index)");

        int index = indexElem.GetInt32();

        var enabledButtons = FindAll<NRewardButton>(rewardsScreen)
            .Where(b => b.IsEnabled && b.Reward != null)
            .ToList();

        if (index < 0 || index >= enabledButtons.Count)
            return Error($"Reward index {index} out of range (screen has {enabledButtons.Count} claimable rewards)");

        var button = enabledButtons[index];
        var reward = button.Reward!;
        string rewardDesc = GetRewardTypeName(reward);
        if (reward is GoldReward g)
            rewardDesc = $"gold ({g.Amount})";
        else if (reward is PotionReward p)
            rewardDesc = $"potion ({SafeGetText(() => p.Potion?.Title)})";
        else if (reward is CardReward)
            rewardDesc = "card (opens card selection)";

        button.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Claiming reward: {rewardDesc}"
        };
    }

    private static Dictionary<string, object?> ExecuteSelectCardReward(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NCardRewardSelectionScreen cardScreen)
            return Error("Card reward selection screen is not open");

        if (!data.TryGetValue("card_index", out var indexElem))
            return Error("Missing 'card_index'");

        int cardIndex = indexElem.GetInt32();

        var cardHolders = FindAllSortedByPosition<NCardHolder>(cardScreen);
        if (cardIndex < 0 || cardIndex >= cardHolders.Count)
            return Error($"Card index {cardIndex} out of range (screen has {cardHolders.Count} cards)");

        var holder = cardHolders[cardIndex];
        string cardName = SafeGetText(() => holder.CardModel?.Title) ?? "unknown";
        holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Selecting card: {cardName}"
        };
    }

    private static Dictionary<string, object?> ExecuteSkipCardReward()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NCardRewardSelectionScreen cardScreen)
            return Error("Card reward selection screen is not open");

        var altButtons = FindAll<NCardRewardAlternativeButton>(cardScreen);
        if (altButtons.Count == 0)
            return Error("No skip option available on this card reward");

        altButtons[0].ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Skipping card reward"
        };
    }

    private static Dictionary<string, object?> ExecuteProceed()
    {
        // Try rewards overlay
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is NRewardsScreen rewardsScreen)
        {
            var btn = FindFirst<NProceedButton>(rewardsScreen);
            if (btn is { IsEnabled: true })
            {
                btn.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Proceeding from rewards" };
            }
        }

        // Try rest site
        if (NRestSiteRoom.Instance is { } restRoom && restRoom.ProceedButton.IsEnabled)
        {
            restRoom.ProceedButton.ForceClick();
            return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Proceeding from rest site" };
        }

        // Try merchant - close inventory first if open, then proceed
        if (NMerchantRoom.Instance is { } merchRoom)
        {
            if (merchRoom.Inventory?.IsOpen == true)
            {
                var backBtn = FindFirst<NBackButton>(merchRoom);
                if (backBtn is { IsEnabled: true })
                    backBtn.ForceClick();
            }
            if (merchRoom.ProceedButton.IsEnabled)
            {
                merchRoom.ProceedButton.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Proceeding from shop" };
            }
        }

        // Try fake merchant - close inventory first if open, then proceed
        if (NEventRoom.Instance is { } evtRoom)
        {
            var fmNode = FindFirst<NFakeMerchant>(evtRoom);
            if (fmNode != null)
            {
                var fmInventory = FindFirst<NMerchantInventory>(fmNode);
                if (fmInventory is { IsOpen: true })
                {
                    var backBtn = FindFirst<NBackButton>(fmNode);
                    if (backBtn is { IsEnabled: true })
                        backBtn.ForceClick();
                }
                var proceedBtn = FindFirst<NProceedButton>(fmNode);
                if (proceedBtn is { IsEnabled: true })
                {
                    proceedBtn.ForceClick();
                    return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Proceeding from fake merchant" };
                }
            }
        }

        // Try treasure room
        var treasureUI = FindFirst<NTreasureRoom>(
            ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root);
        if (treasureUI != null && treasureUI.ProceedButton.IsEnabled)
        {
            treasureUI.ProceedButton.ForceClick();
            return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Proceeding from treasure room" };
        }

        return Error("No proceed button available or enabled");
    }

    private static Dictionary<string, object?> ExecuteSelectCard(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (card index in the grid)");

        int index = indexElem.GetInt32();

        if (overlay is NCardGridSelectionScreen gridScreen)
        {
            var grid = FindFirst<NCardGrid>(gridScreen);
            if (grid == null)
                return Error("Card grid not found in selection screen");

            var holders = FindAllSortedByPosition<NGridCardHolder>(gridScreen);
            if (index < 0 || index >= holders.Count)
                return Error($"Card index {index} out of range ({holders.Count} cards available)");

            var holder = holders[index];
            string cardName = SafeGetText(() => holder.CardModel?.Title) ?? "unknown";
            grid.EmitSignal(NCardGrid.SignalName.HolderPressed, holder);

            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Toggling card selection: {cardName}"
            };
        }
        else if (overlay is NChooseACardSelectionScreen chooseScreen)
        {
            var holders = FindAllSortedByPosition<NGridCardHolder>(chooseScreen);
            if (index < 0 || index >= holders.Count)
                return Error($"Card index {index} out of range ({holders.Count} cards available)");

            var holder = holders[index];
            string cardName = SafeGetText(() => holder.CardModel?.Title) ?? "unknown";
            holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);

            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Choosing card: {cardName}"
            };
        }

        return Error("No card selection screen is open");
    }

    private static Dictionary<string, object?> ExecuteConfirmSelection()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is NChooseACardSelectionScreen)
            return Error("Choose-a-card screen requires no confirmation - use select_card(index) to pick directly");
        if (overlay is not NCardGridSelectionScreen screen)
            return Error("No card selection screen is open");

        // Check all preview containers (upgrade uses UpgradeSinglePreviewContainer / UpgradeMultiPreviewContainer,
        // NDeckCardSelectScreen uses PreviewContainer with %PreviewConfirm)
        foreach (var containerName in new[] { "%UpgradeSinglePreviewContainer", "%UpgradeMultiPreviewContainer", "%PreviewContainer" })
        {
            var container = screen.GetNodeOrNull<Godot.Control>(containerName);
            if (container?.Visible == true)
            {
                var confirm = container.GetNodeOrNull<NConfirmButton>("Confirm")
                              ?? container.GetNodeOrNull<NConfirmButton>("%PreviewConfirm");
                if (confirm is { IsEnabled: true })
                {
                    confirm.ForceClick();
                    return new Dictionary<string, object?>
                    {
                        ["status"] = "ok",
                        ["message"] = "Confirming selection from preview"
                    };
                }
            }
        }

        // Try main confirm button
        var mainConfirm = screen.GetNodeOrNull<NConfirmButton>("Confirm")
                          ?? screen.GetNodeOrNull<NConfirmButton>("%Confirm");
        if (mainConfirm is { IsEnabled: true })
        {
            mainConfirm.ForceClick();
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Confirming selection"
            };
        }

        // Fallback: find ANY enabled NConfirmButton in the screen tree.
        // Covers NCardGridSelectionScreen subclasses (like NDeckEnchantSelectScreen)
        // whose confirm button isn't in any of the known container paths above.
        var allConfirmButtons = FindAll<NConfirmButton>(screen);
        foreach (var btn in allConfirmButtons)
        {
            if (btn.IsEnabled && btn.IsVisibleInTree())
            {
                btn.ForceClick();
                return new Dictionary<string, object?>
                {
                    ["status"] = "ok",
                    ["message"] = "Confirming selection"
                };
            }
        }

        return Error("No confirm button is currently enabled - select more cards first");
    }

    private static Dictionary<string, object?> ExecuteCancelSelection()
    {
        var overlay = NOverlayStack.Instance?.Peek();

        // Handle choose-a-card screen (skip button)
        if (overlay is NChooseACardSelectionScreen chooseScreen)
        {
            var skipButton = chooseScreen.GetNodeOrNull<NClickableControl>("SkipButton");
            if (skipButton is { IsEnabled: true })
            {
                skipButton.ForceClick();
                return new Dictionary<string, object?>
                {
                    ["status"] = "ok",
                    ["message"] = "Skipping card choice"
                };
            }
            return Error("No skip option available - a card must be chosen");
        }

        if (overlay is not NCardGridSelectionScreen screen)
            return Error("No card selection screen is open");

        // If preview is showing, cancel back to selection
        foreach (var containerName in new[] { "%UpgradeSinglePreviewContainer", "%UpgradeMultiPreviewContainer", "%PreviewContainer" })
        {
            var container = screen.GetNodeOrNull<Godot.Control>(containerName);
            if (container?.Visible == true)
            {
                var cancelBtn = container.GetNodeOrNull<NBackButton>("Cancel")
                                ?? container.GetNodeOrNull<NBackButton>("%PreviewCancel");
                if (cancelBtn is { IsEnabled: true })
                {
                    cancelBtn.ForceClick();
                    return new Dictionary<string, object?>
                    {
                        ["status"] = "ok",
                        ["message"] = "Cancelling preview - returning to card selection"
                    };
                }
            }
        }

        // Close the screen entirely
        var closeButton = screen.GetNodeOrNull<NBackButton>("%Close");
        if (closeButton is { IsEnabled: true })
        {
            closeButton.ForceClick();
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Closing card selection screen"
            };
        }

        return Error("No cancel/close button is currently enabled - selection may be mandatory");
    }

    private static Dictionary<string, object?> ExecuteSelectBundle(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NChooseABundleSelectionScreen screen)
            return Error("No bundle selection screen is open");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (bundle index)");

        int index = indexElem.GetInt32();
        var previewContainer = screen.GetNodeOrNull<Godot.Control>("%BundlePreviewContainer");
        if (previewContainer?.Visible == true)
            return Error("A bundle preview is already open - confirm or cancel it first");

        var bundles = FindAll<NCardBundle>(screen);
        if (index < 0 || index >= bundles.Count)
            return Error($"Bundle index {index} out of range ({bundles.Count} bundles available)");

        bundles[index].Hitbox.ForceClick();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Selecting bundle {index}"
        };
    }

    private static Dictionary<string, object?> ExecuteConfirmBundleSelection()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NChooseABundleSelectionScreen screen)
            return Error("No bundle selection screen is open");

        var confirmButton = screen.GetNodeOrNull<NConfirmButton>("%Confirm");
        if (confirmButton is not { IsEnabled: true })
            return Error("Bundle confirm button is not enabled");

        confirmButton.ForceClick();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Confirming bundle selection"
        };
    }

    private static Dictionary<string, object?> ExecuteCancelBundleSelection()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NChooseABundleSelectionScreen screen)
            return Error("No bundle selection screen is open");

        var cancelButton = screen.GetNodeOrNull<NBackButton>("%Cancel");
        if (cancelButton is not { IsEnabled: true })
            return Error("Bundle cancel button is not enabled");

        cancelButton.ForceClick();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Cancelling bundle selection"
        };
    }

    private static Dictionary<string, object?> ExecuteCombatSelectCard(Dictionary<string, JsonElement> data)
    {
        var hand = NPlayerHand.Instance;
        if (hand == null || !hand.IsInCardSelection)
            return Error("No in-combat card selection is active");

        if (!data.TryGetValue("card_index", out var indexElem))
            return Error("Missing 'card_index' (index of the card in hand)");

        int index = indexElem.GetInt32();
        var holders = hand.ActiveHolders;
        if (index < 0 || index >= holders.Count)
            return Error($"Card index {index} out of range ({holders.Count} selectable cards)");

        var holder = holders[index];
        string cardName = SafeGetText(() => holder.CardModel?.Title) ?? "unknown";

        // Emit the Pressed signal - same path the game UI uses
        holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Selecting card from hand: {cardName}"
        };
    }

    private static Dictionary<string, object?> ExecuteCombatConfirmSelection()
    {
        var hand = NPlayerHand.Instance;
        if (hand == null || !hand.IsInCardSelection)
            return Error("No in-combat card selection is active");

        var confirmBtn = hand.GetNodeOrNull<NConfirmButton>("%SelectModeConfirmButton");
        if (confirmBtn == null || !confirmBtn.IsEnabled)
            return Error("Confirm button is not enabled - select more cards first");

        confirmBtn.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Confirming combat card selection"
        };
    }

    private static Dictionary<string, object?> ExecuteSelectRelic(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NChooseARelicSelection screen)
            return Error("No relic selection screen is open");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (relic index)");

        int index = indexElem.GetInt32();

        var holders = FindAll<NRelicBasicHolder>(screen);
        if (index < 0 || index >= holders.Count)
            return Error($"Relic index {index} out of range ({holders.Count} relics available)");

        var holder = holders[index];
        string relicName = SafeGetText(() => holder.Relic?.Model?.Title) ?? "unknown";
        holder.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Selecting relic: {relicName}"
        };
    }

    private static Dictionary<string, object?> ExecuteSkipRelicSelection()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NChooseARelicSelection screen)
            return Error("No relic selection screen is open");

        var skipButton = screen.GetNodeOrNull<NClickableControl>("SkipButton");
        if (skipButton is not { IsEnabled: true })
            return Error("No skip option available");

        skipButton.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Skipping relic selection"
        };
    }

    private static Dictionary<string, object?> ExecuteClaimTreasureRelic(Dictionary<string, JsonElement> data)
    {
        var treasureUI = FindFirst<NTreasureRoom>(
            ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root);
        if (treasureUI == null)
            return Error("Treasure room is not open");

        var relicCollection = treasureUI.GetNodeOrNull<NTreasureRoomRelicCollection>("%RelicCollection");
        if (relicCollection?.Visible != true)
            return Error("Relic collection is not visible - chest may not be opened yet");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (relic index)");

        int index = indexElem.GetInt32();

        var holders = FindAll<NTreasureRoomRelicHolder>(relicCollection)
            .Where(h => h.IsEnabled && h.Visible)
            .ToList();

        if (index < 0 || index >= holders.Count)
            return Error($"Relic index {index} out of range ({holders.Count} relics available)");

        var holder = holders[index];
        string relicName = SafeGetText(() => holder.Relic?.Model?.Title) ?? "unknown";
        holder.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Claiming treasure relic: {relicName}"
        };
    }

    private static Dictionary<string, object?> ExecuteCrystalSphereSetTool(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NCrystalSphereScreen screen)
            return Error("Crystal Sphere screen is not open");

        if (!data.TryGetValue("tool", out var toolElem))
            return Error("Missing 'tool' (expected 'big' or 'small')");

        string tool = toolElem.GetString() ?? "";
        var button = tool switch
        {
            "big" => screen.GetNodeOrNull<NClickableControl>("%BigDivinationButton"),
            "small" => screen.GetNodeOrNull<NClickableControl>("%SmallDivinationButton"),
            _ => null
        };

        if (button == null)
            return Error($"Unknown Crystal Sphere tool: {tool}");
        if (!button.Visible || !button.IsEnabled)
            return Error($"Crystal Sphere tool '{tool}' is not available");

        button.ForceClick();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Setting Crystal Sphere tool to {tool}"
        };
    }

    private static Dictionary<string, object?> ExecuteCrystalSphereClickCell(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NCrystalSphereScreen screen)
            return Error("Crystal Sphere screen is not open");

        if (!data.TryGetValue("x", out var xElem))
            return Error("Missing 'x' (cell x-coordinate)");
        if (!data.TryGetValue("y", out var yElem))
            return Error("Missing 'y' (cell y-coordinate)");

        int x = xElem.GetInt32();
        int y = yElem.GetInt32();

        var cell = FindAll<NCrystalSphereCell>(screen)
            .FirstOrDefault(c => c.Entity.X == x && c.Entity.Y == y);
        if (cell == null)
            return Error($"Crystal Sphere cell ({x}, {y}) was not found");
        if (!cell.Entity.IsHidden || !cell.Visible)
            return Error($"Crystal Sphere cell ({x}, {y}) is not clickable");

        cell.EmitSignal(NClickableControl.SignalName.Released, cell);
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Clicking Crystal Sphere cell ({x}, {y})"
        };
    }

    private static Dictionary<string, object?> ExecuteCrystalSphereProceed()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NCrystalSphereScreen screen)
            return Error("Crystal Sphere screen is not open");

        var proceedButton = screen.GetNodeOrNull<NProceedButton>("%ProceedButton");
        if (proceedButton is not { IsEnabled: true })
            return Error("Crystal Sphere proceed button is not enabled");

        proceedButton.ForceClick();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Proceeding from Crystal Sphere"
        };
    }

    private static Creature? ResolveTarget(CombatState combatState, string entityId)
    {
        // Try to match by entity_id pattern: "model_entry_N"
        // First try matching by combat_id if it's a pure number
        if (uint.TryParse(entityId, out uint combatId))
            return combatState.GetCreature(combatId);

        // Match by entity_id pattern (e.g., "jaw_worm_0")
        // We rebuild the entity IDs the same way as BuildEnemyState
        var entityCounts = new Dictionary<string, int>();
        foreach (var creature in combatState.Enemies)
        {
            if (!creature.IsAlive) continue;
            string baseId = creature.Monster?.Id.Entry ?? "unknown";
            if (!entityCounts.TryGetValue(baseId, out int count))
                count = 0;
            entityCounts[baseId] = count + 1;
            string generatedId = $"{baseId}_{count}";

            if (generatedId == entityId)
                return creature;
        }

        return null;
    }

    internal static Dictionary<string, object?> ExecuteMenuSelect(string option, string? seed = null)
    {
        var tree = (Engine.GetMainLoop()) as SceneTree;
        if (tree?.Root == null)
            return Error("Cannot access scene tree");

        // Game over screen
        var gameOver = FindFirst<NGameOverScreen>(tree.Root);
        if (gameOver != null)
        {
            var fieldName = option.ToLower() switch
            {
                "continue" => "_continueButton",
                "main_menu" => "_mainMenuButton",
                _ => null
            };
            if (fieldName == null)
                return Error($"Unknown game over option: {option}. Use: continue, main_menu");

            var btn = gameOver.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(gameOver);
            if (btn is NClickableControl clickable && clickable.IsEnabled)
            {
                clickable.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = $"Clicked {option}" };
            }
            return Error($"Button '{option}' not available");
        }

        // Main menu — click a menu button
        var mainMenu = FindFirst<NMainMenu>(tree.Root);
        if (mainMenu != null)
        {
            // Check if we're on singleplayer submenu
            var spSubmenu = FindFirst<NSingleplayerSubmenu>(tree.Root);
            if (spSubmenu != null && spSubmenu.Visible)
            {
                if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
                {
                    var backBtn = spSubmenu.GetType().GetField("_backButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(spSubmenu);
                    if (backBtn is NClickableControl backClickable && backClickable.IsEnabled)
                    {
                        backClickable.ForceClick();
                        return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Going back" };
                    }
                    return Error("Back button not available");
                }

                var fieldName = option.ToLower() switch
                {
                    "standard" => "_standardButton",
                    "daily" => "_dailyButton",
                    "custom" => "_customButton",
                    _ => null
                };
                if (fieldName == null)
                    return Error($"Unknown singleplayer option: {option}. Use: standard, daily, custom, back");

                var btn = spSubmenu.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(spSubmenu);
                if (btn is NClickableControl clickable)
                {
                    if (!clickable.IsEnabled)
                        return Error($"Option '{option}' is not available (locked)");
                    clickable.ForceClick();
                    return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = $"Selected {option}" };
                }
                return Error($"Could not find button for '{option}'");
            }

            // Check if we're on character select
            var charSelect = FindFirst<NCharacterSelectScreen>(tree.Root);
            if (charSelect != null && charSelect.Visible)
            {
                // "back" clicks the back/unready button
                if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
                {
                    var backBtn = charSelect.GetType().GetField("_backButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(charSelect)
                        ?? charSelect.GetType().GetField("_unreadyButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(charSelect);
                    if (backBtn is NClickableControl backClickable && backClickable.IsEnabled)
                    {
                        backClickable.ForceClick();
                        return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Going back" };
                    }
                    return Error("Back button not available");
                }

                // "confirm" or "embark" clicks the embark button to start the run
                if (string.Equals(option, "confirm", System.StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(option, "embark", System.StringComparison.OrdinalIgnoreCase))
                {
                    // Set seed before embarking if provided
                    if (!string.IsNullOrEmpty(seed) && charSelect.Lobby != null)
                    {
                        charSelect.Lobby.SetSeed(seed);
                    }

                    var embarkBtn = charSelect.GetType().GetField("_embarkButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(charSelect);
                    if (embarkBtn is NClickableControl embarkClickable && embarkClickable.IsEnabled)
                    {
                        var msg = string.IsNullOrEmpty(seed) ? "Embarking on run" : $"Embarking on run (seed: {seed})";
                        embarkClickable.ForceClick();
                        return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = msg };
                    }
                    return Error("Embark button not available — select a character first");
                }

                var buttons = FindAll<NCharacterSelectButton>(charSelect);
                foreach (var btn in buttons)
                {
                    if (btn.Character != null && (
                        string.Equals(btn.Character.Id.Entry, option, System.StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(SafeGetText(() => btn.Character.Title), option, System.StringComparison.OrdinalIgnoreCase)))
                    {
                        if (btn.IsLocked)
                            return Error($"Character '{option}' is locked");
                        btn.Select();
                        return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = $"Selected {SafeGetText(() => btn.Character.Title)}. Use 'confirm' to embark." };
                    }
                }
                return Error($"Character '{option}' not found. Available: {string.Join(", ", buttons.Where(b => !b.IsLocked).Select(b => b.Character?.Id.Entry))}");
            }

            // Main menu buttons
            var menuFieldName = option.ToLower() switch
            {
                "singleplayer" => "_singleplayerButton",
                "multiplayer" => "_multiplayerButton",
                "compendium" => "_compendiumButton",
                "timeline" => "_timelineButton",
                "settings" => "_settingsButton",
                "continue" => "_continueButton",
                "quit" => "_quitButton",
                _ => null
            };
            if (menuFieldName == null)
                return Error($"Unknown menu option: {option}");

            var menuBtn = mainMenu.GetType().GetField(menuFieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(mainMenu);
            if (menuBtn is NClickableControl menuClickable)
            {
                if (!menuClickable.IsEnabled)
                    return Error($"Option '{option}' is not available");
                menuClickable.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = $"Selected {option}" };
            }
            return Error($"Could not find button for '{option}'");
        }

        return Error("Not on a menu screen");
    }
}
