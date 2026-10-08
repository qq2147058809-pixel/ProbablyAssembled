using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>本 Mod 高端配件分类及固定市场加价，散件与整机共用。</summary>
internal static class HighEndParts
{
    internal const string Identifier = "PCREPAIR_HIGH_END_PARTS";
    internal const string FeatureId = "pcrepair.high_end_markup";
    internal const int MarkupPercent = 20;

    private static bool IsHighEndComputer(GameItem item) =>
        item != null && item.IsGameItemType(Identifier) &&
        (Components.Find(Core.Clean(item.identifier)) != null || ComputerCase.IsCase(item));

    internal static void Apply(GameItem item, bool eligible)
    {
        // 原版上秤和报价仍会引入零售词条；固定市场加价不可叠加。
        item.RemoveFeatureByCategory("retailMarkUp");
        item.RemoveItemFeatureByID("retailMarkUp");
        if (!eligible)
        {
            if (item.IsGameItemType(Identifier)) item.RemoveGameItemType(Identifier);
            item.RemoveItemFeatureByID(FeatureId);
            return;
        }

        if (!item.IsGameItemType(Identifier)) item.SetGameItemType(Identifier);
        var feature = item.FindItemFeatureByID(FeatureId);
        if (feature == null)
        {
            // 原版工厂初始化交易界面需要的字段，避免手工 new ItemFeature 的格式化异常。
            feature = ItemFeatureList.Discount(MarkupPercent);
            feature.identifier = FeatureId;
            feature.category = FeatureId;
            item.AddItemFeature(feature);
        }
        feature.featureType = ItemFeature.FeatureType.Normale;
        feature.valueStage = ItemFeature.ValueStage.Market;
        feature.initiallyShown = true;
        feature.isPublicHidden = false;
        feature.isExposable = false;
        feature.isFeatureExposed = true;
        feature.SetValueModifier(MarkupPercent);
        var display = LanguageText.Get("item.feature.high_end_markup");
        feature.SetPublicDisplay(display);
        feature.SetActualDisplay(display);
    }

    [HarmonyPatch(typeof(TypeHelper), nameof(TypeHelper.GetTypeDisplayName))]
    internal static class NamePatch
    {
        private static bool Prefix(string __0, ref string __result)
        {
            if (__0 != Identifier) return true;
            __result = LanguageText.Get("item.category.high_end_parts");
            return false;
        }
    }

    [HarmonyPatch(typeof(TypeHelper), nameof(TypeHelper.GetTypeColor))]
    internal static class ColorPatch
    {
        private static bool Prefix(string __0, ref UnityEngine.Color __result)
        {
            if (__0 != Identifier) return true;
            __result = TypeHelper.GetTypeColor("LUXURY_ITEM");
            return false;
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.OnAddedToWeightedArea))]
    internal static class WeightedAreaPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(GameItem __instance)
        {
            if (!IsHighEndComputer(__instance)) return;
            // 原版上称可能重新添加零售词条；高端电脑只保留固定20%，不叠加旧的零售机制。
            Apply(__instance, true);
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetCurrentValue))]
    internal static class CurrentValuePatch
    {
        private static void Prefix(GameItem __instance, ref bool useRetailMarkup, ref bool forceMarkup)
        {
            if (!IsHighEndComputer(__instance)) return;
            // 原版没有retailMarkUp词条时会在估价中隐式补零售倍率；固定20%必须同时关闭此路径。
            useRetailMarkup = false;
            forceMarkup = false;
            Apply(__instance, true);
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetNegociatedValue))]
    internal static class NegotiatedValuePatch
    {
        private static void Prefix(GameItem __instance)
        {
            if (IsHighEndComputer(__instance)) Apply(__instance, true);
        }
    }
}
