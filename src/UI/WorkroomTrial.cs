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
    private static IntPtr storePointer, scenePointer;
    private static string run, locale;
    private static int slot;
    private static long retryAt, warnAt, cleanupRetryAt;
    private static bool cleanupPending;
    private static bool entryRequested;
    private static long requestUntil;
    private static int requestNeutralFrames;
    private static bool storageRequested, storageRequestInRoom;
    private static long storageRequestUntil, storageRequestEpoch;
    private static int storageNeutralFrames, storageRequestFrame;
    private static bool RegionInputHeld => flow.BlocksInput || WorkroomGameAdapter.LeaseHeld || cleanupPending;
    internal static bool IsolatesStoreRegion => RegionInputHeld;
    internal static bool RoomSelectionActive => !cleanupPending && (view?.HasRoomSelection ?? false);
    internal static bool OwnsTextInput => !cleanupPending && (view?.OwnsTextInput ?? false);
    internal static bool BlocksNativeInput => RegionInputHeld || (view?.BlocksPanelInput ?? false);
    internal static bool BlocksSharedInput => cleanupPending ||
        (RegionInputHeld && flow.Current != WorkroomTransition.Stage.Inside) || (view?.BlocksPanelInput ?? false);
    internal static bool OwnsPanelMouse => !cleanupPending &&
        (WorkroomStoreKey.OwnsMousePress || (view?.OwnsMousePress ?? false));
    internal static bool OwnsPanelScroll => !cleanupPending && (view?.OwnsScroll ?? false);
    internal static bool OwnsPanelEscape => !cleanupPending && (view?.OwnsEscape ?? false);
    internal static bool ShopEntryVisible => view != null && !cleanupPending &&
        flow.Current == WorkroomTransition.Stage.Home && WorkroomGameAdapter.AtStore();
    internal static bool CanRequestEntry => ShopEntryVisible && !entryRequested && !BlocksNativeInput &&
        !WorkroomStoreKey.CleanupPending &&
        !view.IsDragging && WorkroomGameAdapter.HooksReady && Time.timeScale > 0 &&
        !Il2Cpp.Interactable.isPaused && !WorkroomGameAdapter.PendingGesture();
    internal static bool CanCollectCurrent(bool inRoom) => view != null && !cleanupPending && !entryRequested &&
        flow.Current == (inRoom ? WorkroomTransition.Stage.Inside : WorkroomTransition.Stage.Home) &&
        !view.BlocksPanelInput && !view.IsDragging &&
        WorkroomGameAdapter.HooksReady && WorkroomGameAdapter.AtStore() && Time.timeScale > 0 &&
        !WorkroomGameAdapter.PendingGesture(allowStableSelection: true);
    internal static void ClearRoomSelection(long epoch) => view?.ClearRoomSelection(epoch);
    internal static bool HitShopStorage(Vector2 point) => view != null &&
        flow.Current == WorkroomTransition.Stage.Home && !BlocksNativeInput &&
        Time.timeScale > 0 && WorkroomGameAdapter.AtStore() && view.HitShopStorageDrop(point);
    internal static bool HitShopStorageWindow(Vector2 point) => view != null && !cleanupPending &&
        flow.Current == WorkroomTransition.Stage.Home && view.HitStorageWindow(point);
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
            if (view == null || cleanupPending || entryRequested ||
                view.BlocksPanelInput || view.IsDragging || WorkroomStorageDragPatch.ConsumedFrame == Time.frameCount ||
                !WorkroomGameAdapter.HooksReady || Time.timeScale <= 0 || !WorkroomGameAdapter.AtStore()) return;
            var expected = inRoom ? WorkroomTransition.Stage.Inside : WorkroomTransition.Stage.Home;
            if (flow.Current != expected) { view.Notify("busy"); return; }
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
                Core.Log?.Msg("[工作间开箱] queued; room=" + inRoom + "; " + WorkroomGameAdapter.GestureState());
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

    private static void ProcessStorageRequest()
    {
        if (!storageRequested) return;
        var expected = storageRequestInRoom ? WorkroomTransition.Stage.Inside : WorkroomTransition.Stage.Home;
        if (view == null || cleanupPending || entryRequested || flow.Current != expected ||
            WorkroomStorage.State.Epoch != storageRequestEpoch || view.BlocksPanelInput || view.IsDragging ||
            !WorkroomGameAdapter.HooksReady || !WorkroomGameAdapter.AtStore() || Time.timeScale <= 0 ||
            WorkroomStorageDragPatch.ConsumedFrame >= storageRequestFrame ||
            (Time.frameCount > storageRequestFrame && (Input.GetMouseButtonDown(0) ||
                Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))))
        {
            storageRequested = false;
            Core.Log?.Msg("[工作间开箱] cancelled; context or new input changed.");
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
        Core.Log?.Msg("[工作间开箱] opened; room=" + storageRequestInRoom);
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
            }
            if (store == null) return;
            WorkroomGameAdapter.RecoverIdleQuickTransfer();
            stage = "storage-update";
            WorkroomStorage.Update(store);
            stage = "events";
            var events = EventSystem.current;
            if (!WorkroomGameAdapter.EventsReady(events) || !WorkroomGameAdapter.AtStore() ||
                (BlocksNativeInput && !WorkroomGameAdapter.SameEvents(events)))
            { Reset(); return; }
            if (view != null && !view.Alive) { Reset(); return; }
            if (view == null && Environment.TickCount64 >= retryAt)
            {
                stage = "view-build";
                retryAt = Environment.TickCount64 + 1000;
                // Keep ownership even if partial construction/cleanup fails.
                view = new WorkroomView();
                view.Build(Exit);
            }
            if (view == null) return;
            stage = "overlay";
            EnsureOverlay();
            if (cleanupPending) { Reset(); return; }
            stage = "entry-request";
            ProcessEntryRequest();
            stage = "storage-request";
            ProcessStorageRequest();
            stage = "transition";
            if (RegionInputHeld)
            {
                flow.Tick(Time.unscaledDeltaTime, WorkroomGameAdapter.InputNeutral());
                if (!flow.BlocksInput) WorkroomGameAdapter.Release();
                else
                {
                    WorkroomGameAdapter.SetRegionVisible(flow.RoomVisible);
                    WorkroomGameAdapter.SetSharedInputBlocked(BlocksSharedInput);
                }
            }
            stage = "view-render";
            view.Render(flow, WorkroomGameAdapter.StoreViewport());
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
            WorkroomGameAdapter.Acquire(EventSystem.current);
            if (!flow.Enter()) { WorkroomGameAdapter.Release(); return; }
            view.Render(flow, WorkroomGameAdapter.StoreViewport());
            Core.Log?.Msg("[工作间试作] 店内钥匙进入；原版出门业务未调用。");
        }
        catch (Exception ex)
        {
            try { Reset(); } catch (Exception cleanup) { Warn(cleanup); }
            Warn(ex);
        }
    }

    private static void Exit()
    {
        if (cleanupPending) return;
        if ((view?.BlocksPanelInput ?? false) || (view?.IsDragging ?? false)) return;
        if (WorkroomGameAdapter.PendingGesture()) { view?.Notify("busy"); return; }
        // Hide the workroom tooltip before restoring shop input.
        view?.ReleaseItemInfo();
        if (flow.Exit()) Core.Log?.Msg("[工作间试作] 固定出口返回。");
    }

    internal static void EnsureOverlay()
    {
        if (cleanupPending) return;
        try { view?.EnsureOverlay(); }
        catch (Exception ex)
        {
            // Don't throw back into the game's Letterbox conversion callback.
            cleanupPending = true;
            Warn(ex, "overlay");
        }
    }

    internal static void Reset()
    {
        cleanupPending = true;
        cleanupRetryAt = Environment.TickCount64 + 500;
        entryRequested = false;
        requestNeutralFrames = 0;
        storageRequested = false;
        storageNeutralFrames = 0;
        // Keep the shop lease while any workroom view survives failed cleanup.
        // A later retry may finish disposing it before native shop clicks resume.
        WorkroomStoreKey.Reset();
        view?.Dispose();
        view = null;
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
