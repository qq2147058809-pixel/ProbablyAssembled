using System;
using HarmonyLib;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>
/// 拆机螺丝刀：本 MOD 自有工具，唯一功能是解锁物资箱（损坏机箱）。
/// - 拖到破损机箱上：直接接管原版工具目标方法，解锁并显示“可解锁”；
/// - 双击：解锁自己拥有的一个上锁机箱。
/// 克隆原版螺丝刀继承工具属性，但贴图、名称、用途均为自定义；不带任何原版修复逻辑。
/// </summary>
internal static class UnboxTool
{
    internal const string Id = "pcrepair.tool_screwdriver";
    internal const string SpriteKey = "pcrepair.unbox_tool_sprite";
    internal const string ToolTag = "PCREPAIR_UNBOX_TOOL";

    internal static GameItem Create()
    {
        // 克隆原版螺丝刀继承工具类型与使用手感
        var item = DirectoryMaster.Item("screwdriver", true);
        if (item == null) throw new InvalidOperationException("找不到原版物品模板：screwdriver");
        item.identifier = Id;
        Apply(item);
        item.onLoaded = (Il2CppSystem.Action<GameItem>)(loaded => Apply(loaded));
        return item;
    }

    private static void Apply(GameItem item)
    {
        var name = LanguageText.Get("拆机螺丝刀", "Teardown Screwdriver");
        item.identifierName = name;
        item.SetName(name);
        item.shortDescription = LanguageText.Get(
            "撬开上锁的破损机箱，取出里面的电脑配件。拖到机箱上松开，或双击使用。",
            "Pry open a locked, damaged PC case and salvage its parts. Drop it onto a case or double-click to use.");
        item.longDescription = item.shortDescription;
        item.flavorText = LanguageText.Get(
            "拆报废电脑的老伙计：防滑握柄，加长钢杆，专治卡死机箱。",
            "A scrapyard favorite: grippy handle, reinforced shaft, and a talent for stubborn cases.");
        item.unitCount = 1;
        item.SetValue(10);
        item.SetShape(new GridShapeBuilder(1, 3).SetDataFill(1).Build());
        item.spritePath = SpriteKey;
        item.spriteAtlasPath = string.Empty;
        item.spriteChanged = true;
        item.RemoveAllGameItemType();
        item.SetGameItemType("TOOL");
        item.EnableTag(ToolTag, false);
        // MayTarget/CanTarget invoke these delegates through native DynamicInvoke.
        // Il2CppInterop-generated managed delegates fail that reflection path with
        // TargetException. Route this tool through direct Harmony prefixes instead,
        // as ComponentRepair does, and clear callbacks on creation and load.
        item.mayThisTargetItemFunc = null;
        item.canThisTargetItemFunc = null;
        item.onThisTargetItemFunc = null;
    }

    internal static bool CanUnlock(GameItem? source, GameItem? target) =>
        IsUnboxTool(source) && ComputerCase.IsCase(target) &&
        target!.IsTag(CaseUnboxing.LockedTagName) &&
        PlayerItemAccess.IsOwned(source) && PlayerItemAccess.IsOwned(target);

    internal static bool IsUnboxTool(GameItem? item) =>
        item != null && item.IsTag(ToolTag);

    /// <summary>拖拽系统当前拖着的物品（非拖拽状态返回 null）。</summary>
    internal static GameItem? GetDraggedItem()
    {
        try
        {
            var handler = ItemMouseDragHandler.current;
            return handler == null ? null : handler.currentItem;
        }
        catch
        {
            return null;
        }
    }

    [HarmonyPatch(typeof(ModItemDirectory), "InitDirectory")]
    internal static class UnboxToolDirectoryPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ModItemDirectory __instance)
        {
            try
            {
                Il2CppSystem.Func<GameItem> factory = (Func<GameItem>)(() => Create());
                __instance.Set(Id, factory);
            }
            catch (Exception ex)
            {
                Core.Log?.Error("注册拆机螺丝刀失败：" + ex);
            }
        }
    }

    [HarmonyPatch(typeof(ItemBehaviourManager), nameof(ItemBehaviourManager.Target))]
    internal static class NativeBehaviourGuardPatch
    {
        private static bool Prefix(GameItem __0, GameItem __1) =>
            !IsUnboxTool(__0);
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.MayTarget))]
    internal static class UnlockMayTargetPatch
    {
        private static bool Prefix(GameItem __instance, GameItem __0, ref bool __result)
        {
            if (!IsUnboxTool(__instance)) return true;
            __result = CanUnlock(__instance, __0);
            return false;
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.CanTarget))]
    internal static class UnlockCanTargetPatch
    {
        private static bool Prefix(GameItem __instance, GameItem __0, ref bool __result)
        {
            if (!IsUnboxTool(__instance)) return true;
            __result = CanUnlock(__instance, __0);
            return false;
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.Target))]
    internal static class UnlockTargetPatch
    {
        private static bool Prefix(GameItem __instance, GameItem __0)
        {
            if (!IsUnboxTool(__instance)) return true;
            try
            {
                if (CanUnlock(__instance, __0)) CaseUnboxing.UnlockByScrewdriver(__0);
            }
            catch (Exception ex)
            {
                Core.Log?.Error("拆机螺丝刀拖放解锁失败：" + ex);
                CaseUnboxing.Notify(LanguageText.Get("解锁失败，请检查日志", "Unlock failed. Check the game log."));
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(RichTextGenerator), nameof(RichTextGenerator.UseItemOnOtherItem))]
    internal static class UnlockHoverTextPatch
    {
        private static bool Prefix(GameItem __0, GameItem __1, ref RichText __result)
        {
            if (!CanUnlock(__0, __1)) return true;
            var builder = new RichTextBuilder();
            builder.Add(LanguageText.Get("可解锁", "Ready to unlock"), RenderHandler.ColorPalette.Green);
            __result = builder.Build();
            return false;
        }
    }
}
