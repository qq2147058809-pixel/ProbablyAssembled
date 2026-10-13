using System;
using System.Collections.Generic;

namespace PCExpansion;

/// <summary>只读目录投影；分组不改变保管记录、单件数量或取出事务。</summary>
internal static class WorkroomStorageCatalog
{
    internal static readonly string[] Categories =
        { "all", "cpu", "motherboard", "gpu", "ram", "ssd", "psu", "fan", "cooler", "case", "material", "other" };

    internal sealed class Group
    {
        private readonly List<WorkroomStorageState.Record> records = new();
        internal Group(WorkroomStorageState.Record record) => records.Add(record);
        internal WorkroomStorageState.Record Representative => records[0];
        internal int Count => records.Count;
        internal bool CanTake => Representative.Place == "box" && Representative.Item.Count == 1;
        internal void Add(WorkroomStorageState.Record record) => records.Add(record);

        internal Group? Filter(string? query, string? category, string? state, string? type)
        {
            Group? result = null;
            foreach (var record in records)
            {
                if (!Matches(record, query, category, state, type)) continue;
                if (result == null) result = new Group(record);
                else result.Add(record);
            }
            return result;
        }
    }

    private static bool Matches(WorkroomStorageState.Record record, string? query, string? category, string? state, string? type)
    {
        var item = record.Item;
        if (!string.IsNullOrEmpty(category) && category != "all" && category != CategoryFor(item.Identifier)) return false;
        if (state == "intact" && item.Broken || state == "broken" && !item.Broken) return false;
        if (!string.IsNullOrEmpty(type) && type != "all" && type != TypeFor(item)) return false;
        var text = (query ?? "").Trim();
        return text.Length == 0 || (item.Name ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
            (item.Identifier ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Group only a read-only view. The source Count=1 records and Take transaction remain separate.
    internal static IReadOnlyList<Group> Build(IEnumerable<WorkroomStorageState.Record> records)
    {
        var result = new List<Group>();
        var groups = new Dictionary<(string Name, long Value, bool Broken), Group>();
        foreach (var record in records)
        {
            if (record.Place != "box" && record.Place != "cleanup" && record.Place != "delivery") continue;
            var item = record.Item;
            var eligible = record.Place == "box" && WorkroomItemCodec.CanDisplayGroup(item);
            var key = (item.Name ?? "", item.Value, item.Broken);
            if (eligible && groups.TryGetValue(key, out var existing)) existing.Add(record);
            else
            {
                var group = new Group(record);
                result.Add(group);
                if (eligible) groups.Add(key, group);
            }
        }
        return result;
    }

    internal static string TypeFor(WorkroomItemCodec.Snapshot item)
    {
        if (!WorkroomItemCodec.IsUnassembledSingle(item)) return "other";
        if (WorkroomComponentTemplates.TryPart(item.Identifier, out _, out _, out _)) return "dismantled";
        if (WorkroomItemCodec.IsRepairMaterial(item.Identifier)) return "other";
        return CategoryFor(item.Identifier) is "cpu" or "motherboard" or "gpu" or "ram" or "ssd" or
            "psu" or "fan" or "cooler" or "case" ? "component" : "other";
    }

    internal static string CategoryFor(string identifier)
    {
        if (WorkroomItemCodec.IsRepairMaterial(identifier)) return "material";
        if (WorkroomComponentTemplates.TryPart(identifier, out var family, out _, out _)) return family.Key;
        if (ServerRoomMachine.TryDefinition(identifier, out _)) return "case";
        return Components.Find(identifier)?.Owner.Stem switch
        {
            "component_cpu" => "cpu", "component_motherboard" => "motherboard", "component_gpu" => "gpu",
            "component_ram" => "ram", "component_hdd" => "ssd", "component_psu" => "psu",
            "component_fan" => "fan", "component_cooler" => "cooler", "computer_case" => "case", _ => "other"
        };
    }
}
