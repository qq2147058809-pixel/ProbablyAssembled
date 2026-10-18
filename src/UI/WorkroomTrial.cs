#nullable disable
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PCExpansion;

/// <summary>店内钥匙入口与工作间生命周期；等待原版点击释放，再取得输入。</summary>
internal static class WorkroomTrial
{
    private static readonly WorkroomTransition flow = new();
    private static WorkroomView view;
    private static ServerRoomView serverView;
    private static IntPtr storePointer, scenePointer;
    private static string run, locale;
    private static int slot;
    private static long retryAt, warnAt, cleanupRetryAt;
    private static bool cleanupPending;
    private static bool entryRequested;
    private static long requestUntil;
    private static int requestNeutralFrames;
    private static bool regionRequested;
    private static WorkroomTransition.Region regionTarget;
    private static long regionRequestUntil;
    private static int regionNeutralFrames;
    private static bool storageRequested, storageRequestInRoom;
    private static long storageRequestUntil, storageRequestEpoch;
    private static int storageNeutralFrames, storageRequestFrame;
    private static bool RegionInputHeld => flow.BlocksInput || WorkroomGameAdapter.LeaseHeld || cleanupPending;
    internal static bool IsolatesStoreRegion => RegionInputHeld;
    internal static bool CustomRegionVisible => flow.RegionVisible;
    internal static bool ServerCommandsAllowed => !RoomAudioSettingsUi.BlocksInput && !cleanupPending && !regionRequested &&
        flow.IsStable && flow.CurrentRegion == WorkroomTransition.Region.ServerRoom &&
        WorkroomGameAdapter.AtStore() && WorkroomGameAdapter.HooksReady &&
        Time.timeScale > 0 && !Il2Cpp.Interactable.isPaused &&
        !WorkroomGameAdapter.PendingGesture() && !WorkroomStorage.Busy &&
        !WorkroomItemCodec.CleanupPending;
    internal static bool RoomSelectionActive => !cleanupPending && (view?.HasRoomSelection ?? false);
    internal static bool OwnsTextInput => !cleanupPending && ((view?.OwnsTextInput ?? false) ||
        (serverView?.OwnsTextInput ?? false));
    internal static bool BlocksNativeInput => WorkroomStoryTransfer.Pending || RoomAudioSettingsUi.BlocksInput || RegionInputHeld || (view?.BlocksPanelInput ?? false) ||
        (serverView?.BlocksPanelInput ?? false);
    internal static bool BlocksSharedInput => WorkroomStoryTransfer.Pending || RoomAudioSettingsUi.BlocksInput || cleanupPending ||
        (RegionInputHeld && (!flow.IsStable ||
            flow.CurrentRegion == WorkroomTransition.Region.ServerRoom)) ||
        (view?.BlocksPanelInput ?? false) ||
        (serverView?.BlocksPanelInput ?? false);
    internal static bool OwnsPanelMouse => RoomAudioSettingsUi.BlocksInput || !cleanupPending &&
        (WorkroomStoreKey.OwnsMousePress || (view?.OwnsMousePress ?? false));
    internal static bool OwnsPanelScroll => RoomAudioSettingsUi.BlocksInput || !cleanupPending && (view?.OwnsScroll ?? false);
    internal static bool OwnsPanelEscape => RoomAudioSettingsUi.BlocksInput || !cleanupPending && (view?.OwnsEscape ?? false);
    internal static bool ShopEntryVisible => view != null && !cleanupPending &&
        flow.IsStable && flow.CurrentRegion == WorkroomTransition.Region.Shop && WorkroomGameAdapter.AtStore();
    internal static bool CanRequestEntry => ShopEntryVisible && !entryRequested && !BlocksNativeInput &&
        !WorkroomStoreKey.CleanupPending &&
        !view.IsDragging && WorkroomGameAdapter.HooksReady && Time.timeScale > 0 &&
        !Il2Cpp.Interactable.isPaused && !WorkroomGameAdapter.PendingGesture();
    internal static bool CanCollectCurrent(bool inRoom) => !RoomAudioSettingsUi.BlocksInput && view != null && !cleanupPending && !entryRequested &&
        !regionRequested && flow.IsStable &&
        flow.CurrentRegion == (inRoom ? WorkroomTransition.Region.Workroom : WorkroomTransition.Region.Shop) &&
        !view.BlocksPanelInput && !view.IsDragging &&
        WorkroomGameAdapter.HooksReady && WorkroomGameAdapter.AtStore() && Time.timeScale > 0 &&
        !WorkroomGameAdapter.PendingGesture(allowStableSelection: true);
    internal static bool HitShopStorage(Vector2 point) => view != null &&
        flow.IsStable && flow.CurrentRegion == WorkroomTransition.Region.Shop && !BlocksNativeInput &&
        Time.timeScale > 0 && WorkroomGameAdapter.AtStore() && view.HitShopStorageDrop(point);
    internal static bool HitShopStorageWindow(Vector2 point) => view != null && !cleanupPending &&
        flow.IsStable && flow.CurrentRegion == WorkroomTransition.Region.Shop &&
        WorkroomGameAdapter.AtStore() && view.HitStorageWindow(point);
    internal static bool TryNotifyOperation(string message)
    {
        if (cleanupPending || view == null || !flow.IsStable || !WorkroomGameAdapter.AtStore() || !view.Alive) return false;
        view.NotifyOperation(message);
        return true;
    }

