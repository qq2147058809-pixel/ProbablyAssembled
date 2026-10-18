using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Coalesces native lifecycle and successful state-publication notifications.</summary>
internal static class BusinessRuntime
{
    private static IntPtr observedStore, observedScene;
    private static string? observedRun, observedLocale;
    private static int observedSlot;

    internal static void Wake() => BusinessWork.Schedule("world-bind", Dispatch);

    // UI cleanup resets its own identity while leaving a valid game session
    // alive. Keep this observation separate so a hidden/unready UI cannot turn
    // its repeated reconstruction attempts into business polling.
    internal static void ObserveSession(PlayerStore store, IntPtr scene, string locale)
    {
        if (observedStore == store.Pointer && observedScene == scene && observedRun == store.runID &&
            observedSlot == store.saveSlotId && observedLocale == locale) return;
        observedStore = store.Pointer; observedScene = scene; observedRun = store.runID;
        observedSlot = store.saveSlotId; observedLocale = locale;
        Wake();
    }

    private static bool Dispatch()
    {
        var store = WorkroomGameAdapter.ExistingStore();
        if (store == null) return true; // A load/scene event may precede the inventory scene.
        if (!SaveGeneration.IsSupported(store)) return false;
        WorkroomStorage.Wake(store);
        WorkroomCashRepair.WakeRecovery();
        WorkroomStoryTransfer.WakeRecovery();
        WorkroomItemCodec.WakeCleanup();
        NpcBusiness.Wake(store);
        ServerBusiness.Wake(store);
        return false;
    }

    // Called only once load admission has succeeded; UI resets and language
    // changes must not silently discard recovery jobs for the current save.
    internal static void ChangingSession()
    {
        NpcIntroduction.Invalidate();
        BusinessWork.CancelAll();
        observedStore = observedScene = IntPtr.Zero;
        observedRun = observedLocale = null; observedSlot = 0;
    }
}

[HarmonyPatch]
internal static class BusinessWorldEventsPatch
{
    private struct Observation
    {
        internal IntPtr Store, Client, Head;
        internal bool Arrived;
        internal int Queued;
        internal bool Same(Observation other) => Store == other.Store && Client == other.Client &&
            Head == other.Head && Arrived == other.Arrived && Queued == other.Queued;
    }

    private static Observation Observe()
    {
        var store = PlayerStore.IsInstanceExist() ? PlayerStore.instance : null;
        var queue = store?.storeClientManager?.clientStack;
        return new Observation { Store = store?.Pointer ?? IntPtr.Zero,
            Client = store?.currentClientInstance?.Pointer ?? IntPtr.Zero,
            Arrived = store?.isClientArrived == true, Queued = queue?.Count ?? 0,
            Head = queue != null && queue.Count != 0 ? queue[0]?.Pointer ?? IntPtr.Zero : IntPtr.Zero };
    }

    private static void Prefix(MethodBase __originalMethod, out Observation? __state)
    {
        __state = null;
        // TryCallNextClient can be an idle native poll. Its no-op calls (and
        // repeated UI hide/leave calls) must not restart every business module.
        if (__originalMethod.Name != "TryCallNextClient" && __originalMethod.Name != "GetNextClient" &&
            __originalMethod.Name != "HideClient" && __originalMethod.Name != "OnClientStartLeaving" &&
            __originalMethod.Name != "QueueClientLeaveAnimation") return;
        try
        {
            if (__originalMethod.Name == "GetNextClient")
            {
                var store = WorkroomGameAdapter.ExistingStore();
                if (store?.storeClientManager?.clientStack?.Count > 1)
                {
                    // Ordering must be fixed before the native dequeue, not
                    // by a delayed callback after a different visitor arrives.
                    ServerRoomDeliverySchedule.PrioritizeQueued(store);
                    JiangbaiStory.Prioritize(store);
                }
            }
            __state = Observe();
        }
        catch (Exception ex) { Core.Debug("来客事件快照暂未就绪：" + ex.Message); }
    }

    private static IEnumerable<MethodBase> TargetMethods()
    {
        // Signatures verified in the installed Assembly-CSharp interop assembly.
        // These callbacks wake a later observation; leaving-start is not proof
        // that the visitor is gone, and SaveGame completion is not inferred here.
        foreach (var name in new[] { "BeginDay", "OnShutterOpened", "GetNextClient", "TryCallNextClient",
            "OnItemBought", "OnItemsSold" })
            yield return AccessTools.DeclaredMethod(typeof(PlayerStore), name) ?? throw new MissingMethodException(name);
        foreach (var name in new[] { "OnArrived", "SetImmediatlyArrive", "StartLeavingStore", "StartLeavingStoreRight" })
            yield return AccessTools.DeclaredMethod(typeof(StoreClientMono), name) ?? throw new MissingMethodException(name);
        yield return AccessTools.DeclaredMethod(typeof(StoreClientManager), "AddNextClient") ?? throw new MissingMethodException("AddNextClient");
        foreach (var name in new[] { "HideClient", "OnClientStartLeaving", "QueueClientLeaveAnimation" })
            yield return AccessTools.DeclaredMethod(typeof(StoreUIManager), name) ?? throw new MissingMethodException(name);
    }

    private static Exception? Finalizer(Exception? __exception, Observation? __state)
    {
        // A native operation can change state before throwing. The normal
        // module validators decide what actually committed on the next wake.
        if (__state.HasValue)
            try { if (__state.Value.Same(Observe())) return __exception; }
            catch { /* Changed/unknown native ownership needs a later observation. */ }
        BusinessRuntime.Wake();
        return __exception;
    }
}
