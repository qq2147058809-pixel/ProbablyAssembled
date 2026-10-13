using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace PCExpansion;

/// <summary>Detached mining, lease and daily candidates. The runtime publishes cash and state together.</summary>
internal static class ServerRoomEconomy
{
    private static readonly int[] BasePrices = { 50, 200, 800, 100, 1500 };

    // These are scalar facts from the one authoritative WorkroomStorage item tree.
    internal sealed class MachineFact
    {
        internal MachineFact(string recordId, int uniqueId, string kind, decimal effectiveCompute, int dailyFee, bool canRun)
        {
            if (!Token(recordId) || uniqueId < 0 || kind is not ("mining" or "server") ||
                effectiveCompute < 0 || effectiveCompute > (kind == "mining" ? 360m : 240m) ||
                dailyFee is < 1 or > 42 || !canRun && effectiveCompute != 0)
                throw new InvalidOperationException("机器算力或费用事实无效。");
            RecordId = recordId; UniqueId = uniqueId; Kind = kind; EffectiveCompute = effectiveCompute;
            DailyFee = dailyFee; CanRun = canRun;
        }
        internal string RecordId { get; }
        internal int UniqueId { get; }
        internal string Kind { get; }
        internal decimal EffectiveCompute { get; }
        internal int DailyFee { get; }
        internal bool CanRun { get; }
    }

    internal sealed class LeaseQuote
    {
        internal LeaseQuote(MachineFact machine, int days, int startedDay)
        {
            Days = days; StartedDay = startedDay; DueDay = checked(startedDay + days);
            EffectiveCompute = machine.EffectiveCompute; DailyFee = machine.DailyFee;
            Bonus = LeaseBonus(days); DailyRent = checked(EffectiveCompute * 1.5m);
            GrossRent = checked(DailyRent * days * (1m + Bonus)); PrepaidFees = checked(DailyFee * days);
            NetIncome = checked((int)decimal.Floor(GrossRent - PrepaidFees));
        }
        internal int Days { get; }
        internal int StartedDay { get; }
        internal int DueDay { get; }
        internal decimal EffectiveCompute { get; }
        internal int DailyFee { get; }
        internal decimal DailyRent { get; }
        internal decimal Bonus { get; }
        internal decimal GrossRent { get; }
        internal int PrepaidFees { get; }
        internal int NetIncome { get; }
    }

    internal sealed class CoinOutput
    {
        internal CoinOutput(string coinId, decimal compute, long wholeCoins, ServerRoomState.Fraction progress)
        {
            CoinId = coinId; EffectiveCompute = compute; WholeCoins = wholeCoins;
            Progress = new ServerRoomState.Fraction { Numerator = progress.Numerator, Denominator = progress.Denominator };
        }
        internal string CoinId { get; }
        internal decimal EffectiveCompute { get; }
        internal long WholeCoins { get; }
        internal ServerRoomState.Fraction Progress { get; }
    }

    internal sealed class DayResult
    {
        internal DayResult(ServerRoomState.Change change, int fees, int cashAfter, List<string> paid,
            List<string> skipped, List<string> unavailable, List<CoinOutput> outputs, List<string> expired)
        {
            Change = change; Fees = fees; CashAfter = cashAfter;
            PaidRecords = Array.AsReadOnly(paid.ToArray()); SkippedRecords = Array.AsReadOnly(skipped.ToArray());
            UnavailableRecords = Array.AsReadOnly(unavailable.ToArray()); CoinOutputs = Array.AsReadOnly(outputs.ToArray());
            ExpiredContracts = Array.AsReadOnly(expired.ToArray());
        }
        internal ServerRoomState.Change Change { get; }
        internal int Fees { get; }
        internal int CashAfter { get; }
        internal IReadOnlyList<string> PaidRecords { get; }
        internal IReadOnlyList<string> SkippedRecords { get; }
        internal IReadOnlyList<string> UnavailableRecords { get; }
        internal IReadOnlyList<CoinOutput> CoinOutputs { get; }
        internal IReadOnlyList<string> ExpiredContracts { get; }
    }

    internal static ServerRoomState.Change PreparePower(ServerRoomState.Document source, string recordId, bool poweredOn)
    {
        var rack = MiningRack(source, recordId);
        if (rack.PoweredOn == poweredOn) return NoChange(source);
        var next = ServerRoomState.Advance(source);
        next.Racks.Single(entry => entry.RecordId == recordId).PoweredOn = poweredOn;
        return CheckedChange(next, 0);
    }

