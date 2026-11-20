using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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
using MegaCrit.Sts2.Core.Models;
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
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.ProfileScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using Godot;

namespace STS2_MCP;

public static partial class McpMod
{
    private static Dictionary<string, object?> ExecuteAction(string action, Dictionary<string, JsonElement> data,
        CombatStateWait? wait = null)
    {
        var manager = RunManager.Instance;
        if (!manager.IsInProgress)
            return Error("No run in progress");
        if (manager.NetService.Type.IsMultiplayer())
            return BlockedAction(RunModeConflict(isMultiplayer: true));

        var runState = manager.DebugOnlyGetState()!;
        var player = LocalContext.GetMe(runState);
        if (player == null)
            return Error("Could not find local player");

        var tree = (Godot.Engine.GetMainLoop()) as SceneTree;
        if (tree?.Root != null && IsAnyFtueVisible(tree.Root))
            return Error("Blocking popup active. Use menu_select with one of the advertised popup options before gameplay actions.");

        return action switch
        {
            "play_card" => ExecutePlayCard(player, data, wait),
            "use_potion" => ExecuteUsePotion(player, data),
            "discard_potion" => ExecuteDiscardPotion(player, data),
            "end_turn" => ExecuteEndTurn(player, wait),
            "choose_map_node" => ExecuteChooseMapNode(data, wait),
            "choose_event_option" => ExecuteChooseEventOption(data),
            "advance_dialogue" => ExecuteAdvanceDialogue(),
            "choose_rest_option" => ExecuteChooseRestOption(data),
            "shop_purchase" => ExecuteShopPurchase(player, data),
            "claim_reward" => ExecuteClaimReward(data),
            "select_card_reward" => ExecuteSelectCardReward(data),
            "select_card_reward_alternative" => ExecuteSelectCardRewardAlternative(data),
            "skip_card_reward" => ExecuteSkipCardReward(),
            "proceed" => ExecuteProceed(player, data),
            "select_card" => ExecuteSelectCard(data),
            "confirm_selection" => ExecuteConfirmSelection(),
            "cancel_selection" => ExecuteCancelSelection(),
            "select_bundle" => ExecuteSelectBundle(data),
            "confirm_bundle_selection" => ExecuteConfirmBundleSelection(),
            "cancel_bundle_selection" => ExecuteCancelBundleSelection(),
            "combat_select_card" => ExecuteCombatSelectCard(data),
            "combat_confirm_selection" => ExecuteCombatConfirmSelection(wait),
            "select_relic" => ExecuteSelectRelic(data),
            "skip_relic_selection" => ExecuteSkipRelicSelection(),
            "claim_treasure_relic" => ExecuteClaimTreasureRelic(data),
            "crystal_sphere_set_tool" => ExecuteCrystalSphereSetTool(data),
            "crystal_sphere_click_cell" => ExecuteCrystalSphereClickCell(data),
            "crystal_sphere_proceed" => ExecuteCrystalSphereProceed(),
            _ => Error($"Unknown action: {action}")
        };
    }

    private static Dictionary<string, object?> ExecutePlayCard(Player player, Dictionary<string, JsonElement> data,
        CombatStateWait? wait = null)
    {
        if (GetCombatActionBlock(player) is { } block)
            return BlockedAction(block);

        if (!data.TryGetValue("card_index", out var indexElem))
            return Error("Missing 'card_index'");
        if (ReadTargetPlayerSlot(data, out var targetPlayerSlot) is { } targetError)
            return Error(targetError);

        string? target = data.TryGetValue("target", out var targetElem) && targetElem.ValueKind == JsonValueKind.String
            ? targetElem.GetString() : null;
        return ExecutePlayableCard(player, indexElem.GetInt32(), target, wait, targetPlayerSlot);
    }

