using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>
/// Mod-only repair routing for broken computer parts. Vanilla item targets are left alone;
/// only known repair materials used on this mod's broken items are intercepted here.
/// </summary>
internal static class ComponentRepair
{
    private const string ProgressTagPrefix = "PCREPAIR_REPAIR_MATERIAL_";

    private static readonly HashSet<string> MaterialIds = new(StringComparer.Ordinal)
    {
        "common_electronic",
        "wire",
        "nuts_metal",
        "printer_plastic",
        "scrap_metal",
        "metal_ingot",
        "energy_credit",
    };

    /// <summary>Known vanilla repair materials can target only a broken item made by this mod.</summary>
    internal static bool ShouldRoute(GameItem? source, GameItem? target)
    {
        if (source == null || target == null) return false;
        if (!MaterialIds.Contains(Core.Clean(source.identifier))) return false;
        var spec = Components.Find(Core.Clean(target.identifier));
        return spec != null && spec.Broken;
    }

    private static string[]? RecipeFor(Components.Item spec)
    {
        if (!spec.Broken || spec.Tier < 1 || spec.Tier > 3) return null;

        var ingredients = new List<string>();
        switch (spec.Owner.Tag)
        {
            case Components.MotherboardTag:
            case Components.GpuTag:
                ingredients.Add("common_electronic");
                if (spec.Tier >= 2) ingredients.Add("wire");
                if (spec.Tier >= 3) ingredients.Add("nuts_metal");
                break;

            case Components.FanTag:
                ingredients.Add("nuts_metal");
                if (spec.Tier >= 2) ingredients.Add("printer_plastic");
                if (spec.Tier >= 3) ingredients.Add("common_electronic");
                break;

            case Components.PsuTag:
                ingredients.Add("scrap_metal");
                if (spec.Tier >= 2) ingredients.Add("energy_credit");
                if (spec.Tier >= 3) ingredients.Add("metal_ingot");
                break;

            case Components.CaseTag:
                ingredients.Add("scrap_metal");
                if (spec.Tier >= 2) ingredients.Add("metal_ingot");
                if (spec.Tier >= 3) ingredients.Add("printer_plastic");
                break;

            case Components.CoolerTag:
                ingredients.Add("nuts_metal");
                if (spec.Tier >= 2) ingredients.Add("printer_plastic");
                if (spec.Tier >= 3) ingredients.Add("metal_ingot");
                break;

            default:
                return null;
        }

        return ingredients.ToArray();
    }

    internal static bool IsRepairable(Components.Type type, int tier) =>
        RecipeFor(new Components.Item(type, tier, broken: true)) != null;