    internal static void NotifyStorage()
    {
        if (cleanupPending) return;
        // A UI notification must not interrupt native EndDrag's restoration
        // after a refusal, or change the already completed storage transaction.
        try { view?.NotifyStorage(); }
        catch (Exception ex) { Warn(ex, "storage-notice"); }
    }

    internal static void RequestPanel(WorkroomPanels.Kind kind, bool inRoom)
    {
        try
        {
            if (RoomAudioSettingsUi.BlocksInput || view == null || cleanupPending || entryRequested || regionRequested || !flow.IsStable ||
                view.BlocksPanelInput || view.IsDragging || WorkroomStorageDragPatch.ConsumedFrame == Time.frameCount ||
                !WorkroomGameAdapter.HooksReady || Time.timeScale <= 0 || !WorkroomGameAdapter.AtStore()) return;
            var expected = inRoom ? WorkroomTransition.Region.Workroom : WorkroomTransition.Region.Shop;
            if (flow.CurrentRegion != expected) { view.Notify("busy"); return; }
            if (kind == WorkroomPanels.Kind.Storage)
            {
                // Unity's click can precede the native release dispatch. Keep
                // that dispatch running and open only after both have settled.
                storageRequested = true;
                storageRequestInRoom = inRoom;
                storageRequestEpoch = WorkroomStorage.State.Epoch;
                storageRequestFrame = Time.frameCount;
                storageRequestUntil = Environment.TickCount64 + 3000;
                storageNeutralFrames = 0;
                Core.Debug("[工作间开箱] queued; room=" + inRoom + "; " + WorkroomGameAdapter.GestureState());
                return;
            }
            if (WorkroomGameAdapter.PendingGesture(allowStableSelection: inRoom))
            { view.Notify("busy"); return; }
            storageRequested = false;
            view.OpenPanel(kind, inRoom);
            WorkroomGameAdapter.SetSharedInputBlocked(BlocksSharedInput);
        }
        catch (Exception ex)
        {
            try { Reset(); } catch (Exception cleanup) { Warn(cleanup); }
            Warn(ex);
        }
    }

    internal static void RequestEntry()
    {
        if (BlocksNativeInput || entryRequested) return;
        try
        {
            var store = WorkroomGameAdapter.ExistingStore();
            if (view == null || store == null || store.Pointer != storePointer ||
                store.runID != run || store.saveSlotId != slot || !CanRequestEntry)
            { view?.Notify("unavailable"); return; }
            // Keep native input running until its click/drag release has finished.
            storageRequested = false;
            entryRequested = true;
            requestUntil = Environment.TickCount64 + 3000;
            requestNeutralFrames = 0;
        }
        catch (Exception ex) { entryRequested = false; Warn(ex); }
    }

