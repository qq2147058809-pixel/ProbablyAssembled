using System;
using System.Globalization;
using System.Linq;
using Il2Cpp;

namespace PCExpansion;

/// <summary>开店后把到期预订作为独立洛夕来访安排在当日队首。</summary>
internal static class ServerRoomDeliverySchedule
{
    private sealed class QueueTicket
    {
        internal PlayerStore Owner = null!;
        internal StoreClient Visitor = null!;
        internal string Run = "", Identity = "";
        internal int Slot, Day;
    }
    private static IntPtr openedOwner;
    private static string? openedRun;
    private static int openedSlot, openedDay = -1;
    private static long retryAt, warnAt;
    private static QueueTicket? queueTicket;
    private static bool retryNeeded, requested;
    internal static bool NeedsWork => queueTicket != null || retryNeeded;
    internal static void RequestCheck() { requested = true; retryAt = 0; }

    internal static void OnShutterOpened(PlayerStore store)
    {
        openedOwner = store.Pointer;
        openedRun = store.runID;
        openedSlot = store.saveSlotId;
        openedDay = StoreStation.GetDayCounter();
        // This verified postfix runs after native daily queue construction and
        // before the next client can be taken. Preserve its immediate priority.
        RequestCheck();
        ServerRoomRuntime.Update(store);
        ServerRoomOrders.Update(store);
        Update(store);
        ServerBusiness.Wake(store);
    }

    internal static void Update(PlayerStore store)
    {
        if (!requested && !NeedsWork || Environment.TickCount64 < retryAt) return;
        retryAt = Environment.TickCount64 + 1000;
        retryNeeded = requested = false;
        if (queueTicket != null)
        {
            try { ResolveQueue(); }
            catch (Exception ex) { Warn(ex); }
            if (queueTicket != null) { retryNeeded = true; return; }
        }
        // Queue changes during load also restore priority without pretending the shop opened.
        PrioritizeQueued(store);
        if (store.Pointer != openedOwner || store.runID != openedRun || store.saveSlotId != openedSlot ||
            StoreStation.GetDayCounter() != openedDay) return;
        try { retryNeeded = Arrange(store, openedDay); }
        catch (Exception ex)
        {
            retryNeeded = true;
            Warn(ex);
        }
    }

    internal static bool BeforeSave()
    {
        try { ResolveQueue(); }
        catch (Exception ex) { Warn(ex); }
        if (queueTicket == null) return true;
        ServerBusiness.Wake(queueTicket.Owner);
        return false;
    }

    private static void Warn(Exception ex)
    {
        if (Environment.TickCount64 < warnAt) return;
        warnAt = Environment.TickCount64 + 5000;
        Core.Log?.Warning("洛夕次日送货排队暂未完成：" + ex);
    }

    internal static void ResetSession()
    {
        if (queueTicket != null) throw new InvalidOperationException("送货入队结果未确认，不能清除凭据。");
        openedOwner = IntPtr.Zero; openedRun = null; openedSlot = 0; openedDay = -1;
        retryAt = warnAt = 0; retryNeeded = requested = false;
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
            JiangbaiStory.Prioritize(store);
            return;
        }
    }

    private static bool Arrange(PlayerStore store, int day)
    {
        var manager = store.storeClientManager;
        var stack = manager?.clientStack;
        if (manager == null || stack == null) return true;
        if (!ServerRoomOrders.Ready) return ServerRoomOrders.Error == null;
        if (ServerRoomRuntime.Current?.Enabled != true) return false;

        // An already queued delivery carries its immutable order snapshot.
        // Reassert its queue position after native scheduling or a read/load.
        for (var i = 0; i < stack.Count; i++)
        {
            var queued = stack[i];
            if (!ServerRoomOrderStock.IsDelivery(queued)) continue;
            if (i != 0) { stack.RemoveAt(i); stack.Insert(0, queued); }
            ServerRoomOrderStock.ConfirmQueued(queued);
            JiangbaiStory.Prioritize(store);
            return false;
        }
        if (ServerRoomOrderStock.IsDelivery(store.currentClientInstance?.storeClient)) return false;

        var current = ServerRoomOrders.Current;
        if (current == null || current.LastVisitDay == day || current.Orders.Any(order => order.VisitId != "")) return false;
        var due = ServerRoomOrders.List().Where(order => order.VisitId == "" && order.DueDay <= day)
            .Select(order => order.Id).ToArray();
        if (due.Length == 0) return false;

        var owner = store.Pointer; var run = store.runID; var slot = store.saveSlotId;
        var visitor = PhoneAssemblerNpc.CreateClient();
        ServerRoomOrderStock.PrepareVisitor(visitor, day, due);
        // Retain proof before the native call. A thrown/unknown result must never create a second visitor.
        queueTicket = new QueueTicket { Owner = store, Run = run, Slot = slot, Day = day, Visitor = visitor,
            Identity = ServerRoomOrderStock.QueueIdentity(visitor) };
        manager.AddNextClient(visitor);
        if (!PlayerStore.IsInstanceExist() || PlayerStore.instance == null || PlayerStore.instance.Pointer != owner ||
            PlayerStore.instance.runID != run || PlayerStore.instance.saveSlotId != slot ||
            StoreStation.GetDayCounter() != day)
            throw new InvalidOperationException("送货入队期间存档或日期变化。");
        for (var i = 0; i < stack.Count; i++)
        {
            if (stack[i] == null || stack[i].Pointer != visitor.Pointer) continue;
            if (i != 0) { stack.RemoveAt(i); stack.Insert(0, visitor); }
            ResolveQueue();
            Core.Debug("[深空装机] 洛夕送货已排在第 " + day.ToString(CultureInfo.InvariantCulture) +
                " 天队首，订单数=" + due.Length.ToString(CultureInfo.InvariantCulture));
            return queueTicket != null;
        }
        ResolveQueue();
        if (queueTicket == null) return false;
        Core.Log?.Warning("洛夕送货入队未确认，保留订单并稍后重试。");
        return true;
    }

    private static void ResolveQueue()
    {
        var proof = queueTicket;
        if (proof == null) return;
        if (!PlayerStore.IsInstanceExist() || PlayerStore.instance == null ||
            PlayerStore.instance.Pointer != proof.Owner.Pointer || proof.Owner.runID != proof.Run ||
            proof.Owner.saveSlotId != proof.Slot || !SaveGeneration.IsSupported(proof.Owner) ||
            StoreStation.GetDayCounter() < proof.Day) return;
        var live = proof.Owner.currentClientInstance?.storeClient;
        var found = live != null && live.Pointer == proof.Visitor.Pointer;
        var stack = proof.Owner.storeClientManager?.clientStack;
        if (stack != null)
            for (var index = 0; index < stack.Count; index++)
                if (stack[index] != null && stack[index].Pointer == proof.Visitor.Pointer) found = true;
        if (!found || !ServerRoomOrders.Ready) return;
        if (ServerRoomOrderStock.QueueIdentity(proof.Visitor) != proof.Identity)
            throw new InvalidOperationException("送货入队回读的批次、日期或订单已变化，保留原凭据。");
        ServerRoomOrderStock.ConfirmQueued(proof.Visitor);
        PrioritizeQueued(proof.Owner);
        queueTicket = null;
    }
}
