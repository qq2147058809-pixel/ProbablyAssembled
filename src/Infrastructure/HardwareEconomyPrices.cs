using System;
using System.Collections.Generic;

namespace PCExpansion;

/// <summary>硬件基础价值的唯一价格表；市场、议价、整机加价与实际组成另行处理。</summary>
internal static class HardwareEconomyPrices
{
    private static readonly Dictionary<string, long[]> Components = new(StringComparer.Ordinal)
    {
        ["computer_case"] = new long[] { 180, 300, 500, 850, 1400 },
        ["component_psu"] = new long[] { 80, 130, 220, 380, 650 },
        ["component_motherboard"] = new long[] { 100, 170, 290, 500, 850 },
        ["component_hdd"] = new long[] { 70, 120, 210, 360, 600 },
        ["component_ram"] = new long[] { 60, 100, 180, 265, 350 },
        ["component_gpu"] = new long[] { 150, 270, 430, 700, 1200 },
        ["component_fan"] = new long[] { 30, 50, 85, 145, 250 },
        ["component_cpu"] = new long[] { 160, 280, 470, 800, 1350 },
        ["component_cooler"] = new long[] { 50, 85, 145, 250, 430 }
    };
    private static readonly Dictionary<string, long> Parts = BuildParts();
    private static readonly long[] MiningBodies = { 80, 225, 540, 1000, 1600 };
    private static readonly long[] ServerBodies = { 140, 180, 530, 1040, 2000 };
    private static readonly long[] UnopenedCrates = { 550, 800, 1250, 2000, 2800 };

    private static int Index(int tier) => Math.Clamp(tier, 1, 5) - 1;
    internal static long Component(string stem, int tier) => Components.TryGetValue(stem, out var values)
        ? values[Index(tier)] : throw new ArgumentOutOfRangeException(nameof(stem), stem, "未知硬件价格类别。");
    internal static long Part(string identifier) => Parts.TryGetValue(identifier, out var value)
        ? value : throw new ArgumentOutOfRangeException(nameof(identifier), identifier, "未知子件价格规格。");
    internal static long DedicatedBody(string kind, int tier) => kind switch
    {
        "mining" => MiningBodies[Index(tier)],
        "server" => ServerBodies[Index(tier)],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知专用机箱价格类别。")
    };
    internal static long UnopenedCrate(int tier) => UnopenedCrates[Index(tier)];

    private static Dictionary<string, long> BuildParts()
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        void Add(string key, long value) => result.Add("pcrepair." + key, value);
        void Cores(string key, params long[] values)
        {
            for (var tier = 1; tier <= 5; tier++) Add(key + "_t" + tier, values[tier - 1]);
        }
        Add("gpu_body", 10); Add("ram_body", 10);
        Add("ssd_body", 5); Add("psu_body", 5); Add("fan_body", 5); Add("cooler_body", 5);
        Cores("gpu_core", 110, 230, 375, 630, 1070);
        Cores("ssd_controller", 60, 105, 185, 315, 515);
        Cores("psu_power", 75, 125, 215, 375, 645);
        Cores("fan_rotor", 25, 45, 80, 140, 245);
        Cores("cooler_thermal", 40, 75, 135, 235, 415);
        Add("gpu_memory_4gb", 15); Add("gpu_memory_8gb", 30);
        Add("ram_memory_4gb", 25); Add("ram_memory_8gb", 45); Add("ram_memory_16gb", 85);
        Add("ssd_flash_256gb", 5); Add("ssd_flash_512gb", 10); Add("ssd_flash_1024gb", 20);
        Add("cooler_fan_standard", 5); Add("cooler_fan_enhanced", 10);
        return result;
    }
}
