using System;
using System.Collections.Generic;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>配件分档价值、损坏回收、物资箱期望价和整机组装估价。</summary>
internal static class CaseEconomy
{
    internal const double LootChance = 0.65;
    internal const double BrokenChance = 0.40;
    // Target weighted return: 80% of openings lose about 30%, 20% gain about 10%.
    // Those outcomes average to 78% of purchase price, so price the case at EV / 0.78.
    private const double TargetAverageReturn = 0.78;
    internal const string MachinePriceFeatureId = "pcrepair.machine_profile";
    private const string LegacyMachinePriceFeatureId = "pcrepair.machine_price";

    private static readonly (int Offset, double Weight)[] TierRolls =
    {
        (-2, 0.20), (-1, 0.34), (0, 0.45), (1, 0.01),
    };

    internal sealed class PartRecord
    {
        internal PartRecord(string typeTag, int tier, bool broken)
        {
            TypeTag = typeTag;
            Tier = tier;
            Broken = broken;
        }

        internal string TypeTag { get; }
        internal int Tier { get; }
        internal bool Broken { get; }
    }

    internal static int MinimumLootTier(int caseTier) =>
        Math.Max(1, Math.Clamp(caseTier, 1, Components.TierCount) - 2);

    /// <summary>按物资箱等级窗口抽取配件档位；窗口为 [箱体 T-2, 箱体 T+1]，高一档仅 1%。</summary>
    internal static int RollLootTier(int caseTier, Random random)
    {
        caseTier = Math.Clamp(caseTier, 1, Components.TierCount);
        var roll = random.NextDouble();
        var cumulative = 0.0;
        foreach (var entry in TierRolls)
        {
            cumulative += entry.Weight;
            if (roll < cumulative)
                return Math.Clamp(caseTier + entry.Offset, 1, Components.TierCount);
        }
        return Math.Clamp(caseTier, 1, Components.TierCount);
    }

    /// <summary>箱价按出货期望价值 / 78% 定价，对应目标的加权回收率。</summary>
    internal static long CratePrice(int tier)
    {
        tier = Math.Clamp(tier, 1, Components.TierCount);
        var expected = 0.0;

        foreach (var slot in CaseInteriorUI.SlotTable)
        {
            var type = FindType(slot.Tag);
            if (type == null) continue;

            var averageTierValue = 0.0;
            foreach (var entry in TierRolls)
            {
                var itemTier = Math.Clamp(tier + entry.Offset, 1, Components.TierCount);
                var intact = Components.ValueFor(type, itemTier, false);
                var broken = Components.ValueFor(type, itemTier, true);
                averageTierValue += entry.Weight * (intact * (1 - BrokenChance) + broken * BrokenChance);
            }
            expected += LootChance * averageTierValue;
        }

        var emptyProbability = Math.Pow(1 - LootChance, CaseInteriorUI.SlotTable.Length);
        var cpu = FindType(Components.CpuTag);
        if (cpu != null) expected += emptyProbability * Components.ValueFor(cpu, MinimumLootTier(tier), false);

        return Math.Max(1, (long)Math.Round(expected / TargetAverageReturn));
    }

    /// <summary>按机箱槽位标签计算内部配件共价。</summary>
    internal static long ContentsValue(GameItem caseItem)
    {
        return ContentsValue(ReadParts(caseItem));
    }

    private static long ContentsValue(List<PartRecord> parts)
    {
        long total = 0;
        foreach (var part in parts)
        {
            var type = FindType(part.TypeTag);
            if (type != null) total += Components.ValueFor(type, part.Tier, part.Broken);
        }
        return total;
    }

    /// <summary>
    /// 关闭窗口、放入/取出配件或点击检测时刷新估价与标签。
    /// 损坏机箱锁定时固定箱价，解锁后永远为 0；仅完好机箱可获得组装标签。
    /// </summary>
    internal static void EvaluateCase(GameItem caseItem) => EvaluateCase(caseItem, null);