    internal static ServerRoomState.Change PrepareCoin(ServerRoomState.Document source, string recordId, string coinId)
    {
        var rack = MiningRack(source, recordId); RequireCoin(coinId);
        if (rack.CoinId == coinId) return NoChange(source);
        var next = ServerRoomState.Advance(source);
        next.Racks.Single(entry => entry.RecordId == recordId).CoinId = coinId;
        return CheckedChange(next, 0);
    }

    internal static ServerRoomState.Change PrepareAllCoins(ServerRoomState.Document source, string coinId)
    {
        RequireEnabled(source); RequireCoin(coinId);
        if (source.Racks.Where(rack => rack.Kind == "mining").All(rack => rack.CoinId == coinId)) return NoChange(source);
        var next = ServerRoomState.Advance(source);
        foreach (var rack in next.Racks.Where(rack => rack.Kind == "mining")) rack.CoinId = coinId;
        return CheckedChange(next, 0);
    }

    // Equal priorities use the fixed rack index as a stable secondary ordering.
    internal static ServerRoomState.Change PreparePriority(ServerRoomState.Document source, string recordId, int priority)
    {
        var rack = MiningRack(source, recordId);
        if (priority is < 0 or >= 10) throw new InvalidOperationException("运行优先级必须为 0 至 9。");
        if (rack.Priority == priority) return NoChange(source);
        var next = ServerRoomState.Advance(source);
        next.Racks.Single(entry => entry.RecordId == recordId).Priority = priority;
        return CheckedChange(next, 0);
    }

    internal static LeaseQuote QuoteLease(ServerRoomState.Document source, MachineFact machine, int days, int currentDay)
    {
        RequireEnabled(source); RequireFact(source, machine);
        RequireCurrentDay(source, currentDay);
        if (machine.Kind != "server" || !machine.CanRun || source.Leases.Any(lease => lease.RecordId == machine.RecordId))
            throw new InvalidOperationException("服务器未满足运行条件或仍在出租中。");
        var quote = new LeaseQuote(machine, days, currentDay);
        if (quote.NetIncome <= 0) throw new InvalidOperationException("租约净收入不足 1，不能签约。");
        return quote;
    }

    internal static ServerRoomState.Change PrepareLease(ServerRoomState.Document source, MachineFact machine,
        int days, int currentDay, string contractId)
    {
        RequireEnabled(source); RequireFact(source, machine);
        if (!Token(contractId)) throw new InvalidOperationException("租约身份无效。");
        var existing = source.Leases.SingleOrDefault(lease => lease.ContractId == contractId);
        if (existing != null)
        {
            if (existing.RecordId != machine.RecordId || existing.UniqueId != machine.UniqueId ||
                existing.DurationDays != days || existing.StartedDay != currentDay ||
                existing.EffectiveCompute != machine.EffectiveCompute || existing.DailyFee != machine.DailyFee)
                throw new InvalidOperationException("同一租约身份不能用于不同请求。");
            return NoChange(source);
        }
        var quote = QuoteLease(source, machine, days, currentDay);
        var next = ServerRoomState.Advance(source);
        next.Leases.Add(new ServerRoomState.Lease
        {
            RecordId = machine.RecordId, UniqueId = machine.UniqueId, ContractId = contractId,
            RemainingDays = days, StartedDay = currentDay, DurationDays = days, DueDay = quote.DueDay,
            EffectiveCompute = quote.EffectiveCompute, DailyFee = quote.DailyFee, GrossRent = quote.GrossRent,
            PrepaidFees = quote.PrepaidFees, NetIncome = quote.NetIncome
        });
        return CheckedChange(next, quote.NetIncome);
    }

