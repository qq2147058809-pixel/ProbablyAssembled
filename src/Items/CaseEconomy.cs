using System;
using System.Collections.Generic;
using Il2Cpp;

namespace PCExpansion;

/// <summary>配件分档价值、损坏回收、物资箱基础价和整机组装估价。</summary>
internal static class CaseEconomy
{
    // 原独立抽法的提案权重与中奖率基准；不是新金额约束后的最终边际概率。
    internal const double LootChance = 0.65;
    internal const double BrokenChance = 0.40;
    internal const string MachinePriceFeatureId = "pcrepair.machine_profile";

    private static readonly (int Offset, double Weight)[] TierRolls =
    {
        (-2, 0.20), (-1, 0.34), (0, 0.45), (1, 0.01),
    };

    // 原始提案权重。新规划器另保持原每箱T5/完好T5中奖率，
    // 条件凑值后的每件档位占比和平均T5件数不再由此注释推算。
    private static readonly (int Offset, double Weight)[] Tier5CaseRolls =
    {
        (-2, 0.45), (-1, 0.43), (0, 0.11), (1, 0.01),
    };

    internal sealed class PartRecord
    {
        internal PartRecord(string typeTag, int tier, bool broken, long? value = null, List<PartRecord>? nested = null)
        {
            TypeTag = typeTag;
            Tier = tier;
            Broken = broken;
            Value = value;
            Nested = nested;
        }

        internal string TypeTag { get; }
        internal int Tier { get; }
        internal bool Broken { get; }
        internal long? Value { get; }
        internal List<PartRecord>? Nested { get; }
    }

    /// <summary>统一表中的五档固定箱价；出货不抬价，行情、零售和议价由原报价路径处理。</summary>
    internal static long CratePrice(int tier) => HardwareEconomyPrices.UnopenedCrate(tier);

    internal static double LootTierChance(int caseTier, int partTier)
    {
        caseTier = Math.Clamp(caseTier, 1, Components.TierCount);
        double chance = 0;
        foreach (var entry in GetTierRolls(caseTier))
            if (Math.Clamp(caseTier + entry.Offset, 1, Components.TierCount) == partTier)
                chance += entry.Weight;
        return chance;
    }

    private static (int Offset, double Weight)[] GetTierRolls(int caseTier) =>
        caseTier == Components.TierCount ? Tier5CaseRolls : TierRolls;

    private static long ContentsValue(List<PartRecord> parts)
    {
        long total = 0;
        foreach (var part in parts)
        {
            var type = FindType(part.TypeTag);
            if (type != null) total = checked(total + (part.Value ?? Components.ValueFor(type, part.Tier, part.Broken)));
        }
        return total;
    }

