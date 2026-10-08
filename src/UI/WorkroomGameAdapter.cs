#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>原版接入集中在这里；不调用出门业务或写库存/营业/访问次数。</summary>
internal static class WorkroomGameAdapter
{
    private static readonly HashSet<Type> installed = new();
    private static readonly Type[] required = { typeof(WorkroomInputPatch), typeof(WorkroomStorageTextKeysPatch), typeof(WorkroomStoreUiPatch),
        typeof(WorkroomStoreTickPatch), typeof(WorkroomToolPatch), typeof(WorkroomDragPatch),
        typeof(WorkroomWindowsPatch), typeof(WorkroomLetterboxPatch), typeof(WorkroomLoadPatch),
        typeof(SaveGeneration.EarlyLoadPatch), typeof(SaveGeneration.InitialSavePatch),
        typeof(SaveGeneration.WritePatch), typeof(SaveGeneration.DecodePatch),
        typeof(WorkroomArmPatch), typeof(WorkroomStorageSavePatch), typeof(WorkroomPanelPressPatch),
        typeof(WorkroomPanelScrollPatch), typeof(WorkroomStorageReleasePatch), typeof(WorkroomStorageDragPatch),
        typeof(WorkroomShopInteractablePatch), typeof(WorkroomShopUiInteractablePatch), typeof(WorkroomShopWindowPatch),
        typeof(WorkroomShopWindowFrontPatch), typeof(WorkroomShopItemPressPatch), typeof(WorkroomShopItemActionPatch),
        typeof(WorkroomShopItemDragStartPatch), typeof(WorkroomShopItemSelectPatch),
        typeof(WorkroomStorageGroupReleasePatch), typeof(WorkroomStorageGroupDragPatch),
        typeof(WorkroomMultiSelectPressPatch), typeof(WorkroomMultiSelectReleasePatch), typeof(WorkroomMultiSelectMovePatch), typeof(WorkroomMultiSelectUpdatePatch),
        typeof(WorkroomStoreKeyClickPatch), typeof(WorkroomEntryHoverClickPatch),
        typeof(WorkroomEntryTooltipPatch), typeof(WorkroomEntryTooltipLayerPatch), typeof(WorkroomEntryTooltipShowPatch) };
    private static bool ownsNavigation, regionIsolated;
    private static bool previousNavigation;
    private static EventSystem capturedEvents, navigationEvents;
    private static PlayerStore capturedStore;
    private static EmporiumEntry capturedEntry;
    private static string capturedRun;
    private static int capturedSlot;
    private static readonly WorkroomFlagLease controls = new(), hitTargets = new();
    private static readonly HashSet<IntPtr> shopRoots = new();
    private static readonly HashSet<IntPtr> shopWindows = new(), shopInventories = new(), shopFixtures = new();
    private static long refreshAt, discoverAt, interactionNoticeAt;
    internal static bool LeaseHeld => capturedEvents != null || controls.Held || hitTargets.Held;
    internal static bool HooksReady
    {
        get { foreach (var type in required) if (!installed.Contains(type)) return false; return true; }
    }
    internal static bool SaveHooksReady => installed.Contains(typeof(WorkroomLoadPatch)) &&
        installed.Contains(typeof(WorkroomStorageSavePatch)) &&
        installed.Contains(typeof(SaveGeneration.EarlyLoadPatch)) && installed.Contains(typeof(SaveGeneration.InitialSavePatch)) &&
        installed.Contains(typeof(SaveGeneration.WritePatch)) && installed.Contains(typeof(SaveGeneration.DecodePatch));

    internal static void PatchInstalled(Type type, string owner)
    {
        if (Array.IndexOf(required, type) < 0) return;
        installed.Remove(type);
        var targets = type.GetMethod("TargetMethods", BindingFlags.Static | BindingFlags.NonPublic);
        IEnumerable<MethodBase> expected;
        if (targets != null) expected = (IEnumerable<MethodBase>)targets.Invoke(null, null);
        else
        {
            Type declaringType = null;
            string methodName = null;
            Type[] arguments = null;
            foreach (HarmonyPatch patch in type.GetCustomAttributes(typeof(HarmonyPatch), false))
            {
                declaringType = patch.info.declaringType ?? declaringType;
                methodName = patch.info.methodName ?? methodName;
                arguments = patch.info.argumentTypes ?? arguments;
            }
            expected = new[] { AccessTools.DeclaredMethod(declaringType, methodName, arguments) };
        }
        var count = 0;
        foreach (var method in expected)
        {
            // PatchClassProcessor returns replacement methods. Query the
            // original target's registry and require this class's own patches.
            if (method == null || !ClassPatchesInstalled(type, owner, HarmonyLib.Harmony.GetPatchInfo(method)))
            {
                Core.Log?.Warning("[工作间接入] 缺少本类补丁登记：" + type.Name + " / " + method);
                return;
            }
            count++;
            Core.Debug("[工作间接入] 已安装具体目标：" + type.Name + " / " + method.DeclaringType?.FullName + "." + method);
        }
        if (count == 0) return;
        installed.Add(type);
    }

