using System;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PCExpansion;

// The manager gate precedes its per-handler key filtering and native drag changes.
// Stable frames only check the E/mouse gesture; inventory is read on that click.
internal static class WorkroomQuickTransfer
{
    private static int checkedFrame = -1;
    private static bool consumed;

    internal static void BeforeNativeInput()
    {
        if (!Input.GetKey(KeyCode.E) || !Input.GetMouseButtonDown(0)) return;
        try
        {
            if (WorkroomTrial.ShopEntryVisible) HandleShopPress();
        }
        catch (Exception ex) { Core.Log?.Warning("[工作间快捷入口] " + ex); }
    }

    internal static bool TryReturn(string id, long epoch)
    {
        if (!Input.GetKey(KeyCode.E) || !Input.GetMouseButtonDown(0)) return false;
        if (!WorkroomGameAdapter.InventoryTransferAllowed()) return false;
        if (checkedFrame == Time.frameCount && consumed) return true;
        if (!GestureReady() || !WorkroomGameAdapter.InventoryTransferAllowed()) return false;
        checkedFrame = Time.frameCount; consumed = true;
        WorkroomStorageDragPatch.MarkConsumed();
        try { WorkroomSelection.ReturnToShop(id, epoch); WorkroomTrial.NotifyStorage(); }
        catch (Exception ex) { Core.Log?.Warning("[工作间快捷归还] " + ex); WorkroomStorage.Notify("failed"); }
        return true;
    }

    private static bool GestureReady()
    {
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
            Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) ||
            Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ||
            Input.compositionString.Length != 0 || WorkroomTrial.OwnsTextInput ||
            RoomAudioSettingsUi.BlocksInput || Time.timeScale <= 0 || Interactable.isPaused ||
            (ItemMouseDragHandler.current?.isDragging ?? false)) return false;
        var selected = EventSystem.current?.currentSelectedGameObject;
        return selected == null || selected.GetComponentInParent<TMP_InputField>() == null &&
            selected.GetComponentInParent<InputField>() == null;
    }

    internal static bool AllowPress(Il2CppSystem.Collections.Generic.ISet<KeyCode> keys)
    {
        if (!Input.GetKey(KeyCode.E)) return true;
        if (keys == null || !Input.GetMouseButtonDown(0) ||
            !keys.Cast<Il2CppSystem.Collections.Generic.ICollection<KeyCode>>().Contains(KeyCode.Mouse0)) return true;
        return HandleShopPress();
    }

    private static bool HandleShopPress()
    {
        // Native OnEventPress also enters here directly, without the manager's
        // BeforeNativeInput check. Forbidden scenes retain their native input.
        if (!WorkroomTrial.ShopEntryVisible || !WorkroomGameAdapter.InventoryTransferAllowed()) return true;
        if (checkedFrame == Time.frameCount) return !consumed;
        checkedFrame = Time.frameCount; consumed = false;
        if (!GestureReady() || WorkroomTrial.OwnsPanelMouse || WorkroomTrial.IsolatesStoreRegion) return true;
        try
        {
            var events = EventSystem.current;
            if (events != null)
            {
                var selected = events.currentSelectedGameObject;
                if (selected != null && (selected.GetComponentInParent<TMP_InputField>() != null ||
                    selected.GetComponentInParent<InputField>() != null)) return true;
            }
            var item = RenderHandler.RaycastElement<GameItemElement>(Input.mousePosition, null);
            if (item == null || !WorkroomSelection.CanTransfer(item)) return true;
            var hit = WorkroomNativeTooltip.PointerTarget();
            // Native inventory artwork can itself be a UI raycast target. Only
            // an unrelated foreground surface blocks this item gesture.
            if (hit != null && (item.rectTransform == null ||
                !hit.transform.IsChildOf(item.rectTransform) && !item.rectTransform.IsChildOf(hit.transform))) return true;
            if (!WorkroomTrial.ShopEntryVisible || !WorkroomGameAdapter.InventoryTransferAllowed()) return true;
            // Consume the gesture for every native handler, including a rejected full-room attempt.
            // Native release/reset handlers still run and E alone retains its original meaning.
            consumed = true;
            WorkroomStorageDragPatch.MarkConsumed();
            WorkroomSelection.TransferToRoom(item, WorkroomStorage.State.Epoch);
            WorkroomTrial.NotifyStorage();
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("[工作间快捷转移] " + ex);
            if (consumed) WorkroomStorage.Notify("failed");
        }
        return !consumed;
    }
}
