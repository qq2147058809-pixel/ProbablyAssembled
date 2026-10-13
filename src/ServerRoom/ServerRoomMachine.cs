using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PCExpansion;

/// <summary>专用机沿用 V3 单树封套；这里只处理保存的实物，不操作现金或原生库存。</summary>
internal static class ServerRoomMachine
{
    internal const string Mining = "mining", Server = "server";

    internal sealed class Definition
    {
        internal Definition(string kind, int tier, long baseValue, int gpuSlots, int cpuSlots, int ramSlots)
        {
            Kind = kind; Tier = tier; BaseValue = baseValue;
            GpuSlots = gpuSlots; CpuSlots = cpuSlots; RamSlots = ramSlots;
            Identifier = "pcrepair." + (kind == Mining ? "mining_case" : "server_case") + "_t" + tier;
        }
        internal string Identifier { get; }
        internal string Kind { get; }
        internal int Tier { get; }
        internal long BaseValue { get; }
        internal int GpuSlots { get; }
        internal int CpuSlots { get; }
        internal int RamSlots { get; }
        internal int Slots(string kind) => kind switch
        { "gpu" => GpuSlots, "cpu" => CpuSlots, "ram" => RamSlots, _ => 0 };
    }

    private static readonly IReadOnlyList<Definition> definitions = Array.AsReadOnly(new[]
    {
        new Definition(Mining, 1, 100, 1, 0, 0), new Definition(Mining, 2, 260, 2, 0, 0),
        new Definition(Mining, 3, 550, 3, 0, 0), new Definition(Mining, 4, 1100, 4, 0, 0),
        new Definition(Mining, 5, 2200, 6, 0, 0),
        new Definition(Server, 1, 120, 0, 1, 2), new Definition(Server, 2, 200, 0, 1, 4),
        new Definition(Server, 3, 500, 0, 2, 6), new Definition(Server, 4, 1100, 0, 3, 8),
        new Definition(Server, 5, 2300, 0, 4, 12)
    });
    private static readonly Dictionary<string, Definition> byId = definitions.ToDictionary(d => d.Identifier, StringComparer.Ordinal);
    internal static IReadOnlyList<Definition> Definitions => definitions;
    internal static bool IsKind(string? kind) => kind is Mining or Server;
    internal static bool TryDefinition(string? identifier, out Definition definition)
    {
        if (identifier != null && byId.TryGetValue(identifier, out var found)) { definition = found; return true; }
        definition = null!; return false;
    }
    internal static Definition DefinitionFor(string kind, int tier) => definitions.FirstOrDefault(d => d.Kind == kind && d.Tier == tier)
        ?? throw new InvalidOperationException("专用机器类型或档位无效。");

    internal sealed class Assessment
    {
        internal Assessment(bool intact, IReadOnlyList<string> missing)
        { Intact = intact; MissingParts = missing; }
        internal bool Intact { get; }
        internal bool CanSell => Intact;
        internal bool CanRun => Intact && MissingParts.Count == 0;
        internal IReadOnlyList<string> MissingParts { get; }
    }