    // marketAfter is the already prepared, stable next-day quote from the market module.
    // The first transition may start on any current day when LastSettledDay is null.
    internal static DayResult PrepareDay(ServerRoomState.Document source, IReadOnlyList<MachineFact> machines,
        int fromDay, int toDay, int availableCash, ServerRoomState.MarketState marketAfter)
    {
        ServerRoomState.Validate(source);
        if (fromDay < 0 || toDay < 0 || (long)toDay != (long)fromDay + 1 || availableCash < 0)
            throw new InvalidOperationException("日结必须对应一次成功跨日及非负可用余额。");
        if (source.LastSettledDay == toDay)
            return new DayResult(NoChange(source), 0, availableCash, new(), new(), new(), new(), new());
        if (source.LastSettledDay != null && source.LastSettledDay != fromDay ||
            source.Market.Initialized && source.Market.QuoteDay != fromDay)
            throw new InvalidOperationException("日结来源日期回退或跳过，拒绝补算。");
        ServerRoomMarket.Validate(marketAfter);
        if (!marketAfter.Initialized || marketAfter.QuoteDay != toDay)
            throw new InvalidOperationException("下一日行情尚未准备完成。");
        var facts = RequireFacts(source, machines);
        foreach (var lease in source.Leases)
        {
            var fact = facts[lease.RecordId];
            if (!fact.CanRun || fact.EffectiveCompute != lease.EffectiveCompute || fact.DailyFee != lease.DailyFee ||
                lease.StartedDay > fromDay || (long)lease.DueDay - fromDay != lease.RemainingDays)
                throw new InvalidOperationException("出租期间机器配置或租约日期发生变化。");
        }
        var cash = availableCash; var fees = 0;
        var paid = new List<string>(); var skipped = new List<string>(); var unavailable = new List<string>();
        var computes = new decimal[5];
        foreach (var rack in source.Racks.Where(rack => rack.Kind == "mining" && rack.PoweredOn)
            .OrderBy(rack => rack.Priority).ThenBy(rack => rack.RackIndex))
        {
            var machine = facts[rack.RecordId];
            if (!machine.CanRun) { unavailable.Add(rack.RecordId); continue; }
            if (cash < machine.DailyFee) { skipped.Add(rack.RecordId); continue; }
            cash -= machine.DailyFee; fees = checked(fees + machine.DailyFee); paid.Add(rack.RecordId);
            var index = CoinIndex(rack.CoinId);
            computes[index] = checked(computes[index] + machine.EffectiveCompute);
        }
        var next = ServerRoomState.Advance(source); var outputs = new List<CoinOutput>();
        for (var i = 0; i < 5; i++)
        {
            var coin = next.Coins[i];
            var whole = AddProgress(coin, computes[i], BasePrices[i]);
            outputs.Add(new CoinOutput(coin.Id, computes[i], whole, coin.Progress));
        }
        var expired = new List<string>();
        foreach (var lease in next.Leases)
        {
            lease.RemainingDays = checked(lease.DueDay - toDay);
            if (lease.RemainingDays == 0) expired.Add(lease.ContractId);
            if (lease.RemainingDays < 0) throw new InvalidOperationException("租约已越过到期日，拒绝补算。");
        }
        next.Leases.RemoveAll(lease => lease.RemainingDays == 0);
        // Never share a mutable market list with a caller or the source document.
        next.Market = CloneMarket(marketAfter); next.LastSettledDay = toDay;
        return new DayResult(CheckedChange(next, -fees), fees, cash, paid, skipped, unavailable, outputs, expired);
    }

    internal static void ValidateLease(ServerRoomState.Lease lease, int? lastSettledDay)
    {
        if (lease.UniqueId < 0 || lease.StartedDay < 0 || lease.DurationDays is not (3 or 5 or 7) ||
            (long)lease.DueDay != (long)lease.StartedDay + lease.DurationDays ||
            lease.RemainingDays is <= 0 or > 7 ||
            lease.RemainingDays != (long)lease.DueDay - Math.Max(lease.StartedDay, lastSettledDay ?? lease.StartedDay) ||
            lease.EffectiveCompute <= 0 || lease.EffectiveCompute > 240m || lease.DailyFee is < 1 or > 42)
            throw new InvalidOperationException("租约期限或冻结机器事实无效。");
        var gross = checked(lease.EffectiveCompute * 1.5m * lease.DurationDays * (1m + LeaseBonus(lease.DurationDays)));
        var fees = checked(lease.DailyFee * lease.DurationDays);
        var net = checked((int)decimal.Floor(gross - fees));
        if (lease.GrossRent != gross || lease.PrepaidFees != fees || lease.NetIncome != net || net <= 0)
            throw new InvalidOperationException("租约冻结金额与净收入不一致。");
    }

