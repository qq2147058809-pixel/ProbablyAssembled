using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>
/// 机箱拒绝原版容器落位；配件安装统一使用工作间装配面板。
/// 避免原版吞入配件、嵌套机箱或把机箱外观改成储藏区。
/// </summary>
[HarmonyPatch(typeof(GameItem), nameof(GameItem.MayHaveValidInventorySlot))]
internal static class CaseRejectDropMayPatch
{
    private static bool Prefix(GameItem __instance, ref bool __result)
    {
        if (!ComputerCase.IsCase(__instance)) return true;
        __result = false;
        return false;
    }
}

/// <summary>阻止原版螺丝刀绕过工作间容量规划开封或修复本 Mod 机箱。</summary>
[HarmonyPatch(typeof(MachineBrokenHelper), nameof(MachineBrokenHelper.OnScrewdriverUsed))]
internal static class CaseNativeRepairGuardPatch
{
    private static bool Prefix(GameItem machine) => !ComputerCase.IsCase(machine);
}

[HarmonyPatch(typeof(GameItem), nameof(GameItem.TryFindOneValidInventorySlot))]
internal static class CaseRejectDropFindPatch
{
    private static bool Prefix(GameItem __instance)
    {
        if (!ComputerCase.IsCase(__instance)) return true;
        return false;
    }
}
