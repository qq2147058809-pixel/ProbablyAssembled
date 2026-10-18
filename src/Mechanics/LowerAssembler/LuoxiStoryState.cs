using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PCExpansion;

/// <summary>Detached, run/slot-bound story record. Native inventory ownership lives in the transfer module.</summary>
internal static class LuoxiStoryState
{
    internal const string SaveKey = "pcrepair.luoxi.story.v1";
    internal sealed class Document
    {
        public int Version { get; set; } = 1;
        public string Run { get; set; } = "";
        public int Slot { get; set; }
        public long Revision { get; set; }
        public string Phase { get; set; } = "new";
        public int AcceptedDay { get; set; } = -1;
        public int DueDay { get; set; } = -1;
        public string Visit { get; set; } = "";
        public string VisitKind { get; set; } = "";
        public bool Arrived { get; set; }
        public int LastVisitDay { get; set; } = -1;
        public bool RewardPending { get; set; }
        public List<WorkroomItemCodec.Snapshot> Goods { get; set; } = new();
        public WorkroomItemCodec.Snapshot? Reward { get; set; }
    }
    internal static Document New(string run, int slot) => new() { Run = run, Slot = slot };
    internal static Document Clone(Document value) => Parse(Serialize(value), value.Run, value.Slot);
    internal static string Serialize(Document value) => JsonSerializer.Serialize(value, WorkroomStorageState.JsonOptions);
    internal static Document Parse(string raw, string run, int slot)
    {
        using var json = JsonDocument.Parse(raw);
        RequireUniqueFields(json.RootElement);
        var required = new[] { "Version", "Run", "Slot", "Revision", "Phase", "AcceptedDay", "DueDay", "Visit", "VisitKind", "Arrived", "LastVisitDay", "RewardPending", "Goods", "Reward" };
        if (json.RootElement.ValueKind != JsonValueKind.Object || json.RootElement.EnumerateObject().Count() != required.Length ||
            required.Any(field => !json.RootElement.TryGetProperty(field, out _)))
            throw new InvalidOperationException("洛夕剧情记录缺少必要字段或含未知字段。");
        var value = JsonSerializer.Deserialize<Document>(raw, WorkroomStorageState.JsonOptions)
            ?? throw new InvalidOperationException("洛夕剧情记录为空。");
        if (value.Version != 1 || value.Run != run || value.Slot != slot || value.Revision < 0 ||
            value.Phase is not ("new" or "delivery_pending" or "holding" or "first" or "grace" or "final" or "success" or "failed" or "refused") ||
            value.Goods == null || value.Visit == null || value.VisitKind == null || value.LastVisitDay < -1 || value.LastVisitDay == 0 ||
            value.Visit.Length != 0 && !Guid.TryParseExact(value.Visit, "N", out _) ||
            value.Visit.Length != 0 && value.LastVisitDay < 1 ||
            value.Visit.Length == 0 && (value.Arrived || value.VisitKind.Length != 0) ||
            value.Visit.Length != 0 && value.VisitKind is not ("offer" or "first" or "final") ||
            value.Phase is "first" or "final" && (!value.Arrived || value.VisitKind != value.Phase) ||
            value.Phase is "delivery_pending" or "holding" or "first" or "grace" or "final" or "success" or "failed" &&
                value.Goods.Count != LuoxiStoryGoods.Identifiers.Length && value.Goods.Count != LuoxiStoryGoods.Identifiers.Length + 1 ||
            value.Phase is "holding" or "first" or "grace" or "final" or "success" or "failed" && (value.AcceptedDay < 1 || value.DueDay < (long)value.AcceptedDay + 3) ||
            value.Phase is "new" or "refused" && value.Goods.Count != 0 ||
            value.Phase is "new" or "refused" or "delivery_pending" && (value.AcceptedDay != -1 || value.DueDay != -1) ||
            value.Phase is "holding" or "first" && value.DueDay != (long)value.AcceptedDay + 3 ||
            value.Phase is "grace" or "final" or "failed" && value.DueDay < (long)value.AcceptedDay + 5 ||
            value.RewardPending && value.Phase != "success" || value.Reward != null && value.Phase != "success")
            throw new InvalidOperationException("洛夕剧情记录版本、会话或状态无效。");
        var identities = new HashSet<int>();
        for (var i = 0; i < value.Goods.Count; i++)
        {
            var item = value.Goods[i];
            WorkroomItemCodec.Validate(item);
            // Preserve already issued current-v1 receipts. Only an unissued
            // pending batch may drop its final PSU; owned originals never vanish.
            var expected = i < LuoxiStoryGoods.Identifiers.Length ? LuoxiStoryGoods.Identifiers[i] : "pcrepair.component_psu_t4";
            if (i > LuoxiStoryGoods.Identifiers.Length || item.Identifier != expected || item.Count != 1 || item.Broken)
                throw new InvalidOperationException("洛夕寄存货单无效。");
            foreach (var id in WorkroomItemCodec.Identities(item))
                if (!identities.Add(id)) throw new InvalidOperationException("洛夕货物身份重复。");
        }
        if (value.Reward != null)
        {
            WorkroomItemCodec.Validate(value.Reward);
            if (value.Reward.Identifier != "pcrepair.component_gpu_t4" || value.Reward.Broken || value.Reward.Count != 1 ||
                WorkroomItemCodec.Identities(value.Reward).Any(identities.Contains))
                throw new InvalidOperationException("洛夕奖励凭据无效。");
        }
        return value;
    }
    private static void RequireUniqueFields(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in value.EnumerateObject())
            {
                if (!names.Add(field.Name)) throw new InvalidOperationException("洛夕剧情记录含重复字段。");
                RequireUniqueFields(field.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) RequireUniqueFields(child);
    }
}
