using System;
using HarmonyLib;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>让自建机箱窗口跟随原版出售、存档和重载生命周期。</summary>
[HarmonyPatch(typeof(GameItem), nameof(GameItem.CloseContentWindow))]
internal static class CaseNativeClosePatch
{
    private static void Prefix(GameItem __instance)
    {
        if (!ComputerCase.IsCase(__instance)) return;
        try { CaseInteriorUI.CloseAndForgetCase(__instance); }
        catch (Exception ex) { Core.Log?.Error("关闭机箱交易窗口失败：" + ex); }
    }
}

[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.SaveGame))]
internal static class CaseBeforeSavePatch
{
    private static void Prefix()
    {
        try { CaseInteriorUI.SyncAllCasesBeforeSave(); }
        catch (Exception ex) { Core.Log?.Error("存档前同步机箱失败：" + ex); }
    }
}

[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.LoadGame))]
[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.StartNewGame))]
internal static class CaseSessionResetPatch
{
    private static void Prefix()
    {
        try { CaseInteriorUI.DiscardWindowCache(); }
        catch (Exception ex) { Core.Log?.Error("清理旧存档机箱窗口失败：" + ex); }
    }
}