    internal static void EvaluateCase(GameItem caseItem,
        IReadOnlyList<CaseInteriorUI.CaseWindow.SlotEntry>? liveSlots)
    {
        if (!ComputerCase.IsCase(caseItem)) return;
        try
        {
            var tier = CaseUnboxing.TierOf(caseItem);
            // Detached UI contents are included in our aggregate base price.
            // Do not let native container/misc values count them a second time.
            caseItem.EnableTag("IGNORE_CHILD_VALUE", false);
            caseItem.lateUnitValue = 0;
            if (caseItem.IsTag(Components.BrokenTag))
            {
                caseItem.RemoveItemFeatureByID(MachinePriceFeatureId);
                caseItem.RemoveItemFeatureByID(LegacyMachinePriceFeatureId);
                Components.SetTradeProperties(caseItem, false, false);
                caseItem.SetValue(caseItem.IsTag(CaseUnboxing.LockedTagName) ? CratePrice(tier) : 0);
                return;
            }

            var caseType = FindType(Components.CaseTag);
            var baseValue = caseType == null ? 0 : Components.ValueFor(caseType, tier, false);
            var parts = liveSlots == null ? ReadParts(caseItem) : ReadParts(liveSlots);
            var label = SelectMachineLabel(parts);
            caseItem.SetValue(baseValue + ContentsValue(parts));
            SetMachineProfileFeature(caseItem, label, MachineBonusPercent(label));
            var highestTier = 0;
            var luxuryEligible = false;
            foreach (var part in parts)
            {
                highestTier = Math.Max(highestTier, part.Tier);
                if (part.Tier >= 5 || (part.Tier == 4 &&
                    (part.TypeTag == Components.GpuTag || part.TypeTag == Components.CpuTag)))
                    luxuryEligible = true;
            }
            Components.SetTradeProperties(caseItem, luxuryEligible, highestTier == 5);
        }
        catch (Exception ex)
        {
            Core.Log?.Error("机箱价值评估失败：" + ex);
        }
        finally
        {
            // 配件装入、取出、测试及关闭窗口后，原版收购清单读取的资格标签同步更新。
            LowerAssemblerNpc.RefreshPurchaseEligibility(caseItem);
        }
    }

    internal static int MachineBonusPercent(string? label) => label switch
    {
        "整机" => 20,
        "性价比机器" => 25,
        "刀把机" => 10,
        _ => 0,
    };

    internal static long ApplyMachineBonus(long value, int percent) =>
        (long)Math.Round(value * (1.0 + percent / 100.0));

    private static void SetMachineProfileFeature(GameItem item, string? label, int percent)
    {
        // Remove the previous hand-built feature. It lacks native initialization data and
        // crashes GetFormattedActualModifier while the negotiation UI renders the item.
        item.RemoveItemFeatureByID(LegacyMachinePriceFeatureId);
        if (percent <= 0 || string.IsNullOrEmpty(label))
        {
            item.RemoveItemFeatureByID(MachinePriceFeatureId);
            return;
        }

        var feature = item.FindItemFeatureByID(MachinePriceFeatureId);
        if (feature == null)
        {
            // Use the game's factory to initialize all fields required by its negotiation UI.
            feature = ItemFeatureList.Discount(percent);
            feature.identifier = MachinePriceFeatureId;
            item.AddItemFeature(feature);
        }

        feature.featureType = ItemFeature.FeatureType.Normale;
        feature.valueStage = ItemFeature.ValueStage.Final;
        feature.initiallyShown = true;
        feature.isPublicHidden = true;
        feature.isExposable = false;
        feature.isFeatureExposed = true;
        feature.SetValueModifier(percent);
        var display = label switch
        {
            "整机" => LanguageText.Get("整机", "Complete PC"),
            "刀把机" => LanguageText.Get("刀把机", "Bottleneck Build"),
            "性价比机器" => LanguageText.Get("性价比机器", "Value Build"),
            _ => label,
        };
        feature.SetPublicDisplay(display);
        feature.SetActualDisplay(display);
    }