    private static long AddProgress(ServerRoomState.Coin coin, decimal compute, int basePrice)
    {
        if (compute == 0) return 0;
        var bits = decimal.GetBits(compute);
        var scale = (bits[3] >> 16) & 0xff;
        var addedNumerator = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
        var addedDenominator = BigInteger.Pow(10, scale) * basePrice;
        var oldNumerator = BigInteger.Parse(coin.Progress.Numerator, CultureInfo.InvariantCulture);
        var oldDenominator = BigInteger.Parse(coin.Progress.Denominator, CultureInfo.InvariantCulture);
        var numerator = oldNumerator * addedDenominator + addedNumerator * oldDenominator;
        var denominator = oldDenominator * addedDenominator;
        var whole = BigInteger.DivRem(numerator, denominator, out var remainder);
        var divisor = BigInteger.GreatestCommonDivisor(remainder, denominator);
        coin.Progress = new ServerRoomState.Fraction
        { Numerator = (remainder / divisor).ToString(CultureInfo.InvariantCulture), Denominator = (denominator / divisor).ToString(CultureInfo.InvariantCulture) };
        var count = checked((long)whole);
        coin.Units10 = checked(coin.Units10 + checked(count * 10));
        // Mining adds no purchase cost. Configured operating expenses remain the cash delta.
        return count;
    }

    private static Dictionary<string, MachineFact> RequireFacts(ServerRoomState.Document source, IReadOnlyList<MachineFact> machines)
    {
        if (machines == null || machines.Count != source.Racks.Count)
            throw new InvalidOperationException("机架真实配置事实不完整。");
        var result = new Dictionary<string, MachineFact>(StringComparer.Ordinal);
        foreach (var machine in machines)
        {
            RequireFact(source, machine);
            if (!result.TryAdd(machine.RecordId, machine)) throw new InvalidOperationException("机器事实重复。");
        }
        return result;
    }

    private static void RequireFact(ServerRoomState.Document source, MachineFact machine)
    {
        if (machine == null) throw new InvalidOperationException("机器配置事实缺失。");
        var rack = source.Racks.SingleOrDefault(entry => entry.RecordId == machine.RecordId);
        if (rack == null || rack.UniqueId != machine.UniqueId || rack.Kind != machine.Kind)
            throw new InvalidOperationException("真实机器事实与机架身份不一致。");
    }

    private static ServerRoomState.RackReference MiningRack(ServerRoomState.Document source, string recordId)
    {
        RequireEnabled(source);
        return source.Racks.SingleOrDefault(rack => rack.RecordId == recordId && rack.Kind == "mining")
            ?? throw new InvalidOperationException("请选择已上架的矿机。");
    }

    private static void RequireEnabled(ServerRoomState.Document source)
    {
        ServerRoomState.Validate(source);
        if (!source.Enabled) throw new InvalidOperationException("机房尚未启用。");
    }

    private static void RequireCurrentDay(ServerRoomState.Document source, int day)
    {
        if (day < 0 || source.LastSettledDay != null && source.LastSettledDay != day ||
            source.Market.Initialized && source.Market.QuoteDay != day)
            throw new InvalidOperationException("租约报价日期已变化。");
    }

    private static decimal LeaseBonus(int days) => days switch
    { 3 => 0m, 5 => 0.03m, 7 => 0.05m, _ => throw new InvalidOperationException("租期仅允许三、五、七日。") };
    private static int CoinIndex(string id)
    {
        for (var i = 0; i < ServerRoomState.CoinIds.Count; i++) if (ServerRoomState.CoinIds[i] == id) return i;
        throw new InvalidOperationException("挖矿币种无效。");
    }
    private static void RequireCoin(string coinId) { CoinIndex(coinId); }
    private static bool Token(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 &&
        !value.Any(char.IsControl) && value == value.Trim();
    private static ServerRoomState.Change NoChange(ServerRoomState.Document source) => new(ServerRoomState.Clone(source), 0, false);
    private static ServerRoomState.Change CheckedChange(ServerRoomState.Document next, int cashDelta)
    { ServerRoomState.Validate(next); return new ServerRoomState.Change(next, cashDelta); }
    private static ServerRoomState.MarketState CloneMarket(ServerRoomState.MarketState source) => new()
    {
        Initialized = source.Initialized, QuoteDay = source.QuoteDay, RandomState = source.RandomState, BoomMarker = source.BoomMarker,
        Prices = new List<decimal>(source.Prices), History = source.History.Select(quote =>
            new ServerRoomState.Quote { Day = quote.Day, Prices = new List<decimal>(quote.Prices) }).ToList()
    };
}
