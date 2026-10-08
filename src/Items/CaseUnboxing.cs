using System;
using System.Collections.Generic;
using Il2Cpp;

namespace PCExpansion;

/// <summary>物资箱的一次性掉落和锁定；开封仅在工作间容量规划成功后提交。</summary>
internal static class CaseUnboxing
{
    private const string SpawnedTag = "PCREPAIR_SPAWNED";
    private const string LockedTag = "PCREPAIR_LOCKED";
    internal const string SpawnedTagName = SpawnedTag;
    internal const string LockedTagName = LockedTag;
    private static readonly Random Rng = new();

    /// <summary>生成时调用：首次创建的损坏机箱随机装填配件并上锁（读档重建时跳过）。</summary>
    internal static void EnsureLootAndLock(GameItem caseItem, int tier)
    {
        if (caseItem.IsTag(WorkroomMachineAssembly.Tag)) return;
        if (WorkroomItemCodec.Restoring || SaveGeneration.Decoding) return;
        if (caseItem.IsTag(SpawnedTag)) return;

        RollLoot(caseItem, tier);
        caseItem.EnableTag(SpawnedTag, false);
        caseItem.EnableTag(LockedTag, false);

        try
        {
            LockHelper.LockUpContainer(caseItem);
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("LockUpContainer 失败（锁定标签已生效）：" + ex.Message);
        }
        Core.Log?.Msg("[深空装机] 物资箱已生成：T" + tier + "，配件已随机装填并上锁。");
    }

    /// <summary>仅用于工作间隔离候选，成功提交前不改变库存中的原机箱。</summary>
    internal static void ReleaseWorkroomLock(GameItem item)
    {
        LockHelper.UnlockContainer(item, true);
        LockHelper.DisableChildBehindLock(item);
        item.DisableTag(LockedTag, false);
        item.modifiedState?.dict?.Remove(LockedTag);
    }

    /// <summary>从物品 ID 解析机箱等级（pcrepair.computer_case_t{n}_broken）。</summary>
    internal static int TierOf(GameItem caseItem)
    {
        var id = Core.Clean(caseItem.identifier);
        var tierStart = id.IndexOf("_t", StringComparison.Ordinal);
        if (tierStart >= 0 && int.TryParse(id.Substring(tierStart + 2, 1), out var tier)) return tier;
        return 1;
    }

    private static void RollLoot(GameItem caseItem, int tier)
    {
        var models = new List<string>();
        for (var i = 0; i < CaseContents.SlotTags.Length; i++)
        {
            var def = CaseContents.SlotTags[i];
            if (Rng.NextDouble() >= CaseEconomy.LootChance) continue;

            var tierRoll = CaseEconomy.RollLootTier(tier, Rng);
            var broken = Rng.NextDouble() < CaseEconomy.BrokenChance;
            var id = "pcrepair." + StemOfTag(def) + "_t" + tierRoll + (broken ? "_broken" : "");
            models.Add(id);
        }

        if (models.Count == 0)
        {
            var minimumTier = CaseEconomy.MinimumLootTier(tier);
            models.Add("pcrepair.component_cpu_t" + minimumTier);
            Core.Log?.Msg("[深空装机] 物资箱随机结果为空，保底生成 T" + minimumTier + " CPU。");
        }
        // Finish every random choice before calling factories. Each temporary
        // entity has a cleanup custodian; no partial usable envelope is published.
        var contents = new List<WorkroomItemCodec.Snapshot>();
        foreach (var model in models)
            contents.Add(WorkroomComponentParts.CaptureFactory(() => Components.Create(model)));
        WorkroomCrateContents.Write(caseItem, contents);
        Core.Log?.Msg("[深空装机] 物资箱完整记录已固定：" + contents.Count + " 件（T" + Math.Max(1, tier - 2) + " 至 T" + Math.Min(5, tier + 1) + "）。");
    }

    private static string StemOfTag(string componentTag)
    {
        foreach (var type in Components.All)
            if (type.Tag == componentTag)
                return type.Stem;
        return "component_cpu";
    }

    internal static void Notify(string message)
    {
        try
        {
            StoreUIManager.Instance?.Notify(message, "#FFFFFF");
        }
        catch (Exception ex)
        {
            Core.Debug("Notify 失败：" + ex.Message);
        }
    }
}
