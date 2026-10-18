using System;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Keep native close handling without sorting a panel inside a disabled canvas.</summary>
[HarmonyPatch(typeof(StoreUI), nameof(StoreUI.OnDisable))]
internal static class ReputationPanelDisablePatch
{
    private static void Prefix(StoreUI __instance, out bool __state)
    {
        __state = false;
        if (!__instance.isPaperUI || __instance.gameObject.name != "_ReputationPanel") return;
        var parent = __instance.transform.parent;
        while (parent != null && parent.gameObject.name != "CanvasPaperUI") parent = parent.parent;
        if (parent == null || parent.gameObject.activeInHierarchy) return;
        // OnDisable uses this flag only for SetAsFirstSibling. Unity forbids
        // that operation while the parent is being deactivated. Preserve the
        // remainder of the native method, including StoreUIManager.CloseUI.
        __state = true;
        __instance.isPaperUI = false;
    }

    private static Exception? Finalizer(StoreUI __instance, bool __state, Exception? __exception)
    {
        try { if (__state && __instance != null) __instance.isPaperUI = true; }
        catch (Exception ex) { Core.Log?.Warning("声望面板关闭后恢复标记失败：" + ex.Message); }
        return __exception;
    }
}
