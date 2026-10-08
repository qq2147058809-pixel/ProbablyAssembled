using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;

namespace PCExpansion;

/// <summary>新物资箱的唯一内容封套；完整单件身份在第一次抽取后固定。</summary>
internal static class WorkroomCrateContents
{
    internal const string Tag = "PCREPAIR_CRATE_CONTENTS_V1";
    internal sealed class Document
    {
        public Document() { }
        public int Version { get; set; }
        public List<WorkroomItemCodec.Snapshot> Items { get; set; } = new();
    }

    internal static Document Envelope(List<WorkroomItemCodec.Snapshot> items) => new() { Version = 1, Items = items };
    internal static List<WorkroomItemCodec.Snapshot> Read(GameItem item)
    {
        if (!item.IsTag(Tag)) return new();
        if (!ComputerCase.IsCase(item) || item.IsTag(WorkroomMachineAssembly.Tag))
            throw new InvalidOperationException("物资箱内容与本体类别不符。");
        var saved = WorkroomAssemblyData.Decode<Document>(item.state.GetTag(Tag).valueString)
            ?? throw new InvalidOperationException("物资箱内容封套为空。");
        if (saved.Version != 1) throw new InvalidOperationException("物资箱内容版本不支持。");
        Validate(saved.Items);
        return saved.Items;
    }

    internal static void Write(GameItem item, List<WorkroomItemCodec.Snapshot> items)
    {
        Validate(items);
        var raw = WorkroomAssemblyData.Encode(Envelope(items));
        item.EnableTag(Tag, false);
        item.state.GetTag(Tag).SetString(raw);
    }

    internal static void Clear(GameItem item)
    {
        // Remove the completed contents, including disabled copies, rather than
        // retaining another whole tree inside an empty case's saved state.
        item.state?.dict?.Remove(Tag);
        item.modifiedState?.dict?.Remove(Tag);
    }

    // Only captured/persistent data reaches this check. A new factory may
    // temporarily have a case body before its complete contents are published.
    internal static void ValidateNode(WorkroomItemCodec.Node node)
    {
        if (Components.Find(node.Identifier)?.Owner.IsCase != true) return;
        var hasContents = node.SlotItems.Count != 0;
        var locked = WorkroomMachineAssembly.ActiveTag(node, CaseUnboxing.LockedTagName);
        var spawned = WorkroomMachineAssembly.ActiveTag(node, CaseUnboxing.SpawnedTagName);
        var modifiedLocked = node.ModifiedState.TryGetValue(CaseUnboxing.LockedTagName, out var modified) &&
            modified.TryGetValue("valueEnabled", out var active) && active.ValueKind == System.Text.Json.JsonValueKind.True;
        if ((locked || modifiedLocked) && !hasContents || hasContents && (!locked || !spawned))
            throw new InvalidOperationException("未开封物资箱缺少完整内容或锁定状态，原文保留。");
    }

    internal static void Validate(List<WorkroomItemCodec.Snapshot> items)
    {
        if (items == null || items.Count == 0 || items.Count > CaseContents.SlotTags.Length)
            throw new InvalidOperationException("物资箱内容数量无效。");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var identities = new HashSet<int>();
        foreach (var item in items)
        {
            WorkroomItemCodec.Validate(item);
            var spec = Components.Find(item.Identifier);
            if (spec == null || spec.Owner.IsCase || item.Count != 1 || item.Nodes.Count != 1 ||
                item.Nodes[0].SlotItems.Count != 0 || item.Nodes[0].Composition != null || item.Nodes[0].Machine != null)
                throw new InvalidOperationException("物资箱只能包含当前独立整件。");
            var tag = spec.Owner.Tag;
            counts[tag] = counts.GetValueOrDefault(tag) + 1;
            if (counts[tag] > CaseContents.SlotTags.Count(slot => slot == tag))
                throw new InvalidOperationException("物资箱内容超过抽取类别上限。");
            foreach (var id in WorkroomItemCodec.Identities(item))
                if (!identities.Add(id)) throw new InvalidOperationException("物资箱内容身份重复。");
        }
    }
}
