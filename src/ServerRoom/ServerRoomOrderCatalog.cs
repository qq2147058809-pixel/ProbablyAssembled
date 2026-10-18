using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PCExpansion;

/// <summary>The daily selection contains complete intact components, never sub-parts or cases.</summary>
internal static class ServerRoomOrderCatalog
{
    internal const int DailyCount = 10;
    private static readonly string[] kinds = {
        "component_cpu", "component_gpu", "component_ram", "component_hdd",
        "component_motherboard", "component_psu", "component_fan", "component_cooler"
    };
    internal sealed class Model
    {
        public Model() { }
        public string Kind { get; set; } = "";
        public int Tier { get; set; }
        internal string Key => Kind + ":" + Tier;
    }
    internal static bool IsComponent(string? kind) => kinds.Contains(kind, StringComparer.Ordinal);
    internal static string Identifier(string kind, int tier)
    {
        if (tier is < 1 or > 5) throw new InvalidOperationException("预订档位无效。");
        if (IsComponent(kind)) return "pcrepair." + kind + "_t" + tier;
        if (kind is "mining" or "server") return "pcrepair." + (kind == "mining" ? "mining_case" : "server_case") + "_t" + tier;
        throw new InvalidOperationException("预订型号无效。");
    }
    internal static IReadOnlyList<Model> Pick(Random random)
    {
        var pool = kinds.SelectMany(kind => Enumerable.Range(1, 5).Select(tier => new Model { Kind = kind, Tier = tier })).ToArray();
        for (var i = pool.Length - 1; i > 0; i--)
        { var index = random.Next(i + 1); (pool[i], pool[index]) = (pool[index], pool[i]); }
        return pool.Take(DailyCount).ToArray();
    }
    internal static IReadOnlyList<Model> ForDay(string run, int slot, int day)
    {
        if (string.IsNullOrWhiteSpace(run) || slot < 0 || day < 0) throw new InvalidOperationException("每日配件目录会话无效。");
        // Stable seed also protects reloads of a same-day save made before the
        // first catalog publication. The published selection remains authoritative.
        var identity = run + "\0" + slot.ToString(CultureInfo.InvariantCulture) + "\0" + day.ToString(CultureInfo.InvariantCulture);
        uint seed = 2166136261;
        foreach (var character in identity) seed = unchecked((seed ^ character) * 16777619);
        return Pick(new Random(unchecked((int)seed)));
    }
}