    private static void ProcessEntryRequest()
    {
        if (!entryRequested) return;
        if (!ShopEntryVisible || BlocksNativeInput || WorkroomStoreKey.CleanupPending || !WorkroomGameAdapter.HooksReady ||
            Time.timeScale <= 0 || Il2Cpp.Interactable.isPaused)
        { entryRequested = false; view?.Notify("unavailable"); return; }
        if (Environment.TickCount64 >= requestUntil)
        { entryRequested = false; view.Notify("busy"); return; }
        var neutral = WorkroomGameAdapter.InputNeutral() && !WorkroomGameAdapter.PendingGesture();
        requestNeutralFrames = neutral ? requestNeutralFrames + 1 : 0;
        if (requestNeutralFrames < 2) return;
        entryRequested = false;
        Enter();
    }

    private static bool RegionTransitionReady()
    {
        return !RoomAudioSettingsUi.BlocksInput && view != null && !cleanupPending && !entryRequested && !storageRequested &&
            !WorkroomStoreKey.CleanupPending && !WorkroomStorage.Busy &&
            !WorkroomItemCodec.CleanupPending && WorkroomStorage.State.PendingRecords.Count == 0 &&
            !ComputerSupplierNpcs.StockPending && !NpcStockOffers.Pending &&
            !view.HasOpenPanels && !view.BlocksPanelInput && !view.OwnsTextInput &&
            !view.IsDragging && !view.HasRoomSelection &&
            !(serverView?.BlocksPanelInput ?? false) &&
            WorkroomGameAdapter.HooksReady && WorkroomGameAdapter.AtStore() &&
            Time.timeScale > 0 && !Il2Cpp.Interactable.isPaused &&
            !WorkroomGameAdapter.PendingGesture();
    }

    internal static void RequestServerEntry() => RequestRegion(WorkroomTransition.Region.ServerRoom);
    private static void RequestServerExit() => RequestRegion(WorkroomTransition.Region.Workroom);

    private static void RequestRegion(WorkroomTransition.Region target)
    {
        var source = target == WorkroomTransition.Region.Workroom
            ? WorkroomTransition.Region.ServerRoom : WorkroomTransition.Region.Workroom;
        if (regionRequested || !flow.IsStable ||
            flow.CurrentRegion != source) return;
        try
        {
            if (!RegionTransitionReady()) { view?.Notify("busy"); return; }
            // Unity's click can arrive before the native mouse-up tail. Start
            // the fade only after the original gesture and two neutral frames.
            regionRequested = true;
            regionTarget = target;
            regionRequestUntil = Environment.TickCount64 + 3000;
            regionNeutralFrames = 0;
        }
        catch (Exception ex) { regionRequested = false; Warn(ex, "region-request"); }
    }

    private static void ProcessRegionRequest()
    {
        if (!regionRequested) return;
        var source = regionTarget == WorkroomTransition.Region.Workroom
            ? WorkroomTransition.Region.ServerRoom : WorkroomTransition.Region.Workroom;
        if (!flow.IsStable || flow.CurrentRegion != source || !RegionTransitionReady())
        { regionRequested = false; return; }
        if (Environment.TickCount64 >= regionRequestUntil)
        { regionRequested = false; view?.Notify("busy"); return; }
        var neutral = WorkroomGameAdapter.InputNeutral() && !WorkroomGameAdapter.PendingGesture();
        regionNeutralFrames = neutral ? regionNeutralFrames + 1 : 0;
        if (regionNeutralFrames < 2) return;
        regionRequested = false;
        view?.ReleaseItemInfo();
        serverView?.ClosePanels();
        if (!flow.Request(regionTarget)) view?.Notify("busy");
        else if (regionTarget == WorkroomTransition.Region.Shop)
            Core.Debug("[工作间] 固定出口返回店铺。");
    }

