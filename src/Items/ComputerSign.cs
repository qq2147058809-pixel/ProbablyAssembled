using System;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>博士出售的 2×2 电脑灯牌，使用原版橱窗灯牌标签。</summary>
internal static class ComputerSign
{
    internal const string Id = "pcrepair.sign_computer";
    internal const string SpriteKey = "pcrepair.sign_computer_sprite";
    internal const string ShowcaseType = "PCREPAIR_COMPUTER_MATERIAL";
    internal const int SellerInviteChance = 75;
    internal const int ThiefInviteChance = 40;

    internal static GameItem Create()
    {
        var item = DirectoryMaster.Item("sign_food", true);
        if (item == null) throw new InvalidOperationException("找不到原版灯牌模板：sign_food");
        Apply(item);
        item.onLoaded = (Il2CppSystem.Action<GameItem>)(loaded => Apply(loaded));
        return item;
    }

    private static void Apply(GameItem item)
    {
        item.identifier = Id;
        var name = LanguageText.Get("text.224f4a634028");
        item.identifierName = name;
        item.SetName(name);
        item.shortDescription = LanguageText.Get("text.01fc8dc622e5");
        item.longDescription = item.shortDescription;
        item.flavorText = LanguageText.Get("text.2fe62616a758");
        item.unitCount = 1;
        item.SetValue(100);
        // 用户提供的 256×256 PNG 原样嵌入；占格独立定义，不能按贴图像素推算。
        item.SetShape(new GridShapeBuilder(2, 2).SetDataFill(1).Build());
        item.spritePath = SpriteKey;
        item.spriteAtlasPath = string.Empty;
        item.spriteChanged = true;
        ShowcaseHelper.InitShowcaseSpecialItem(item, ShowcaseType);
    }

    [HarmonyPatch(typeof(ModItemDirectory), "InitDirectory")]
    internal static class DirectoryPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ModItemDirectory __instance)
        {
            try
            {
                __instance.Set(Id, (Il2CppSystem.Func<GameItem>)(Func<GameItem>)Create);
                Core.Log?.Msg("已注册 2×2 电脑灯牌。");
            }
            catch (Exception ex) { Core.Log?.Error("注册电脑灯牌失败：" + ex); }
        }
    }

    // 原版博士库存的官方 Late 扩展点；拥有灯牌时不再出售，和其他灯牌一致。
    [HarmonyPatch(typeof(ModHook), nameof(ModHook.FireOnPlaceInventorInventoryItemLate))]
    internal static class DoctorStockPatch
    {
        private static void Postfix(Il2CppSystem.Collections.Generic.List<GameItem> __0)
        {
            try
            {
                if (__0 != null)
                    foreach (var owned in __0)
                        if (owned != null && Core.Clean(owned.identifier) == Id) return;
                var offers = EmporiumEntry.Instance?.GetAllNonOwnedItem();
                if (offers != null)
                    foreach (var offer in offers)
                        if (offer != null && Core.Clean(offer.identifier) == Id) return;
                var store = PlayerStore.Instance;
                if (store == null) return;
                store.AddDirectSellingItemToTable(DirectoryMaster.Item(Id, true), false, false, false, 0);
            }
            catch (Exception ex) { Core.Log?.Error("博士上柜电脑灯牌失败：" + ex); }
        }
    }

    [HarmonyPatch(typeof(TypeHelper), nameof(TypeHelper.GetTypeDisplayName))]
    internal static class ShowcaseNamePatch
    {
        private static bool Prefix(string __0, ref string __result)
        {
            if (Core.Clean(__0) != ShowcaseType) return true;
            __result = LanguageText.Get("text.1ec95543c328");
            return false;
        }
    }

    [HarmonyPatch(typeof(TypeHelper), nameof(TypeHelper.GetTypeColor))]
    internal static class ShowcaseColorPatch
    {
        private static bool Prefix(string __0, ref UnityEngine.Color __result)
        {
            if (Core.Clean(__0) != ShowcaseType) return true;
            __result = new UnityEngine.Color(0.34f, 0.65f, 0.34f, 1f);
            return false;
        }
    }
}
