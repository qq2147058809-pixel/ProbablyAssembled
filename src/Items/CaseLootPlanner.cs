using System;
using System.Collections.Generic;

namespace PCExpansion;

/// <summary>在原类别/档位边界内规划30%–200%回款，独立约束T5及完好T5每箱中奖率。</summary>
internal static class CaseLootPlanner
{
    internal sealed record Plan(string[] Models, int TotalValue, int Band, int Rarity);
    private sealed record Choice(string? Model, int Value, double Weight, int Rarity);
    private static readonly object Sync = new();
    private static readonly Distribution?[] Cache = new Distribution[Components.TierCount];

    internal static Plan Roll(int tier, Random random)
    {
        tier = Math.Clamp(tier, 1, Components.TierCount);
        Distribution distribution;
        lock (Sync)
            distribution = Cache[tier - 1] ??= new Distribution(tier);
        return distribution.Roll(random);
    }

    private sealed class Distribution
    {
        private readonly int price, minimum, maximum;
        private readonly Choice[][] choices;
        // suffix[i,c,s]为后续槽组成精确金额s且T5类别恰为c的原始权重。
        // 类别：0无T5，1仅破损T5，2至少一件完好T5。
        private readonly double[,,] suffix;
        private readonly double[,] joint;

        internal Distribution(int tier)
        {
            price = checked((int)CaseEconomy.CratePrice(tier));
            minimum = checked((price * 3 + 9) / 10);
            maximum = checked(price * 2);
            choices = new Choice[CaseContents.SlotTags.Length][];
            for (var slot = 0; slot < choices.Length; slot++)
            {
                Components.Type? type = null;
                foreach (var candidate in Components.All)
                    if (candidate.Tag == CaseContents.SlotTags[slot]) { type = candidate; break; }
                if (type == null) throw new InvalidOperationException("物资箱配件类别不存在。");
                var list = new List<Choice> { new(null, 0, 1 - CaseEconomy.LootChance, 0) };
                for (var partTier = 1; partTier <= Components.TierCount; partTier++)
                {
                    var chance = CaseEconomy.LootTierChance(tier, partTier);
                    if (chance <= 0) continue;
                    for (var state = 0; state < 2; state++)
                    {
                        var broken = state == 1;
                        list.Add(new Choice("pcrepair." + type.Stem + "_t" + partTier + (broken ? "_broken" : ""),
                            checked((int)Components.ValueFor(type, partTier, broken)),
                            CaseEconomy.LootChance * chance * (broken ? CaseEconomy.BrokenChance : 1 - CaseEconomy.BrokenChance),
                            partTier == 5 ? broken ? 1 : 2 : 0));
                    }
                }
                choices[slot] = list.ToArray();
            }

            suffix = new double[choices.Length + 1, 3, maximum + 1];
            suffix[choices.Length, 0, 0] = 1;
            for (var slot = choices.Length - 1; slot >= 0; slot--)
                foreach (var choice in choices[slot])
                    for (var category = 0; category < 3; category++)
                        for (var value = choice.Value; value <= maximum; value++)
                            suffix[slot, Math.Max(category, choice.Rarity), value] +=
                                choice.Weight * suffix[slot + 1, category, value - choice.Value];

            joint = BalanceTargets(tier);
        }

        private double TargetMass(int value)
        {
            double BandMass(double low, double high, double weight)
            {
                var left = value == minimum ? low * price : Math.Max(low * price, value - 0.5);
                var right = value == maximum ? high * price : Math.Min(high * price, value + 0.5);
                return Math.Max(0, right - left) * weight / ((high - low) * price);
            }
            return BandMass(0.3, 0.9, 0.10) + BandMass(0.9, 1, 0.70) +
                BandMass(1, 1.3, 0.18) + BandMass(1.3, 2, 0.02);
        }

