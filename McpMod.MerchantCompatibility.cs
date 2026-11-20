using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace STS2_MCP;

public static partial class McpMod
{
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

    private static bool TryStartMerchantPurchase(MerchantInventory inventory, MerchantEntry entry)
    {
        if (UseSynchronizedMerchant)
        {
            var synchronizer = MerchantSynchronizerProperty!.GetValue(RunManager.Instance);
            if (synchronizer == null
                || !ReferenceEquals(GetSynchronizedMerchantInventory(synchronizer), inventory)
                || !GetMerchantEntries(inventory).Any(candidate => ReferenceEquals(candidate, entry)))
                return false;

            var purchase = SynchronizedPurchaseMethod
                ?? throw new MissingMethodException(synchronizer.GetType().FullName, "PurchaseLocalEntry");
            TaskHelper.RunSafely(purchase.CreateDelegate<Func<MerchantEntry, Task<bool>>>(synchronizer)(entry));
            return true;
        }

        var legacyPurchase = LegacyPurchaseMethod
            ?? throw new MissingMethodException(typeof(MerchantEntry).FullName, "OnTryPurchaseWrapper");
        TaskHelper.RunSafely(
            legacyPurchase.CreateDelegate<Func<MerchantInventory, bool, Task<bool>>>(entry)(inventory, false));
        return true;
    }
}
