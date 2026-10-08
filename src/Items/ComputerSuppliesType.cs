using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>九类机箱/配件的附加类型，复用原版物品分类标签的显示路径。</summary>
internal static class ComputerSuppliesType
{
    internal const string Identifier = "PCREPAIR_COMPUTER_SUPPLIES";
    internal const string NameKey = "item.category.computer_supplies";

    [HarmonyPatch(typeof(TypeHelper), nameof(TypeHelper.GetTypeDisplayName))]
    internal static class NamePatch
    {
        private static bool Prefix(string __0, ref string __result)
        {
            if (__0 != Identifier) return true;
            __result = LanguageText.Get(NameKey);
            return false;
        }
    }

    [HarmonyPatch(typeof(TypeHelper), nameof(TypeHelper.GetTypeColor))]
    internal static class ColorPatch
    {
        private static bool Prefix(string __0, ref UnityEngine.Color __result)
        {
            if (__0 != Identifier) return true;
            // 其他原版界面查询类型颜色时也沿用材料颜色，不另设配色。
            __result = TypeHelper.GetTypeColor("MATERIAL");
            return false;
        }
    }
}
