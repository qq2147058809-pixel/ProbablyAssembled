using System;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace PCExpansion;

/// <summary>只过滤子窗口自身的按下和滚轮；原版释放、移动和更新继续运行。</summary>
[HarmonyPatch(typeof(InputActionHandler), nameof(InputActionHandler.UpdatePress))]
internal static class WorkroomPanelPressPatch
{
    private static void Prefix(ref Il2CppSystem.Collections.Generic.ISet<KeyCode> __0)
    {
        var mouse = WorkroomTrial.OwnsPanelMouse;
        var escape = WorkroomTrial.OwnsPanelEscape;
        if ((!mouse && !escape) || __0 == null) return;
        var keys = __0.Cast<Il2CppSystem.Collections.Generic.ICollection<KeyCode>>();
        if (keys.Count == 0) return;
        var filtered = new Il2CppSystem.Collections.Generic.HashSet<KeyCode>();
        var iterator = __0.Cast<Il2CppSystem.Collections.Generic.IEnumerable<KeyCode>>().GetEnumerator();
        try
        {
            var walker = iterator.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (walker.MoveNext())
            {
                var key = iterator.Current;
                if (mouse && key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6) continue;
                if (escape && key == KeyCode.Escape) continue;
                filtered.Add(key);
            }
        }
        finally { iterator.TryCast<Il2CppSystem.IDisposable>()?.Dispose(); }
        // Never mutate the manager's set: all handlers share it in this frame.
        __0 = filtered.Cast<Il2CppSystem.Collections.Generic.ISet<KeyCode>>();
    }
}

[HarmonyPatch(typeof(InputActionHandler), nameof(InputActionHandler.UpdateMouseScroll))]
internal static class WorkroomPanelScrollPatch
{
    private static bool Prefix() => !WorkroomTrial.OwnsPanelScroll;
}

/// <summary>EndDrag也会被取消/reset调用；仅在真实鼠标释放作用域中存入。</summary>
[HarmonyPatch(typeof(ItemMouseDragHandler), nameof(ItemMouseDragHandler.OnEventRelease))]
internal static class WorkroomStorageReleasePatch
{
    [ThreadStatic] internal static IntPtr Handler;
    private static void Prefix(ItemMouseDragHandler __instance,
        Il2CppSystem.Collections.Generic.ISet<KeyCode> __0, out IntPtr __state)
    {
        __state = Handler;
        var key = __instance.lastMouseClickedKey;
        Handler = key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6 && __0 != null &&
            __0.Cast<Il2CppSystem.Collections.Generic.ICollection<KeyCode>>().Contains(key)
            ? __instance.Pointer : IntPtr.Zero;
    }
    private static Exception? Finalizer(Exception? __exception, IntPtr __state)
    { Handler = __state; return __exception; }
}

[HarmonyPatch(typeof(ItemMouseDragHandler), nameof(ItemMouseDragHandler.EndDrag))]
internal static class WorkroomStorageDragPatch
{
    [ThreadStatic] private static IntPtr handling;
    internal static int ConsumedFrame { get; private set; } = -1;
    internal static void MarkConsumed() => ConsumedFrame = Time.frameCount;

    private static bool Prefix(ItemMouseDragHandler __instance, out bool __state)
    {
        __state = false;
        if (handling == __instance.Pointer) return false;
        var item = __instance.currentItem;
        if (!__instance.isDragging || item == null) return true;
        var target = WorkroomTrial.HitShopStorage(Input.mousePosition);
        if (!target && !WorkroomTrial.HitShopStorageWindow(Input.mousePosition)) return true;
        ClearTarget(__instance);
        if (!target)
        {
            // Native EndDrag restores the item before we report the rejected
            // title/filter-area drop. Never interrupt its restoration tail.
            __state = WorkroomStorageReleasePatch.Handler == __instance.Pointer &&
                Input.GetKeyUp(__instance.lastMouseClickedKey);
            return true;
        }
        // Cancellation over our window must restore the source, never store it.
        if (WorkroomStorageReleasePatch.Handler != __instance.Pointer ||
            !Input.GetKeyUp(__instance.lastMouseClickedKey)) return true;

        MarkConsumed();
        var source = item.parentInventory;
        var count = item.unitCount;
        var original = true;
        handling = __instance.Pointer;
        try
        {
            if (WorkroomStorage.Store(item)) original = false;
            // A callback can mutate before throwing. Only a proven unchanged
            // source may use native EndDrag's no-target restoration tail.
            if (original)
                original = !item.IsDestroyed() && item.unitCount == count && WorkroomStorage.Contains(source, item);
        }
        catch (Exception ex)
        {
            Core.Log?.Error("储物拖放异常，回读来源并停止落位：" + ex);
            WorkroomStorage.Notify("failed");
            try { original = !item.IsDestroyed() && item.unitCount == count && WorkroomStorage.Contains(source, item); }
            catch { original = false; }
        }
        finally
        {
            try
            {
                if (!original) Stop(__instance, item);
                ClearTarget(__instance);
            }
            finally { handling = IntPtr.Zero; }
        }
        WorkroomTrial.NotifyStorage();
        return original;
    }

    private static void Postfix(bool __state)
    {
        if (__state) WorkroomStorage.Notify("invalid_position");
    }

    private static void ClearTarget(ItemMouseDragHandler drag)
    {
        // The native target preview belongs to this drag handler. Drop over
        // our window must remove its previous inventory placement highlight.
        try { drag.highlightManager?.Clear(); }
        catch (Exception ex) { Core.Log?.Warning("储物拖放落点预览清理失败：" + ex); }
        drag.lastItem = null; drag.lastInventory = null; drag.lastSlot = null;
    }

    private static void Stop(ItemMouseDragHandler drag, GameItemElement item)
    {
        // AbortDrag alone omits live fake-background restoration. A detached
        // candidate belongs to the storage cleanup transaction, not to EndDrag.
        try
        {
            if (!item.IsDestroyed()) item.ToggleFakeBackground(false, 1, 1);
        }
        catch (Exception ex) { Core.Log?.Warning("储物拖放背景清理待检查：" + ex.Message); }
        drag.isDragging = false;
        try { drag.AbortDrag(); }
        catch (Exception ex) { Core.Log?.Warning("储物拖放手势清理异常：" + ex.Message); }
        finally
        {
            drag.isDragging = false;
            ClearTarget(drag);
            drag.currentItem = null;
            drag.lastMouseClickedKey = KeyCode.None;
        }
    }
}
