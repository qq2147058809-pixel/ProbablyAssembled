using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Il2Cpp;

namespace PCExpansion;

internal static class LuoxiStoryGoods
{
    internal static readonly string[] Identifiers = {
        "pcrepair.component_cpu_t4", "pcrepair.component_gpu_t4", "pcrepair.component_motherboard_t4",
        "pcrepair.component_ram_t4", "pcrepair.component_hdd_t4" };

    internal static WorkroomItemCodec.Snapshot Create(string identifier, bool stolen)
    {
        using var timing = new WorkroomFeedbackDiagnostics.Operation("LuoxiGoods " + identifier);
        var item = Components.Create(identifier);
        WorkroomItemCodec.Candidate? candidate = null;
        try
        {
            GeneralHelper.SetItemOwned(item, true);
            if (stolen) StolenHelper.InitStolenItem(item, 30);
            if (stolen) MaterializeOriginalParts(item);
            var result = WorkroomItemCodec.Capture(item, out var nodes);
            candidate = new WorkroomItemCodec.Candidate { Root = item, Nodes = nodes };
            return result;
        }
        finally
        {
            candidate ??= new WorkroomItemCodec.Candidate { Root = item, Nodes = new List<GameItem> { item } };
            WorkroomItemCodec.Destroy(candidate);
        }
    }

    private static void MaterializeOriginalParts(GameItem item)
    {
        // CPU and motherboard are independent whole components, outside the
        // six disassembly families. They retain their native original graph.
        if (!WorkroomComponentTemplates.TryWhole(item.identifier, out var family, out var tier, out _)) return;
        var origin = WorkroomItemCodec.Capture(item);
        if (origin.Broken) throw new InvalidOperationException("寄存货物必须完好。");
        var plan = new List<(string Kind, WorkroomComponentTemplates.PartDefinition Definition, long Value)>();
        foreach (var kind in family.Kinds)
            for (var i = 0; i < family.DefaultCount(kind, tier); i++)
                plan.Add((kind, WorkroomComponentTemplates.DefaultPart(family, kind, tier),
                    WorkroomComponentTemplates.PartValue(family, kind, tier, origin.Value)));
        var remainder = checked(origin.Value - plan.Sum(part => part.Value));
        if (remainder < 0) throw new InvalidOperationException("寄存内部件分价无效。");
        var core = plan.FindIndex(part => family.Key == "ram" ? part.Kind == "memory" : family.PerformanceKind(part.Kind));
        var chosen = plan[core]; plan[core] = (chosen.Kind, chosen.Definition, checked(chosen.Value + remainder));
        var parts = new List<WorkroomItemCodec.Snapshot>();
        foreach (var part in plan)
            parts.Add(WorkroomComponentParts.CaptureFactory(() =>
            {
                var native = WorkroomComponentParts.Create(part.Definition, false, part.Value);
                try
                {
                    GeneralHelper.SetItemOwned(native, true);
                    StolenHelper.InitStolenItem(native, 30);
                    if (part.Kind == "body")
                    {
                        native.EnableTag(WorkroomComponentParts.OriginTag, false);
                        native.state.GetTag(WorkroomComponentParts.OriginTag).SetString(JsonSerializer.Serialize(origin, WorkroomStorageState.JsonOptions));
                    }
                    return native;
                }
                catch { WorkroomItemCodec.Destroy(new WorkroomItemCodec.Candidate { Root = native, Nodes = new List<GameItem> { native } }); throw; }
            }));
        WorkroomComponentParts.ValidateComposition(parts);
        item.EnableTag(WorkroomComponentParts.CompositionTag, false);
        item.state.GetTag(WorkroomComponentParts.CompositionTag).SetString(WorkroomAssemblyData.Encode(parts));
        WorkroomComponentParts.RefreshComposition(item);
    }

    // Identity and internal structure are strict. Rotation, UI placement and
    // derived prices/text are deliberately absent from the custody contract.
    internal static bool SameOriginal(WorkroomItemCodec.Snapshot expected, WorkroomItemCodec.Snapshot actual)
    {
        if (expected.Identifier != actual.Identifier || expected.Count != 1 || actual.Count != 1 || actual.Broken ||
            expected.Nodes.Count != actual.Nodes.Count || expected.Nodes.Count == 0 ||
            expected.Nodes[0].UniqueId != actual.Nodes[0].UniqueId) return false;
        var left = expected.Nodes.OrderBy(node => node.UniqueId).ToArray();
        var right = actual.Nodes.OrderBy(node => node.UniqueId).ToArray();
        for (var i = 0; i < left.Length; i++)
        {
            var x = left[i]; var y = right[i];
            if (x.UniqueId != y.UniqueId || x.Identifier != y.Identifier || x.Machine != null || y.Machine != null ||
                !SamePart(x.Part, y.Part) || !SameChildren(x.SlotItems, y.SlotItems) ||
                (x.Composition == null) != (y.Composition == null) ||
                x.Composition != null && !SameChildren(x.Composition, y.Composition!)) return false;
        }
        return true;
    }
    private static bool SameChildren(List<WorkroomItemCodec.Snapshot> left, List<WorkroomItemCodec.Snapshot> right)
    {
        if (left.Count != right.Count) return false;
        var a = left.OrderBy(item => item.Nodes[0].UniqueId).ToArray();
        var b = right.OrderBy(item => item.Nodes[0].UniqueId).ToArray();
        for (var i = 0; i < a.Length; i++) if (!SameOriginal(a[i], b[i])) return false;
        return true;
    }
    private static bool SamePart(WorkroomComponentParts.PartInfo? left, WorkroomComponentParts.PartInfo? right)
    {
        if (left == null || right == null) return left == right;
        return left.Version == right.Version && left.Family == right.Family && left.Kind == right.Kind &&
            left.Tier == right.Tier && left.CapacityGB == right.CapacityGB && left.Specification == right.Specification &&
            !right.Broken;
    }
}