    private static void ProcessStorageRequest()
    {
        if (!storageRequested) return;
        var expected = storageRequestInRoom ? WorkroomTransition.Region.Workroom : WorkroomTransition.Region.Shop;
        if (view == null || cleanupPending || entryRequested || regionRequested || !flow.IsStable ||
            flow.CurrentRegion != expected ||
            WorkroomStorage.State.Epoch != storageRequestEpoch || view.BlocksPanelInput || view.IsDragging ||
            !WorkroomGameAdapter.HooksReady || !WorkroomGameAdapter.AtStore() || Time.timeScale <= 0 ||
            WorkroomStorageDragPatch.ConsumedFrame >= storageRequestFrame ||
            (Time.frameCount > storageRequestFrame && (Input.GetMouseButtonDown(0) ||
                Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))))
        {
            storageRequested = false;
            Core.Debug("[工作间开箱] cancelled; context or new input changed.");
            return;
        }
        if (Environment.TickCount64 >= storageRequestUntil)
        {
            storageRequested = false;
            Core.Log?.Warning("[工作间开箱] timeout; " + WorkroomGameAdapter.GestureState());
            view.Notify("busy");
            return;
        }
        var neutral = WorkroomGameAdapter.InputNeutral() &&
            !WorkroomGameAdapter.PendingGesture(allowStableSelection: true);
        storageNeutralFrames = neutral ? storageNeutralFrames + 1 : 0;
        if (storageNeutralFrames < 2) return;
        storageRequested = false;
        view.OpenPanel(WorkroomPanels.Kind.Storage, storageRequestInRoom);
        WorkroomGameAdapter.SetSharedInputBlocked(BlocksSharedInput);
        Core.Debug("[工作间开箱] opened; room=" + storageRequestInRoom);
    }

    internal static void Update()
    {
        var stage = "session";
        try
        {
            if (cleanupPending)
            {
                if (Environment.TickCount64 >= cleanupRetryAt) Reset();
                return;
            }
            var store = WorkroomGameAdapter.ExistingStore();
            var pointer = store == null ? IntPtr.Zero : store.Pointer;
            var scene = store == null ? IntPtr.Zero : Il2Cpp.EmporiumEntry.Instance.Pointer;
            var currentRun = store == null ? null : store.runID;
            var currentSlot = store == null ? 0 : store.saveSlotId;
            var currentLocale = LanguageText.LocaleCode;
            if (pointer != storePointer || scene != scenePointer || currentRun != run ||
                currentSlot != slot || currentLocale != locale)
            {
                Reset();
                storePointer = pointer; scenePointer = scene; run = currentRun; slot = currentSlot; locale = currentLocale;
                if (store != null) BusinessRuntime.ObserveSession(store, scene, currentLocale);
            }
            if (store == null) return;
            WorkroomGameAdapter.RecoverIdleQuickTransfer();
            stage = "events";
            var events = EventSystem.current;
            if (!WorkroomGameAdapter.EventsReady(events) || !WorkroomGameAdapter.AtStore() ||
                (BlocksNativeInput && !WorkroomGameAdapter.SameEvents(events)))
            { Reset(); return; }
            if ((view != null && !view.Alive) || (serverView != null && !serverView.Alive))
            { Reset(); return; }
            if (view == null && Environment.TickCount64 >= retryAt)
            {
                stage = "view-build";
                retryAt = Environment.TickCount64 + 1000;
                // Keep ownership even if partial construction/cleanup fails.
                view = new WorkroomView();
                NpcVisitPerformance.Begin(NpcVisitPerformance.WorkroomBuild);
                try { view.Build(Exit, RequestServerEntry); }
                finally { NpcVisitPerformance.End(NpcVisitPerformance.WorkroomBuild); }
            }
            if (view == null) return;
            stage = "overlay";
            EnsureOverlay();
            if (cleanupPending) { Reset(); return; }
            stage = "entry-request";
            ProcessEntryRequest();
            stage = "storage-request";
            ProcessStorageRequest();
            stage = "region-request";
            ProcessRegionRequest();
            stage = "transition";
            if (RegionInputHeld)
            {
                flow.Tick(Time.unscaledDeltaTime, WorkroomGameAdapter.InputNeutral());
                if (flow.ReadyToSwitch) CompleteRegionSwitch();
                if (!flow.BlocksInput) WorkroomGameAdapter.Release();
                else
                {
                    WorkroomGameAdapter.SetRegionVisible(flow.RegionVisible);
                    WorkroomGameAdapter.SetSharedInputBlocked(BlocksSharedInput);
                }
            }
            stage = "view-render";
            // Audio failures are isolated inside their owner and cannot reset or roll back gameplay.
            RoomAudio.Tick(flow.CurrentRegion);
            RoomAudioSettingsUi.Tick(flow.IsStable && flow.RegionVisible && Time.timeScale > 0 && !Il2Cpp.Interactable.isPaused,
                !(view?.IsDragging ?? false) && !WorkroomGameAdapter.PendingGesture() && !WorkroomStorage.Busy);
            view.Render(flow, WorkroomGameAdapter.StoreViewport());
            serverView?.Render(flow.ServerRoomVisible,
                flow.IsStable && flow.CurrentRegion == WorkroomTransition.Region.ServerRoom);
            stage = "shared-input";
            WorkroomGameAdapter.SetSharedInputBlocked(BlocksSharedInput);
        }
        catch (Exception ex)
        {
            Warn(ex, stage);
            // A failed Reset already owns cleanup and scheduled its retry.
            // Do not repeat the same native cleanup twice in this frame.
            if (!cleanupPending)
                try { Reset(); } catch (Exception cleanup) { Warn(cleanup, "cleanup"); }
            retryAt = Environment.TickCount64 + 1000;
        }
    }

    private static void CompleteRegionSwitch()
    {
        var target = flow.TargetRegion;
        try
        {
            if (target == WorkroomTransition.Region.ServerRoom)
            {
                if (serverView == null)
                {
                    serverView = new ServerRoomView();
                    serverView.Build(RequestServerExit);
                }
                if (!serverView.Alive) throw new InvalidOperationException("机房占位区域创建失败。");
                serverView.EnsureOverlay();
            }
            else if (target == WorkroomTransition.Region.Workroom)
            {
                if (view == null || !view.Alive) throw new InvalidOperationException("工作间区域不可用。");
                view.EnsureOverlay();
            }
            flow.CommitSwitch();
        }
        catch (Exception ex)
        {
            Warn(ex, "region-target");
            if (target == WorkroomTransition.Region.ServerRoom && serverView != null)
            {
                try { serverView.Dispose(); serverView = null; }
                catch (Exception cleanup)
                {
                    cleanupPending = true;
                    cleanupRetryAt = Environment.TickCount64 + 500;
                    Warn(cleanup, "region-target-cleanup");
                    return;
                }
            }
            flow.CancelSwitch();
        }
    }

    private static void Enter()
    {
        if (BlocksNativeInput || view == null) return;
        try
        {
            var store = WorkroomGameAdapter.ExistingStore();
            if (store == null || store.Pointer != storePointer || store.runID != run || store.saveSlotId != slot ||
                Il2Cpp.EmporiumEntry.Instance.Pointer != scenePointer || LanguageText.LocaleCode != locale ||
                !WorkroomGameAdapter.AtStore() || !WorkroomGameAdapter.HooksReady || Time.timeScale <= 0)
            { view.Notify("unavailable"); return; }
            if (WorkroomGameAdapter.PendingGesture())
            { view.Notify("busy"); return; }
            var prepared = view;
            var events = EventSystem.current;
            NpcVisitPerformance.Begin(NpcVisitPerformance.WorkroomBuild);
            try { prepared.EnsureRoom(); }
            finally { NpcVisitPerformance.End(NpcVisitPerformance.WorkroomBuild); }
            // Native UI creation and selection callbacks can reset the session.
            if (!EntryContextMatches(prepared, events) || !CanRequestEntry) return;
            WorkroomGameAdapter.Acquire(events);
            if (!EntryContextMatches(prepared, events) || !WorkroomGameAdapter.LeaseHeld ||
                !WorkroomGameAdapter.SameEvents(events) || RoomAudioSettingsUi.BlocksInput ||
                WorkroomStoryTransfer.Pending || view.BlocksPanelInput || view.IsDragging ||
                WorkroomGameAdapter.PendingGesture())
            {
                // A reset owns failed cleanup; a replacement view owns its own lease.
                if (ReferenceEquals(view, prepared) && !cleanupPending) WorkroomGameAdapter.Release();
                return;
            }
            if (!flow.Enter()) { WorkroomGameAdapter.Release(); return; }
            view.Render(flow, WorkroomGameAdapter.StoreViewport());
            Core.Debug("[工作间试作] 店内钥匙进入；原版出门业务未调用。");
        }
        catch (Exception ex)
        {
            try { Reset(); } catch (Exception cleanup) { Warn(cleanup); }
            Warn(ex);
        }
    }

    private static bool EntryContextMatches(WorkroomView prepared, EventSystem events)
    {
        var current = WorkroomGameAdapter.ExistingStore();
        var activeEvents = EventSystem.current;
        return ReferenceEquals(view, prepared) && !cleanupPending && prepared.Alive &&
            current != null && current.Pointer == storePointer && current.runID == run && current.saveSlotId == slot &&
            Il2Cpp.EmporiumEntry.Instance.Pointer == scenePointer && LanguageText.LocaleCode == locale &&
            WorkroomGameAdapter.EventsReady(events) && activeEvents != null && activeEvents.Pointer == events.Pointer &&
            flow.IsStable && flow.CurrentRegion == WorkroomTransition.Region.Shop &&
            WorkroomGameAdapter.AtStore() && WorkroomGameAdapter.HooksReady && !WorkroomStoreKey.CleanupPending &&
            Time.timeScale > 0 && !Il2Cpp.Interactable.isPaused;
    }

    private static void Exit()
    {
        RequestRegion(WorkroomTransition.Region.Shop);
    }

    internal static void EnsureOverlay()
    {
        if (cleanupPending) return;
        try { view?.EnsureOverlay(); serverView?.EnsureOverlay(); }
        catch (Exception ex)
        {
            // Don't throw back into the game's Letterbox conversion callback.
            cleanupPending = true;
            Warn(ex, "overlay");
        }
    }

    internal static void Reset()
    {
        RoomAudioSettingsUi.Reset();
        RoomAudio.Reset();
        cleanupPending = true;
        cleanupRetryAt = Environment.TickCount64 + 500;
        entryRequested = false;
        requestNeutralFrames = 0;
        regionRequested = false;
        regionNeutralFrames = 0;
        storageRequested = false;
        storageNeutralFrames = 0;
        // Keep the shop lease while any workroom view survives failed cleanup.
        // A later retry may finish disposing it before native shop clicks resume.
        WorkroomStoreKey.Reset();
        view?.Dispose();
        view = null;
        serverView?.Dispose();
        serverView = null;
        WorkroomGameAdapter.Release();
        flow.Abort();
        storePointer = scenePointer = IntPtr.Zero; run = locale = null; slot = 0;
        retryAt = 0;
        cleanupRetryAt = 0;
        cleanupPending = false;
    }

    internal static void OnSceneChanged()
    {
        try { Reset(); } catch (Exception ex) { Warn(ex); }
    }

    private static void Warn(Exception ex, string stage = "lifecycle")
    {
        if (Environment.TickCount64 < warnAt) return;
        warnAt = Environment.TickCount64 + 5000;
        Core.Log?.Warning($"工作间试作已暂停，稍后重试；stage={stage}：{ex}");
    }
}