    private static bool ClassPatchesInstalled(Type type, string owner, Patches patches)
    {
        if (patches == null || string.IsNullOrEmpty(owner)) return false;
        var count = 0;
        return DeclaredPatchesInstalled(type, owner, "Prefix", typeof(HarmonyPrefix), patches.Prefixes, ref count) &&
            DeclaredPatchesInstalled(type, owner, "Postfix", typeof(HarmonyPostfix), patches.Postfixes, ref count) &&
            DeclaredPatchesInstalled(type, owner, "Transpiler", typeof(HarmonyTranspiler), patches.Transpilers, ref count) &&
            DeclaredPatchesInstalled(type, owner, "Finalizer", typeof(HarmonyFinalizer), patches.Finalizers, ref count) && count > 0;
    }

    private static bool DeclaredPatchesInstalled(Type type, string owner, string name, Type attribute,
        IEnumerable<Patch> patches, ref int count)
    {
        foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (method.Name != name && !method.IsDefined(attribute, false)) continue;
            count++;
            var found = false;
            foreach (var patch in patches)
                if (patch.owner == owner && patch.PatchMethod == method) { found = true; break; }
            if (!found) return false;
        }
        return true;
    }

    internal static void ReportPatchStatus()
    {
        var status = "[工作间接入] 补丁登记核对：" + installed.Count + "/" + required.Length + " 类。";
        if (HooksReady) Core.Log?.Msg(status);
        else Core.Log?.Warning(status + "工作间接入未就绪。");
    }

    internal static void ResetPatchStatus() => installed.Clear();

    internal static PlayerStore ExistingStore()
    {
        if (!PlayerStore.IsInstanceExist()) return null;
        var store = PlayerStore.instance;
        var scene = EmporiumEntry.Instance;
        return store != null && scene != null && store.gridInv != null &&
            ((scene.isActiveAndEnabled && scene.gameObject.activeInHierarchy) ||
             (LeaseHeld && SameStore())) ? store : null;
    }

    private static bool SameStore() => capturedStore != null && WorkroomUiCleanup.IsAlive(capturedEntry) &&
        PlayerStore.IsInstanceExist() && PlayerStore.instance != null &&
        PlayerStore.instance.Pointer == capturedStore.Pointer && PlayerStore.instance.runID == capturedRun &&
        PlayerStore.instance.saveSlotId == capturedSlot && EmporiumEntry.Instance != null &&
        EmporiumEntry.Instance.Pointer == capturedEntry.Pointer;

    internal static bool AtStore()
    {
        var map = MapUIManager.Instance;
        return map != null && !map.isOutside && !map.inTransit;
    }

    internal static bool EventsReady(EventSystem events) => events != null &&
        events.isActiveAndEnabled && events.currentInputModule != null;

    internal static bool PendingGesture(bool allowStableSelection = false)
    {
        if (WorkroomTrial.RoomSelectionActive && !allowStableSelection) return true;
        var multi = ItemMultiSelectHandler.current;
        if (multi != null && (int)multi.state != 0 && !(allowStableSelection && (int)multi.state == 3)) return true;
        var drag = ItemMouseDragHandler.current;
        if (drag != null && (drag.isDragging || drag.currentItem != null)) return true;
        if (UIDragHandler.isDraggingAnyUI || UIDragHandler.currentDragger != null) return true;
        var selection = ItemSelectHandler.current;
        if (selection != null && (selection.isEquipped || selection.currentItem != null)) return true;
        var quick = ItemQuickTransferHandler.current;
        if (quick != null && quick.currentItem != null && !InputNeutral()) return true;
        var alt = ItemQuickTransferAltHandler.current;
        if (alt != null && alt.currentItem != null && !InputNeutral()) return true;
        var alt1 = ItemQuickTransferAltHandler1.current;
        return alt1 != null && alt1.currentItem != null && !InputNeutral();
    }

    internal static bool InputNeutral()
    {
        // UI ownership follows the mouse gesture, not unrelated keyboard holds.
        // Keep the release edge so its native cleanup finishes before transition.
        for (var i = 0; i < 7; i++)
            if (Input.GetMouseButton(i) || Input.GetMouseButtonDown(i) || Input.GetMouseButtonUp(i)) return false;
        return true;
    }

    internal static void RecoverIdleQuickTransfer()
    {
        if (!InputNeutral()) return;
        static bool Released(KeyCode key) => key == KeyCode.None ||
            (!Input.GetKey(key) && !Input.GetKeyDown(key) && !Input.GetKeyUp(key));
        var quick = ItemQuickTransferHandler.current;
        if (quick != null && Released(quick.key) && Released(quick.quickTransferKey) &&
            (quick.isQuickTransfer || quick.currentItem != null)) quick.OnEventReset();
        var alt = ItemQuickTransferAltHandler.current;
        if (alt != null && Released(alt.key) && Released(alt.quickTransferKey) &&
            (alt.isQuickTransfer || alt.currentItem != null)) alt.OnEventReset();
        var shift = ItemQuickTransferAltHandler1.current;
        if (shift != null && Released(shift.key) && Released(shift.quickTransferKey) &&
            (shift.isQuickTransfer || shift.currentItem != null)) shift.OnEventReset();
    }

    // Read only at a storage request/timeout, never in the per-frame gate.
    internal static string GestureState()
    {
        try
        {
            var multi = ItemMultiSelectHandler.current;
            var drag = ItemMouseDragHandler.current;
            var select = ItemSelectHandler.current;
            return "frame=" + Time.frameCount + "; multi=" + (multi == null ? "none" : ((int)multi.state).ToString()) +
                "; drag=" + (drag?.isDragging ?? false) + "/item=" + (drag?.currentItem != null) +
                "; uiDrag=" + UIDragHandler.isDraggingAnyUI + "/owner=" + (UIDragHandler.currentDragger != null) +
                "; select=" + (select?.isEquipped ?? false) + "/item=" + (select?.currentItem != null) +
                "; quick=" + (ItemQuickTransferHandler.current?.isQuickTransfer ?? false) +
                "; alt=" + (ItemQuickTransferAltHandler.current?.isQuickTransfer ?? false) +
                "; alt1=" + (ItemQuickTransferAltHandler1.current?.isQuickTransfer ?? false) +
                "; roomSelection=" + WorkroomTrial.RoomSelectionActive;
        }
        catch (Exception ex) { return "state-unreadable=" + ex.GetType().Name; }
    }

    internal static Rect StoreViewport()
    {
        var width = LetterboxManager.GameAreaWidth;
        var height = LetterboxManager.GameAreaHeight;
        if (width > 0 && height > 0)
            return new Rect(LetterboxManager.GameAreaX, LetterboxManager.GameAreaY, width, height);
        return new Rect(0, 0, Screen.width, Screen.height);
    }

    internal static void ForgetLetterboxCanvases(Canvas key, Canvas room, Canvas black)
    {
        // Reclaim only our registrations. Repeated native conversion must not grow its list.
        if (ReferenceEquals(key, null) && ReferenceEquals(room, null) && ReferenceEquals(black, null)) return;
        var manager = LetterboxManager._Instance_k__BackingField;
        if (!WorkroomUiCleanup.IsAlive(manager)) return;
        var canvases = manager.convertedCanvases;
        if (canvases == null) return;
        var keyPointer = ReferenceEquals(key, null) ? IntPtr.Zero : key.Pointer;
        var roomPointer = ReferenceEquals(room, null) ? IntPtr.Zero : room.Pointer;
        var blackPointer = ReferenceEquals(black, null) ? IntPtr.Zero : black.Pointer;
        for (var i = canvases.Count - 1; i >= 0; i--)
        {
            var canvas = canvases[i];
            if (ReferenceEquals(canvas, null)) continue;
            var pointer = canvas.Pointer;
            if (pointer == keyPointer || pointer == roomPointer || pointer == blackPointer) canvases.RemoveAt(i);
        }
    }

    internal static void Acquire(EventSystem events)
    {
        if (LeaseHeld || !HooksReady || !EventsReady(events) || InputActionManager.current == null ||
            StoreUIManager.Instance == null) throw new InvalidOperationException("工作间输入接入未就绪。");
        if (!float.IsFinite(Time.timeScale) || Time.timeScale <= 0)
            throw new InvalidOperationException("当前游戏处于暂停或切换中。");
        capturedStore = ExistingStore();
        capturedEntry = EmporiumEntry.Instance;
        if (capturedStore == null || !AssemblyDebugUi.Alive(capturedEntry))
            throw new InvalidOperationException("店铺对象未就绪。");
        capturedRun = capturedStore.runID;
        capturedSlot = capturedStore.saveSlotId;
        capturedEvents = events;
        refreshAt = discoverAt = 0;
        try
        {
            ClearStoreHighlights("acquire");
            CaptureControl(capturedEntry);
            CaptureControl(StoreUIManager.Instance);
            CaptureStoreObjects();
            controls.Suspend();
            SetSharedInputBlocked(true);
            events.SetSelectedGameObject(null);
        }
        catch { Release(); throw; }
    }

    private static void CaptureControl(Behaviour control)
    {
        if (!AssemblyDebugUi.Alive(control)) return;
        controls.Add(control.Pointer, () => control.enabled, value => control.enabled = value,
            () => WorkroomUiCleanup.IsAlive(control) && SameStore());
    }

    private static long highlightWarnAt;
    private static void ClearStoreHighlights(string phase)
    {
        try
        {
            if (!SameStore()) return;
            var seen = new HashSet<IntPtr>();
            var pools = 0; var visible = 0; var remaining = 0; var skipped = 0;
            foreach (var inventory in new GameInventory[] { capturedStore.gridInv, capturedEntry.invElement })
            {
                if (inventory == null || !seen.Add(inventory.Pointer)) continue;
                try
                {
                    var grid = inventory.TryCast<GameGridInventory>();
                    var scrolling = grid == null ? inventory.TryCast<GameGridScrollableInventory>() : null;
                    var handler = grid != null ? grid.handler : scrolling?.handler;
                    var background = grid != null ? grid.background : scrolling?.background;
                    var nodes = grid != null ? grid.highlightSquares : scrolling?.highlightSquares;
                    if (!WorkroomUiCleanup.IsAlive(handler) || !WorkroomUiCleanup.IsAlive(background) || nodes == null)
                    { skipped++; continue; }
                    var valid = true; var before = 0;
                    for (var i = 0; i < nodes.Count; i++)
                    {
                        var node = nodes[i];
                        if (!WorkroomUiCleanup.IsAlive(node) || !WorkroomUiCleanup.IsAlive(node.image))
                        { valid = false; break; }
                        if (node.image.enabled) before++;
                    }
                    if (!valid) { skipped++; continue; }
                    // The concrete native functions only disable this pool's
                    // Images; they do not change items, positions or selection.
                    if (grid != null) grid.ClearHighlight();
                    else scrolling.ClearHighlight();
                    pools++; visible += before;
                    for (var i = 0; i < nodes.Count; i++)
                        if (WorkroomUiCleanup.IsAlive(nodes[i]) && WorkroomUiCleanup.IsAlive(nodes[i].image) && nodes[i].image.enabled)
                            remaining++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    if (Environment.TickCount64 >= highlightWarnAt)
                    {
                        highlightWarnAt = Environment.TickCount64 + 5000;
                        Core.Log?.Warning("[工作间高亮] skipped; phase=" + phase + "; " + ex.Message);
                    }
                }
            }
            Core.Log?.Msg("[工作间高亮] phase=" + phase + "; pools=" + pools + "; visible=" + visible +
                "; remaining=" + remaining + "; skipped=" + skipped);
        }
        catch (Exception ex)
        {
            // A cosmetic cleanup must not turn into an input recovery loop.
            if (Environment.TickCount64 < highlightWarnAt) return;
            highlightWarnAt = Environment.TickCount64 + 5000;
            Core.Log?.Warning("[工作间高亮] unavailable; phase=" + phase + "; " + ex.Message);
        }
    }

    private static void CaptureStoreObjects()
    {
        var ui = StoreUIManager.Instance;
        var commonWindowRoot = RenderHandler.current?.windowRoot;
        if (!WorkroomUiCleanup.IsAlive(commonWindowRoot))
            throw new InvalidOperationException("原版窗口根未就绪。");
        // Refresh ownership from current roots; old leased flags remain held
        // for restoration but do not grant ownership to a reused native pointer.
        shopRoots.Clear(); shopWindows.Clear(); shopInventories.Clear(); shopFixtures.Clear();
        // SetActive(false) stops native coroutines. In particular the shutter's
        // OpenShutter timer must reach its unlock callback after a workroom visit.
        // The workroom's opaque Overlay covers the shop. Lease only hit targets;
        // native renderers/canvases/graphics keep their own live display state.
        void Track(GameObject node)
        {
            if (!WorkroomUiCleanup.IsAlive(node)) return;
            // A shop field can name an ancestor of the common native canvas.
            // Lease the owned window/item nodes below it, never that shared root.
            if (commonWindowRoot.transform.IsChildOf(node.transform)) return;
            shopRoots.Add(node.transform.Pointer);
            foreach (var raycaster in node.GetComponentsInChildren<BaseRaycaster>(true))
                hitTargets.Add(raycaster.Pointer, () => raycaster.enabled, value => raycaster.enabled = value,
                    () => WorkroomUiCleanup.IsAlive(raycaster) && SameStore());
            // Native Image/TMP nodes can share the common window canvas.
            // Disable their hit targets without snapshotting transient visuals.
            foreach (var graphic in node.GetComponentsInChildren<Graphic>(true))
            {
                hitTargets.Add(graphic.Pointer, () => graphic.raycastTarget, value => graphic.raycastTarget = value,
                    () => WorkroomUiCleanup.IsAlive(graphic) && SameStore(), 1);
            }
            foreach (var collider in node.GetComponentsInChildren<Collider>(true))
                hitTargets.Add(collider.Pointer, () => collider.enabled, value => collider.enabled = value,
                    () => WorkroomUiCleanup.IsAlive(collider) && SameStore());
            foreach (var collider in node.GetComponentsInChildren<Collider2D>(true))
                hitTargets.Add(collider.Pointer, () => collider.enabled, value => collider.enabled = value,
                    () => WorkroomUiCleanup.IsAlive(collider) && SameStore());
        }
        Track(capturedEntry.storePhysical);
        Track(capturedEntry.storeInteractables);
        Track(ui.worldInteractCanvas);
        Track(ui.clientGameObjectParent);
        Track(ui.clientCounter);
        Track(ui.pcScreenObject);
        Track(ui.paperUICanvasObject);
        Track(ui.arm);
        Track(ui.body);
        Track(ui.controlHintOverlay);
        if (ui.primaryCanvas != null) Track(ui.primaryCanvas.gameObject);
        if (ui.secondaryCanvas != null) Track(ui.secondaryCanvas.gameObject);
        if (ui.additionalCanvas != null) Track(ui.additionalCanvas.gameObject);
        // These closed inventory windows need explicit ownership too. They can
        // live outside the physical shop hierarchy and open via independent clicks.
        // Start only from concrete scene fixtures and native shop inventories.
        // Their content/examine windows are separately parented to windowRoot.
        var visited = new HashSet<IntPtr>();
        foreach (var window in new[] { capturedEntry.docInvWindow, capturedEntry.invWindow,
            capturedEntry.showcaseWindow, capturedEntry.trashInvWindow, capturedEntry.drainInvWindow,
            capturedEntry.frontInvWindow, capturedEntry.backInvWindow, capturedEntry.bazarLeftInvWindow,
            capturedEntry.hiddenWindow, capturedEntry.faucetWindow, capturedEntry.cassettePlayerWindow,
            capturedEntry.vendingMachineWindow, capturedEntry.vendingFountainWindow,
            capturedEntry.hirelingWindow, capturedEntry.responseWindow, capturedEntry.responseWindowClosable,
            capturedEntry.afterhourWindow }) TrackGraph(window);
        foreach (var inventory in new GameInventory[] { capturedStore.gridInv, capturedEntry.invElement,
            capturedEntry.showcaseElement, capturedEntry.docInvElement, capturedEntry.trashInvElement,
            capturedEntry.swapBufferElement, capturedEntry.drainInvElement, capturedEntry.frontInvinvElement,
            capturedEntry.backInvinvElement, capturedEntry.backInvinvElementCounter,
            capturedEntry.bazarLeftinvElement, capturedEntry.hiddenElement, capturedEntry.faucetElement,
            capturedEntry.cassettePlayerElement, capturedEntry.vendingMachineElement,
            capturedEntry.vendingFountainElement, capturedEntry.soldElement, capturedEntry.trashcanInvElement,
            capturedEntry.hirelingInv, capturedEntry.responseInventory, capturedEntry.responseInventoryClosable,
            capturedEntry.afterhourInventory, capturedEntry.afterhourPocketSlotInvLeft,
            capturedEntry.afterhourPocketSlotInvBackpack, capturedEntry.afterhourPocketSlotInvRight }) TrackGraph(inventory);
        // IL2CPP shares cassettePlayer's getter with GameItem.shape; mods can detour both.
        // Read the same native field directly to keep scene discovery outside that hook.
        foreach (var fixture in new[] { capturedEntry.dossier, capturedEntry.trashcan, capturedEntry.drain,
            capturedEntry.faucet, capturedEntry._cassettePlayer_k__BackingField })
        {
            if (fixture == null) continue;
            var element = fixture.TryCast<GameItemElement>();
            if (element != null && element.IsDestroyed()) continue;
            shopFixtures.Add(fixture.Pointer);
            TrackGraph(fixture);
        }

        void TrackGraph(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase node)
        {
            if (node == null || !visited.Add(node.Pointer)) return;
            var item = node.TryCast<GameItem>();
            if (item != null)
            {
                var element = item.TryCast<GameItemElement>();
                if (element != null)
                {
                    if (element.IsDestroyed()) return;
                    if (WorkroomUiCleanup.IsAlive(element.handler)) Track(element.handler.gameObject);
                }
                TrackGraph(item.contentWindow);
                TrackGraph(item.examineWindow);
            }
            var inventory = node.TryCast<GameInventory>();
            if (inventory != null) shopInventories.Add(inventory.Pointer);
            var window = node.TryCast<PixelWindow>();
            if (window != null)
            {
                if (!WorkroomUiCleanup.IsAlive(window._handler_k__BackingField)) return;
                TrackWindow(window);
            }
            // Generated interop represents native interfaces as wrapper classes,
            // so GameItem/PixelWindow cannot be implicitly cast to this interface.
            var children = node.TryCast<GraphNodeStorage>()?.children;
            if (children != null)
                for (var i = 0; i < children.Count; i++) TrackGraph(children[i]);
        }

        void TrackWindow(PixelWindow window)
        {
            var root = window?._handler_k__BackingField;
            if (!WorkroomUiCleanup.IsAlive(root)) return;
            shopWindows.Add(window.Pointer);
            Track(root.gameObject);
            controls.Add(window.Pointer, () => window.canMove, value => window.canMove = value,
                () => WorkroomUiCleanup.IsAlive(root) && SameStore());
        }

        // Capture existing window hit targets without Hide/Close or moving items.
        // Leave the common window canvas alive for future workroom inventories.
        var windows = WindowsHandler.current?.visibleWindows;
        if (windows != null)
            for (var i = 0; i < windows.Count; i++)
            {
                var window = windows[i];
                var root = window?._handler_k__BackingField;
                if (AssemblyDebugUi.Alive(root))
                {
                    // Custom workroom panels use their own GameObjects/Canvas,
                    // never native PixelWindows; don't acquire unrelated globals.
                    if (!IsShopWindow(window)) continue;
                    TrackGraph(window);
                }
            }
        // A display-only node need not have a leased hit target. Validate current
        // inventory ownership instead of a count that includes older leases.
        if (!shopInventories.Contains(capturedStore.gridInv.Pointer))
            throw new InvalidOperationException("未找到可隔离的店铺主库存。");
    }

    internal static void SetRegionVisible(bool inside)
    {
        if (!LeaseHeld || !SameStore()) throw new InvalidOperationException("工作间店铺上下文已失效。");
        if (inside == regionIsolated)
        {
            if (inside) MaintainRegionIsolation();
            return;
        }
        if (inside) hitTargets.Suspend();
        else
        {
            hitTargets.Restore();
            ClearStoreHighlights("reveal");
        }
        regionIsolated = inside;
        refreshAt = discoverAt = 0;
        Core.Log?.Msg(inside ? "[工作间遮盖] mode=opaque-overlay; native-visual-leases=none; hit-flags=" + hitTargets.Count :
            "[工作间遮盖] Overlay即将退出；店铺命中已恢复，等待出口输入释放。");
    }

    private static void MaintainRegionIsolation()
    {
        var now = Environment.TickCount64;
        if (now < refreshAt) return;
        refreshAt = now + 250;
        if (now >= discoverAt)
        {
            discoverAt = now + 1000;
            CaptureStoreObjects();
        }
        // Native coroutines may enable a formerly disabled target while away.
        // Remember that requested state for exit, then suspend it again.
        hitTargets.Suspend();
        controls.Suspend();
    }

    private static bool IsShopTransform(Transform target)
    {
        if (!WorkroomUiCleanup.IsAlive(target)) return false;
        for (var parent = target; WorkroomUiCleanup.IsAlive(parent); parent = parent.parent)
            if (shopRoots.Contains(parent.Pointer)) return true;
        return false;
    }

    private static bool IsShopWindow(PixelWindow window, HashSet<IntPtr> seen = null)
    {
        if (window == null || !WorkroomUiCleanup.IsAlive(window._handler_k__BackingField)) return false;
        if (shopWindows.Contains(window.Pointer) || IsShopTransform(window._handler_k__BackingField.transform)) return true;
        seen ??= new HashSet<IntPtr>();
        if (!seen.Add(window.Pointer)) return false;
        var parents = window.parentItems;
        if (parents != null)
            for (var i = 0; i < parents.Count; i++)
                if (IsShopItem(parents[i], seen)) return true;
        return false;
    }

    private static bool IsShopItem(GameItem item, HashSet<IntPtr> seen = null)
    {
        if (item == null) return false;
        var element = item.TryCast<GameItemElement>();
        if (element != null && element.IsDestroyed()) return false;
        if (shopFixtures.Contains(item.Pointer)) return true;
        if (element != null && WorkroomUiCleanup.IsAlive(element.handler) && IsShopTransform(element.handler.transform)) return true;
        seen ??= new HashSet<IntPtr>();
        if (!seen.Add(item.Pointer)) return false;
        var inventory = item.parentInventory;
        if (inventory == null) return false;
        if (shopInventories.Contains(inventory.Pointer)) return true;
        var parent = inventory.parent;
        if (parent == null) return false;
        var parentItem = parent.TryCast<GameItem>();
        if (parentItem != null) return IsShopItem(parentItem, seen);
        return IsShopWindow(parent.TryCast<PixelWindow>(), seen);
    }

    internal static bool AllowShopItem(GameItem item)
    {
        if (!WorkroomTrial.IsolatesStoreRegion) return true;
        try
        {
            if (!IsolationContextReady()) return false;
            if (!IsShopItem(item)) return true;
            NoteBlockedInteraction("item=" + item.identifier);
            return false;
        }
        catch (Exception ex)
        {
            // A failed read does not grant ownership to the unknown object.
            NoteBlockedInteraction("item-read: " + ex.Message);
            return false;
        }
    }

    internal static bool OwnsMousePress(Il2CppSystem.Collections.Generic.ISet<KeyCode> keys)
    {
        if (keys == null || !WorkroomTrial.OwnsPanelMouse) return false;
        var values = keys.Cast<Il2CppSystem.Collections.Generic.ICollection<KeyCode>>();
        for (var key = KeyCode.Mouse0; key <= KeyCode.Mouse6; key++)
            if (values.Contains(key)) return true;
        return false;
    }

    internal static bool AllowShopItemPress(Il2CppSystem.Collections.Generic.ISet<KeyCode> keys)
    {
        // The native manager inlines UpdatePress; concrete handlers must also
        // reject presses owned by our UI while retaining their release tails.
        if (WorkroomTrial.OwnsTextInput || OwnsMousePress(keys)) return false;
        if (!WorkroomTrial.IsolatesStoreRegion) return true;
        try
        {
            if (!IsolationContextReady()) return false;
            return AllowShopItem(RenderHandler.RaycastElement<GameItemElement>(Input.mousePosition, null));
        }
        catch (Exception ex)
        {
            NoteBlockedInteraction("item-hit-read: " + ex.Message);
            return false;
        }
    }

    internal static bool AllowShopInteraction(Component target)
    {
        if (!WorkroomTrial.IsolatesStoreRegion) return true;
        try
        {
            if (!IsolationContextReady()) return false;
            if (!WorkroomUiCleanup.IsAlive(target) || !IsShopTransform(target.transform)) return true;
            NoteBlockedInteraction(target.gameObject.name);
            return false;
        }
        catch (Exception ex)
        {
            NoteBlockedInteraction("target-read: " + ex.Message);
            return false;
        }
    }

    internal static bool AllowShopWindow(PixelWindow window)
    {
        if (!WorkroomTrial.IsolatesStoreRegion) return true;
        try
        {
            if (!IsolationContextReady()) return false;
            var root = window?._handler_k__BackingField;
            if (!IsShopWindow(window)) return true;
            NoteBlockedInteraction(root.gameObject.name);
            return false;
        }
        catch (Exception ex)
        {
            NoteBlockedInteraction("window-read: " + ex.Message);
            return false;
        }
    }

    // Called only within the initiation gates' try/catch. While cleanup still
    // protects the region, a replaced/unknown session must not reopen its input.
    private static bool IsolationContextReady()
    {
        if (SameStore()) return true;
        NoteBlockedInteraction("context-unavailable: 等待店铺上下文恢复或区域释放");
        return false;
    }

    private static void NoteBlockedInteraction(string target)
    {
        if (Environment.TickCount64 < interactionNoticeAt) return;
        interactionNoticeAt = Environment.TickCount64 + 2000;
        Core.Log?.Msg("[工作间隔离] 已拦截店铺交互：" + target);
    }

    internal static void SetSharedInputBlocked(bool blocked)
    {
        var events = capturedEvents ?? EventSystem.current;
        if (!AssemblyDebugUi.Alive(events)) return;
        if (blocked && !ownsNavigation)
        {
            navigationEvents = events;
            previousNavigation = events.sendNavigationEvents;
            ownsNavigation = true;
            events.sendNavigationEvents = false;
            events.SetSelectedGameObject(null);
        }
        else if (!blocked) ReleaseNavigation();
    }

    private static void ReleaseNavigation()
    {
        if (!ownsNavigation) return;
        if (WorkroomUiCleanup.IsAlive(navigationEvents))
        {
            var events = EventSystem.current;
            if (events != null && events.Pointer == navigationEvents.Pointer) events.SetSelectedGameObject(null);
            if (!navigationEvents.sendNavigationEvents) navigationEvents.sendNavigationEvents = previousNavigation;
        }
        ownsNavigation = false;
        navigationEvents = null;
    }

    internal static bool SameEvents(EventSystem events) =>
        (capturedEvents == null || (events != null && events.Pointer == capturedEvents.Pointer)) &&
        (navigationEvents == null || (events != null && events.Pointer == navigationEvents.Pointer));

    internal static void Release()
    {
        var cleanup = new WorkroomUiCleanup();
        cleanup.Run("lease.hitTargets", () =>
        {
            hitTargets.Restore();
            // Preserve the existing exit cleanup without restoring snapshots
            // of highlight Image.enabled or other transient display state.
            ClearStoreHighlights("restore");
        });
        cleanup.Run("lease.controls", controls.Restore);
        cleanup.Run("lease.navigation", ReleaseNavigation);
        cleanup.ThrowIfFailed();
        hitTargets.Clear(); controls.Clear(); shopRoots.Clear();
        shopWindows.Clear(); shopInventories.Clear(); shopFixtures.Clear();
        capturedEvents = null; capturedStore = null; capturedEntry = null; capturedRun = null;
        capturedSlot = 0; regionIsolated = false; refreshAt = discoverAt = 0;
    }
}

