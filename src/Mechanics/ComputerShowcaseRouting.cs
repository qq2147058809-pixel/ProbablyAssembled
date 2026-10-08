using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>仅在原版橱窗类别估值中隔离电脑收购商品；不修改实体、库存或可见分类。</summary>
internal static class ComputerShowcaseRouting
{
    [ThreadStatic] private static int calculationDepth;

    private static bool IsShowcaseContents(Il2CppSystem.Collections.Generic.List<GameItem> items)
    {
        // 原版 childItems 每次返回新 List。确认物品的原生身份及数量，不能比较列表地址。
        var showcaseItems = EmporiumEntry.Instance?.showcaseElement?.childItems;
        if (showcaseItems == null || showcaseItems.Count != items.Count) return false;
        var counts = new Dictionary<IntPtr, int>();
        foreach (var item in showcaseItems)
        {
            var pointer = item == null ? IntPtr.Zero : item.Pointer;
            counts.TryGetValue(pointer, out var count);
            counts[pointer] = count + 1;
        }
        foreach (var item in items)
        {
            var pointer = item == null ? IntPtr.Zero : item.Pointer;
            if (!counts.TryGetValue(pointer, out var count) || count == 0) return false;
            counts[pointer] = count - 1;
        }
        return true;
    }

    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetDisplayCaseType))]
    internal static class ShowcaseCalculationPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(out int __state)
        {
            __state = calculationDepth;
            calculationDepth++;
        }

        // 包括原版异常和嵌套调用，均恢复原先范围，避免影响后续交易估价。
        private static Exception? Finalizer(Exception? __exception, int __state)
        {
            calculationDepth = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.GetTypesByValue))]
    internal static class ShowcaseValueItemsPatch
    {
        private static void Prefix(ref Il2CppSystem.Collections.Generic.List<GameItem> items,
            Il2CppSystem.Collections.Generic.List<string> typesToInclude)
        {
            if (calculationDepth == 0 || items == null || typesToInclude == null) return;
            try
            {
                // 只接管原版橱窗的类别计算；同一调用期间的其他分类估值保持原样。
                if (!IsShowcaseContents(items)) return;
                var filtered = new Il2CppSystem.Collections.Generic.List<GameItem>();
                var excluded = false;
                foreach (var item in items)
                {
                    if (UpperComputerBuyerNpc.IsEligibleShowcaseItem(item)) excluded = true;
                    else filtered.Add(item);
                }
                // 新列表仅作为本次计算参数；其他原版材料和灯牌仍按原版方式参与招客。
                if (excluded) items = filtered;
            }
            catch (Exception ex)
            {
                Core.Log?.Warning("隔离电脑橱窗招客估值失败，保留原版计算：" + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetTypeTotalValue))]
    internal static class ShowcaseCategoryValuePatch
    {
        private static void Postfix(string type, ref int __result)
        {
            try
            {
                var items = EmporiumEntry.Instance?.showcaseElement?.childItems;
                if (items == null) return;
                var result = __result;
                foreach (var item in items)
                {
                    if (item == null || !item.IsGameItemType(type) ||
                        !UpperComputerBuyerNpc.IsEligibleShowcaseItem(item)) continue;
                    // 与原版 GetTypeTotalValue 的 GetCurrentValue 参数及 int 求和一致。
                    // 混放普通材料时，电脑价值也不能把原版来客升级成发明家。
                    result = unchecked(result - (int)item.GetCurrentValue(false, true, true, false, 0));
                }
                __result = result;
            }
            catch (Exception ex)
            {
                Core.Log?.Warning("隔离电脑橱窗类别价值失败，保留原版结果：" + ex.Message);
            }
        }
    }
}
