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
        internal Group(WorkroomStorageState.Record record, string category)
        { records.Add(record); Category = category; }
        internal WorkroomStorageState.Record Representative => records[0];
        internal IReadOnlyList<WorkroomStorageState.Record> Records => records;
        internal int Count => records.Count;
        internal string Category { get; }
        internal bool CanTake => Representative.Place == "box" && Representative.Item.Count == 1;
        internal void Add(WorkroomStorageState.Record record) => records.Add(record);

        internal bool Matches(string? query, string? category, string? state)
        {
            var item = Representative.Item;
            if (!string.IsNullOrEmpty(category) && category != "all" && category != Category) return false;
            if (state == "intact" && item.Broken || state == "broken" && !item.Broken) return false;
            var text = (query ?? "").Trim();
            return text.Length == 0 || (item.Name ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (item.Identifier ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    // Call only when the storage epoch/revision changes. Each eligible instance
    // is parsed once; dictionary grouping preserves the first appearance order.
    internal static IReadOnlyList<Group> Build(IEnumerable<WorkroomStorageState.Record> records)
    {
        var result = new List<Group>();
        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (record.Place != "box" && record.Place != "cleanup" && record.Place != "delivery") continue;
            var category = CategoryFor(record.Item.Identifier);
            var key = record.Place == "box" ? WorkroomItemCodec.CatalogKey(record.Item) : null;
            if (key != null && groups.TryGetValue(key, out var existing)) existing.Add(record);
            else
            {
                var group = new Group(record, category);
                result.Add(group);
                if (key != null) groups.Add(key, group);
            }
        }
        return result;
    }

    internal static string CategoryFor(string identifier)
    {
        if (WorkroomItemCodec.IsRepairMaterial(identifier)) return "material";
        if (WorkroomComponentTemplates.TryPart(identifier, out var family, out _, out _)) return family.Key;
        return Components.Find(identifier)?.Owner.Stem switch
        {
            "component_cpu" => "cpu", "component_motherboard" => "motherboard", "component_gpu" => "gpu",
            "component_ram" => "ram", "component_hdd" => "ssd", "component_psu" => "psu",
            "component_fan" => "fan", "component_cooler" => "cooler", "computer_case" => "case", _ => "other"
        };
    }
}