[HarmonyPatch(typeof(Arm), nameof(Arm.Update))]
internal static class WorkroomArmPatch
{
    private static bool Prefix() => !WorkroomTrial.BlocksNativeInput;
}

/// <summary>Own only changed flags; retain failed restores for a later cleanup attempt.</summary>
internal sealed class WorkroomFlagLease
{
    private sealed class Flag
    {
        internal Func<bool> Read, Valid;
        internal Action<bool> Write;
        internal bool Original, Held;
    }
    private readonly List<Flag> flags = new();
    private readonly HashSet<(IntPtr, int)> keys = new();
    internal int Count => flags.Count;
    internal bool Held { get { foreach (var flag in flags) if (flag.Held) return true; return false; } }
    internal void Add(IntPtr key, Func<bool> read, Action<bool> write, Func<bool> valid, int property = 0)
    {
        if (!keys.Add((key, property))) return;
        flags.Add(new Flag { Read = read, Write = write, Valid = valid, Original = read() });
    }
    internal void Suspend()
    {
        foreach (var flag in flags)
        {
            if (!flag.Valid() || !flag.Read()) continue;
            // A previously disabled target can be enabled by a native timer.
            // Preserve that new desired state rather than restoring stale false.
            flag.Original = true;
            flag.Held = true; // Set before native mutation: a setter can mutate and then throw.
            flag.Write(false);
        }
    }
    internal void Restore()
    {
        Exception failure = null;
        for (var i = flags.Count - 1; i >= 0; i--)
        {
            var flag = flags[i];
            if (!flag.Held) continue;
            try
            {
                if (flag.Valid() && !flag.Read()) flag.Write(flag.Original);
                flag.Held = false;
            }
            catch (Exception ex) { failure ??= ex; }
        }
        if (failure != null) throw failure;
    }
    internal void Clear() { flags.Clear(); keys.Clear(); }
}

