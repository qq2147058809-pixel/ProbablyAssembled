using System;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>独立的八页文档；创建/读档只恢复物品，阅读才接入场景手册。</summary>
internal static class ComputerManual
{
    internal const string Id = "pcrepair.computer_build_guide";
    internal const string SpriteKey = "pcrepair.computer_build_guide_sprite";

    internal static GameItem Create()
    {
        var item = DirectoryMaster.Item("water_guide", false);
        if (item == null) throw new InvalidOperationException("找不到原版手册模板：water_guide");
        Apply(item);
        item.onLoaded = (Il2CppSystem.Action<GameItem>)(loaded => Apply(loaded));
        return item;
    }

    private static void Apply(GameItem item)
    {
        item.identifier = Id;
        var name = LanguageText.Get("manual.item.name");
        item.identifierName = name;
        item.SetName(name);
        item.shortDescription = LanguageText.Get("manual.item.description");
        item.longDescription = item.shortDescription;
        item.flavorText = string.Empty;
        item.unitCount = 1;
        item.SetValue(25);
        item.SetShape(new GridShapeBuilder(2, 3).SetDataFill(1).Build());
        item.spritePath = SpriteKey;
        item.spriteAtlasPath = string.Empty;
        item.spriteChanged = true;
        item.RemoveAllGameItemType();
        item.SetGameItemType("DOCUMENT");
        item.EnableTag("IMPORTANT_TAG", false);
        item.EnableTag("GUIDE_PAPER_TAG", false);
        // Keep water_guide's native CanRead callbacks. Native May/Can aggregate
        // these through DynamicInvoke, which cannot invoke managed trampolines.
        // Activation is routed below; remove only the template's water UI action.
        item.onActivateSlotItemFunc = null;
        BookHelper.InitGuide(item);
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.ActivateSlotItem))]
    internal static class ActivatePatch
    {
        private static bool Prefix(GameItem __instance)
        {
            if (__instance.identifier != Id) return true;
            try
            {
                // Native checks retain ownership exceptions, inventory vetoes,
                // forceDisableActivate and the template's reading qualification.
                if (!__instance.MayActivateSlotItem() || !__instance.CanActivateSlotItem())
                    return false;
                ComputerManualUI.Open();
                // The item action is null. Continue native inventory callbacks,
                // book activation audio and inventory-state refresh.
                return true;
            }
            catch (Exception ex)
            {
                Core.Log?.Error("电脑装机交易手册互动失败：" + ex);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(ModItemDirectory), "InitDirectory")]
    internal static class DirectoryPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ModItemDirectory __instance)
        {
            try { __instance.Set(Id, (Il2CppSystem.Func<GameItem>)(Func<GameItem>)Create); }
            catch (Exception ex) { Core.Log?.Error("注册电脑装机交易手册失败：" + ex); }
        }
    }
}