    private static Dictionary<string, object?> ExecutePlayableCard(Player player, int cardIndex,
        string? targetId, CombatStateWait? wait, int? targetPlayerSlot = null)
    {
        var combatState = player.Creature.CombatState;
        if (combatState == null)
            return Error("No combat state");

        var hand = player.PlayerCombatState?.Hand;
        if (hand == null)
            return Error("No hand available");

        if (cardIndex < 0 || cardIndex >= hand.Cards.Count)
            return Error($"card_index {cardIndex} out of range (hand has {hand.Cards.Count} cards)");

        var card = hand.Cards[cardIndex];

        if (!card.CanPlay(out var reason, out _))
            return Error($"Card '{card.Title}' cannot be played: {reason}");
        bool targetsPlayer = card.TargetType is TargetType.AnyAlly or TargetType.AnyPlayer;
        if (targetPlayerSlot.HasValue && !targetsPlayer)
            return Error("This card does not take a player target.");

        Creature? target = null;
        if (card.TargetType == TargetType.AnyEnemy)
        {
            if (targetId == null)
                return Error("Card requires a target. Provide 'target' with an entity_id.");

            target = ResolveTarget(combatState, targetId);
            if (target == null)
                return Error($"Target '{targetId}' not found among alive enemies");
        }
        else if (targetsPlayer && IsMultiplayerRun())
        {
            if (targetPlayerSlot is not int slot)
                return Error("This card requires 'target_player_slot' from the current players list.");
            target = ResolvePlayerTarget(player, card.TargetType, slot);
            if (target == null || !card.IsValidTarget(target))
                return Error("Choose a living player eligible for this card from the current players list.");
        }

        // Play the card via the action queue (same path as the game UI)
        var play = new PlayCardAction(card, target);
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(play);
        if (wait != null)
            wait.CardCompletion = play.CompletionTask;

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Playing '{card.Title}'" + (target != null ? $" targeting {GetTargetName(target)}" : "")
        };
    }

    private static Dictionary<string, object?> ExecuteEndTurn(Player player, CombatStateWait? wait)
    {
        if (GetCombatActionBlock(player) is { } block)
            return BlockedAction(block);

        if (wait != null)
            wait.EndedTurnNumber = player.PlayerCombatState?.TurnNumber;
        PlayerCmd.EndTurn(player, canBackOut: false);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Ending turn"
        };
    }

    private static Dictionary<string, object?> ExecuteUsePotion(Player player, Dictionary<string, JsonElement> data)
    {
        if (!player.CanUseOrRemovePotions)
            return BlockedAction(new("potion_inventory_locked", "The game has locked potion use and removal during the current choice.", false));
        if (!data.TryGetValue("slot", out var slotElem))
            return Error("Missing 'slot' (potion slot index)");
        if (ReadTargetPlayerSlot(data, out var targetPlayerSlot) is { } targetError)
            return Error(targetError);

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
        bool inCombat = CombatManager.Instance.IsInProgress;
        if (potion.Usage == PotionUsage.CombatOnly && !inCombat)
            return Error($"Potion '{SafeGetText(() => potion.Title)}' can only be used in combat");
        if (potion.Usage == PotionUsage.Automatic)
            return Error($"Potion '{SafeGetText(() => potion.Title)}' is automatic and cannot be manually used");
        if (inCombat && GetCombatActionBlock(player) is { } block)
            return BlockedAction(block);
        if (!potion.PassesCustomUsabilityCheck)
            return Error($"Potion '{SafeGetText(() => potion.Title)}' cannot be used right now");
        if (targetPlayerSlot.HasValue && potion.TargetType is not (TargetType.AnyPlayer or TargetType.AnyAlly))
            return Error("This potion does not take a player target.");
        if (IsMultiplayerRun() && data.ContainsKey("target") && potion.TargetType is (TargetType.AnyPlayer or TargetType.AnyAlly))
            return Error("Use 'target_player_slot' to target a player with this potion.");

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
                target = player.Creature;
                break;
            case TargetType.AnyAlly:
            case TargetType.AnyPlayer:
                if (targetPlayerSlot is int playerSlot)
                {
                    target = ResolvePlayerTarget(player, potion.TargetType, playerSlot);
                    if (target == null)
                        return Error("Choose a living player eligible for this potion from the current players list.");
                }
                else if (potion.TargetType == TargetType.AnyAlly && IsMultiplayerRun())
                    return Error("This potion requires 'target_player_slot' from the current players list.");
                else
                    target = player.Creature;
                break;
            default:
                target = null;
                break;
        }

        potion.EnqueueManualUse(target);

        string targetMsg = target == player.Creature ? " on self" : target != null ? $" targeting {GetTargetName(target)}" : "";

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Using potion '{SafeGetText(() => potion.Title)}' from slot {slot}{targetMsg}"
        };
    }

    private static string? ReadTargetPlayerSlot(Dictionary<string, JsonElement> data, out int? slot)
    {
        slot = null;
        if (!data.TryGetValue("target_player_slot", out var value))
            return null;
        if (!IsMultiplayerRun())
            return "'target_player_slot' is available in multiplayer.";
        if (data.ContainsKey("target"))
            return "Provide either 'target' or 'target_player_slot'.";
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int index) || index < 0)
            return "'target_player_slot' must be a non-negative integer.";
        slot = index;
        return null;
    }

    private static Creature? ResolvePlayerTarget(Player actor, TargetType targetType, int slot)
    {
        var players = actor.RunState.Players;
        if (slot >= players.Count)
            return null;
        var player = players[slot];
        return player.Creature.IsAlive && (targetType != TargetType.AnyAlly || player != actor)
            ? player.Creature : null;
    }

    private static string GetTargetName(Creature target) =>
        SafeGetText(() => target.Monster?.Title ?? target.Player?.Character.Title) ?? "target";

    private static Dictionary<string, object?> ExecuteDiscardPotion(Player player, Dictionary<string, JsonElement> data)
    {
        if (!player.CanUseOrRemovePotions)
            return BlockedAction(new("potion_inventory_locked", "The game has locked potion use and removal during the current choice.", false));
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

        string potionName = SafeGetText(() => potion.Title) ?? "unknown";
        if (IsMultiplayerRun())
        {
            bool inCombat = CombatManager.Instance.IsInProgress;
            if (inCombat && GetCombatActionBlock(player) is { } block)
                return BlockedAction(block);
            // Use the same synchronized action as the native potion menu.
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(
                new DiscardPotionGameAction(player, (uint)slot, inCombat));
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Submitted discard for potion '{potionName}' from slot {slot}"
            };
        }

        _ = PotionCmd.Discard(potion);
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Discarded potion '{potionName}' from slot {slot}"
        };
    }

    private static Dictionary<string, object?> ExecuteChooseEventOption(Dictionary<string, JsonElement> data)
    {
        if (RunManager.Instance.DebugOnlyGetState()?.CurrentRoom is not EventRoom eventRoom)
            return Error("Event room is not open");

        if (!data.TryGetValue("index", out var indexElem) ||
            indexElem.ValueKind != JsonValueKind.Number || !indexElem.TryGetInt32(out int index) || index < 0)
            return Error("Provide a nonnegative integer 'index' from the current event state.");

        var layout = NEventRoom.Instance?.Layout;
        var button = GetActiveEventOptionButtons(GetReadyLocalEvent(eventRoom), layout)
            .FirstOrDefault(candidate => candidate.GetIndex() == index);
        if (button == null)
            return BlockedAction(new("event_option_unavailable",
                $"Event option {index} is unavailable. Check the current event state.", true));

        bool eventInputReady = CanUseEventControls(layout) &&
            !IsControlVisibleInTree(GetEventDialogueHitbox(layout));
        if (GetEventOptionBlock(button, eventInputReady, IsEventOptionButtonEnabled(button),
                EventOptionWillKillPlayer(button)) is { } block)
            return BlockedAction(block);

        bool isVote = button.Event.IsShared && !button.Option.IsProceed &&
            button.Event.Owner is { RunState.Players.Count: > 1 };
        string title = SafeGetText(() => button.Option.Title) ?? "option";
        button.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["choice_status"] = isVote ? "vote_submitted" : "submitted",
            ["message"] = isVote ? $"Submitted vote for {title}." : $"Submitted event choice {title}."
        };
    }

    private static Dictionary<string, object?> ExecuteAdvanceDialogue()
    {
        if (RunManager.Instance.DebugOnlyGetState()?.CurrentRoom is not EventRoom)
            return Error("Event room is not open");

        var layout = NEventRoom.Instance?.Layout;
        var hitbox = GetEventDialogueHitbox(layout);
        if (!CanUseEventControls(layout) || hitbox == null || !IsControlVisibleOrActionable(hitbox))
            return BlockedAction(new("event_dialogue_unavailable",
                "The dialogue advance control is unavailable. Check the current event state.", true));

        hitbox.ForceClick();

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Advancing dialogue"
        };
    }

    private static bool CanUseEventControls(NEventLayout? layout) =>
        IsNodeVisible(layout) && !RunManager.Instance.IsChangingRooms &&
        !IsMapScreenOpenOrVisible() && NOverlayStack.Instance?.Peek() == null && GetOpenModalNode() == null;

    private static EventModel? GetReadyLocalEvent(EventRoom room) =>
        !RunManager.Instance.IsChangingRooms && NEventRoom.Instance != null ? room.LocalMutableEvent : null;

    private static NClickableControl? GetEventDialogueHitbox(NEventLayout? layout) =>
        (layout as NAncientEventLayout)?.GetNodeOrNull<NClickableControl>("%DialogueHitbox");

    private static IEnumerable<NEventOptionButton> GetActiveEventOptionButtons(EventModel? liveEvent, NEventLayout? layout)
    {
        if (liveEvent == null || layout == null || !IsLiveNode(layout))
            yield break;

        var options = liveEvent.CurrentOptions;
        foreach (var button in layout.OptionButtons)
        {
            if (!IsLiveNode(button) || !ReferenceEquals(button.Event, liveEvent))
                continue;

            int index = button.GetIndex();
            // The native layout replaces the model's options with a synthetic Proceed button on completion.
            if (liveEvent.IsFinished ? index == 0 && button.Option.IsProceed :
                index < options.Count && ReferenceEquals(button.Option, options[index]))
                yield return button;
        }
    }

    private static bool IsEventOptionButtonEnabled(NEventOptionButton button) =>
        IsControlVisibleOrActionable(button) && button.MouseFilter != Control.MouseFilterEnum.Ignore;

    private static bool EventOptionWillKillPlayer(NEventOptionButton button) =>
        button.Event.Owner is { } owner && button.Option.WillKillPlayer?.Invoke(owner) == true;

    private static ActionBlock? GetEventOptionBlock(NEventOptionButton button, bool eventInputReady,
        bool isEnabled, bool willKillPlayer)
    {
        if (button.Option.IsLocked)
            return new("event_option_locked", "This event option is locked.", false);
        if (willKillPlayer && button.Event.Owner is { RunState.Players.Count: > 1 })
            return new("event_option_lethal_multiplayer", "The game blocks this lethal choice in multiplayer.", false);
        if (!eventInputReady || !isEnabled)
            return new("event_option_not_ready", "This event option is not ready. Check the current event state.", true);

        return null;
    }

    private static Dictionary<string, object?> ExecuteChooseRestOption(Dictionary<string, JsonElement> data) =>
        ExecuteChooseRestOption(data, out _);

    private static Dictionary<string, object?> ExecuteChooseRestOption(Dictionary<string, JsonElement> data,
        out Task<Dictionary<string, object?>>? completion)
    {
        completion = null;
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
        if (button.Option is MendRestSiteOption mend)
            return SubmitMend(restRoom, button, mend, data, out completion);
        if (data.ContainsKey("target_player_slot"))
            return Error("This rest option does not take a teammate target.");
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
        if (!data.TryGetValue("index", out var indexElem) ||
            indexElem.ValueKind != JsonValueKind.Number || !indexElem.TryGetInt32(out int index))
            return Error("Missing or invalid 'index' (shop item index)");

        MerchantInventory? inventory;
        System.Action? openInventory = null;

        if (player.RunState.CurrentRoom is MerchantRoom merchantRoom)
        {
            inventory = GetLocalMerchantInventory(merchantRoom);
            if (NMerchantRoom.Instance is { Inventory: { IsOpen: false } } room)
                openInventory = room.OpenInventory;
        }
        else if (player.RunState.CurrentRoom is EventRoom eventRoom
                 && eventRoom.CanonicalEvent is FakeMerchant
                 && GetReadyLocalEvent(eventRoom) is FakeMerchant fakeMerchant)
        {
            if (fakeMerchant.StartedFight)
                return Error("The fake merchant is no longer available");

            inventory = GetLocalMerchantInventory(fakeMerchant);
            if (NEventRoom.Instance is { } eventView &&
                FindFirst<NFakeMerchant>(eventView) is { Inventory: { IsOpen: false } } view &&
                view.MerchantButton is { Visible: true, IsEnabled: true } button)
                openInventory = button.ForceClick;
        }
        else
        {
            return Error("Not in a shop");
        }

        if (inventory == null)
            return Error("Shop inventory not ready yet; wait a moment and retry");

        if (GetLastMerchantPurchase(inventory) is { } previous &&
            BuildMerchantPurchaseResult(previous) is { } receipt && receipt["status"] is "pending")
        {
            if (previous.Index == index)
                return receipt;

            var blocked = BlockedAction(new("purchase_in_progress",
                "Finish the pending order before requesting a different item.", false));
            blocked["requested_item_index"] = index;
            blocked["pending_item_index"] = previous.Index;
            return blocked;
        }
        if (!CanUseShopControls())
            return Error("Shop controls are blocked. Check the current state.");

        var allEntries = GetMerchantEntries(inventory);
        if (index < 0 || index >= allEntries.Count)
            return Error($"Shop item index {index} out of range ({allEntries.Count} items)");

        var entry = allEntries[index];
        if (!entry.IsStocked)
            return Error("Item is sold out");
        if (!entry.EnoughGold)
            return Error($"Not enough gold (need {entry.Cost}, have {player.Gold})");

        int price = entry.Cost;
        // Interactive orders use the native inventory UI and may await a selection.
        openInventory?.Invoke();
        var completion = StartMerchantPurchase(inventory, entry);
        if (completion == null)
            return Error("Shop inventory changed. Read shop state before trying again.");
        return BuildMerchantPurchaseResult(RecordMerchantPurchase(inventory, index, price, completion));
    }

    private static Dictionary<string, object?> ExecuteChooseMapNode(Dictionary<string, JsonElement> data,
        CombatStateWait? wait = null, Player? mapVoter = null)
    {
        var mapScreen = NMapScreen.Instance;
        if (mapScreen == null || (!mapScreen.IsOpen && !IsNodeVisible(mapScreen)))
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
        if (mapVoter != null)
        {
            if (IsMultiplayerMapTraveling())
                return BlockedAction(new("map_travel_in_progress", "The party is already traveling.", true));

            // The native control pings teammates when the same node is selected again.
            bool ping = RunManager.Instance.MapSelectionSynchronizer.GetVote(mapVoter)?.coord == pt.coord;
            mapScreen.OnMapPointSelectedLocally(target);
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["vote_status"] = ping ? "pinged" : "submitted",
                ["vote_col"] = pt.coord.col,
                ["vote_row"] = pt.coord.row,
                ["message"] = ping
                    ? $"Pinged your vote at ({pt.coord.col},{pt.coord.row})."
                    : $"Voted for {pt.PointType} at ({pt.coord.col},{pt.coord.row}). Check multiplayer state for the resolved destination."
            };
        }

        if (wait != null && RunManager.Instance.DebugOnlyGetState() is { } run)
            wait.MapTravel = new(run, run.CurrentRoom, run.CurrentActIndex, pt.coord);
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

        var cardHolders = GetCardRewardHolders(cardScreen);
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

    private static List<NCardHolder> GetCardRewardHolders(NCardRewardSelectionScreen screen) =>
        FindAllSortedByPosition<NCardHolder>(screen).Where(holder => holder.CardModel != null).ToList();

    private static IEnumerable<(CardRewardAlternative Option, NCardRewardAlternativeButton Button)>
        GetCardRewardAlternatives(NCardRewardSelectionScreen screen)
    {
        if (GetInstanceFieldValue(screen, "_extraOptions") is not IReadOnlyList<CardRewardAlternative> options ||
            GetInstanceFieldValue(screen, "_rewardAlternativesContainer") is not Control container)
            yield break;

        // Godot keeps replaced buttons in the tree until the end of the frame.
        var buttons = container.GetChildren().OfType<NCardRewardAlternativeButton>()
            .Where(button => !button.IsQueuedForDeletion()).ToList();
        if (buttons.Count != options.Count)
            yield break;

        for (int index = 0; index < options.Count; index++)
            yield return (options[index], buttons[index]);
    }

    private static TaskCompletionSource<int?>? GetPendingCardRewardChoice(NCardRewardSelectionScreen screen) =>
        GetInstanceFieldValue(screen, "_completionSource") is TaskCompletionSource<int?> choice &&
        !choice.Task.IsCompleted ? choice : null;

    private static Dictionary<string, object?> ExecuteSkipCardReward()
    {
        return ExecuteCardRewardAlternative("Skip");
    }

    private static Dictionary<string, object?> ExecuteSelectCardRewardAlternative(Dictionary<string, JsonElement> data)
    {
        if (!data.TryGetValue("alternative_id", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String ||
            idElement.GetString() is not { Length: > 0 } id)
            return Error("Missing or invalid 'alternative_id'");

        return ExecuteCardRewardAlternative(id);
    }

    private static Dictionary<string, object?> ExecuteCardRewardAlternative(string id)
    {
        if (IsMapScreenOpenOrVisible() || NOverlayStack.Instance?.Peek() is not NCardRewardSelectionScreen cardScreen)
            return Error("Card reward selection screen is not open");

        var choice = GetPendingCardRewardChoice(cardScreen);
        if (choice == null)
            return Error("The card reward is not ready for another choice");

        var (_, button) = GetCardRewardAlternatives(cardScreen)
            .FirstOrDefault(pair => pair.Option.OptionId == id && IsControlVisibleOrActionable(pair.Button));
        if (button == null)
            return Error(id == "Skip" ? "No skip option available on this card reward" :
                $"Card reward alternative '{id}' is not available");

        button.ForceClick();
        if (!choice.Task.IsCompletedSuccessfully)
            return Error($"The card reward did not accept alternative '{id}'");

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = id == "Skip" ? "Skipping card reward" : $"Submitted card reward alternative '{id}'",
            ["alternative_id"] = id
        };
    }

    private static Dictionary<string, object?> ExecuteProceed(Player player, Dictionary<string, JsonElement> data)
    {
        bool leaveShop = false;
        if (data.TryGetValue("leave_shop", out var leaveShopElement))
        {
            if (leaveShopElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return Error("'leave_shop' must be a boolean.");
            leaveShop = leaveShopElement.GetBoolean();
        }

        bool isShopRoom = player.RunState.CurrentRoom is MerchantRoom or EventRoom { CanonicalEvent: FakeMerchant };
        if (leaveShop)
        {
            if (!isShopRoom)
                return Error("'leave_shop' is available only in a shop or Fake Merchant room.");
            return ExecuteShopProceed(player, leaveShop: true);
        }

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

        if (isShopRoom)
            return ExecuteShopProceed(player, leaveShop: false);

        if (player.RunState.CurrentRoom is TreasureRoom && !IsMapScreenOpenOrVisible())
        {
            var treasure = GetVisibleTreasureRoom();
            if (treasure == null || GetAvailableTreasureProceedButton(treasure) is not { } button)
                return BlockedAction(new("treasure_not_ready",
                    "The treasure room's Proceed or Skip control is not ready. Check the treasure state.", true));

            bool isSkip = button.IsSkip;
            button.ForceClick();
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = isSkip ? "Skipping treasure relic selection" : "Proceeding from treasure room"
            };
        }

        return Error("No proceed button available or enabled");
    }

    private static NBackButton? GetAvailableShopBackButton(NMerchantInventory inventory)
    {
        // The fake merchant uses a subclass with the same inherited private Back button.
        return GetInstanceFieldValue(inventory, "_backButton") is NBackButton { IsEnabled: true } button
            && IsNodeVisible(button) ? button : null;
    }

    private static bool CanUseShopControls() =>
        !IsMapScreenOpenOrVisible() && NOverlayStack.Instance?.Peek() == null && GetOpenModalNode() == null;

    private static Dictionary<string, object?> ExecuteShopProceed(Player player, bool leaveShop)
    {
        if (!CanUseShopControls())
            return Error("Shop controls are blocked. Check the current state.");

        var room = player.RunState.CurrentRoom;
        NMerchantInventory? inventory;
        NProceedButton? proceedButton;
        string roomName;
        if (room is MerchantRoom && NMerchantRoom.Instance is { } merchantRoom)
        {
            (inventory, proceedButton, roomName) = (merchantRoom.Inventory, merchantRoom.ProceedButton, "shop");
        }
        else if (room is EventRoom { CanonicalEvent: FakeMerchant }
                 && NEventRoom.Instance is { } eventRoom
                 && FindFirst<NFakeMerchant>(eventRoom) is { } fakeMerchant)
        {
            (inventory, proceedButton, roomName) = (fakeMerchant.Inventory, FindFirst<NProceedButton>(fakeMerchant), "fake merchant");
        }
        else
        {
            return Error("Shop controls are not ready. Check the current state.");
        }

        if (inventory is { IsOpen: true })
        {
            var backButton = GetAvailableShopBackButton(inventory);
            if (backButton == null)
                return Error("Shop inventory cannot close yet. Check the shop state before retrying.");
            if (leaveShop && !IsLiveNode(proceedButton))
                return Error("The shop exit is not ready. Check the shop state before retrying.");

            // The native InventoryClosed signal enables Proceed during this click.
            backButton.ForceClick();
            if (!leaveShop)
            {
                return new Dictionary<string, object?>
                {
                    ["status"] = "ok",
                    ["message"] = "Closing shop inventory. Check the shop state before calling proceed again to leave."
                };
            }

            if (!ReferenceEquals(room, player.RunState.CurrentRoom) || !CanUseShopControls())
                return Error("Shop controls changed after closing the inventory. Check the current state.");
        }

        if (inventory?.IsOpen == true || !IsControlVisibleOrActionable(proceedButton))
            return Error("The shop exit is not ready. Check the shop state before retrying.");

        proceedButton!.ForceClick();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Proceeding from {roomName}"
        };
    }

    private static Dictionary<string, object?> ExecuteSelectCard(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (card index in the grid)");

        int index = indexElem.GetInt32();

        if (overlay is NCardGridSelectionScreen gridScreen)
        {
            if (GetVisibleCardSelectionPreview(gridScreen) != null)
                return Error("A card preview is already open - confirm or cancel it first");

            var grid = FindFirst<NCardGrid>(gridScreen);
            if (grid == null)
                return Error("Card grid not found in selection screen");

            var holders = GetSelectableGridCards(gridScreen);
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
            var holders = GetSelectableGridCards(chooseScreen);
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

    private static readonly string[] CardSelectionPreviewPaths =
    {
        "%UpgradeSinglePreviewContainer",
        "%UpgradeMultiPreviewContainer",
        "%EnchantSinglePreviewContainer",
        "%EnchantMultiPreviewContainer",
        "%PreviewContainer"
    };

    private static Control? GetVisibleCardSelectionPreview(NCardGridSelectionScreen screen)
    {
        foreach (var path in CardSelectionPreviewPaths)
        {
            var preview = screen.GetNodeOrNull<Control>(path);
            if (IsNodeVisible(preview))
                return preview;
        }

        return null;
    }

    private static T? GetEnabledSelectionButton<T>(Node parent, string path) where T : NClickableControl
    {
        var button = parent.GetNodeOrNull<T>(path);
        return IsControlVisibleOrActionable(button) ? button : null;
    }

    private static (Control? Preview, NConfirmButton? Confirm, NBackButton? Cancel)
        GetCardSelectionControls(NCardGridSelectionScreen screen)
    {
        var preview = GetVisibleCardSelectionPreview(screen);
        if (preview != null)
        {
            return (preview,
                GetEnabledSelectionButton<NConfirmButton>(preview, "Confirm")
                    ?? GetEnabledSelectionButton<NConfirmButton>(preview, "%PreviewConfirm"),
                GetEnabledSelectionButton<NBackButton>(preview, "Cancel")
                    ?? GetEnabledSelectionButton<NBackButton>(preview, "%PreviewCancel"));
        }

        return (null,
            GetEnabledSelectionButton<NConfirmButton>(screen, "Confirm")
                ?? GetEnabledSelectionButton<NConfirmButton>(screen, "%Confirm"),
            GetEnabledSelectionButton<NBackButton>(screen, "%Close"));
    }

    private static List<NGridCardHolder> GetSelectableGridCards(Node screen) =>
        FindAllSortedByPosition<NGridCardHolder>(screen)
            .Where(holder => holder.CardModel != null && IsNodeVisible(holder))
            .ToList();

    private static Dictionary<string, object?> ExecuteConfirmSelection()
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is NChooseACardSelectionScreen)
            return Error("Choose-a-card screen requires no confirmation - use select_card(index) to pick directly");
        if (overlay is not NCardGridSelectionScreen screen)
            return Error("No card selection screen is open");

        var (preview, confirm, _) = GetCardSelectionControls(screen);
        if (confirm == null)
            return Error("No confirm button is currently enabled");

        confirm.ForceClick();
        bool openedPreview = preview == null
            && IsLiveNode(screen)
            && NOverlayStack.Instance?.Peek() == screen
            && GetVisibleCardSelectionPreview(screen) != null;
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = openedPreview
                ? "Preview opened. Use confirm_selection to commit it."
                : "Confirming card selection"
        };
    }

    private static Dictionary<string, object?> ExecuteCancelSelection()
    {
        var overlay = NOverlayStack.Instance?.Peek();

        if (overlay is NChooseACardSelectionScreen chooseScreen)
        {
            var skip = GetEnabledSelectionButton<NClickableControl>(chooseScreen, "SkipButton");
            if (skip == null)
                return Error("No skip option available - a card must be chosen");

            skip.ForceClick();
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Skipping card choice"
            };
        }

        if (overlay is not NCardGridSelectionScreen screen)
            return Error("No card selection screen is open");

        var (preview, _, cancel) = GetCardSelectionControls(screen);
        if (cancel == null)
            return Error("No cancel/close button is currently enabled");

        cancel.ForceClick();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = preview != null
                ? "Cancelling preview - returning to card selection"
                : "Closing card selection screen"
        };
    }

    private static bool IsBundlePreviewShowing(NChooseABundleSelectionScreen screen) =>
        IsNodeVisible(screen.GetNodeOrNull<Control>("%BundlePreviewContainer"));

    private static Dictionary<string, object?> ExecuteSelectBundle(Dictionary<string, JsonElement> data)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay is not NChooseABundleSelectionScreen screen)
            return Error("No bundle selection screen is open");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (bundle index)");

        int index = indexElem.GetInt32();
        if (IsBundlePreviewShowing(screen))
            return Error("A bundle preview is already open - confirm or cancel it first");

        var bundles = FindAll<NCardBundle>(screen);
        if (index < 0 || index >= bundles.Count)
            return Error($"Bundle index {index} out of range ({bundles.Count} bundles available)");

        var hitbox = bundles[index].Hitbox;
        if (!IsControlVisibleOrActionable(hitbox))
            return Error($"Bundle {index} is not selectable");

        hitbox.ForceClick();
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

        var confirmButton = GetEnabledSelectionButton<NConfirmButton>(screen, "%Confirm");
        if (!IsBundlePreviewShowing(screen) || confirmButton == null)
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

        var cancelButton = GetEnabledSelectionButton<NBackButton>(screen, "%Cancel");
        if (!IsBundlePreviewShowing(screen) || cancelButton == null)
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
        if (holder.CardModel == null)
            return Error($"No selectable card at index {index}");

        string cardName = SafeGetText(() => holder.CardModel.Title) ?? "unknown";

        // Emit the Pressed signal - same path the game UI uses
        holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Selecting card from hand: {cardName}"
        };
    }

    private static Dictionary<string, object?> ExecuteCombatConfirmSelection(CombatStateWait? wait = null)
    {
        var hand = NPlayerHand.Instance;
        if (hand == null || !hand.IsInCardSelection)
            return Error("No in-combat card selection is active");

        var confirmBtn = GetEnabledSelectionButton<NConfirmButton>(hand, "%SelectModeConfirmButton");
        if (confirmBtn == null)
            return Error("Confirm button is not enabled - select more cards first");

        if (wait != null)
            wait.HandConfirmation = new(hand, GetHandSelectionPrompt(hand));
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

        var holders = GetRelicSelectionHolders(screen);
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

    private static List<NRelicBasicHolder> GetRelicSelectionHolders(NChooseARelicSelection screen) =>
        FindAll<NRelicBasicHolder>(screen).Where(holder => holder.Relic?.Model != null).ToList();

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
        if (GetVisibleTreasureRoom() is not { } treasure)
            return Error("Treasure room is not open");

        if (!data.TryGetValue("index", out var indexElem))
            return Error("Missing 'index' (relic index)");

        int index = indexElem.GetInt32();

        var holders = GetAvailableTreasureRelicHolders(treasure);
        if (holders.Count == 0)
            return BlockedAction(new("treasure_not_ready",
                "No relic can be claimed yet. Check the treasure state.", true));

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
        if (GetActiveCrystalSphere() is not { } screen)
            return Error("Crystal Sphere screen is not open");

        if (!data.TryGetValue("tool", out var toolElem) || toolElem.ValueKind != JsonValueKind.String)
            return Error("Missing 'tool' (expected 'big' or 'small')");

        string tool = toolElem.GetString() ?? "";
        if (tool is not ("big" or "small"))
            return Error($"Unknown Crystal Sphere tool: {tool}");

        var minigame = GetCrystalSphereMinigame(screen);
        var button = GetCrystalSphereToolButton(screen, tool);
        if (!CanDivineAtCrystalSphere(minigame) || !IsControlVisibleOrActionable(button))
            return Error($"Crystal Sphere tool '{tool}' is not available");

        button!.ForceClick();
        if (GetCrystalSphereTool(minigame) != tool)
            return Error($"Crystal Sphere did not select the '{tool}' tool");

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Selected Crystal Sphere tool '{tool}'",
            ["tool"] = tool
        };
    }

    private static Dictionary<string, object?> ExecuteCrystalSphereClickCell(Dictionary<string, JsonElement> data)
    {
        if (GetActiveCrystalSphere() is not { } screen)
            return Error("Crystal Sphere screen is not open");

        if (!data.TryGetValue("x", out var xElem) || xElem.ValueKind != JsonValueKind.Number ||
            !xElem.TryGetInt32(out int x))
            return Error("Missing or invalid 'x' (cell x-coordinate)");
        if (!data.TryGetValue("y", out var yElem) || yElem.ValueKind != JsonValueKind.Number ||
            !yElem.TryGetInt32(out int y))
            return Error("Missing or invalid 'y' (cell y-coordinate)");

        var minigame = GetCrystalSphereMinigame(screen);
        if (minigame == null)
            return Error("Crystal Sphere is loading");
        if (!CanDivineAtCrystalSphere(minigame))
            return Error("No Crystal Sphere divinations remain");
        var cell = FindAll<NCrystalSphereCell>(screen)
            .FirstOrDefault(c => c.Entity.X == x && c.Entity.Y == y);
        if (cell == null)
            return Error($"Crystal Sphere cell ({x}, {y}) was not found");
        if (!cell.Entity.IsHidden || !IsControlVisibleOrActionable(cell))
            return Error($"Crystal Sphere cell ({x}, {y}) is not clickable");

        int previousCount = minigame.DivinationCount;
        cell.EmitSignal(NClickableControl.SignalName.Released, cell);
        if (minigame.DivinationCount >= previousCount)
            return Error($"Crystal Sphere did not divine cell ({x}, {y})");

        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = $"Divined Crystal Sphere cell ({x}, {y})",
            ["divinations_remaining"] = minigame.DivinationCount
        };
    }

    private static Dictionary<string, object?> ExecuteCrystalSphereProceed()
    {
        if (GetActiveCrystalSphere() is not { } screen)
            return Error("Crystal Sphere screen is not open");

        var proceedButton = FindCrystalSphereProceedButton(screen);
        if (proceedButton == null)
            return Error("Crystal Sphere proceed button is not enabled");

        proceedButton.ForceClick();
        return new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["message"] = "Proceeding from Crystal Sphere"
        };
    }

    private static NCrystalSphereScreen? GetActiveCrystalSphere() =>
        IsMapScreenOpenOrVisible() ? null : NOverlayStack.Instance?.Peek() as NCrystalSphereScreen;

    private static CrystalSphereMinigame? GetCrystalSphereMinigame(NCrystalSphereScreen screen) =>
        GetInstanceFieldValue(screen, "_entity") as CrystalSphereMinigame;

    private static bool CanDivineAtCrystalSphere(CrystalSphereMinigame? minigame) =>
        minigame is { DivinationCount: > 0 };

    private static string GetCrystalSphereTool(CrystalSphereMinigame? minigame) => minigame?.CrystalSphereTool switch
    {
        CrystalSphereMinigame.CrystalSphereToolType.Big => "big",
        CrystalSphereMinigame.CrystalSphereToolType.Small => "small",
        _ => "none"
    };

    private static NClickableControl? GetCrystalSphereToolButton(NCrystalSphereScreen screen, string tool) =>
        screen.GetNodeOrNull<NClickableControl>(tool == "big" ? "%BigDivinationButton" : "%SmallDivinationButton");

    private static NProceedButton? FindCrystalSphereProceedButton(NCrystalSphereScreen screen)
    {
        var namedButton = screen.GetNodeOrNull<NProceedButton>("%ProceedButton");
        if (IsControlVisibleOrActionable(namedButton))
            return namedButton;

        return FindAll<NProceedButton>(screen)
            .FirstOrDefault(IsControlVisibleOrActionable);
    }

    private static Creature? ResolveTarget(ICombatState combatState, string entityId)
    {
        bool useCombatId = uint.TryParse(entityId, out uint combatId);
        foreach (var (creature, id) in GetLivingEnemies(combatState))
        {
            if (useCombatId ? creature.CombatId == combatId : id == entityId)
                return creature;
        }

        return null;
    }

    internal static Dictionary<string, object?> ExecuteMenuSelect(string option, string? seed = null)
    {
        option = option.Trim();

        if (string.IsNullOrEmpty(option))
            return Error("Missing menu option");

        var tree = (Engine.GetMainLoop()) as SceneTree;
        if (tree?.Root == null)
            return Error("Cannot access scene tree");

        // Game over screen. Prefer the active overlay stack entry so hidden or
        // preloaded game-over nodes cannot hijack normal menu navigation.
        var gameOver = NOverlayStack.Instance?.Peek() as NGameOverScreen;
        gameOver ??= FindAll<NGameOverScreen>(tree.Root).FirstOrDefault(IsNodeVisible);
        if (gameOver != null)
        {
            if (string.Equals(option, "continue", System.StringComparison.OrdinalIgnoreCase))
                return Error("Game-over option 'continue' is not actionable. Use: main_menu");

            if (!string.Equals(option, "main_menu", System.StringComparison.OrdinalIgnoreCase))
                return Error($"Unknown game over option: {option}. Use: main_menu");

            var result = ClickMenuButtonField(gameOver, "_mainMenuButton", "Returning to main menu");
            if ((string?)result["status"] == "ok")
                return result;

            var returnMethod = gameOver.GetType().GetMethod(
                "ReturnToMainMenu",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (returnMethod != null)
            {
                returnMethod.Invoke(gameOver, null);
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Returning to main menu" };
            }

            return Error("Game-over main_menu option is not available");
        }

        // Tutorial/FTUE popup - "Enable Tutorials?" dialog
        var tutorialFtue = FindVisibleAcceptTutorialsFtue(tree.Root);
        if (tutorialFtue != null && IsFtueNodeActive(tutorialFtue))
        {
            if (!string.Equals(option, "yes", System.StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(option, "no", System.StringComparison.OrdinalIgnoreCase))
            {
                return Error($"Unknown tutorial prompt option: {option}. Use: yes, no");
            }

            var popup = tutorialFtue.GetType().GetField("_verticalPopup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(tutorialFtue);
            if (popup != null)
            {
                var isYes = string.Equals(option, "yes", System.StringComparison.OrdinalIgnoreCase);
                var btnField = isYes ? "<YesButton>k__BackingField" : "<NoButton>k__BackingField";
                var btn = popup.GetType().GetField(btnField, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(popup);
                if (btn is NClickableControl clickable &&
                    IsPopupButtonActionable(clickable))
                {
                    clickable.ForceClick();
                    return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = $"Tutorials: {(isYes ? "enabled" : "disabled")}" };
                }
            }
            return Error("Tutorial popup visible but buttons not accessible");
        }

        // Any other FTUE/tutorial popup with a confirm button.
        var ftue = FindVisibleGenericFtue(tree.Root);
        if (ftue != null)
        {
            if (!string.Equals(option, "advance", System.StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(option, "proceed", System.StringComparison.OrdinalIgnoreCase))
            {
                return Error($"Unknown tutorial option: {option}. Use: advance, proceed");
            }

            var ftueClickable = FindFtueAdvanceButton(ftue);
            if (ftueClickable != null)
            {
                ftueClickable.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Dismissed tutorial popup" };
            }

            return Error("Tutorial popup visible but no actionable advance/proceed control is available; retry after the next state poll");
        }

        // Generic blocking menu popups, such as the first-run warning that appears
        // over character select on fresh profiles.
        var verticalPopup = FindVisibleVerticalPopup(tree.Root);
        if (verticalPopup != null)
            return ExecutePopupOption(GetPopupOptions(verticalPopup), option);

        var popupButtonOptions = GetVisiblePopupButtonOptions(tree.Root);
        if (popupButtonOptions.Count > 0)
            return ExecutePopupOption(popupButtonOptions, option);

        // Timeline screen - advance through epoch reveals.
        var timelineScreen = FindFirst<NTimelineScreen>(tree.Root);
        if (timelineScreen != null && IsNodeVisible(timelineScreen))
        {
            if (string.Equals(option, "advance", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(option, "proceed", System.StringComparison.OrdinalIgnoreCase))
            {
                // Check for timeline tutorial screen (first-time "Proceed" button).
                var tutorial = FindFirst<NTimelineTutorial>(tree.Root);
                if (tutorial != null && IsNodeVisible(tutorial))
                {
                    var ackBtn = tutorial.GetType().GetField("_acknowledgeButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(tutorial);
                    if (ackBtn is NClickableControl ackClickable &&
                        ackClickable.IsEnabled &&
                        IsNodeVisible(ackClickable))
                    {
                        ackClickable.ForceClick();
                        return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Clicked tutorial proceed button" };
                    }
                }

                // Check for confirm button
                var confirmBtn = FindFirst<NConfirmButton>(tree.Root);
                if (confirmBtn != null && IsNodeVisible(confirmBtn) && confirmBtn.IsEnabled)
                {
                    confirmBtn.ForceClick();
                    return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Clicked confirm button" };
                }

                // Check for any clickable proceed button
                var proceedBtn = FindFirst<NProceedButton>(tree.Root);
                if (proceedBtn != null && IsNodeVisible(proceedBtn) && proceedBtn.IsEnabled)
                {
                    proceedBtn.ForceClick();
                    return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Clicked proceed button" };
                }

                // Check for inspect screen (epoch detail view) - close it.
                var inspectScreen = FindFirst<NEpochInspectScreen>(tree.Root);
                if (inspectScreen != null && IsNodeVisible(inspectScreen))
                {
                    var closeBtn = inspectScreen.GetType().GetField("_closeButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(inspectScreen);
                    if (closeBtn is NClickableControl closeClickable &&
                        closeClickable.IsEnabled &&
                        IsNodeVisible(closeClickable))
                    {
                        closeClickable.ForceClick();
                        return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Closed epoch inspect screen" };
                    }
                    inspectScreen.Close();
                    return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Closed epoch inspect screen" };
                }

                // Check for queued unlock screens.
                var queuedUnlockResult = TryHandleQueuedTimelineUnlock(timelineScreen);
                if (queuedUnlockResult != null)
                    return queuedUnlockResult;

                var unrevealedEpochs = GetProgressEpochIdsByState("Obtained", "ObtainedNoSlot");
                if (unrevealedEpochs.Count > 0)
                    return new Dictionary<string, object?>
                    {
                        ["status"] = "ok",
                        ["message"] = "Epoch unlocks are obtained but not revealed; not forcing timeline reveal from automation",
                        ["pending_epoch_ids"] = unrevealedEpochs,
                        ["manual_action_required"] = true,
                        ["done"] = true
                    };

                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "No more epochs to advance", ["done"] = true };
            }
            else if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
            {
                // Try NBackButton directly on NTimelineScreen
                var backBtn = timelineScreen.GetType().GetField("_backButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(timelineScreen);
                if (backBtn is NClickableControl backClickable)
                {
                    if (backClickable.IsEnabled)
                    {
                        backClickable.ForceClick();
                        return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Going back from timeline" };
                    }
                }
                // Try the submenu stack
                var stack = timelineScreen.GetType().GetField("_stack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(timelineScreen);
                if (stack != null)
                {
                    var popMethod = stack.GetType().GetMethod("Pop");
                    popMethod?.Invoke(stack, null);
                    return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Popped timeline from stack" };
                }
                return Error("Back button not available on timeline");
            }
            return Error($"Unknown timeline option: {option}. Use: advance, back");
        }

        // Multiplayer Join Friend screen — friend list, refresh, back, or join_<index>/<player_id>.
        var joinScreen = FindFirst<NJoinFriendScreen>(tree.Root);
        if (joinScreen != null && IsNodeVisible(joinScreen))
        {
            return ExecuteJoinScreenMenuOption(joinScreen, option);
        }

        // Multiplayer Load Game lobby — confirm/embark/unready/back to resume saved MP run.
        var loadLobby = FindFirst<NMultiplayerLoadGameScreen>(tree.Root);
        if (loadLobby != null && IsNodeVisible(loadLobby))
        {
            return ExecuteLoadLobbyMenuOption(loadLobby, option);
        }

        // Character select can outlive or be mounted separately from NMainMenu,
        // so handle it before main-menu-specific routing.
        var charSelect = FindFirst<NCharacterSelectScreen>(tree.Root);
        if (charSelect != null && IsNodeVisible(charSelect))
        {
            return ExecuteCharacterSelectMenuOption(charSelect, option, seed);
        }

        var profileScreen = FindFirst<NProfileScreen>(tree.Root);
        if (profileScreen != null && IsNodeVisible(profileScreen))
        {
            return ExecuteProfileSelectMenuOption(profileScreen, option);
        }

        Control? detailScreen = FindFirst<NCompendiumSubmenu>(tree.Root);
        if (!IsNodeVisible(detailScreen))
            detailScreen = FindFirst<NSettingsScreen>(tree.Root);
        if (detailScreen != null && IsNodeVisible(detailScreen))
        {
            if (!string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
                return Error($"Unknown screen option: {option}. Use back.");
            if (GetVisibleMenuBackButton(detailScreen) is not { IsEnabled: true } back)
                return Error("Back button is not available yet.");
            back.ForceClick();
            return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Going back to the main menu" };
        }

        // Main menu — click a menu button
        var mainMenu = FindFirst<NMainMenu>(tree.Root);
        if (mainMenu != null)
        {
            // Check if we're on singleplayer submenu
            var spSubmenu = FindFirst<NSingleplayerSubmenu>(tree.Root);
            if (spSubmenu != null && IsNodeVisible(spSubmenu))
            {
                if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
                {
                    return ClickMenuButtonField(spSubmenu, "_backButton", "Going back", "Back button is not available");
                }

                var fieldName = option.ToLowerInvariant() switch
                {
                    "standard" => "_standardButton",
                    "daily" => "_dailyButton",
                    "custom" => "_customButton",
                    _ => null
                };
                if (fieldName == null)
                    return Error($"Unknown singleplayer option: {option}. Use: standard, daily, custom, back");

                return ClickMenuButtonField(spSubmenu, fieldName, $"Selected {option}", $"Option '{option}' is not available (locked)");
            }

            // Check if we're on multiplayer host submenu
            var mpHostSubmenu = FindFirst<NMultiplayerHostSubmenu>(tree.Root);
            if (mpHostSubmenu != null && IsNodeVisible(mpHostSubmenu))
            {
                if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
                {
                    return ClickMenuButtonField(mpHostSubmenu, "_backButton", "Going back", "Back button is not available");
                }

                var fieldName = option.ToLowerInvariant() switch
                {
                    "standard" => "_standardButton",
                    "daily" => "_dailyButton",
                    "custom" => "_customButton",
                    _ => null
                };
                if (fieldName == null)
                    return Error($"Unknown multiplayer host option: {option}. Use: standard, daily, custom, back");

                return ClickMenuButtonField(mpHostSubmenu, fieldName, $"Selected {option}", $"Option '{option}' is not available (locked)");
            }

            // Check if we're on multiplayer submenu
            var mpSubmenu = FindFirst<NMultiplayerSubmenu>(tree.Root);
            if (mpSubmenu != null && IsNodeVisible(mpSubmenu))
            {
                if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
                {
                    return ClickMenuButtonField(mpSubmenu, "_backButton", "Going back", "Back button is not available");
                }

                var fieldName = option.ToLowerInvariant() switch
                {
                    "host" => "_hostButton",
                    "join" => "_joinButton",
                    "load" => "_loadButton",
                    "abandon" => "_abandonButton",
                    _ => null
                };
                if (fieldName == null)
                    return Error($"Unknown multiplayer option: {option}. Use: host, join, load, abandon, back");

                return ClickMenuButtonField(mpSubmenu, fieldName, $"Selected {option}", $"Option '{option}' is not available");
            }

            // Main menu buttons
            var normalizedMainMenuOption = option.ToLowerInvariant();
            if (normalizedMainMenuOption == "timeline")
            {
                var unrevealedEpochs = GetProgressEpochIdsByState("Obtained", "ObtainedNoSlot");
                if (unrevealedEpochs.Count > 0)
                    return TimelineUnlocksNeedManualReveal(unrevealedEpochs);
            }

            var menuFieldName = normalizedMainMenuOption switch
            {
                "singleplayer" => "_singleplayerButton",
                "multiplayer" => "_multiplayerButton",
                "compendium" => "_compendiumButton",
                "timeline" => "_timelineButton",
                "settings" => "_settingsButton",
                "continue" => "_continueButton",
                "abandon_run" => "_abandonRunButton",
                "quit" => "_quitButton",
                _ => null
            };
            if (menuFieldName == null)
                return Error($"Unknown menu option: {option}");

            return ClickMenuButtonField(mainMenu, menuFieldName, $"Selected {option}", $"Option '{option}' is not available");
        }

        return Error("Not on a menu screen");
    }

    private static Dictionary<string, object?> TimelineUnlocksNeedManualReveal(List<string> unrevealedEpochs)
    {
        return new Dictionary<string, object?>
        {
            ["status"] = "error",
            ["error"] = "Timeline has obtained epochs that still need to be revealed manually; not opening Timeline because this game state logs invalid unlock-state errors when entered through automation",
            ["pending_epoch_ids"] = unrevealedEpochs,
            ["manual_action_required"] = true
        };
    }

    private static Dictionary<string, object?> ExecutePopupOption(
        List<(string Name, NClickableControl Button)> options,
        string option)
    {
        foreach (var candidate in options)
        {
            if (!string.Equals(candidate.Name, option, System.StringComparison.OrdinalIgnoreCase))
                continue;

            if (!IsPopupButtonActionable(candidate.Button))
                return Error($"Popup option '{candidate.Name}' is disabled");

            candidate.Button.ForceClick();
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Selected popup option: {candidate.Name}"
            };
        }

        return Error($"Popup option '{option}' is not actionable. Use: {string.Join(", ", options.Select(candidate => candidate.Name))}");
    }

    private static Dictionary<string, object?> ExecuteProfileSelectMenuOption(
        NProfileScreen profileScreen,
        string option)
    {
        if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
        {
            var backBtn = GetInstanceFieldValue(profileScreen, "_backButton")
                ?? FindFirst<NBackButton>(profileScreen);
            if (backBtn is NClickableControl backClickable &&
                backClickable.IsEnabled &&
                IsNodeVisible(backClickable))
            {
                backClickable.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Going back from profile select" };
            }
            return Error("Back button not available on profile select");
        }

        var normalized = option.ToLowerInvariant();
        if (normalized.StartsWith("profile_", System.StringComparison.Ordinal))
            normalized = normalized["profile_".Length..];
        else if (normalized.StartsWith("slot_", System.StringComparison.Ordinal))
            normalized = normalized["slot_".Length..];

        if (int.TryParse(normalized, out var profileId))
            return ExecuteProfileAction("switch", profileId);

        return Error($"Unknown profile select option: {option}. Use: profile_1, profile_2, profile_3, back");
    }

    private static Dictionary<string, object?> ExecuteJoinScreenMenuOption(
        NJoinFriendScreen joinScreen,
        string option)
    {
        var normalized = option.ToLowerInvariant();

        if (normalized == "back")
        {
            return ClickMenuButtonField(joinScreen, "_backButton", "Going back from join screen", "Back button is not available");
        }

        if (normalized == "refresh")
        {
            var refreshBtn = GetInstanceFieldValue(joinScreen, "_refreshButton") as NClickableControl;
            if (refreshBtn != null && refreshBtn.IsEnabled && IsNodeVisible(refreshBtn))
            {
                refreshBtn.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Refreshing friend list" };
            }
            return Error("Refresh button not available");
        }

        // join_<index> or join_<player_id>
        if (normalized.StartsWith("join_", System.StringComparison.Ordinal))
        {
            var key = normalized["join_".Length..];
            var buttonContainer = GetInstanceFieldValue(joinScreen, "_buttonContainer") as Control;
            if (buttonContainer == null)
                return Error("Friend list container not available");

            // Build the in-order friend list once
            var friendButtons = new List<NJoinFriendButton>();
            foreach (var child in buttonContainer.GetChildren())
            {
                if (child is NJoinFriendButton fbtn)
                    friendButtons.Add(fbtn);
            }

            if (friendButtons.Count == 0)
                return Error("No friends with open lobbies. Use 'refresh' to retry.");

            // Try as index first
            if (int.TryParse(key, out int idx))
            {
                if (idx < 0 || idx >= friendButtons.Count)
                    return Error($"Friend index {idx} out of range (0..{friendButtons.Count - 1})");
                if (!friendButtons[idx].IsEnabled)
                    return Error($"Friend at index {idx} is not joinable right now");
                friendButtons[idx].ForceClick();
                return new Dictionary<string, object?>
                {
                    ["status"] = "ok",
                    ["message"] = $"Joining friend at index {idx}"
                };
            }

            // Otherwise treat as ulong player id
            if (ulong.TryParse(key, out ulong playerId))
            {
                var match = friendButtons.FirstOrDefault(b => b.PlayerId == playerId);
                if (match == null)
                    return Error($"No friend with player_id {playerId} in current list. Available indices: 0..{friendButtons.Count - 1}");
                if (!match.IsEnabled)
                    return Error($"Friend {playerId} is not joinable right now");
                match.ForceClick();
                return new Dictionary<string, object?>
                {
                    ["status"] = "ok",
                    ["message"] = $"Joining friend {playerId}"
                };
            }

            return Error($"Unknown join target: '{key}'. Use join_<index> (e.g. join_0) or join_<player_id>.");
        }

        return Error($"Unknown join screen option: {option}. Use: refresh, back, join_<index>, join_<player_id>");
    }

    private static Dictionary<string, object?> ExecuteLoadLobbyMenuOption(
        NMultiplayerLoadGameScreen loadLobby,
        string option)
    {
        var normalized = option.ToLowerInvariant();

        if (normalized == "confirm" || normalized == "embark" || normalized == "ready")
        {
            var confirmBtn = GetInstanceFieldValue(loadLobby, "_confirmButton") as NClickableControl;
            if (confirmBtn != null && confirmBtn.IsEnabled && IsNodeVisible(confirmBtn))
            {
                confirmBtn.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Readied up to load saved MP run" };
            }
            return Error("Confirm button not available — already ready, or lobby not yet initialized");
        }

        if (normalized == "unready")
        {
            var unreadyBtn = GetInstanceFieldValue(loadLobby, "_unreadyButton") as NClickableControl;
            if (unreadyBtn != null && unreadyBtn.IsEnabled && IsNodeVisible(unreadyBtn))
            {
                unreadyBtn.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Retracted ready vote" };
            }
            return Error("Unready button not available — you have not confirmed yet");
        }

        if (normalized == "back")
        {
            return ClickMenuButtonField(loadLobby, "_backButton", "Going back from load lobby", "Back button is not available");
        }

        return Error($"Unknown load lobby option: {option}. Use: confirm, embark, unready, back");
    }

    private static Dictionary<string, object?> ExecuteCharacterSelectMenuOption(
        NCharacterSelectScreen charSelect,
        string option,
        string? seed)
    {
        if (string.Equals(option, "back", System.StringComparison.OrdinalIgnoreCase))
        {
            // _backButton leaves the lobby (and disconnects in MP). _unreadyButton is a
            // separate control that only becomes enabled after the player has hit
            // confirm/embark in MP — it retracts the ready vote without leaving. Surface
            // them as distinct options so callers can pick deliberately. Fall back to
            // unready when only it is actionable so older callers don't get stuck.
            var backBtn = GetInstanceFieldValue(charSelect, "_backButton") as NClickableControl;
            if (backBtn != null && backBtn.IsEnabled && IsControlVisibleOrActionable(backBtn))
            {
                backBtn.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Going back" };
            }
            var unreadyFallback = GetInstanceFieldValue(charSelect, "_unreadyButton") as NClickableControl;
            if (unreadyFallback != null && unreadyFallback.IsEnabled && IsControlVisibleOrActionable(unreadyFallback))
            {
                unreadyFallback.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Retracted ready vote (back fell through to unready)" };
            }
            return Error("Back button not available");
        }

        if (string.Equals(option, "unready", System.StringComparison.OrdinalIgnoreCase))
        {
            var unreadyBtn = GetInstanceFieldValue(charSelect, "_unreadyButton") as NClickableControl;
            if (unreadyBtn != null && unreadyBtn.IsEnabled && IsControlVisibleOrActionable(unreadyBtn))
            {
                unreadyBtn.ForceClick();
                return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = "Retracted ready vote" };
            }
            return Error("Unready button not available — you have not confirmed yet");
        }

        if (string.Equals(option, "confirm", System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(option, "embark", System.StringComparison.OrdinalIgnoreCase))
        {
            if (GetInstanceFieldValue(charSelect, "_embarkButton") is not NClickableControl { IsEnabled: true } embark
                || !IsNodeVisible(embark))
                return Error("Embark button not available — select a character first");

            seed = string.IsNullOrWhiteSpace(seed) ? null : seed.Trim();
            if (seed != null)
            {
                if (charSelect.Lobby == null)
                {
                    return Error("Seeded embark is not supported for standard singleplayer from this API. Seed was not applied and the run was not started.");
                }

                try
                {
                    charSelect.Lobby.SetSeed(seed);
                }
                catch (System.Exception ex)
                {
                    return Error($"Seeded embark failed before starting the run: {ex.Message}");
                }
            }

            embark.ForceClick();
            var message = seed == null ? "Embarking on run" : $"Embarking on run (seed: {seed})";
            return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = message };
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

    private static Dictionary<string, object?>? TryHandleQueuedTimelineUnlock(NTimelineScreen timelineScreen)
    {
        bool isQueued;
        try
        {
            isQueued = timelineScreen.IsScreenQueued();
        }
        catch (System.ObjectDisposedException)
        {
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Timeline changed while checking queued unlocks; retry after the next state poll",
                ["retry"] = true
            };
        }

        if (!isQueued)
            return null;

        var queuedScreen = TryPeekQueuedTimelineScreen(timelineScreen);
        var queuedType = queuedScreen?.GetType().Name ?? "unlock";
        var queuedEpochIds = GetQueuedTimelineEpochIds(queuedScreen);
        var alreadyRevealed = queuedEpochIds
            .Where(id => string.Equals(GetProgressEpochState(id), "Revealed", System.StringComparison.OrdinalIgnoreCase))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (alreadyRevealed.Count > 0)
        {
            var alreadyRevealedSet = new HashSet<string>(alreadyRevealed, System.StringComparer.OrdinalIgnoreCase);
            var pendingEpochIds = queuedEpochIds
                .Where(id => !alreadyRevealedSet.Contains(id))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Queued timeline unlock references already revealed epochs; not opening it to avoid an invalid unlock path",
                ["queued_unlock_type"] = queuedType,
                ["already_revealed_epoch_ids"] = alreadyRevealed,
                ["pending_epoch_ids"] = pendingEpochIds,
                ["manual_action_required"] = pendingEpochIds.Count > 0,
                ["done"] = true
            };
        }

        if (string.Equals(queuedType, "NUnlockTimelineScreen", System.StringComparison.Ordinal) &&
            IsTimelineScreenBusy(timelineScreen))
        {
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Timeline expansion is queued, but the timeline is still animating; retry after the next state poll",
                ["queued_unlock_type"] = queuedType,
                ["pending_epoch_ids"] = queuedEpochIds,
                ["retry"] = true
            };
        }

        try
        {
            timelineScreen.OpenQueuedScreen();
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Opening queued {queuedType}",
                ["queued_unlock_type"] = queuedType,
                ["pending_epoch_ids"] = queuedEpochIds
            };
        }
        catch (System.ObjectDisposedException)
        {
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = "Timeline changed before the queued unlock could open; retry after the next state poll",
                ["queued_unlock_type"] = queuedType,
                ["retry"] = true
            };
        }
        catch (System.Exception ex)
        {
            return new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Skipped queued {queuedType}: {ex.Message}",
                ["queued_unlock_type"] = queuedType,
                ["retry"] = true
            };
        }
    }

    private static object? TryPeekQueuedTimelineScreen(NTimelineScreen timelineScreen)
    {
        try
        {
            var queue = GetInstanceFieldValue(timelineScreen, "_unlockScreens");
            if (queue == null)
                return null;

            var count = queue.GetType().GetProperty("Count")?.GetValue(queue) as int?;
            if (count <= 0)
                return null;

            return queue.GetType().GetMethod("Peek")?.Invoke(queue, null);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsTimelineScreenBusy(NTimelineScreen timelineScreen)
    {
        try
        {
            var isUiVisible = GetInstanceFieldValue(timelineScreen, "_isUiVisible") as bool?;
            if (isUiVisible == false)
                return true;

            var inputBlocker = GetInstanceFieldValue(timelineScreen, "_inputBlocker") as Control;
            if (inputBlocker != null && IsNodeVisible(inputBlocker))
                return true;
        }
        catch (System.ObjectDisposedException)
        {
            return true;
        }

        return false;
    }

    private static List<string> GetQueuedTimelineEpochIds(object? queuedScreen)
    {
        var ids = new List<string>();
        if (queuedScreen == null)
            return ids;

        AddEpochIdsFromObject(GetInstanceFieldValue(queuedScreen, "_epoch"), ids);
        AddEpochIdsFromObject(GetInstanceFieldValue(queuedScreen, "_unlockedEpochs"), ids);
        AddEpochIdsFromObject(GetInstanceFieldValue(queuedScreen, "_erasToUnlock"), ids);

        return ids.Distinct(System.StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddEpochIdsFromObject(object? value, List<string> ids)
    {
        if (value == null)
            return;

        if (value is string id)
        {
            ids.Add(id);
            return;
        }

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
                AddEpochIdsFromObject(item, ids);
            return;
        }

        var model = value.GetType().GetProperty("Model")?.GetValue(value) ?? value;
        var modelId = model.GetType().GetProperty("Id")?.GetValue(model)?.ToString();
        if (!string.IsNullOrEmpty(modelId))
            ids.Add(modelId);
    }

    private static string? GetProgressEpochState(string epochId)
    {
        try
        {
            var progress = MegaCrit.Sts2.Core.Saves.SaveManager.Instance?.Progress;
            return progress?.Epochs
                .FirstOrDefault(epoch => string.Equals(epoch.Id, epochId, System.StringComparison.OrdinalIgnoreCase))
                ?.State
                .ToString();
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GetProgressEpochIdsByState(params string[] states)
    {
        var stateSet = new HashSet<string>(states, System.StringComparer.OrdinalIgnoreCase);
        try
        {
            var progress = MegaCrit.Sts2.Core.Saves.SaveManager.Instance?.Progress;
            if (progress == null)
                return new List<string>();

            return progress.Epochs
                .Where(epoch => stateSet.Contains(epoch.State.ToString()))
                .Select(epoch => epoch.Id)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static NBackButton? GetVisibleMenuBackButton(Control screen) =>
        GetInstanceFieldValue(screen, "_backButton") is NBackButton button && IsNodeVisible(button) ? button : null;

    private static Dictionary<string, object?> ClickMenuButtonField(
        object owner,
        string fieldName,
        string successMessage,
        string? disabledMessage = null)
    {
        var btn = GetInstanceFieldValue(owner, fieldName);
        if (btn is NClickableControl clickable)
        {
            if (!clickable.Visible || !clickable.IsVisibleInTree())
                return Error(disabledMessage ?? $"Option '{fieldName}' is not available");

            if (!clickable.IsEnabled)
                return Error(disabledMessage ?? $"Option '{fieldName}' is not available");

            clickable.ForceClick();
            return new Dictionary<string, object?> { ["status"] = "ok", ["message"] = successMessage };
        }

        return Error($"Could not find button '{fieldName}'");
    }
}