// These are concrete original native methods, independently dispatched by the
// EventSystem. Suppress only targets belonging to the captured shop region.
[HarmonyPatch(typeof(Interactable))]
internal static class WorkroomShopInteractablePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var name in new[] { "OnPointerClick", "HandleClick", "OnClick", "OnDoubleClick" })
            yield return AccessTools.DeclaredMethod(typeof(Interactable), name) ??
                throw new MissingMethodException(typeof(Interactable).FullName, name);
    }
    private static bool Prefix(Interactable __instance) => WorkroomGameAdapter.AllowShopInteraction(__instance);
}

[HarmonyPatch(typeof(InteractableUI), "UnityEngine_EventSystems_IPointerClickHandler_OnPointerClick")]
internal static class WorkroomShopUiInteractablePatch
{
    private static bool Prefix(InteractableUI __instance) => WorkroomGameAdapter.AllowShopInteraction(__instance);
}

[HarmonyPatch(typeof(PixelWindow))]
internal static class WorkroomShopWindowPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.DeclaredMethod(typeof(PixelWindow), "Show", Type.EmptyTypes) ??
            throw new MissingMethodException(typeof(PixelWindow).FullName, "Show()");
        yield return AccessTools.DeclaredMethod(typeof(PixelWindow), "Show",
            new[] { typeof(Vector2), typeof(Vector2), typeof(bool) }) ??
            throw new MissingMethodException(typeof(PixelWindow).FullName, "Show(Vector2, Vector2, bool)");
    }
    private static bool Prefix(PixelWindow __instance, ref PixelWindow __result)
    {
        if (WorkroomGameAdapter.AllowShopWindow(__instance)) return true;
        __result = __instance;
        return false;
    }
}