        private double[,] BalanceTargets(int tier)
        {
            var mass = new double[3, maximum + 1];
            var target = new double[maximum + 1];
            var chance = CaseEconomy.LootChance * CaseEconomy.LootTierChance(tier, 5);
            var noT5 = Math.Pow(1 - chance, choices.Length);
            var noIntactT5 = Math.Pow(1 - chance * (1 - CaseEconomy.BrokenChance), choices.Length);
            var desired = new[] { noT5, noIntactT5 - noT5, 1 - noIntactT5 };
            for (var value = minimum; value <= maximum; value++)
            {
                target[value] = TargetMass(value);
                var total = suffix[0, 0, value] + suffix[0, 1, value] + suffix[0, 2, value];
                if (total <= 0) throw new InvalidOperationException("物资箱目标金额没有合法组合。");
                for (var category = 0; category < 3; category++)
                    mass[category, value] = target[value] * suffix[0, category, value] / total;
            }

            // 同时保持金额列边际及原T5三组行边际。不可达格保持零，
            // 不把同一个完整金额分布强塞给无法达到高端金额的T5组别。
            for (var iteration = 0; iteration < 512; iteration++)
            {
                for (var category = 0; category < 3; category++)
                {
                    double row = 0;
                    for (var value = minimum; value <= maximum; value++) row += mass[category, value];
                    if (desired[category] > 0 && row <= 0)
                        throw new InvalidOperationException("物资箱T5稀有度没有可行分布。");
                    var factor = row > 0 ? desired[category] / row : 0;
                    for (var value = minimum; value <= maximum; value++) mass[category, value] *= factor;
                }
                for (var value = minimum; value <= maximum; value++)
                {
                    var total = mass[0, value] + mass[1, value] + mass[2, value];
                    if (total <= 0) throw new InvalidOperationException("物资箱联合分布丢失目标金额。");
                    var factor = target[value] / total;
                    for (var category = 0; category < 3; category++) mass[category, value] *= factor;
                }
                double error = 0;
                for (var category = 0; category < 3; category++)
                {
                    double row = 0;
                    for (var value = minimum; value <= maximum; value++) row += mass[category, value];
                    error = Math.Max(error, Math.Abs(row - desired[category]));
                }
                if (error < 1e-12) return mass;
            }
            throw new InvalidOperationException("物资箱回款与T5稀有度校准未收敛，停止生成。");
        }

        internal Plan Roll(Random random)
        {
            var bandRoll = random.NextDouble();
            var band = bandRoll < 0.10 ? 0 : bandRoll < 0.80 ? 1 : bandRoll < 0.98 ? 2 : 3;
            var low = band == 0 ? 0.3 : band == 1 ? 0.9 : band == 2 ? 1 : 1.3;
            var high = band == 0 ? 0.9 : band == 1 ? 1 : band == 2 ? 1.3 : 2;
            var value = Math.Clamp((int)Math.Round(price * (low + (high - low) * random.NextDouble())), minimum, maximum);
            var total = joint[0, value] + joint[1, value] + joint[2, value];
            var roll = random.NextDouble() * total;
            var category = 2;
            for (var candidate = 0; candidate < 3; candidate++)
            {
                if (roll < joint[candidate, value]) { category = candidate; break; }
                roll -= joint[candidate, value];
            }

            var models = new List<string>();
            var remaining = value;
            var currentCategory = 0;
            for (var slot = 0; slot < choices.Length; slot++)
            {
                double Weight(Choice choice)
                {
                    if (choice.Value > remaining) return 0;
                    var nextCategory = Math.Max(currentCategory, choice.Rarity);
                    if (nextCategory > category) return 0;
                    double tail = 0;
                    for (var suffixCategory = 0; suffixCategory < 3; suffixCategory++)
                        if (Math.Max(nextCategory, suffixCategory) == category)
                            tail += suffix[slot + 1, suffixCategory, remaining - choice.Value];
                    return choice.Weight * tail;
                }
                double sum = 0;
                foreach (var choice in choices[slot]) sum += Weight(choice);
                if (sum <= 0) throw new InvalidOperationException("物资箱固定金额的后续组合不存在。");
                roll = random.NextDouble() * sum;
                Choice? selected = null;
                foreach (var choice in choices[slot])
                {
                    var weight = Weight(choice);
                    if (weight <= 0) continue;
                    selected = choice;
                    if (roll < weight) break;
                    roll -= weight;
                }
                if (selected == null) throw new InvalidOperationException("物资箱组合抽取失败。");
                if (selected.Model != null) models.Add(selected.Model);
                remaining -= selected.Value;
                currentCategory = Math.Max(currentCategory, selected.Rarity);
            }
            if (remaining != 0 || currentCategory != category || models.Count == 0)
                throw new InvalidOperationException("物资箱内容与目标金额或T5类别不一致。");
            return new Plan(models.ToArray(), value, band, category);
        }
    }
}