    private static bool Contains(GameInventory inventory, GameItem item)
    {
        try
        {
            var children = inventory.childItems;
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child != null && child.Pointer == item.Pointer) return true;
            }
        }
        catch (Exception ex)
        {
            Core.Debug("维修物品归属检查失败：" + ex.Message);
        }
        return false;
    }

    private static string ProgressTag(string materialId) => ProgressTagPrefix + materialId;

    private static string SlotProgressTag(int slotIndex, string materialId) =>
        "PCREPAIR_SLOT_REPAIR_" + slotIndex + "_" + materialId;

    // 配件放进机箱后由型号标签重建；把已消耗材料的进度同时保存，避免读档丢失。
    internal static void SaveSlotProgress(GameItem caseItem, GameItem? part, int slotIndex)
    {
        foreach (var materialId in MaterialIds)
        {
            var slotTag = SlotProgressTag(slotIndex, materialId);
            if (part != null && part.IsTag(ProgressTag(materialId))) caseItem.EnableTag(slotTag, false);
            else if (caseItem.IsTag(slotTag)) caseItem.DisableTag(slotTag, false);
        }
    }

    internal static void RestoreSlotProgress(GameItem caseItem, GameItem part, int slotIndex)
    {
        if (!part.IsTag(Components.BrokenTag)) return;
        foreach (var materialId in MaterialIds)
            if (caseItem.IsTag(SlotProgressTag(slotIndex, materialId)))
                part.EnableTag(ProgressTag(materialId), false);
    }

    private static int ProgressCount(GameItem item, string[] recipe)
    {
        var count = 0;
        foreach (var materialId in recipe)
            if (item.IsTag(ProgressTag(materialId))) count++;
        return count;
    }

    private static bool HasProgress(GameItem item, string materialId) => item.IsTag(ProgressTag(materialId));

    private static string MaterialName(string id) => id switch
    {
        "common_electronic" => LanguageText.Get("电子零件", "Electronic Parts"),
        "wire" => LanguageText.Get("线缆", "Wire"),
        "nuts_metal" => LanguageText.Get("金属螺丝", "Metal Screws"),
        "printer_plastic" => LanguageText.Get("3D打印耗材", "3D Printer Filament"),
        "scrap_metal" => LanguageText.Get("废金属", "Scrap Metal"),
        "metal_ingot" => LanguageText.Get("金属锭", "Metal Ingot"),
        "energy_credit" => LanguageText.Get("能量电池", "Energy Cell"),
        _ => id,
    };

    internal static string RepairHint(Components.Item spec)
    {
        if (!spec.Broken) return string.Empty;
        var recipe = RecipeFor(spec);
        if (recipe == null)
            return LanguageText.Get(spec.Tier > 3 ? " 此等级配件无法维修。" : " 此类型配件无法维修。",
                spec.Tier > 3 ? " Parts at this tier cannot be repaired." : " This part type cannot be repaired.");

        var locationRule = spec.Owner.IsCase
            ? LanguageText.Get("维修前必须清空机箱内部。", "Empty the PC case before repairing it.")
            : LanguageText.Get("维修前必须先将配件从机箱中取出。", "Remove the part from the PC case before repairing it.");
        var materials = new List<string>();
        foreach (var materialId in recipe) materials.Add(MaterialName(materialId));
        return LanguageText.IsChinese
            ? " 维修材料：" + string.Join("、", materials) + "。" + locationRule + "将材料逐个拖到损坏物品上，材料会被消耗。"
            : " Repair materials: " + string.Join(", ", materials) + ". " + locationRule + " Drop the materials onto the broken item one at a time; they will be consumed.";
    }

    private static void HandleDrop(GameItem source, GameItem target)
    {
        var spec = Components.Find(Core.Clean(target.identifier));
        if (spec == null || !spec.Broken) return;

        if (CaseInteriorUI.IsItemInsideAnyCase(target))
        {
            Notify(LanguageText.Get("无法在机箱内维修，请先将配件取出", "Remove the part from the PC case before repairing it."));
            return;
        }

        if (spec.Owner.IsCase && CaseInteriorUI.HasAnyStoredContents(target))
        {
            Notify(LanguageText.Get("机箱内仍有配件，请清空后再维修", "Empty the PC case before repairing it."));
            return;
        }

        var recipe = RecipeFor(spec);
        if (recipe == null)
        {
            Notify(LanguageText.Get(spec.Tier > 3 ? "T4及以上配件无法维修" : "该配件无法维修",
                spec.Tier > 3 ? "Parts at T4 and above cannot be repaired." : "This part cannot be repaired."));
            return;
        }

        if (!PlayerItemAccess.IsOwned(target) || !PlayerItemAccess.IsOwned(source))
        {
            Notify(LanguageText.Get("只能使用自己拥有的材料维修自己的物品", "Use your own materials to repair an item you own."));
            return;
        }

        var inventory = target.parentInventory;
        if (inventory == null || !Contains(inventory, target))
        {
            Notify(LanguageText.Get("无法确认损坏物品所在的格位，请放稳后再维修", "Place the broken item in an inventory before repairing it."));
            return;
        }

        var materialInventory = source.parentInventory;
        if (materialInventory == null || !Contains(materialInventory, source))
        {
            Notify(LanguageText.Get("维修材料需要先放进物品栏", "Move the repair material into an inventory first."));
            return;
        }

        var materialId = Core.Clean(source.identifier);
        var requiredIndex = Array.IndexOf(recipe, materialId);
        if (requiredIndex < 0)
        {
            Notify(LanguageText.Get("该配件不需要这种维修材料", "This part does not need that material."));
            return;
        }

        if (HasProgress(target, materialId))
        {
            Notify(LanguageText.Get("这种维修材料已经投入过了", "That material has already been added."));
            return;
        }

        var progress = ProgressCount(target, recipe);
        if (progress == recipe.Length - 1)
        {
            CompleteRepair(inventory, materialInventory, source, target, spec);
            return;
        }

        var progressTag = ProgressTag(materialId);
        target.EnableTag(progressTag, false);
        if (!ConsumeOne(materialInventory, source))
        {
            target.DisableTag(progressTag, false);
            Notify(LanguageText.Get("维修材料没有成功消耗，请重试", "The repair material was not consumed. Try again."));
            return;
        }

        target.Validate();
        inventory.Validate();
        var completed = ProgressCount(target, recipe);
        var remaining = new List<string>();
        foreach (var needed in recipe)
            if (!HasProgress(target, needed)) remaining.Add(MaterialName(needed));

        Notify(LanguageText.Get("维修材料已投入（" + completed + "/" + recipe.Length + "），还需要：" +
               string.Join("、", remaining), "Repair material added (" + completed + "/" + recipe.Length + "). Still needed: " +
               string.Join(", ", remaining)));
        Core.Log?.Msg("[维修] " + spec.DisplayName + " 材料进度 " + completed + "/" + recipe.Length +
                      "；投入 " + MaterialName(materialId) + "。");
    }

    private static bool ConsumeOne(GameInventory inventory, GameItem material)
    {
        if (!Contains(inventory, material)) return false;
        var count = material.unitCount;
        if (count <= 0) return false;
        if (count > 1)
        {
            material.SetUnitCount(count - 1);
            inventory.Validate();
            return true;
        }
        return inventory.Expel(material);
    }

    private static void CompleteRepair(GameInventory inventory, GameInventory materialInventory,
        GameItem finalMaterial, GameItem brokenItem, Components.Item spec)
    {
        var intactId = "pcrepair." + spec.Owner.Stem + "_t" + spec.Tier;
        // Keep the existing GameItem instance in its current grid slot. Expelling the
        // broken item and inserting a replacement can leave the grid with stale slot
        // state, which makes repaired parts pile up at the inventory's top-left.
        CaseInteriorUI.CloseAndForgetCase(brokenItem);

        try
        {
            Components.ApplySpec(brokenItem, intactId);
            GeneralHelper.SetItemOwned(brokenItem, true);
            brokenItem.Validate();
        }
        catch (Exception ex)
        {
            try
            {
                Components.ApplySpec(brokenItem, spec.Id);
                brokenItem.Validate();
            }
            catch (Exception rollbackEx)
            {
                Core.Log?.Error("[维修] 写入完好状态失败且无法恢复损坏状态：" + spec.Id + "；" + rollbackEx);
            }
            Core.Log?.Error("[维修] 无法将配件切换为完好状态：" + spec.Id + "；" + ex);
            Notify(LanguageText.Get("配件状态更新失败，维修未完成", "Could not update the part. Repair cancelled."));
            return;
        }

        if (!ConsumeOne(materialInventory, finalMaterial))
        {
            try
            {
                Components.ApplySpec(brokenItem, spec.Id);
                brokenItem.Validate();
                inventory.Validate();
            }
            catch (Exception ex)
            {
                Core.Log?.Error("[维修] 消耗材料失败后无法恢复损坏配件：" + spec.Id + "；" + ex);
            }
            Notify(LanguageText.Get("维修材料没有成功消耗，已取消维修", "The repair material was not consumed. Repair cancelled."));
            return;
        }

        foreach (var materialId in MaterialIds)
        {
            var progressTag = ProgressTag(materialId);
            if (brokenItem.IsTag(progressTag)) brokenItem.DisableTag(progressTag, false);
        }

        brokenItem.Validate();
        inventory.Validate();
        Notify(LanguageText.Get("维修完成：" + brokenItem.GetDisplayName(), "Repair complete: " + brokenItem.GetDisplayName()));
        Core.Log?.Msg("[维修] 完成 " + spec.Id + " -> " + intactId + "；消耗 " +
                      MaterialName(Core.Clean(finalMaterial.identifier)) + "。");
    }

    private static void Notify(string message)
    {
        try { StoreUIManager.Instance?.Notify(message, "#FFFFFF"); }
        catch (Exception ex) { Core.Debug("维修提示失败：" + ex.Message); }
    }

    [HarmonyPatch(typeof(ItemBehaviourManager), nameof(ItemBehaviourManager.Target))]
    private static class RepairBehaviourRoutingPatch
    {
        // Keep vanilla target behaviours from treating mod parts as their own repair targets.
        private static bool Prefix(GameItem __0, GameItem __1) => !ShouldRoute(__0, __1);
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.MayTarget))]
    private static class RepairMayTargetPatch
    {
        private static bool Prefix(GameItem __instance, GameItem __0, ref bool __result)
        {
            if (!ShouldRoute(__instance, __0)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.CanTarget))]
    private static class RepairCanTargetPatch
    {
        private static bool Prefix(GameItem __instance, GameItem __0, ref bool __result)
        {
            if (!ShouldRoute(__instance, __0)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.Target))]
    private static class RepairTargetPatch
    {
        private static bool Prefix(GameItem __instance, GameItem __0)
        {
            if (!ShouldRoute(__instance, __0)) return true;
            try { HandleDrop(__instance, __0); }
            catch (Exception ex)
            {
                Core.Log?.Error("[维修] 拖放维修处理失败：" + ex);
                Notify(LanguageText.Get("维修操作失败，请检查日志", "Repair failed. Check the game log for details."));
            }
            return false;
        }
    }
}
