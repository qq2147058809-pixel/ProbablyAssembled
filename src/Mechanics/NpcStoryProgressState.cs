using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PCExpansion;

/// <summary>Only observed, paid business and observed security thresholds; no inferred history.</summary>
internal static class NpcStoryProgressState
{
    internal const string SaveKey = "pcexpansion.npc-story-progress.v1";
    internal sealed class Document
    {
        public int Version { get; set; } = 1;
        public string Run { get; set; } = "";
        public int Slot { get; set; }
        public long Revision { get; set; }
        public double LuoxiTurnover { get; set; }
        public bool SecurityReached25 { get; set; }
        public bool SecurityReached50 { get; set; }
        // Explicit quest payments use stable, run-local operation keys. Native
        // transactions are measured once by their enclosing cash operation.
        public List<string> PaymentReceipts { get; set; } = new();
    }
    internal static Document New(string run, int slot) => new() { Run = run, Slot = slot };
    internal static string Serialize(Document value) => JsonSerializer.Serialize(value);
    internal static Document Clone(Document value) => Parse(Serialize(value), value.Run, value.Slot);
    internal static Document Parse(string raw, string run, int slot)
    {
        using var json = JsonDocument.Parse(raw);
        var fields = new HashSet<string>(new[] { "Version", "Run", "Slot", "Revision", "LuoxiTurnover", "SecurityReached25", "SecurityReached50", "PaymentReceipts" }, StringComparer.Ordinal);
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("NPC进度记录不是对象。");
        foreach (var field in json.RootElement.EnumerateObject())
            if (!fields.Remove(field.Name)) throw new InvalidOperationException("NPC进度记录含重复或未知字段。");
        if (fields.Count != 0) throw new InvalidOperationException("NPC进度记录缺少字段。");
        var value = JsonSerializer.Deserialize<Document>(raw) ?? throw new InvalidOperationException("NPC进度记录为空。");
        if (value.Version != 1 || value.Run != run || string.IsNullOrEmpty(run) || value.Slot != slot || value.Revision < 0 ||
            !double.IsFinite(value.LuoxiTurnover) || value.LuoxiTurnover < 0 || value.SecurityReached50 && !value.SecurityReached25 ||
            value.PaymentReceipts == null || value.PaymentReceipts.Any(receipt => string.IsNullOrWhiteSpace(receipt) || receipt.Length > 160) ||
            value.PaymentReceipts.Distinct(StringComparer.Ordinal).Count() != value.PaymentReceipts.Count)
            throw new InvalidOperationException("NPC进度记录版本、会话或数据无效。");
        return value;
    }
    internal static void ObserveSecurity(Document value, double reputation)
    {
        if (!double.IsFinite(reputation)) throw new InvalidOperationException("治安部声望无效。");
        if (reputation >= 25) value.SecurityReached25 = true;
        if (reputation >= 50) value.SecurityReached50 = true;
    }
    internal static bool AddPayment(Document value, double amount, string? receipt)
    {
        if (!double.IsFinite(amount) || amount <= 0) throw new InvalidOperationException("成交金额必须为正数。");
        if (receipt != null)
        {
            if (string.IsNullOrWhiteSpace(receipt) || receipt.Length > 160) throw new InvalidOperationException("成交凭据无效。");
            if (value.PaymentReceipts.Contains(receipt)) return false;
        }
        var total = value.LuoxiTurnover + amount;
        if (!double.IsFinite(total) || total <= value.LuoxiTurnover) throw new InvalidOperationException("成交累计金额溢出。");
        value.LuoxiTurnover = total;
        if (receipt != null) value.PaymentReceipts.Add(receipt);
        return true;
    }
}
