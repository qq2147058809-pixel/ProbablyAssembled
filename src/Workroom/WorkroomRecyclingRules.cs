using System;
using System.Linq;

namespace PCExpansion;

/// <summary>仅用于回收的物理规格比较和新增破损整件资格，不放宽一般快照等价。</summary>
internal static class WorkroomRecyclingRules
{
    private static bool Bare(WorkroomItemCodec.Snapshot item) => item.Count == 1 && item.Nodes.Count == 1 &&
        item.Nodes[0].Machine == null && item.Nodes[0].Composition == null && item.Nodes[0].SlotItems.Count == 0;

    internal static bool WholeTarget(WorkroomItemCodec.Snapshot item)
    {
        var spec = Components.Find(item.Identifier);
        if (spec == null || !spec.Broken || !item.Broken || !Bare(item) || item.Nodes[0].Part != null ||
            !(spec.Owner.Tag == Components.CpuTag || spec.Tier >= 4 &&
                (spec.Owner.Tag == Components.MotherboardTag || spec.Owner.IsCase))) return false;
        // An empty repair/recycling target must not have an active current
        // crate, machine or component-content record.
        foreach (var tag in item.Nodes[0].State.Concat(item.Nodes[0].ModifiedState))
        {
            if (tag.Key != CaseUnboxing.LockedTagName &&
                tag.Key != WorkroomCrateContents.Tag &&
                tag.Key != WorkroomMachineAssembly.Tag &&
                tag.Key != WorkroomComponentParts.CompositionTag &&
                tag.Key != WorkroomComponentParts.PartTag) continue;
            // Disabled tags can remain in native saved state after unboxing.
            // Only a proved disabled marker is harmless; unknown state refuses.
            if (!tag.Value.TryGetValue("valueEnabled", out var enabled) || enabled.ValueKind != System.Text.Json.JsonValueKind.False) return false;
        }
        return true;
    }

    internal static bool SamePhysicalSpec(WorkroomItemCodec.Snapshot a, WorkroomItemCodec.Snapshot b)
    {
        if (a.Broken != b.Broken || !Bare(a) || !Bare(b)) return false;
        if (WholeTarget(a)) return a.Identifier == b.Identifier && WholeTarget(b);
        if (!WorkroomComponentTemplates.TryPart(a.Identifier, out _, out _, out _) ||
            !WorkroomComponentTemplates.TryPart(b.Identifier, out _, out _, out _) ||
            WorkroomComponentTemplates.Definition(a.Identifier).Identifier != WorkroomComponentTemplates.Definition(b.Identifier).Identifier ||
            a.Nodes[0].Part is not { } left || b.Nodes[0].Part is not { } right) return false;
        return left.Version == right.Version && left.Family == right.Family && left.Kind == right.Kind &&
            left.Tier == right.Tier && left.CapacityGB == right.CapacityGB &&
            left.Specification == right.Specification && left.Broken == right.Broken;
    }

    internal static string[] WholeRecipe(Components.Item item) => item.Owner.Tag == Components.CpuTag
        ? new[] { "common_electronic" } : item.Owner.Tag == Components.MotherboardTag
        ? new[] { "common_electronic", "wire", "scrap_metal" }
        : new[] { "scrap_metal", "nuts_metal", "printer_plastic" };

    internal static string WholeFallback(Components.Item item) => item.Owner.Tag == Components.CpuTag ? "common_electronic" : "scrap_metal";
}