    internal static void ValidateInfo(WorkroomMachineAssembly.MachineInfo info, string identifier, int identity)
    {
        if (info == null || info.Version != 3 || !IsKind(info.Kind) || !TryDefinition(identifier, out var definition) ||
            info.Kind != definition.Kind || info.Parts == null)
            throw new InvalidOperationException("专用机器封套无效。");
        // Reject any nested machine before invoking Codec.Validate: a body cannot
        // refer back to this envelope, and a GPU/RAM composition contains leaves.
        var body = Single(info.Body);
        if (info.Body.Identifier != identifier || body.UniqueId != identity || body.Machine != null ||
            body.Composition != null || body.Part != null || body.SlotItems.Count != 0 || HasAssemblyTag(body))
            throw new InvalidOperationException("专用机器本体不是独立裸箱。");
        WorkroomItemCodec.Validate(info.Body);
        var identities = new HashSet<int>(WorkroomItemCodec.Identities(info.Body));
        var slots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var installed in info.Parts)
        {
            if (installed == null || installed.Index < 0 || installed.Index >= definition.Slots(installed.Kind) ||
                !slots.Add(installed.Kind + "." + installed.Index))
                throw new InvalidOperationException("专用机器插槽无效或重复。");
            ValidateComponent(installed.Kind, installed.Item);
            foreach (var id in WorkroomItemCodec.Identities(installed.Item))
                if (!identities.Add(id)) throw new InvalidOperationException("专用机器内部物品身份重复。");
        }
        _ = Value(info); // Includes overflow detection without adding market modifiers.
    }

    internal static void ValidateNode(WorkroomItemCodec.Node node)
    {
        if (node == null || node.Machine == null || !IsKind(node.Machine.Kind))
            throw new InvalidOperationException("专用机器快照缺少封套。");
        ValidateInfo(node.Machine, node.Identifier, node.UniqueId);
        if (node.Composition != null || node.Part != null || node.SlotItems == null || node.SlotItems.Count != 0 ||
            !WorkroomMachineAssembly.ActiveTag(node, WorkroomMachineAssembly.Tag))
            throw new InvalidOperationException("专用机器快照与 V3 标签不一致。");
        WorkroomAssemblyData.ValidateNode(node);
        if (node.Scalars == null || !node.Scalars.TryGetValue("unitCount", out var count) || count.GetInt32() != 1 ||
            !node.Scalars.TryGetValue("unitValue", out var value) || value.GetInt64() != Value(node.Machine))
            throw new InvalidOperationException("专用机器快照数量或价格与组成不符。");
    }

    private static WorkroomItemCodec.Node Single(WorkroomItemCodec.Snapshot item)
    {
        if (item == null || item.Count != 1 || item.Value < 0 || item.Nodes == null || item.Nodes.Count != 1 ||
            item.Nodes[0] == null || item.Nodes[0].Identifier != item.Identifier || item.Nodes[0].SlotItems == null)
            throw new InvalidOperationException("专用机器使用的实物快照无效。");
        return item.Nodes[0];
    }

    private static bool HasAssemblyTag(WorkroomItemCodec.Node node) =>
        Active(node.State, WorkroomMachineAssembly.Tag) || Active(node.ModifiedState, WorkroomMachineAssembly.Tag);
    private static bool Active(Dictionary<string, Dictionary<string, JsonElement>>? state, string tag) =>
        state != null && state.TryGetValue(tag, out var fields) && fields != null &&
        fields.TryGetValue("valueEnabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;

    private static void ValidateComponent(string kind, WorkroomItemCodec.Snapshot item)
    {
        var node = Single(item);
        var spec = Components.Find(item.Identifier);
        var expected = kind switch { "gpu" => Components.GpuTag, "cpu" => Components.CpuTag, "ram" => Components.RamTag, _ => "" };
        if (spec == null || spec.Tier is < 1 or > 5 || expected.Length == 0 || spec.Owner.Tag != expected ||
            node.Part != null || node.Machine != null || node.SlotItems.Count != 0 || HasAssemblyTag(node))
            throw new InvalidOperationException("专用机器只接受对应完整配件。");
        if (node.Composition is { } composition)
        {
            if (composition.Count == 0) throw new InvalidOperationException("完整配件的组成为空。");
            foreach (var part in composition)
            {
                var leaf = Single(part);
                if (leaf.Machine != null || leaf.Composition != null || leaf.SlotItems.Count != 0 || HasAssemblyTag(leaf))
                    throw new InvalidOperationException("专用机器配件的组成不是叶子件。");
            }
            WorkroomComponentParts.ValidateComposition(composition);
            if (WorkroomComponentParts.EffectiveTier(composition) != spec.Tier ||
                composition.Aggregate(0L, (sum, part) => checked(sum + part.Value)) != item.Value)
                throw new InvalidOperationException("专用机器配件身份或价格与真实组成不符。");
        }
        WorkroomItemCodec.Validate(item);
    }

    internal static long Value(WorkroomMachineAssembly.MachineInfo info)
    {
        if (info == null || info.Body == null || info.Parts == null || info.Body.Value < 0)
            throw new InvalidOperationException("专用机器缺少保存价值。");
        return info.Parts.Aggregate(info.Body.Value, (sum, part) => part?.Item == null || part.Item.Value < 0
            ? throw new InvalidOperationException("专用机器配件保存价值无效。") : checked(sum + part.Item.Value));
    }

    private static bool SnapshotBroken(WorkroomItemCodec.Snapshot item) => item.Broken || Components.Find(item.Identifier)?.Broken == true ||
        item.Nodes.Any(node => Active(node.State, Components.BrokenTag) || Active(node.ModifiedState, Components.BrokenTag) ||
            Active(node.State, "BROKEN_MACHINE") || Active(node.ModifiedState, "BROKEN_MACHINE") || node.Part?.Broken == true ||
            node.Composition?.Any(SnapshotBroken) == true);
    internal static bool IntactComponent(WorkroomItemCodec.Snapshot item) => !SnapshotBroken(item);
    internal static bool Broken(WorkroomMachineAssembly.MachineInfo info) =>
        SnapshotBroken(info.Body) || info.Parts.Any(part => SnapshotBroken(part.Item));

    internal static List<CaseEconomy.PartRecord> Flatten(WorkroomMachineAssembly.MachineInfo info)
    {
        ValidateInfo(info, info.Body.Identifier, info.Body.Nodes[0].UniqueId);
        return info.Parts.Select(part =>
        {
            var spec = Components.Find(part.Item.Identifier)!;
            var tier = part.Item.Nodes[0].Composition is { } composition ? WorkroomComponentParts.EffectiveTier(composition) : spec.Tier;
            return new CaseEconomy.PartRecord(spec.Owner.Tag, tier, SnapshotBroken(part.Item), part.Item.Value);
        }).ToList();
    }

    internal static Assessment Assess(WorkroomMachineAssembly.MachineInfo info)
    {
        ValidateInfo(info, info.Body.Identifier, info.Body.Nodes[0].UniqueId);
        var required = info.Kind == Mining ? new[] { "gpu" } : new[] { "cpu", "ram" };
        var missing = required.Where(kind => !info.Parts.Any(part => part.Kind == kind)).ToArray();
        return new Assessment(!Broken(info), Array.AsReadOnly(missing));
    }
}
