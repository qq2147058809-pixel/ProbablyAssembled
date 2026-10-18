using System;
using System.Linq;
using System.Text.Json;

namespace PCExpansion;

internal static class JiangbaiStoryState
{
    internal const string SaveKey = "pcrepair.jiangbai.hideout.v1";
    internal sealed class Document
    {
        public int Version { get; set; } = 1;
        public string Run { get; set; } = "";
        public int Slot { get; set; }
        public long Revision { get; set; }
        public string Phase { get; set; } = "new";
        public int AcceptedDay { get; set; } = -1;
        public int DueDay { get; set; } = -1;
        public double Deducted { get; set; }
        public double ReputationBefore { get; set; }
        public double ReputationTarget { get; set; }
        public string Visit { get; set; } = "";
        public string VisitKind { get; set; } = "";
        public bool Arrived { get; set; }
        public int LastVisitDay { get; set; } = -1;
        public bool FarewellStarted { get; set; }
        public bool RewardPending { get; set; }
        public WorkroomItemCodec.Snapshot? Reward { get; set; }
    }

    internal static Document New(string run, int slot) => new() { Run = run, Slot = slot };
    internal static string Serialize(Document value) => JsonSerializer.Serialize(value, WorkroomStorageState.JsonOptions);
    internal static Document Clone(Document value) => Parse(Serialize(value), value.Run, value.Slot);
    internal static Document Parse(string raw, string run, int slot)
    {
        using var json = JsonDocument.Parse(raw);
        var fields = new[] { "Version", "Run", "Slot", "Revision", "Phase", "AcceptedDay", "DueDay", "Deducted",
            "ReputationBefore", "ReputationTarget", "Visit", "VisitKind", "Arrived", "LastVisitDay", "FarewellStarted", "RewardPending", "Reward" };
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != fields.Length ||
            fields.Any(field => !root.TryGetProperty(field, out _))) throw new InvalidOperationException("江白剧情记录字段无效。");
        var value = JsonSerializer.Deserialize<Document>(raw, WorkroomStorageState.JsonOptions)
            ?? throw new InvalidOperationException("江白剧情记录为空。");
        if (value.Version != 1 || value.Run != run || value.Slot != slot || value.Revision < 0 ||
            value.Phase is not ("new" or "accepting" or "holding" or "settling" or "success" or "refused") ||
            !double.IsFinite(value.Deducted) || value.Deducted < 0 || !double.IsFinite(value.ReputationBefore) || !double.IsFinite(value.ReputationTarget) ||
            value.Visit == null || value.VisitKind == null || value.LastVisitDay < -1 || value.LastVisitDay == 0 ||
            value.Visit.Length != 0 && (!Guid.TryParseExact(value.Visit, "N", out _) || value.LastVisitDay < 1 || value.VisitKind is not ("offer" or "farewell")) ||
            value.Visit.Length == 0 && (value.Arrived || value.VisitKind.Length != 0) ||
            value.Phase is "new" or "refused" && (value.AcceptedDay != -1 || value.DueDay != -1 || value.Deducted != 0 || value.FarewellStarted) ||
            value.Phase is "accepting" or "holding" or "settling" or "success" && (value.AcceptedDay < 1 || value.DueDay != (long)value.AcceptedDay + 7) ||
            value.Phase is "settling" or "success" && !value.FarewellStarted ||
            value.RewardPending && value.Phase != "success" || value.Reward != null && value.Phase is not ("settling" or "success"))
            throw new InvalidOperationException("江白剧情记录会话或状态无效。");
        if (value.Reward != null)
        {
            WorkroomItemCodec.Validate(value.Reward);
            if (value.Reward.Identifier != MysteriousContactCard.Id || value.Reward.Count != 1 || value.Reward.Broken)
                throw new InvalidOperationException("江白名片凭据无效。");
        }
        return value;
    }

    internal static double AcceptanceTarget(double reputation)
    {
        if (!double.IsFinite(reputation)) throw new InvalidOperationException("治安部声望无效。");
        return Math.Min(reputation, -90d);
    }
    internal static bool SuppressesMerchant(Document value) => value.Phase is not ("new" or "success");
    internal static bool ShowResident(Document value) => value.Phase == "holding" && !value.FarewellStarted;
}
