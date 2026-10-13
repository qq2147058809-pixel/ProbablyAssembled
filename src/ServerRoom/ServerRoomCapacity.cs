using System;
using System.Collections.Generic;
using System.Linq;

namespace PCExpansion;

/// <summary>读取真实 GPU 显存和本台服务器内存；算力与费用不操作经营状态。</summary>
internal static class ServerRoomCapacity
{
    private static readonly int[] compute = { 10, 16, 25, 39, 60 };
    private static readonly int[] gpuMemory = { 8, 8, 12, 16, 32 };
    private static readonly int[] cpuMemory = { 8, 16, 32, 48, 64 };
    private static readonly int[] fee = { 1, 2, 3, 4, 6 };

    internal sealed class GpuResult
    {
        internal GpuResult(int index, int tier, long memory, bool intact)
        {
            Index = index; Tier = tier; MemoryGB = memory; Intact = intact;
            BaseCompute = compute[tier - 1]; RequiredMemoryGB = gpuMemory[tier - 1];
            Fraction = Math.Min((decimal)memory / RequiredMemoryGB, 1m);
            EffectiveCompute = intact ? BaseCompute * Fraction : 0m;
        }
        internal int Index { get; }
        internal int Tier { get; }
        internal bool Intact { get; }
        internal long MemoryGB { get; }
        internal long RequiredMemoryGB { get; }
        internal decimal BaseCompute { get; }
        internal decimal Fraction { get; }
        internal decimal EffectiveCompute { get; }
    }

    internal sealed class Result
    {
        internal Result(string kind, int tier, decimal baseCompute, decimal effectiveCompute, long memory, long required,
            int dailyFee, ServerRoomMachine.Assessment assessment, IReadOnlyList<GpuResult> gpus)
        {
            Kind = kind; Tier = tier; BaseCompute = baseCompute; EffectiveCompute = effectiveCompute;
            MemoryGB = memory; RequiredMemoryGB = required; DailyFee = dailyFee;
            CanRun = assessment.CanRun; Intact = assessment.Intact; MissingParts = assessment.MissingParts; Gpus = gpus;
        }
        internal string Kind { get; }
        internal int Tier { get; }
        internal bool CanRun { get; }
        internal bool Intact { get; }
        internal decimal BaseCompute { get; }
        internal decimal EffectiveCompute { get; }
        internal decimal Fraction => BaseCompute == 0 ? 0 : EffectiveCompute / BaseCompute;
        internal long MemoryGB { get; }
        internal long RequiredMemoryGB { get; }
        // Configuration fee only. Off/missing/broken machines are not charged here.
        internal int DailyFee { get; }
        internal IReadOnlyList<string> MissingParts { get; }
        internal IReadOnlyList<GpuResult> Gpus { get; }
    }

    private static int Tier(WorkroomItemCodec.Snapshot item) => item.Nodes[0].Composition is { } parts
        ? WorkroomComponentParts.EffectiveTier(parts) : Components.Find(item.Identifier)!.Tier;
    private static long Memory(WorkroomItemCodec.Snapshot item)
    {
        if (item.Nodes[0].Composition is { } parts)
            return parts.Aggregate(0L, (sum, part) => checked(sum + (part.Nodes[0].Part?.CapacityGB ?? 0)));
        if (!WorkroomComponentTemplates.TryWhole(item.Identifier, out var family, out var tier, out _))
            return 0;
        return family.Kinds.Aggregate(0L, (sum, kind) => checked(sum +
            (long)family.DefaultCount(kind, tier) * WorkroomComponentTemplates.DefaultPart(family, kind, tier).CapacityGB));
    }

    internal static Result Read(WorkroomMachineAssembly.MachineInfo info)
    {
        var assessment = ServerRoomMachine.Assess(info);
        if (!ServerRoomMachine.TryDefinition(info.Body.Identifier, out var definition))
            throw new InvalidOperationException("专用机器定义缺失。");
        var dailyFee = fee[definition.Tier - 1];
        decimal baseCompute = 0, effective = 0;
        long memory = 0, required = 0;
        var gpus = new List<GpuResult>();
        foreach (var installed in info.Parts)
        {
            if (installed.Kind == "ram")
            { memory = checked(memory + Memory(installed.Item)); dailyFee = checked(dailyFee + 1); continue; }
            var tier = Tier(installed.Item);
            baseCompute += compute[tier - 1]; dailyFee = checked(dailyFee + fee[tier - 1]);
            if (installed.Kind == "gpu")
            {
                // The full machine assessment is used for run admission. A broken
                // card still has a configured fee, but cannot contribute compute.
                var gpu = new GpuResult(installed.Index, tier, Memory(installed.Item), ServerRoomMachine.IntactComponent(installed.Item));
                gpus.Add(gpu); memory = checked(memory + gpu.MemoryGB); required = checked(required + gpu.RequiredMemoryGB);
                effective += gpu.EffectiveCompute;
            }
            else required = checked(required + cpuMemory[tier - 1]);
        }
        if (info.Kind == ServerRoomMachine.Server)
            effective = required == 0 ? 0 : baseCompute * Math.Min((decimal)memory / required, 1m);
        if (!assessment.CanRun) effective = 0;
        return new Result(info.Kind, definition.Tier, baseCompute, effective, memory, required, dailyFee, assessment,
            Array.AsReadOnly(gpus.OrderBy(gpu => gpu.Index).ToArray()));
    }
}
