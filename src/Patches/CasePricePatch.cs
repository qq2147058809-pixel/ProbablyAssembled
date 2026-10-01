using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>原版估价和成交前同步整机总价，百分比仅由 Final 阶段计算一次。</summary>
[HarmonyPatch(typeof(GameItem), nameof(GameItem.GetCurrentValue))]
[HarmonyPatch(typeof(GameItem), nameof(GameItem.GetRefreshedValue))]
[HarmonyPatch(typeof(GameItem), nameof(GameItem.GetNegociatedValue))]
[HarmonyPatch(typeof(GameItem), nameof(GameItem.GetValue))]
[HarmonyPatch(typeof(GameItem), nameof(GameItem.OnAddedToWeightedArea))]
internal static class CasePriceSyncPatch
{
    private static readonly HashSet<IntPtr> Syncing = new();

    [HarmonyPriority(Priority.First)]
    private static void Prefix(GameItem __instance)
    {
        if (!ComputerCase.IsCase(__instance) || !Syncing.Add(__instance.Pointer)) return;
        try { CaseInteriorUI.SyncCaseForTrading(__instance); }
        catch (Exception ex) { Core.Log?.Warning("同步整机报价失败：" + ex.Message); }
        finally { Syncing.Remove(__instance.Pointer); }
    }
}

/// <summary>旧式 GetValue 不读 ItemFeature，单独兼容该接口的整机百分比。</summary>
[HarmonyPatch(typeof(GameItem), nameof(GameItem.GetValue))]
internal static class CaseLegacyValuePatch
{
    private static void Postfix(GameItem __instance, ref long __result)
    {
        if (!ComputerCase.IsCase(__instance)) return;
        try
        {
            __result = CaseEconomy.ApplyMachineBonus(__result,
                CaseEconomy.MachineBonusPercent(CaseEconomy.MachineLabel(__instance)));
        }
        catch (Exception ex) { Core.Log?.Warning("计算整机百分比报价失败：" + ex.Message); }
    }
}
