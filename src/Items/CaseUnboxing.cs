using System;
using HarmonyLib;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>
/// 物资箱（损坏机箱）的锁定与解锁。
/// - 生成（工厂创建时，仅一次）：随机把配件写入面板槽位标签（等级为机箱 T-2 至 T+1，
///   高一档概率 1%，含损坏件，空箱保底最低允许 T 度的 CPU），并上锁：PCREPAIR_LOCKED 标签 + 原版
///   LockHelper.LockUpContainer；PCREPAIR_SPAWNED 标记防止读档重建时重新掷 loot。
/// - 锁定状态：双击不开面板（提示需螺丝刀），定价为按当前掉落规则计算的固定箱价。
/// - 螺丝刀使用（原版路由到 MachineBrokenHelper.OnScrewdriverUsed，这里拦截）：
///   调用原版 LockHelper.UnlockContainer 解锁；开封后机箱本体价值归零，正常开面板取件。
/// </summary>
internal static class CaseUnboxing
{
    private const string SpawnedTag = "PCREPAIR_SPAWNED";
    private const string LockedTag = "PCREPAIR_LOCKED";
    internal const string SpawnedTagName = SpawnedTag;
    internal const string LockedTagName = LockedTag;
    private static readonly Random Rng = new();

    [HarmonyPatch(typeof(MachineBrokenHelper), nameof(MachineBrokenHelper.OnScrewdriverUsed))]
    internal static class ScrewdriverOnCasePatch
    {
        private static bool Prefix(GameItem machine)
        {
            if (!ComputerCase.IsCase(machine)) return true;
            HandleScrewdriver(machine);
            return false;
        }
    }

    /// <summary>处理螺丝刀用在机箱上：锁定则解锁，已解锁则吞掉原版修复逻辑。</summary>
    private static void HandleScrewdriver(GameItem machine)
    {
        if (!machine.IsTag(LockedTag))
        {
            // 已解锁的机箱：螺丝刀不再有作用，同时吞掉原版修复逻辑。
            return;
        }
        UnlockByScrewdriver(machine);
    }

    /// <summary>双击拆机螺丝刀：解锁背包里上锁的物资箱（取最先找到的一个）。</summary>
    internal static void TryUnlockLockedCaseInInventory(GameItem? tool = null)
    {
        if (!PlayerItemAccess.IsOwned(tool))
        {
            Notify(LanguageText.Get("只能使用自己拥有的拆机螺丝刀", "You must own the teardown screwdriver to use it."));
            return;
        }
        var items = EmporiumEntry.Instance?.GetAllOwnedItems();
        if (items == null)
        {
            Notify(LanguageText.Get("当前不在店内，无法使用", "You can only use this while at the shop."));
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var candidate = items[i];
            if (!ComputerCase.IsCase(candidate) || !candidate.IsTag(LockedTag) ||
                !PlayerItemAccess.IsOwned(candidate)) continue;
            UnlockByScrewdriver(candidate);
            return;
        }
        Notify(LanguageText.Get("没有自己拥有的上锁机箱", "You do not own a locked PC case."));
    }

    /// <summary>
    /// 解锁物资箱：移除锁定标签 + 走原版 LockHelper 解锁 + 原版解锁音效，
    /// 内容物保持不变；解锁后按分档固定价定价。
    /// </summary>
    internal static void UnlockByScrewdriver(GameItem caseItem)
    {
        if (!PlayerItemAccess.IsOwned(caseItem))
        {
            Notify(LanguageText.Get("只能解锁自己拥有的机箱", "You can only unlock a PC case you own."));
            return;
        }

        if (!caseItem.IsTag(LockedTag))
        {
            Notify(LanguageText.Get("这台机箱没有上锁", "This PC case is not locked."));
            return;
        }

        var tier = TierOf(caseItem);

        try
        {
            LockHelper.UnlockContainer(caseItem, true);
            LockHelper.DisableChildBehindLock(caseItem);
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("UnlockContainer 失败（解锁标签已生效）：" + ex.Message);
        }
        caseItem.DisableTag(LockedTag, false);

        // 开封后的物资箱不再作为整箱出售；配件可从槽位单独取出。
        CaseEconomy.EvaluateCase(caseItem);

        Core.Log?.Msg("[深空装机] 物资箱已用拆机螺丝刀解锁（T" + tier + "）。");
        Notify(LanguageText.Get("已解锁，双击机箱取出配件", "Unlocked. Double-click the case to retrieve its parts."));
    }

    /// <summary>生成时调用：首次创建的损坏机箱随机装填配件并上锁（读档重建时跳过）。</summary>
    internal static void EnsureLootAndLock(GameItem caseItem, int tier)
    {
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
        var rolled = 0;
        for (var i = 0; i < CaseInteriorUI.SlotTable.Length; i++)
        {
            var def = CaseInteriorUI.SlotTable[i];
            if (Rng.NextDouble() >= CaseEconomy.LootChance) continue;

            var tierRoll = CaseEconomy.RollLootTier(tier, Rng);
            var broken = Rng.NextDouble() < CaseEconomy.BrokenChance;
            var id = "pcrepair." + StemOfTag(def.Tag) + "_t" + tierRoll + (broken ? "_broken" : "");
            caseItem.EnableTag(CaseInteriorUI.SlotTagPrefix + i + "_" + id, false);
            rolled++;
        }

        if (rolled > 0)
        {
            Core.Log?.Msg("[深空装机] 物资箱随机生成配件 " + rolled + " 件（T" + Math.Max(1, tier - 2) + " 至 T" + Math.Min(5, tier + 1) + "）。");
            return;
        }

        // 保底也遵守箱体 T-2 的下限，避免 T4/T5 箱子极少数掉出 T1。
        var cpuSlot = 0;
        for (var i = 0; i < CaseInteriorUI.SlotTable.Length; i++)
            if (CaseInteriorUI.SlotTable[i].Tag == Components.CpuTag) { cpuSlot = i; break; }
        var minimumTier = CaseEconomy.MinimumLootTier(tier);
        caseItem.EnableTag(CaseInteriorUI.SlotTagPrefix + cpuSlot + "_pcrepair.component_cpu_t" + minimumTier, false);
        Core.Log?.Msg("[深空装机] 物资箱随机结果为空，保底生成 T" + minimumTier + " CPU。");
    }

    /// <summary>双击未解锁的物资箱时提示（不允许直接看内部）。只影响本 MOD 的物资箱。</summary>
    internal static void NotifyClosedCase()
    {
        Notify(LanguageText.Get("机箱卡死了，需要用拆机螺丝刀打开", "This case is jammed. Use the teardown screwdriver to open it."));
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
