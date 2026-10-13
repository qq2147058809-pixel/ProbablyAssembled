using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace PCExpansion;

/// <summary>机房模块的纯存档模型；物品树由工作间保管，这里只保存机架引用。</summary>
internal static class ServerRoomState
{
    internal const string SaveKey = "pcexpansion.server-room.v1";
    internal const int Version = 1, EnableCost = 120, VisitLimit = 2;
    private static readonly int[] UpgradeCosts = { 250, 1200, 3100, 7000, 9000 };
    private static readonly int[] SlotCounts = { 1, 2, 4, 6, 8, 10 };
    private static readonly string[] coinIds = { "anchor", "matrix", "stellar", "pulse", "singularity" };
    internal static IReadOnlyList<string> CoinIds { get; } = System.Array.AsReadOnly(coinIds);
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 32 };

    internal sealed class Document
    {
        public Document() { }
        public int Version { get; set; } = ServerRoomState.Version;
        public string Run { get; set; } = "";
        public int Slot { get; set; }
        public long Revision { get; set; }
        public bool Enabled { get; set; }
        public List<Upgrade> Upgrades { get; set; } = new();
        public List<RackReference> Racks { get; set; } = new();
        public List<Coin> Coins { get; set; } = new();
        public MarketState Market { get; set; } = new();
        public List<Lease> Leases { get; set; } = new();
        public VisitState Visit { get; set; } = new();
        public int? LastSettledDay { get; set; }
        // Runtime-only command provenance; never serialized into a player save.
        internal Binding? RuntimeBinding { get; set; }
    }

    internal sealed class Upgrade
    {
        public Upgrade() { }
        public bool Power { get; set; }
        public bool Cooling { get; set; }
    }

    internal sealed class RackReference
    {
        public RackReference() { }
        public int RackIndex { get; set; }
        public string RecordId { get; set; } = "";
        public int UniqueId { get; set; }
        public string Kind { get; set; } = "";
        public bool PoweredOn { get; set; }
        public string CoinId { get; set; } = "anchor";
        public int Priority { get; set; }
    }

    internal sealed class Coin
    {
        public Coin() { }
        public string Id { get; set; } = "";
        public long Units10 { get; set; }
        public decimal Cost { get; set; }
        public Fraction Progress { get; set; } = new();
    }

    // Fraction of one whole coin. Exact carry avoids changing the first-coin night.
    internal sealed class Fraction
    {
        public Fraction() { }
        public string Numerator { get; set; } = "0";
        public string Denominator { get; set; } = "1";
    }

    internal sealed class MarketState
    {
        public MarketState() { }
        public bool Initialized { get; set; }
        public int? QuoteDay { get; set; }
        public List<decimal> Prices { get; set; } = new();
        public List<Quote> History { get; set; } = new();
        public string RandomState { get; set; } = "";
        public string BoomMarker { get; set; } = "";
    }

    internal sealed class Quote
    {
        public Quote() { }
        public int Day { get; set; }
        public List<decimal> Prices { get; set; } = new();
    }

    internal sealed class Lease
    {
        public Lease() { }
        public string RecordId { get; set; } = "";
        public int UniqueId { get; set; }
        public string ContractId { get; set; } = "";
        public int RemainingDays { get; set; }
        public int StartedDay { get; set; }
        public int DurationDays { get; set; }
        public int DueDay { get; set; }
        public decimal EffectiveCompute { get; set; }
        public int DailyFee { get; set; }
        public decimal GrossRent { get; set; }
        public int PrepaidFees { get; set; }
        public int NetIncome { get; set; }
    }

    internal sealed class VisitState
    {
        public VisitState() { }
        public string VisitId { get; set; } = "";
        public int Purchased { get; set; }
    }

    internal sealed class Binding
    {
        internal IntPtr Owner { get; }
        internal string Run { get; }
        internal int Slot { get; }
        internal long Epoch { get; }
        internal long Revision { get; }
        internal Binding(IntPtr owner, string run, int slot, long epoch, long revision)
        {
            if (owner == IntPtr.Zero || !Token(run) || slot < 0 || epoch < 0 || revision < 0)
                throw new InvalidOperationException("机房会话绑定无效。");
            Owner = owner; Run = run; Slot = slot; Epoch = epoch; Revision = revision;
        }
        internal bool SameSession(Binding? other) => other != null && Owner == other.Owner &&
            Run == other.Run && Slot == other.Slot && Epoch == other.Epoch;
        internal void Require(Binding current)
        {
            if (!SameSession(current) || Revision != current.Revision)
                throw new InvalidOperationException("机房会话或版本已变化。");
        }
    }

    internal sealed class Change
    {
        internal Document Next { get; }
        internal int CashDelta { get; }
        internal bool Changed { get; }
        internal Change(Document next, int cashDelta, bool changed = true)
        { Next = next; CashDelta = cashDelta; Changed = changed; }
    }

    internal static Document New(string run, int slot)
    {
        var result = new Document { Run = run, Slot = slot };
        for (var i = 0; i < 5; i++)
        {
            result.Upgrades.Add(new Upgrade());
            result.Coins.Add(new Coin { Id = coinIds[i] });
        }
        Validate(result);
        return result;
    }

    // Presence is supplied separately: a present null/empty/truncated entry is corruption.
    internal static Document? Load(bool keyPresent, string? raw, string run, int slot)
    {
        if (!Token(run) || slot < 0) throw new InvalidOperationException("机房存档会话无效。");
        if (!keyPresent) return null;
        if (string.IsNullOrEmpty(raw)) throw new InvalidOperationException("机房记录为空，保留原文。");
        return Parse(raw, run, slot);
    }

    internal static Document Parse(string raw, string run, int slot)
    {
        using var parsed = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 32 });
        CheckRaw(parsed.RootElement);
        var value = JsonSerializer.Deserialize<Document>(raw, JsonOptions)
            ?? throw new InvalidOperationException("机房记录不是对象。");
        Validate(value);
        if (value.Run != run || value.Slot != slot)
            throw new InvalidOperationException("机房记录不属于当前 run 或档槽。");
        return value;
    }

    internal static string Serialize(Document value)
    { Validate(value); return JsonSerializer.Serialize(value, JsonOptions); }

    internal static Document Clone(Document value)
    {
        var clone = Parse(Serialize(value), value.Run, value.Slot);
        clone.RuntimeBinding = value.RuntimeBinding;
        return clone;
    }

    internal static int CompletedStages(Document value)
    {
        Validate(value);
        return value.Upgrades.TakeWhile(stage => stage.Power && stage.Cooling).Count();
    }
    internal static int UnlockedSlots(Document value)
    { var stages = CompletedStages(value); return !value.Enabled ? 0 : SlotCounts[stages]; }
    internal static int HighestTier(Document value)
    { var stages = CompletedStages(value); return !value.Enabled ? 0 : Math.Min(5, stages + 1); }

    internal static Change PrepareEnable(Document source)
    {
        Validate(source);
        if (source.Enabled) throw new InvalidOperationException("机房已经启用。");
        var next = Advance(source); next.Enabled = true;
        Validate(next);
        return new Change(next, -EnableCost);
    }

    // true buys the next power stage; false buys its cooling stage.
    internal static Change PrepareUpgrade(Document source, bool power)
    {
        var stage = CompletedStages(source);
        if (!source.Enabled || stage == 5) throw new InvalidOperationException("当前不能扩建机房。");
        var old = source.Upgrades[stage];
        if (power ? old.Power : old.Cooling) throw new InvalidOperationException("该项升级已经购买。");
        var next = Advance(source);
        if (power) next.Upgrades[stage].Power = true; else next.Upgrades[stage].Cooling = true;
        Validate(next);
        return new Change(next, -UpgradeCosts[stage]);
    }

    internal static Document Advance(Document source)
    {
        var next = Clone(source);
        next.Revision = checked(source.Revision + 1);
        return next;
    }

    internal static void Validate(Document value)
    {
        if (value == null || value.Version != Version || !Token(value.Run) || value.Slot < 0 || value.Revision < 0 ||
            value.Upgrades == null || value.Upgrades.Count != 5 || value.Racks == null || value.Coins == null ||
            value.Market == null || value.Leases == null || value.Visit == null)
            throw new InvalidOperationException("机房封套字段无效。");
        var incomplete = false; var complete = 0;
        foreach (var stage in value.Upgrades)
        {
            if (stage == null || !value.Enabled && (stage.Power || stage.Cooling) || incomplete && (stage.Power || stage.Cooling))
                throw new InvalidOperationException("机房升级顺序无效。");
            if (stage.Power && stage.Cooling) complete++;
            else incomplete = true;
        }
        var limit = value.Enabled ? SlotCounts[complete] : 0;
        var racks = new HashSet<int>(); var records = new HashSet<string>(StringComparer.Ordinal); var identities = new HashSet<int>();
        foreach (var rack in value.Racks)
            if (rack == null || rack.RackIndex < 0 || rack.RackIndex >= limit || !racks.Add(rack.RackIndex) ||
                !Token(rack.RecordId) || !records.Add(rack.RecordId) || rack.UniqueId < 0 || !identities.Add(rack.UniqueId) ||
                rack.Kind != "mining" && rack.Kind != "server" || rack.Kind == "server" && rack.PoweredOn ||
                !coinIds.Contains(rack.CoinId, StringComparer.Ordinal) ||
                rack.Priority < 0 || rack.Priority >= 10)
                throw new InvalidOperationException("机架引用或运行状态无效。");
        if (value.Coins.Count != 5) throw new InvalidOperationException("五币记录不完整。");
        for (var i = 0; i < 5; i++)
        {
            var coin = value.Coins[i];
            if (coin == null || coin.Id != coinIds[i] || coin.Units10 < 0 || coin.Cost < 0 || coin.Units10 == 0 && coin.Cost != 0)
                throw new InvalidOperationException("钱包数量或持仓成本无效。");
            ValidateFraction(coin.Progress);
        }
        // The existing empty market/lease shape remains valid for current M2 saves.
        ServerRoomMarket.Validate(value.Market);
        if (value.LastSettledDay < 0 || value.LastSettledDay != null &&
            (!value.Market.Initialized || value.Market.QuoteDay != value.LastSettledDay))
            throw new InvalidOperationException("行情与最后日结日期不一致。");
        var leasedRecords = new HashSet<string>(StringComparer.Ordinal);
        var contracts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lease in value.Leases)
        {
            if (lease == null || !Token(lease.RecordId) || !Token(lease.ContractId) ||
                !leasedRecords.Add(lease.RecordId) || !contracts.Add(lease.ContractId))
                throw new InvalidOperationException("租约身份重复或无效。");
            var machine = value.Racks.SingleOrDefault(rack => rack.RecordId == lease.RecordId);
            if (machine == null || machine.Kind != "server" || machine.UniqueId != lease.UniqueId)
                throw new InvalidOperationException("租约没有匹配的服务器。");
            ServerRoomEconomy.ValidateLease(lease, value.LastSettledDay);
        }
        if (value.Visit.VisitId == null || value.Visit.VisitId != "" && !Token(value.Visit.VisitId) ||
            value.Visit.Purchased < 0 || value.Visit.Purchased > VisitLimit || value.Visit.VisitId == "" && value.Visit.Purchased != 0)
            throw new InvalidOperationException("设备来访额度无效。");
    }

    private static bool Token(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 &&
        !value.Any(char.IsControl) && value == value.Trim();

    private static void ValidateFraction(Fraction value)
    {
        if (value == null || !Unsigned(value.Numerator) || !Unsigned(value.Denominator))
            throw new InvalidOperationException("挖矿进度分数格式无效。");
        var numerator = BigInteger.Parse(value.Numerator, System.Globalization.CultureInfo.InvariantCulture);
        var denominator = BigInteger.Parse(value.Denominator, System.Globalization.CultureInfo.InvariantCulture);
        if (denominator <= 0 || numerator >= denominator || BigInteger.GreatestCommonDivisor(numerator, denominator) != BigInteger.One)
            throw new InvalidOperationException("挖矿进度须为约分后的非负余量。");
    }

    private static bool Unsigned(string? value) => value != null && value.Length is > 0 and <= 128 &&
        (value.Length == 1 || value[0] != '0') && value.All(c => c is >= '0' and <= '9');

    private static void CheckRaw(JsonElement root)
    {
        CheckObject(root, "Version", "Run", "Slot", "Revision", "Enabled", "Upgrades", "Racks", "Coins", "Market", "Leases", "Visit", "LastSettledDay");
        foreach (var upgrade in Array(root, "Upgrades")) CheckObject(upgrade, "Power", "Cooling");
        foreach (var rack in Array(root, "Racks"))
            CheckObject(rack, "RackIndex", "RecordId", "UniqueId", "Kind", "PoweredOn", "CoinId", "Priority");
        foreach (var coin in Array(root, "Coins"))
        { CheckObject(coin, "Id", "Units10", "Cost", "Progress"); CheckObject(coin.GetProperty("Progress"), "Numerator", "Denominator"); }
        var market = root.GetProperty("Market");
        CheckObject(market, "Initialized", "QuoteDay", "Prices", "History", "RandomState", "BoomMarker");
        Array(market, "Prices");
        foreach (var quote in Array(market, "History")) { CheckObject(quote, "Day", "Prices"); Array(quote, "Prices"); }
        foreach (var lease in Array(root, "Leases"))
            CheckObject(lease, "RecordId", "UniqueId", "ContractId", "RemainingDays", "StartedDay", "DurationDays", "DueDay",
                "EffectiveCompute", "DailyFee", "GrossRent", "PrepaidFees", "NetIncome");
        CheckObject(root.GetProperty("Visit"), "VisitId", "Purchased");
    }

    private static JsonElement.ArrayEnumerator Array(JsonElement owner, string name)
    {
        var value = owner.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("机房列表字段无效：" + name);
        return value.EnumerateArray();
    }

    private static void CheckObject(JsonElement value, params string[] required)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("机房记录缺少对象。");
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!fields.Add(field.Name) || !required.Contains(field.Name, StringComparer.Ordinal))
                throw new InvalidOperationException("机房字段重复或不支持：" + field.Name);
        if (fields.Count != required.Length || required.Any(name => !fields.Contains(name)))
            throw new InvalidOperationException("机房记录缺少必需字段。");
    }
}