// HasSelection includes unfinished native band/group gestures. Only Selected (3)
// survives clicks on our storage UI; native dragging still gets its release tail.
[HarmonyPatch(typeof(ItemMultiSelectHandler), nameof(ItemMultiSelectHandler.OnEventPress))]
internal static class WorkroomMultiSelectPressPatch
{
    private static bool Prefix(Il2CppSystem.Collections.Generic.ISet<KeyCode> __0) => !WorkroomTrial.OwnsTextInput && !WorkroomTrial.IsolatesStoreRegion &&
        !WorkroomGameAdapter.OwnsMousePress(__0);
}

[HarmonyPatch(typeof(ItemMultiSelectHandler), nameof(ItemMultiSelectHandler.OnEventRelease))]
internal static class WorkroomMultiSelectReleasePatch
{
    private static bool Prefix(ItemMultiSelectHandler __instance) =>
        (!WorkroomTrial.IsolatesStoreRegion || (int)__instance.state is 1 or 2 or 4 or 5) &&
        !((int)__instance.state == 3 && WorkroomTrial.OwnsPanelMouse);
}

[HarmonyPatch(typeof(ItemMultiSelectHandler), nameof(ItemMultiSelectHandler.OnEventMouseMove))]
internal static class WorkroomMultiSelectMovePatch
{
    private static bool Prefix() => !WorkroomTrial.IsolatesStoreRegion;
}