    internal static int HighestStoredPartTier(GameItem caseItem)
    {
        var highest = 0;
        foreach (var part in ReadParts(caseItem)) highest = Math.Max(highest, part.Tier);
        return highest;
    }

    /// <summary>返回满足条件的唯一机型，用于检测成功反馈；机型判定和售价不依赖 Tooltip 标签。</summary>
    internal static string? MachineLabel(GameItem caseItem)
    {
        if (!ComputerCase.IsCase(caseItem) || caseItem.IsTag(Components.BrokenTag)) return null;
        return SelectMachineLabel(ReadParts(caseItem));
    }

    internal static string? MachineLabel(IReadOnlyList<CaseInteriorUI.CaseWindow.SlotEntry> slots) =>
        SelectMachineLabel(ReadParts(slots));

    /// <summary>返回检测失败原因；null 表示至少每类一件、跨度合规且内存同档。</summary>
    internal static string? ValidateBuild(IReadOnlyList<CaseInteriorUI.CaseWindow.SlotEntry> slots)
    {
        var presentTypes = new HashSet<string>();
        var partTiers = new List<int>();
        int? ramTier = null;

        foreach (var entry in slots)
        {
            var item = entry.Slot.childItem;
            if (item == null) continue;
            if (item.IsTag(Components.BrokenTag)) return LanguageText.Get("含有损坏配件", "Broken part detected");
            var tier = Components.TierOf(item);
            if (tier < 1) continue;
            presentTypes.Add(entry.Tag);
            partTiers.Add(tier);

            if (entry.Tag == Components.RamTag)
            {
                if (ramTier.HasValue && ramTier.Value != tier) return LanguageText.Get("内存必须使用同一 T 度", "All memory sticks must be the same tier");
                ramTier = tier;
            }
        }

        var missing = new List<string>();
        foreach (var type in Components.All)
            if (!type.IsCase && !presentTypes.Contains(type.Tag)) missing.Add(type.Name);
        if (missing.Count > 0)
        {
            var englishMissing = new List<string>();
            foreach (var type in Components.All)
                if (!type.IsCase && !presentTypes.Contains(type.Tag)) englishMissing.Add(type.EnglishName);
            return LanguageText.Get("缺少 " + string.Join("、", missing), "Missing: " + string.Join(", ", englishMissing));
        }
        if (partTiers.Count == 0) return LanguageText.Get("没有可检测的配件", "No parts to test");

        var min = partTiers[0];
        var max = partTiers[0];
        foreach (var tier in partTiers)
        {
            min = Math.Min(min, tier);
            max = Math.Max(max, tier);
        }
        return max - min <= 3 ? null : LanguageText.Get("配件 T 度跨度超过三档", "Part tiers are more than three steps apart");
    }

    internal static string ValidationFailureEnglish(string localizedFailure)
    {
        if (!LanguageText.IsChinese) return localizedFailure;
        // ValidateBuild returns English directly while English is active. This mapping covers
        // the title path if the locale changes while an already-open window remains alive.
        return localizedFailure switch
        {
            "含有损坏配件" => "Broken part detected",
            "内存必须使用同一 T 度" => "All memory sticks must be the same tier",
            "没有可检测的配件" => "No parts to test",
            "配件 T 度跨度超过三档" => "Part tiers are more than three steps apart",
            _ when localizedFailure.StartsWith("缺少 ", StringComparison.Ordinal) => "Some required parts are missing",
            _ => localizedFailure
        };
    }

    internal static string ValidationFailureShort(string failure)
    {
        if (failure == "含有损坏配件" || failure == "Broken part detected")
            return LanguageText.Get("配件破损", "Broken parts");
        if (failure.Contains("内存") || failure.Contains("memory sticks"))
            return LanguageText.Get("内存不同档", "RAM mismatch");
        if (failure.StartsWith("缺少 ", StringComparison.Ordinal) || failure.StartsWith("Missing:", StringComparison.Ordinal))
            return LanguageText.Get("配件缺失", "Parts missing");
        if (failure.Contains("跨度") || failure.Contains("three steps"))
            return LanguageText.Get("跨度过大", "Tier gap");
        return LanguageText.Get("检测失败", "Test failed");
    }

