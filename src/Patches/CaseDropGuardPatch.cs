using System;
using HarmonyLib;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>
/// 完全禁止往机箱拖入物品：拖放系统会把机箱当作"可容纳槽位的物品"来询问
/// （MayHaveValidInventorySlot / TryFindOneValidInventorySlot），机箱一律回答"否"，
/// 拖放自然被拒，螺丝刀与配件都不会被吞。拆机螺丝刀由工具 Target 方法补丁解锁。
/// 面板内的拖放走窗口 GameSlotInventory 自己的同名方法，不受影响；
/// 双击插入走 CaseInteriorUI.TryInsertComponent，也不经过这两个方法；机箱之间也禁止嵌套，
/// 否则破损机箱会被当作内部容器塞进完好机箱，导致外壳被原版改成储藏区贴图并丢失锁定/修复交互。
/// </summary>
[HarmonyPatch(typeof(GameItem), nameof(GameItem.MayHaveValidInventorySlot))]
internal static class CaseRejectDropMayPatch
{
    private static bool Prefix(GameItem __instance, ref bool __result)
    {
        if (!ComputerCase.IsCase(__instance)) return true;
        // Tools use MayTarget/CanTarget. Reporting an insertion slot here would
        // show the misleading red "cannot insert" tooltip for a screwdriver.
        __result = false;
        return false;
    }
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