[HarmonyPatch(typeof(ItemMultiSelectHandler), nameof(ItemMultiSelectHandler.OnEventUpdate))]
internal static class WorkroomMultiSelectUpdatePatch
{
    private static bool Prefix(ItemMultiSelectHandler __instance) =>
        !WorkroomTrial.IsolatesStoreRegion || (int)__instance.state is 1 or 2 or 4 or 5;
}

[HarmonyPatch(typeof(InputActionManager), nameof(InputActionManager.Update))]
internal static class WorkroomInputPatch
{
    private static bool Prefix() => !WorkroomTrial.BlocksSharedInput;
}

// Update calls UpdateKeys for each handler before dispatching press/release.
// Suppress only new native presses while TMP owns text input. Keep key polling,
// released keys and native cleanup running, including the final key-up frame.
[HarmonyPatch(typeof(InputActionManager), nameof(InputActionManager.UpdateKeys))]
internal static class WorkroomStorageTextKeysPatch
{
    private static void Postfix(InputActionManager __instance)
    {
        if (WorkroomTrial.OwnsTextInput && __instance.tempKeysPressed != null)
            __instance.tempKeysPressed.Cast<Il2CppSystem.Collections.Generic.ICollection<KeyCode>>().Clear();
    }
}

[HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.Update))]
internal static class WorkroomStoreUiPatch
{
    private static bool Prefix() => !WorkroomTrial.BlocksNativeInput;
}

[HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.Update))]
internal static class WorkroomStoreTickPatch
{
    private static bool Prefix() => !WorkroomTrial.BlocksNativeInput;
}

[HarmonyPatch(typeof(ToolboxHelper), nameof(ToolboxHelper.HandleHotkeys))]
internal static class WorkroomToolPatch
{
    private static bool Prefix() => !WorkroomTrial.BlocksNativeInput && !WorkroomTrial.OwnsTextInput;
}

[HarmonyPatch(typeof(WindowsHandler), nameof(WindowsHandler.Update))]
internal static class WorkroomWindowsPatch
{
    private static bool Prefix() => !WorkroomTrial.BlocksSharedInput && !WorkroomTrial.OwnsTextInput;
}

[HarmonyPatch(typeof(UIDragHandler))]
internal static class WorkroomDragPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var name in new[] { "OnBeginDrag", "OnDrag", "OnPointerDown" })
            yield return AccessTools.DeclaredMethod(typeof(UIDragHandler), name) ??
                throw new MissingMethodException(typeof(UIDragHandler).FullName, name);
    }
    private static bool Prefix(UIDragHandler __instance, MethodBase __originalMethod) => !WorkroomTrial.BlocksSharedInput &&
        (__originalMethod.Name == "OnDrag" || (!WorkroomTrial.OwnsPanelMouse &&
            WorkroomGameAdapter.AllowShopInteraction(__instance)));
}