    /// <summary>
    /// 工作间装配、拆卸和交易检查时刷新估价与标签。
    /// 损坏机箱锁定时固定箱价，解锁后永远为 0；仅完好机箱可获得组装标签。
    /// </summary>
    internal static void EvaluateCase(GameItem caseItem)
    {
        if (!ComputerCase.IsCase(caseItem)) return;
        var diagnosticStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var tier = CaseUnboxing.TierOf(caseItem);
            var machine = WorkroomMachineFacts.Read(caseItem);
            // Stored contents are included in our aggregate base price.
            // Do not let native container/misc values count them a second time.
            caseItem.EnableTag("IGNORE_CHILD_VALUE", false);
            caseItem.lateUnitValue = 0;
            if (machine == null && caseItem.IsTag(Components.BrokenTag))
            {
                caseItem.RemoveItemFeatureByID(MachinePriceFeatureId);
                Components.SetTradeProperties(caseItem, false, false);
                caseItem.SetValue(caseItem.IsTag(CaseUnboxing.LockedTagName) ? CratePrice(tier) : 0);
                LowerAssemblerNpc.RefreshCasePurchaseEligibility(caseItem, null, new List<PartRecord>());
                return;
            }

            var caseType = FindType(Components.CaseTag);
            var baseValue = machine?.BodyValue ?? (caseType == null ? 0 : Components.ValueFor(caseType, tier, false));
            var parts = machine != null ? new List<PartRecord>(machine.Parts) : ReadParts(caseItem);
            var label = machine == null ? SelectMachineLabel(parts) :
                machine.Complete ? SelectMachineLabel(parts) : null;
            var normalValue = machine == null ? checked(baseValue + ContentsValue(parts)) : machine.Value;
            // The serialized machine value must always equal its saved body and parts.
            // Market premiums belong to quote getters, never to the item snapshot.
            caseItem.SetValue(normalValue);
            SetMachineProfileFeature(caseItem, label, MachineBonusPercent(label));
            var highestTier = 0;
            var highEndEligible = false;
            foreach (var part in parts)
            {
                highestTier = Math.Max(highestTier, part.Tier);
                if (!part.Broken && (part.Tier >= 5 || (part.Tier == 4 &&
                    (part.TypeTag == Components.GpuTag || part.TypeTag == Components.CpuTag))))
                    highEndEligible = true;
            }
            Components.SetTradeProperties(caseItem, highEndEligible && machine?.BodyBroken != true && !parts.Exists(p => p.Broken),
                highestTier == 5 && machine?.BodyBroken != true && !parts.Exists(p => p.Broken));
            LowerAssemblerNpc.RefreshCasePurchaseEligibility(caseItem, label, parts);
        }
        catch (Exception ex)
        {
            caseItem.DisableTag(LowerAssemblerNpc.PurchaseTag, false);
            Core.Log?.Error("机箱价值评估失败：" + ex);
        }
        finally
        {
            WorkroomFeedbackDiagnostics.CaseEvaluated(caseItem, diagnosticStart);
        }
    }

    internal static int MachineBonusPercent(string? label) => label switch
    {
        "整机" => 20,
        "性价比机器" => 30,
        "刀把机" => 10,
        _ => 0,
    };

    internal static long ApplyMachineBonus(long value, int percent) =>
        (long)Math.Round(value * (1.0 + percent / 100.0));

    private static void SetMachineProfileFeature(GameItem item, string? label, int percent)
    {
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
            "整机" => LanguageText.Get("text.9c1b7c6f70ae"),
            "刀把机" => LanguageText.Get("text.5c278353c0c4"),
            "性价比机器" => LanguageText.Get("text.639bf4e1ec1f"),
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
        InspectCase(caseItem, out var label);
        return label;
    }

    internal static List<PartRecord> InspectCase(GameItem caseItem, out string? label)
    {
        var machine = WorkroomMachineFacts.Read(caseItem);
        var parts = machine != null ? new List<PartRecord>(machine.Parts) : ReadParts(caseItem);
        label = caseItem.IsTag(Components.BrokenTag) || (machine != null && !machine.Complete)
            ? null : SelectMachineLabel(parts);
        return parts;
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
        // CPU 与显卡均比其他配件的最高档位低至少一档，即为刀把机。
        if (cpu <= highest - 1 && gpu <= highest - 1) return "刀把机";

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
        if (WorkroomMachineFacts.Read(caseItem) is { } machine) return new List<PartRecord>(machine.Parts);
        var parts = new List<PartRecord>();
        foreach (var snapshot in WorkroomCrateContents.Read(caseItem))
        {
            var spec = Components.Find(snapshot.Identifier) ?? throw new InvalidOperationException("物资箱内容型号无效。");
            parts.Add(new PartRecord(spec.Owner.Tag, spec.Tier, !WorkroomComponentAssembly.Intact(snapshot), snapshot.Value));
        }
        return parts;
    }
    internal static List<PartRecord> Parts(GameItem caseItem) => ReadParts(caseItem);

    private static Components.Type? FindType(string tag)
    {
        foreach (var type in Components.All)
            if (type.Tag == tag) return type;
        return null;
    }
}
