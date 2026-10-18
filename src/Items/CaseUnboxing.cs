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
        Core.Debug("[深空装机] 物资箱已生成：T" + tier + "，配件已随机装填并上锁。");
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
        var plan = CaseLootPlanner.Roll(tier, Rng);
        // Finish every random choice before calling factories. Each temporary
        // entity has a cleanup custodian; no partial usable envelope is published.
        var contents = new List<WorkroomItemCodec.Snapshot>();
        long actualValue = 0;
        foreach (var model in plan.Models)
        {
            contents.Add(WorkroomComponentParts.CaptureFactory(() => Components.Create(model)));
            actualValue = checked(actualValue + contents[contents.Count - 1].Value);
        }
        if (actualValue != plan.TotalValue)
            throw new InvalidOperationException("物资箱实际内容价值与固定出货计划不一致。");
        WorkroomCrateContents.Write(caseItem, contents);
        Core.Debug("[深空装机] 物资箱完整记录已固定：" + contents.Count + " 件，基础价值 " +
            plan.TotalValue + "/" + CaseEconomy.CratePrice(tier) + "（仅基础估值）。");
    }

    internal static void Notify(string message)
    {
        try
        {
            ModOperationNotice.Show(message);
        }
        catch (Exception ex)
        {
            Core.Debug("Notify 失败：" + ex.Message);
        }
    }
}