[HarmonyPatch(typeof(PlayerStore))]
internal static class WorkroomLoadPatch
{
    private sealed class LoadState
    {
        internal bool Started;
    }
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var name in new[] { "LoadGame", "StartNewGame" })
            yield return AccessTools.DeclaredMethod(typeof(PlayerStore), name) ??
                throw new MissingMethodException(typeof(PlayerStore).FullName, name);
    }
    // Restore the old shop before LoadGame replaces its player/stock context.
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(MethodBase __originalMethod, PlayerStore __instance, out LoadState __state)
    {
        __state = new LoadState();
        // Refusing this load must leave both the live room and its modData intact.
        if (!WorkroomGameAdapter.SaveHooksReady || !WorkroomCashRepair.AllowSessionChange() ||
            !NpcStockOffers.AllowSessionChange()) return false;
        if (__originalMethod.Name == "LoadGame" && !SaveGeneration.PrepareLoad(__instance)) return false;
        __state.Started = true;
        WorkroomStorage.ResetSession();
        WorkroomTrial.OnSceneChanged();
        if (__originalMethod.Name == "StartNewGame") SaveGeneration.BeginNew(__instance);
        return true;
    }

    private static Exception Finalizer(Exception __exception, PlayerStore __instance, LoadState __state)
    {
        if (__exception != null && __state?.Started == true) SaveGeneration.Failed();
        return __exception;
    }
}

[HarmonyPatch(typeof(LetterboxManager), nameof(LetterboxManager.ConvertCanvases))]
internal static class WorkroomLetterboxPatch
{
    private static void Postfix() => WorkroomTrial.EnsureOverlay();
}
