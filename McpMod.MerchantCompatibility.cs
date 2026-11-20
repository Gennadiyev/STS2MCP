using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
    private sealed record MerchantPurchaseAttempt(int Index, int Price, Task<bool> Completion);
    private static readonly ConditionalWeakTable<MerchantInventory, MerchantPurchaseAttempt> MerchantPurchases = new();

    // The beta moved inventory lookup and purchases from the room and entry
    // to the synchronizer. AllEntries also changed its CLR return signature.
    private static readonly PropertyInfo? MerchantSynchronizerProperty =
        typeof(RunManager).GetProperty("MerchantSynchronizer");
    private static readonly MethodInfo? SynchronizedInventoryGetter =
        MerchantSynchronizerProperty?.PropertyType.GetMethod("GetLocalInventory", Type.EmptyTypes);
    private static readonly MethodInfo? SynchronizedPurchaseMethod =
        MerchantSynchronizerProperty?.PropertyType.GetMethod("PurchaseLocalEntry", new[] { typeof(MerchantEntry) });
    private static readonly bool UseSynchronizedMerchant =
        SynchronizedInventoryGetter != null && SynchronizedPurchaseMethod != null;
    private static readonly MethodInfo? LegacyInventoryGetter =
        typeof(MerchantRoom).GetMethod("GetLocalInventory", Type.EmptyTypes);
    private static readonly MethodInfo? LegacyPurchaseMethod =
        typeof(MerchantEntry).GetMethod("OnTryPurchaseWrapper", new[] { typeof(MerchantInventory), typeof(bool) });
    private static readonly PropertyInfo? MerchantEntriesProperty =
        typeof(MerchantInventory).GetProperty("AllEntries");

    private static MerchantInventory? GetLocalMerchantInventory(MerchantRoom merchantRoom)
    {
        if (UseSynchronizedMerchant)
            return GetSynchronizedMerchantInventory(MerchantSynchronizerProperty!.GetValue(RunManager.Instance));

        var getter = LegacyInventoryGetter
            ?? throw new MissingMethodException(typeof(MerchantRoom).FullName, "GetLocalInventory");
        return getter.CreateDelegate<Func<MerchantInventory?>>(merchantRoom)();
    }

    private static MerchantInventory? GetLocalMerchantInventory(FakeMerchant fakeMerchant)
    {
        var eventInventory = fakeMerchant.Inventory;
        if (!UseSynchronizedMerchant || eventInventory == null)
            return eventInventory;

        var localInventory = GetSynchronizedMerchantInventory(MerchantSynchronizerProperty!.GetValue(RunManager.Instance));
        return ReferenceEquals(eventInventory, localInventory) ? localInventory : null;
    }

    private static MerchantInventory? GetSynchronizedMerchantInventory(object? synchronizer)
    {
        if (synchronizer == null)
            return null;

        var getter = SynchronizedInventoryGetter
            ?? throw new MissingMethodException(synchronizer.GetType().FullName, "GetLocalInventory");
        try
        {
            return getter.CreateDelegate<Func<MerchantInventory?>>(synchronizer)();
        }
        catch (KeyNotFoundException)
        {
            // Registration can lag the room transition.
            return null;
        }
    }

    private static IReadOnlyList<MerchantEntry> GetMerchantEntries(MerchantInventory inventory)
    {
        var getter = MerchantEntriesProperty
            ?? throw new MissingMemberException(typeof(MerchantInventory).FullName, "AllEntries");
        var value = getter.GetValue(inventory);
        return value switch
        {
            IReadOnlyList<MerchantEntry> list => list,
            IEnumerable<MerchantEntry> entries => entries.ToList(),
            _ => throw new InvalidOperationException("Merchant inventory does not expose an entry collection.")
        };
    }

    private static Task<bool>? StartMerchantPurchase(MerchantInventory inventory, MerchantEntry entry)
    {
        if (UseSynchronizedMerchant)
        {
            var synchronizer = MerchantSynchronizerProperty!.GetValue(RunManager.Instance);
            if (synchronizer == null
                || !ReferenceEquals(GetSynchronizedMerchantInventory(synchronizer), inventory)
                || !GetMerchantEntries(inventory).Any(candidate => ReferenceEquals(candidate, entry)))
                return null;

            var purchase = SynchronizedPurchaseMethod
                ?? throw new MissingMethodException(synchronizer.GetType().FullName, "PurchaseLocalEntry");
            return purchase.CreateDelegate<Func<MerchantEntry, Task<bool>>>(synchronizer)(entry);
        }

        var legacyPurchase = LegacyPurchaseMethod
            ?? throw new MissingMethodException(typeof(MerchantEntry).FullName, "OnTryPurchaseWrapper");
        return legacyPurchase.CreateDelegate<Func<MerchantInventory, bool, Task<bool>>>(entry)(inventory, false);
    }

    private static MerchantPurchaseAttempt? GetLastMerchantPurchase(MerchantInventory inventory) =>
        MerchantPurchases.TryGetValue(inventory, out var purchase) ? purchase : null;

    private static MerchantPurchaseAttempt RecordMerchantPurchase(
        MerchantInventory inventory, int index, int price, Task<bool> completion)
    {
        var purchase = new MerchantPurchaseAttempt(index, price, completion);
        MerchantPurchases.Remove(inventory);
        MerchantPurchases.Add(inventory, purchase);
        TaskHelper.RunSafely(completion);
        return purchase;
    }

    private static Dictionary<string, object?> BuildMerchantPurchaseResult(MerchantPurchaseAttempt purchase)
    {
        var result = purchase.Completion.Status switch
        {
            TaskStatus.RanToCompletion when purchase.Completion.Result => new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["message"] = $"Purchased item for {purchase.Price} gold."
            },
            TaskStatus.RanToCompletion or TaskStatus.Canceled =>
                BlockedAction(new("purchase_rejected", "The game rejected or cancelled the purchase.", false)),
            TaskStatus.Faulted =>
                BlockedAction(new("purchase_failed", "The shop purchase failed in the game.", false)),
            _ => new Dictionary<string, object?>
            {
                ["status"] = "pending",
                ["message"] = "Purchase is underway. Resolve any open selection and check shop state for the result."
            }
        };

        result["item_index"] = purchase.Index;
        result["price"] = purchase.Price;
        return result;
    }
}
