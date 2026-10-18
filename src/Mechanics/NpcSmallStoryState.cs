using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PCExpansion;

/// <summary>Small conversations have their own receipts; neither core's contract is changed.</summary>
internal static class NpcSmallStoryState
{
    internal const string SaveKey = "pcexpansion.npc-small-stories.v1";
    internal static readonly string[] NodeIds = { "L1", "L2", "L3", "L4", "L5", "J1", "J2", "J3", "J4", "J5" };
    internal sealed class Node
    {
        public string Status { get; set; } = "new";
        public int CompletedDay { get; set; } = -1;
        public WorkroomItemCodec.Snapshot? Item { get; set; }
        public List<WorkroomItemCodec.Snapshot> Gifts { get; set; } = new();
        public int CashBefore { get; set; }
        public int CashTarget { get; set; }
    }
    internal sealed class Visit
    {
        public string Token { get; set; } = "";
        public string Node { get; set; } = "";
        public string Entry { get; set; } = "";
        public int Day { get; set; } = -1;
        public int LastStoryDay { get; set; } = -1;
        public bool FinishedOnce { get; set; }
        public double TurnoverAtArrival { get; set; }
        public bool StoryEnded { get; set; }
    }
    internal sealed class Document
    {
        public int Version { get; set; } = 1;
        public string Run { get; set; } = "";
        public int Slot { get; set; }
        public long Revision { get; set; }
        public Dictionary<string, Node> Nodes { get; set; } = NodeIds.ToDictionary(id => id, _ => new Node());
        public Visit Luoxi { get; set; } = new();
        public Visit Jiangbai { get; set; } = new();
    }
    internal static Document New(string run, int slot) => new() { Run = run, Slot = slot };
    internal static string Serialize(Document value) => JsonSerializer.Serialize(value, WorkroomStorageState.JsonOptions);
    internal static Document Clone(Document value) => Parse(Serialize(value), value.Run, value.Slot);
    internal static Document Parse(string raw, string run, int slot)
    {
        using var json = JsonDocument.Parse(raw);
        RequireUnique(json.RootElement);
        RequireFields(json.RootElement, "Version", "Run", "Slot", "Revision", "Nodes", "Luoxi", "Jiangbai");
        RequireFields(json.RootElement.GetProperty("Nodes"), NodeIds);
        foreach (var field in json.RootElement.GetProperty("Nodes").EnumerateObject())
            RequireFields(field.Value, "Status", "CompletedDay", "Item", "Gifts", "CashBefore", "CashTarget");
        foreach (var actor in new[] { "Luoxi", "Jiangbai" })
            RequireFields(json.RootElement.GetProperty(actor), "Token", "Node", "Entry", "Day", "LastStoryDay", "FinishedOnce", "TurnoverAtArrival", "StoryEnded");
        var value = JsonSerializer.Deserialize<Document>(raw, WorkroomStorageState.JsonOptions)
            ?? throw new InvalidOperationException("小剧情记录为空。");
        if (value.Version != 1 || value.Run != run || string.IsNullOrEmpty(run) || value.Slot != slot || value.Revision < 0 ||
            value.Nodes == null || !NodeIds.All(value.Nodes.ContainsKey)) throw new InvalidOperationException("小剧情记录会话无效。");
        var allIdentities = new HashSet<int>();
        foreach (var pair in value.Nodes)
        {
            var n = pair.Value;
            if (n == null || n.Status is not ("new" or "accepted" or "consuming" or "paying" or "paid" or "gift_pending" or "done" or "refused") ||
                !Day(n.CompletedDay) || n.Gifts == null || n.Gifts.Count is not (0 or 2) ||
                n.CompletedDay != -1 && n.Status is not ("done" or "refused")) throw new InvalidOperationException("小剧情节点状态无效。");
            if (pair.Key is not ("L2" or "J2" or "L4") && (n.Status is not ("new" or "done") || n.Item != null || n.Gifts.Count != 0))
                throw new InvalidOperationException("闲聊不能包含交易。");
            if (pair.Key == "L4" && n.Status is not ("new" or "gift_pending" or "done" or "refused") ||
                pair.Key == "J2" && n.Status == "gift_pending") throw new InvalidOperationException("小剧情节点交易阶段不符。");
            if (n.Item != null)
            {
                WorkroomItemCodec.Validate(n.Item);
                if (pair.Key is not ("L2" or "J2") || n.Item.Identifier != (pair.Key == "L2" ? "pcrepair.component_psu_t1" : "pcrepair.component_fan_t1") ||
                    n.Item.Count != 1 || n.Item.Broken) throw new InvalidOperationException("小委托货物不符。");
                foreach (var id in WorkroomItemCodec.Identities(n.Item))
                    if (!allIdentities.Add(id)) throw new InvalidOperationException("小剧情交接身份重复。");
            }
            if (n.Status is "consuming" or "paying" or "paid" && n.Item == null)
                throw new InvalidOperationException("小委托缺少交接凭据。");
            if (pair.Key == "L2" && n.Status is "gift_pending" or "done" && n.Item == null ||
                pair.Key == "J2" && n.Status == "done" && n.Item == null)
                throw new InvalidOperationException("已成交委托缺少原交易凭据。");
            if (n.Status == "paying" && ((long)n.CashTarget - n.CashBefore != (pair.Key == "L2" ? 75 : 30)))
                throw new InvalidOperationException("小委托付款凭据不符。");
            if (n.Gifts.Count != 0)
            {
                if (pair.Key is not ("L2" or "L4")) throw new InvalidOperationException("节点没有材料奖励。");
                var ids = new HashSet<int>();
                for (var i = 0; i < 2; i++)
                {
                    var gift = n.Gifts[i]; WorkroomItemCodec.Validate(gift);
                    if (gift.Identifier != (i == 0 ? "common_electronic" : "wire") || gift.Count != 1 || gift.Broken ||
                        WorkroomItemCodec.Identities(gift).Any(id => !ids.Add(id) || !allIdentities.Add(id))) throw new InvalidOperationException("材料赠礼凭据不符。");
                }
            }
            if (n.Status == "gift_pending" && n.Gifts.Count != 2) throw new InvalidOperationException("待领材料缺少凭据。");
        }
        ValidateVisit(value.Luoxi, 'L'); ValidateVisit(value.Jiangbai, 'J');
        return value;
    }
    private static bool Day(int day) => day == -1 || day >= 1;
    private static void ValidateVisit(Visit v, char actor)
    {
        if (v == null || v.Token == null || v.Node == null || v.Entry == null || !Day(v.Day) || !Day(v.LastStoryDay) ||
            !double.IsFinite(v.TurnoverAtArrival) || v.TurnoverAtArrival < 0 ||
            v.Token.Length != 0 && (!Guid.TryParseExact(v.Token, "N", out _) || v.Day < 1) ||
            v.Token.Length == 0 && (v.Node.Length != 0 || v.Entry.Length != 0 || v.StoryEnded) ||
            v.Node.Length != 0 && (!NodeIds.Contains(v.Node) || v.Node[0] != actor || v.Entry.Length == 0 || !v.Entry.StartsWith(v.Node + "-", StringComparison.Ordinal)) ||
            v.Node.Length == 0 && v.Entry.Length != 0) throw new InvalidOperationException("小剧情来访凭据无效。");
    }
    private static void RequireFields(JsonElement root, params string[] fields)
    {
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != fields.Length || fields.Any(f => !root.TryGetProperty(f, out _)))
            throw new InvalidOperationException("小剧情记录缺少字段或含未知字段。");
    }
    private static void RequireUnique(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in root.EnumerateObject())
            { if (!names.Add(field.Name)) throw new InvalidOperationException("小剧情记录字段重复。"); RequireUnique(field.Value); }
        }
        else if (root.ValueKind == JsonValueKind.Array) foreach (var child in root.EnumerateArray()) RequireUnique(child);
    }

    // A core never depends on these small nodes. Core pending visits are handled by the caller.
    internal static string Select(Document d, bool luoxi, int day, string corePhase, bool reached25)
    {
        var v = luoxi ? d.Luoxi : d.Jiangbai;
        if (v.LastStoryDay == day) return "";
        bool Seen(string id) => d.Nodes[id].CompletedDay >= 1;
        bool Waiting(string id) => !Seen(id);
        bool After(string id, int days) => Seen(id) && (long)day >= (long)d.Nodes[id].CompletedDay + days;
        if (luoxi)
        {
            if (corePhase is "success" or "refused" or "failed" && Waiting("L3"))
                return corePhase == "success" ? "L3-001" : corePhase == "refused" ? "L3-301" : "L3-401";
            if (Waiting("L1") && v.TurnoverAtArrival > 0) return "L1-001";
            if (Waiting("L2") && Seen("L1") && v.TurnoverAtArrival >= StoryContent.Number("L2.turnover", 2500) && d.Nodes["L2"].Status == "new") return "L2-001";
            if (corePhase == "success" && Waiting("L4") && After("L3", StoryContent.Number("L4.delayDays", 3))) return Resume(d, "L4");
            if (corePhase == "success" && Waiting("L5") && After("L4", StoryContent.Number("L5.delayDays", 5)) && v.TurnoverAtArrival >= StoryContent.Number("L5.turnover", 10000)) return "L5-001";
            if (Waiting("L2") && d.Nodes["L2"].Status != "new") return Resume(d, "L2");
        }
        else
        {
            if (corePhase == "refused") return "";
            if (corePhase == "success" && Waiting("J3")) return "J3-001";
            if (Waiting("J1") && v.FinishedOnce) return "J1-001";
            if (Waiting("J2") && Seen("J1") && reached25 && d.Nodes["J2"].Status == "new") return "J2-001";
            if (corePhase == "success" && Waiting("J4") && Seen("J3")) return "J4-001";
            if (corePhase == "success" && Waiting("J5") && After("J4", StoryContent.Number("J5.delayDays", 7))) return "J5-001";
            if (Waiting("J2") && d.Nodes["J2"].Status != "new") return Resume(d, "J2");
        }
        return "";
    }
    internal static string Resume(Document d, string node) => (node, d.Nodes[node].Status) switch
    {
        ("L2", "accepted" or "consuming" or "paying") => "L2-301",
        ("L2", "paid" or "gift_pending") => "L2-330",
        ("L2", "done") => "L2-332",
        ("L2", "refused") => "L2-202",
        ("J2", "accepted" or "consuming" or "paying") => "J2-301",
        ("J2", "paid" or "done") => "J2-310",
        ("J2", "refused") => "J2-202",
        ("L4", "gift_pending") => "L4-301",
        ("L4", "done") => "L4-303",
        ("L4", "refused") => "L4-202",
        _ => node + "-001"
    };
}
