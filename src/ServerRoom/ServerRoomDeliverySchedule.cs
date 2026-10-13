using System;
using System.Globalization;
using System.Linq;
using Il2Cpp;

namespace PCExpansion;

/// <summary>开店后把到期预订作为独立洛夕来访安排在当日队首。</summary>
internal static class ServerRoomDeliverySchedule
{
    private static IntPtr openedOwner;
    private static string? openedRun;
    private static int openedSlot, openedDay = -1;
    private static long retryAt, warnAt;

    internal static void OnShutterOpened(PlayerStore store)
    {
        openedOwner = store.Pointer;
        openedRun = store.runID;
        openedSlot = store.saveSlotId;
        openedDay = StoreStation.GetDayCounter();
        retryAt = 0;
        Update(store);
    }

    internal static void Update(PlayerStore store)
    {
        if (store.Pointer != openedOwner || store.runID != openedRun || store.saveSlotId != openedSlot ||
            StoreStation.GetDayCounter() != openedDay || Environment.TickCount64 < retryAt) return;
        retryAt = Environment.TickCount64 + 1000;
        try { Arrange(store, openedDay); }
        catch (Exception ex)
        {
            if (Environment.TickCount64 < warnAt) return;
            warnAt = Environment.TickCount64 + 5000;
            Core.Log?.Warning("洛夕次日送货排队暂未完成：" + ex);
        }
    }

    internal static void ResetSession()
    {
        openedOwner = IntPtr.Zero; openedRun = null; openedSlot = 0; openedDay = -1;
        retryAt = warnAt = 0;
    }

    internal static void PrioritizeQueued(PlayerStore store)
    {
        var stack = store.storeClientManager?.clientStack;
        if (stack == null) return;
        for (var i = 0; i < stack.Count; i++)
        {
            var queued = stack[i];
            if (!ServerRoomOrderStock.IsDelivery(queued)) continue;
            if (i != 0) { stack.RemoveAt(i); stack.Insert(0, queued); }
            return;
        }
    }

    private static void Arrange(PlayerStore store, int day)
    {
        var manager = store.storeClientManager;
        var stack = manager?.clientStack;
        if (manager == null || stack == null) return;
        ServerRoomRuntime.Update(store);
        ServerRoomOrders.Update(store);
        if (!ServerRoomOrders.Ready || ServerRoomRuntime.Current?.Enabled != true) return;

        // An already queued delivery carries its immutable order snapshot.
        // Reassert its queue position after native scheduling or a read/load.
        for (var i = 0; i < stack.Count; i++)
        {
            var queued = stack[i];
            if (!ServerRoomOrderStock.IsDelivery(queued)) continue;
            if (i != 0) { stack.RemoveAt(i); stack.Insert(0, queued); }
            ServerRoomOrderStock.ConfirmQueued(queued);
            return;
        }
        if (store.isClientArrived && ServerRoomOrderStock.IsDelivery(store.currentClientInstance?.storeClient))
            return;

        var current = ServerRoomOrders.Current;
        if (current == null || current.LastVisitDay == day || current.Orders.Any(order => order.VisitId != "")) return;
        var due = ServerRoomOrders.List().Where(order => order.VisitId == "" && order.DueDay <= day)
            .Select(order => order.Id).ToArray();
        if (due.Length == 0) return;

        var owner = store.Pointer; var run = store.runID; var slot = store.saveSlotId;
        var visitor = PhoneAssemblerNpc.CreateClient();
        ServerRoomOrderStock.PrepareVisitor(visitor, day, due);
        manager.AddNextClient(visitor);
        if (!PlayerStore.IsInstanceExist() || PlayerStore.instance == null || PlayerStore.instance.Pointer != owner ||
            PlayerStore.instance.runID != run || PlayerStore.instance.saveSlotId != slot ||
            StoreStation.GetDayCounter() != day)
            throw new InvalidOperationException("送货入队期间存档或日期变化。");
        for (var i = 0; i < stack.Count; i++)
        {
            if (stack[i] == null || stack[i].Pointer != visitor.Pointer) continue;
            if (i != 0) { stack.RemoveAt(i); stack.Insert(0, visitor); }
            ServerRoomOrderStock.ConfirmQueued(visitor);
            Core.Log?.Msg("[深空装机] 洛夕送货已排在第 " + day.ToString(CultureInfo.InvariantCulture) +
                " 天队首，订单数=" + due.Length.ToString(CultureInfo.InvariantCulture));
            return;
        }
        Core.Log?.Warning("洛夕送货入队未确认，保留订单并稍后重试。");
    }
}
