using System;
using System.Collections.Generic;
using System.Linq;

namespace PCExpansion;

/// <summary>六类配方、固定槽上限、默认来源及真实经济参数。</summary>
internal static class WorkroomComponentTemplates
{
    internal sealed class Template
    {
        internal readonly string Key, Stem;
        internal readonly int BoardWidth, BoardHeight;
        internal readonly string[] Kinds;
        private readonly Dictionary<string, int[]> defaults;
        internal string Name => Text(Key);
        internal Template(string key, string stem, int width, int height, string[] kinds, int[][] quantities)
        {
            Key = key; Stem = stem; BoardWidth = width; BoardHeight = height; Kinds = kinds;
            defaults = kinds.Select((kind, i) => (kind, quantities[i])).ToDictionary(p => p.kind, p => p.Item2);
        }
        internal int Count(string kind, int tier) => MaxCount(kind);
        internal int MaxCount(string kind) => kind == "memory" ? 6 : kind == "flash" ? 4 : defaults.ContainsKey(kind) ? 1 : 0;
        internal int MinCount(string kind) => defaults.ContainsKey(kind) ? 1 : 0;
        internal int DefaultCount(string kind, int tier) => tier is >= 1 and <= 5 && defaults.TryGetValue(kind, out var counts) ? counts[tier - 1] : 0;
        internal string PartName(string kind) => Text(Key + "." + kind);
        internal bool PerformanceKind(string kind) => kind is "core" or "controller" or "power" or "rotor" or "thermal";
        internal int DamageWeight(string kind) => kind switch
        { "core" or "memory" or "controller" or "flash" => 3, "power" or "rotor" or "thermal" => 2, _ => 1 };
    }
    private static int[] Ones() => new[] { 1, 1, 1, 1, 1 };
    internal static readonly IReadOnlyList<Template> All = new[]
    {
        new Template("gpu", "component_gpu", 2, 4, new[] { "body", "core", "memory" }, new[] { Ones(), Ones(), new[] {2,2,3,4,4} }),
        new Template("ram", "component_ram", 1, 3, new[] { "body", "memory" }, new[] { Ones(), new[] {2,2,2,3,4} }),
        new Template("ssd", "component_hdd", 3, 1, new[] { "body", "controller", "flash" }, new[] { Ones(), Ones(), new[] {1,2,2,4,4} }),
        new Template("psu", "component_psu", 2, 2, new[] { "body", "power" }, new[] { Ones(), Ones() }),
        new Template("fan", "component_fan", 2, 2, new[] { "body", "rotor" }, new[] { Ones(), Ones() }),
        new Template("cooler", "component_cooler", 2, 2, new[] { "body", "thermal", "fan" }, new[] { Ones(), Ones(), Ones() })
    };
    internal sealed class PartDefinition
    {
        internal Template Family = null!;
        internal string Kind = "", Identifier = "", Specification = "";
        internal int Tier, CapacityGB, ProcessingTier;
        internal long? FixedValue;
    }
    internal static readonly IReadOnlyList<PartDefinition> Definitions = BuildDefinitions();
    private static readonly Dictionary<string, PartDefinition> byIdentifier = IndexDefinitions();
    private static Dictionary<string, PartDefinition> IndexDefinitions()
    {
        var result = new Dictionary<string, PartDefinition>(StringComparer.Ordinal);
        foreach (var definition in Definitions)
        {
            result.Add(ItemId(definition, false), definition);
            result.Add(ItemId(definition, true), definition);
        }
        return result;
    }
    private static IReadOnlyList<PartDefinition> BuildDefinitions()
    {
        var result = new List<PartDefinition>();
        void Common(string key, string kind, string spec, int capacity, long value, int processing = 1)
        {
            var family = All.First(f => f.Key == key);
            result.Add(new PartDefinition { Family = family, Kind = kind, Specification = spec, CapacityGB = capacity,
                FixedValue = value, ProcessingTier = processing, Tier = 0,
                Identifier = "pcrepair." + key + "_" + kind + (spec == "" ? "" : "_" + spec) });
        }
        void Core(string key, string kind, long[] values)
        {
            var family = All.First(f => f.Key == key);
            for (var tier = 1; tier <= 5; tier++) result.Add(new PartDefinition { Family = family, Kind = kind, Tier = tier,
                ProcessingTier = tier, FixedValue = values[tier - 1], Identifier = PartId(family, kind, tier) });
        }
        foreach (var family in All) Common(family.Key, "body", "", 0, family.Key == "gpu" ? 10 : 5);
        Core("gpu", "core", new long[] {96,168,266,423,635});
        Core("ssd", "controller", new long[] {56,88,135,203,301});
        Core("psu", "power", new long[] {70,111,175,273,423});
        Core("fan", "rotor", new long[] {25,41,67,106,166});
        Core("cooler", "thermal", new long[] {35,60,98,153,243});
        Common("gpu", "memory", "4gb", 4, 12); Common("gpu", "memory", "8gb", 8, 24, 3);
        Common("ram", "memory", "4gb", 4, 25); Common("ram", "memory", "8gb", 8, 40); Common("ram", "memory", "16gb", 16, 70, 3);
        Common("ssd", "flash", "256gb", 256, 4); Common("ssd", "flash", "512gb", 512, 8); Common("ssd", "flash", "1024gb", 1024, 16, 3);
        Common("cooler", "fan", "standard", 0, 5); Common("cooler", "fan", "enhanced", 0, 8, 3);
        return result;
    }
    internal static PartDefinition Definition(string identifier) => byIdentifier.TryGetValue(identifier, out var value) ? value :
        throw new InvalidOperationException("未知部件规格。");
    internal static PartDefinition DefaultPart(Template family, string kind, int tier)
    {
        string? spec = (family.Key, kind) switch
        {
            (_, "body") => "",
            ("gpu", "memory") => (tier == 5 ? 8 : 4) + "gb",
            ("ram", "memory") => (tier == 1 ? 4 : tier == 2 ? 8 : 16) + "gb",
            ("ssd", "flash") => (tier <= 2 ? 256 : tier <= 4 ? 512 : 1024) + "gb",
            ("cooler", "fan") => tier <= 3 ? "standard" : "enhanced",
            _ => null
        };
        return Definition(spec == null ? PartId(family, kind, tier) : "pcrepair." + family.Key + "_" + kind + (spec == "" ? "" : "_" + spec));
    }
    internal static string Text(string key, params object[] args) => LanguageText.Get("workroom.component." + key, args);
    internal static string ItemId(PartDefinition definition, bool broken) => definition.Identifier + (broken ? "_broken" : "");
    internal static string PartId(Template template, string kind, int tier) => "pcrepair." + template.Key + "_" + kind + "_t" + tier;
    internal static string WholeId(Template template, int tier, bool broken) => "pcrepair." + template.Stem + "_t" + tier + (broken ? "_broken" : "");
    internal static bool TryPart(string identifier, out Template template, out string kind, out int tier)
    {
        if (byIdentifier.TryGetValue(identifier, out var value)) { template = value.Family; kind = value.Kind; tier = value.Tier; return true; }
        template = null!; kind = ""; tier = 0; return false;
    }
    internal static bool TryWhole(string identifier, out Template template, out int tier, out bool broken)
    {
        var spec = Components.Find(identifier);
        if (spec != null) foreach (var family in All) if (family.Stem == spec.Owner.Stem)
        { template = family; tier = spec.Tier; broken = spec.Broken; return true; }
        template = null!; tier = 0; broken = false; return false;
    }
    internal static long StandardWholeValue(Template family, int tier) => family.Kinds.Sum(kind =>
        checked(DefaultPart(family, kind, tier).FixedValue!.Value * family.DefaultCount(kind, tier)));
    internal static long BaseValue(Template template, int tier) => StandardWholeValue(template, tier);
    internal static int RamTier(long capacity) => capacity < 16 ? 1 : capacity < 32 ? 2 : capacity < 48 ? 3 : capacity < 64 ? 4 : 5;
    internal static long MaximumWholeValue(Template family, int tier)
    {
        if (family.Key == "ram")
        {
            long maximum = 0;
            var count = family.MaxCount("memory");
            var body = Definition("pcrepair.ram_body").FixedValue!.Value;
            var p4 = Definition("pcrepair.ram_memory_4gb").FixedValue!.Value;
            var p8 = Definition("pcrepair.ram_memory_8gb").FixedValue!.Value;
            var p16 = Definition("pcrepair.ram_memory_16gb").FixedValue!.Value;
            for (var four = 0; four <= count; four++) for (var eight = 0; eight + four <= count; eight++)
            for (var sixteen = 0; sixteen + eight + four <= count; sixteen++)
            {
                var capacity = four * 4 + eight * 8 + sixteen * 16;
                if (capacity > 0 && RamTier(capacity) == tier)
                    maximum = Math.Max(maximum, checked(body + four * p4 + eight * p8 + sixteen * p16));
            }
            return maximum;
        }
        return family.Kinds.Sum(kind => checked(Definitions.Where(d => d.Family == family && d.Kind == kind &&
            (!family.PerformanceKind(kind) || d.Tier == tier)).Max(d => d.FixedValue!.Value) * family.MaxCount(kind)));
    }
    internal static long PartValue(Template template, string kind, int tier, long total)
    {
        if (total < 0) throw new InvalidOperationException("部件总价不能为负。");
        return (long)((decimal)DefaultPart(template, kind, tier).FixedValue!.Value * total / BaseValue(template, tier));
    }
}