    private static string? SelectMachineLabel(List<PartRecord> parts)
    {
        foreach (var part in parts)
            if (part.Broken) return null;
        if (!IsComplete(parts) || !IsTierCompatible(parts)) return null;

        var cpu = HighestTier(parts, Components.CpuTag);
        var gpu = HighestTier(parts, Components.GpuTag);
        if (cpu == 0 || gpu == 0) return null;

        var highest = 0;
        foreach (var part in parts) highest = Math.Max(highest, part.Tier);
        if (cpu <= highest - 2 && gpu <= highest - 2) return "刀把机";

        var psu = HighestTier(parts, Components.PsuTag);
        if (psu > 0 && psu >= Math.Max(cpu, gpu) - 1)
        {
            var valueForMoney = true;
            foreach (var part in parts)
            {
                if (part.TypeTag == Components.CpuTag || part.TypeTag == Components.GpuTag || part.TypeTag == Components.PsuTag)
                    continue;
                if (part.Tier >= cpu || part.Tier >= gpu)
                {
                    valueForMoney = false;
                    break;
                }
            }
            if (valueForMoney) return "性价比机器";
        }

        return "整机";
    }

    private static bool IsComplete(List<PartRecord> parts)
    {
        var present = new HashSet<string>();
        foreach (var part in parts) present.Add(part.TypeTag);
        foreach (var type in Components.All)
            if (!type.IsCase && !present.Contains(type.Tag)) return false;
        return true;
    }

    private static bool IsTierCompatible(List<PartRecord> parts)
    {
        if (parts.Count == 0) return false;
        var min = int.MaxValue;
        var max = 0;
        int? ramTier = null;
        foreach (var part in parts)
        {
            min = Math.Min(min, part.Tier);
            max = Math.Max(max, part.Tier);
            if (part.TypeTag != Components.RamTag) continue;
            if (ramTier.HasValue && ramTier.Value != part.Tier) return false;
            ramTier = part.Tier;
        }
        return max - min <= 3;
    }

    private static int HighestTier(List<PartRecord> parts, string typeTag)
    {
        var tier = 0;
        foreach (var part in parts)
            if (part.TypeTag == typeTag) tier = Math.Max(tier, part.Tier);
        return tier;
    }

    private static List<PartRecord> ReadParts(GameItem caseItem)
    {
        var parts = new List<PartRecord>();
        for (var i = 0; i < CaseInteriorUI.SlotTable.Length; i++)
        {
            var slot = CaseInteriorUI.SlotTable[i];
            var type = FindType(slot.Tag);
            if (type == null) continue;
            for (var tier = 1; tier <= Components.TierCount; tier++)
            {
                var id = "pcrepair." + type.Stem + "_t" + tier;
                var broken = caseItem.IsTag(CaseInteriorUI.SlotTagPrefix + i + "_" + id + "_broken");
                if (!broken && !caseItem.IsTag(CaseInteriorUI.SlotTagPrefix + i + "_" + id)) continue;
                parts.Add(new PartRecord(type.Tag, tier, broken));
                break;
            }
        }
        return parts;
    }

    private static List<PartRecord> ReadParts(
        IReadOnlyList<CaseInteriorUI.CaseWindow.SlotEntry> slots)
    {
        var parts = new List<PartRecord>();
        foreach (var entry in slots)
        {
            var child = entry.Slot.childItem;
            if (child == null) continue;
            var spec = Components.Find(Core.Clean(child.identifier));
            if (spec == null || spec.Owner.IsCase || spec.Owner.Tag != entry.Tag) continue;
            parts.Add(new PartRecord(spec.Owner.Tag, spec.Tier,
                spec.Broken || child.IsTag(Components.BrokenTag)));
        }
        return parts;
    }

    private static Components.Type? FindType(string tag)
    {
        foreach (var type in Components.All)
            if (type.Tag == tag) return type;
        return null;
    }
}
