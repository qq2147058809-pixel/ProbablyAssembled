using Il2Cpp;

namespace PCExpansion;

/// <summary>Only explicit world/business events start this bounded main-thread recovery window.</summary>
internal static class ServerBusiness
{
    internal static void Wake(PlayerStore store)
    {
        ServerRoomDeliverySchedule.RequestCheck();
        var owner = store.Pointer; var run = store.runID; var slot = store.saveSlotId;
        BusinessWork.Schedule("server-business", () =>
        {
            if (!PlayerStore.IsInstanceExist() || PlayerStore.instance == null ||
                PlayerStore.instance.Pointer != owner || store.runID != run || store.saveSlotId != slot) return false;
            ServerRoomRuntime.RetryRecovery();
            ServerRoomRuntime.Update(store);
            if (ServerRoomRuntime.Error != null && !ServerRoomRuntime.Pending) return false;
            ServerRoomDaily.Tick();
            ServerRoomOrders.Update(store);
            ServerRoomOrderStock.Update(store);
            ServerRoomDeliverySchedule.Update(store);
            // No idle polling. A visit/unknown receipt gets at most 120 attempts;
            // arrival, departure, purchase, save or scene events can wake it again.
            return ServerRoomRuntime.NeedsWork || ServerRoomDaily.NeedsWork || ServerRoomOrders.NeedsWork ||
                ServerRoomOrderStock.NeedsWork || ServerRoomDeliverySchedule.NeedsWork;
        }, delayMs: 50, intervalMs: 500, maxAttempts: 120);
    }
}
