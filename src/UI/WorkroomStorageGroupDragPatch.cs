using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace PCExpansion;

/// <summary>多选的结束入口独立于单件拖动；只接受对应鼠标键的真实释放。</summary>
[HarmonyPatch(typeof(ItemMultiSelectHandler), nameof(ItemMultiSelectHandler.OnEventRelease))]
internal static class WorkroomStorageGroupReleasePatch
{
    [ThreadStatic] internal static IntPtr Handler;
    [HarmonyPriority(Priority.First)]
    private static void Prefix(ItemMultiSelectHandler __instance,
        Il2CppSystem.Collections.Generic.ISet<KeyCode> __0, out IntPtr __state)
    {
        __state = Handler;
        var key = __instance.key;
        Handler = key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6 && __0 != null &&
            __0.Cast<Il2CppSystem.Collections.Generic.ICollection<KeyCode>>().Contains(key)
            ? __instance.Pointer : IntPtr.Zero;
    }

    private static Exception? Finalizer(Exception? __exception, IntPtr __state)
    { Handler = __state; return __exception; }
}

[HarmonyPatch(typeof(ItemMultiSelectHandler), nameof(ItemMultiSelectHandler.EndGroupDrag))]
internal static class WorkroomStorageGroupDragPatch
{
    [ThreadStatic] private static IntPtr handling;

    // A successful reset must release every native reference before Store can
    // destroy the first wrapper. Null collections are an unknown reset result.
    internal static bool ResetComplete(ItemMultiSelectHandler handler) => (int)handler.state == 0 &&
        handler.selectedItems != null && handler.selectedItems.Count == 0 &&
        handler.selectedHomes != null && handler.selectedHomes.Count == 0 &&
        handler.dragOffsets != null && handler.dragOffsets.Count == 0 &&
        handler.selectionHighlights != null && handler.selectionHighlights.Count == 0 &&
        handler.hoverInventory == null && handler.hoverItem == null &&
        handler.hoverInventoryNodes != null && handler.hoverInventoryNodes.Count == 0 &&
        handler.hoverHighlightNodes != null && handler.hoverHighlightNodes.Count == 0 &&
        !handler.precisePlacementActive && handler.precisePreviewNodes != null && handler.precisePreviewNodes.Count == 0 &&
        handler.bandWindow == null && handler.bandInventory == null;

    private static bool Prefix(ItemMultiSelectHandler __instance)
    {
        if (handling == __instance.Pointer) return false;
        if ((int)__instance.state != 5) return true;
        var point = Input.mousePosition;
        var accepts = WorkroomTrial.HitShopStorage(point);
        if (!accepts && !WorkroomTrial.HitShopStorageWindow(point)) return true;
        var release = WorkroomStorageGroupReleasePatch.Handler == __instance.Pointer && Input.GetKeyUp(__instance.key);
        var epoch = WorkroomStorage.State.Epoch;
        var candidates = new List<WorkroomStorage.NativeDrop>();
        var complete = true;
        WorkroomStorageDragPatch.MarkConsumed();
        handling = __instance.Pointer;
        try
        {
            // Snapshot the entire command before resetting. A partial/unknown
            // snapshot only restores the gesture and never consumes an item.
            if (accepts && release)
            {
                try
                {
                    var seen = new HashSet<IntPtr>();
                    foreach (var item in __instance.selectedItems)
                    {
                        if (item == null) throw new InvalidOperationException("群拖选中引用缺失，禁止收纳。");
                        if (!seen.Add(item.Pointer)) continue;
                        if (!__instance.selectedHomes.TryGetValue(item, out var home) || home == null ||
                            !WorkroomStorage.Contains(home, item))
                            throw new InvalidOperationException("群拖来源无法完整确认，禁止收纳。");
                        candidates.Add(new WorkroomStorage.NativeDrop(item, home));
                    }
                }
                catch (Exception ex)
                { complete = false; Core.Log?.Error("群拖快照未完成，仅恢复原版手势：" + ex); }
            }
            // OnEventReset restores live ghosts first, then clears highlights,
            // homes/offsets and drop targets, and finally returns state to Idle.
            __instance.OnEventReset();
            if (!ResetComplete(__instance))
                throw new InvalidOperationException("群拖重置未完整释放原版引用，禁止收纳。");
            if (accepts && release && complete)
                WorkroomStorage.StoreShopGroup(candidates, epoch, __instance.Pointer);
            else if (!complete) WorkroomStorage.Notify("source_changed");
        }
        catch (Exception ex)
        {
            // Do not force ClearSelection/state after a failed native reset.
            // The remaining references keep their native owner for retry.
            Core.Log?.Error("群拖入箱中止；未交接物品由原版来源保管：" + ex);
            WorkroomStorage.Notify("recovery");
        }
        finally { handling = IntPtr.Zero; }
        WorkroomTrial.NotifyStorage();
        // Header/cancel/refusal must not run the old inventory placement tail.
        return false;
    }
}
