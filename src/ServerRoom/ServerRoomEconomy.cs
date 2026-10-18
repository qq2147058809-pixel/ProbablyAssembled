using System;
using System.Collections.Generic;
using System.Linq;

namespace PCExpansion;

/// <summary>Detached operating plans; cash, wallets, maintenance and ledger publish as one candidate.</summary>
internal static class ServerRoomEconomy
{
    private static readonly int[] PowerLimits = { 120, 400, 1600, 4200, 11000, 14000 };
    private static readonly int[] HeatLimits = { 100, 350, 1400, 3700, 10000, 12500 };
    private static readonly int[] ServiceFees = { 5, 8, 12, 18, 25 };
    private static readonly int[] ServiceMaterials = { 1, 1, 2, 2, 3 };

    internal sealed class MachineFact
    {
        internal MachineFact(string recordId, int uniqueId, string kind, int tier, decimal effectiveCompute,
            decimal watts, bool canRun, IReadOnlyList<ServerRoomMiningModel.Card> cards, string configuration)
        {
            if (!Token(recordId) || uniqueId < 0 || kind is not ("mining" or "server") || tier is < 1 or > 5 ||
                effectiveCompute < 0 || effectiveCompute > (kind == "mining" ? 360m : 240m) ||
                watts <= 0 || watts > 2000m || !Token(configuration) || cards == null)
                throw new InvalidOperationException("机器配置事实无效。");
            RecordId = recordId; UniqueId = uniqueId; Kind = kind; Tier = tier; EffectiveCompute = effectiveCompute;
            Watts = watts; CanRun = canRun; Configuration = configuration;
            Cards = Array.AsReadOnly(cards.Select(card => new ServerRoomMiningModel.Card
                { Tier = card.Tier, VramGb = card.VramGb, Intact = card.Intact }).ToArray());
        }
        internal string RecordId { get; }
        internal int UniqueId { get; }
        internal string Kind { get; }
        internal int Tier { get; }
        internal decimal EffectiveCompute { get; }
        internal decimal Watts { get; }
        // Compatibility display only; running totals come exclusively from Plan.
        internal int DailyFee => ServerRoomMiningModel.ElectricityCost(Watts, 1m);
        internal bool CanRun { get; }
        internal string Configuration { get; }
        internal IReadOnlyList<ServerRoomMiningModel.Card> Cards { get; }
    }
    internal sealed class LeaseQuote
    {
        internal LeaseQuote(MachineFact machine, int days, int startedDay, decimal electricity)
        {
            Days = days; StartedDay = startedDay; DueDay = checked(startedDay + days);
            EffectiveCompute = machine.EffectiveCompute; Watts = machine.Watts; SigningElectricity = electricity;
            DailyFee = ServerRoomMiningModel.ElectricityCost(Watts, electricity);
            Bonus = ServerRoomLeaseContract.Bonus(days); DailyRent = EffectiveCompute * 2.5m;
            GrossRent = ServerRoomLeaseContract.Gross(EffectiveCompute, days); PrepaidFees = checked(DailyFee * days);
            NetIncome = checked((int)decimal.Floor(GrossRent - PrepaidFees));
        }
        internal int Days { get; }
        internal int StartedDay { get; }
        internal int DueDay { get; }
        internal decimal EffectiveCompute { get; }
        internal decimal Watts { get; }
        internal decimal SigningElectricity { get; }
        internal int DailyFee { get; }
        internal decimal DailyRent { get; }
        internal decimal Bonus { get; }
        internal decimal GrossRent { get; }
        internal int PrepaidFees { get; }
        internal int NetIncome { get; }
    }
    internal sealed class PlannedMachine
    {
        internal string RecordId = "";
        internal int UniqueId, Coin, Mode;
        internal string Reason = "";
        internal bool Runs => Reason.Length == 0;
        internal ServerRoomMiningModel.Capability Capacity = new();
        internal ServerRoomMiningModel.Production Production = new();
    }
    internal sealed class OperatingPlan
    {
        internal List<PlannedMachine> Machines { get; } = new();
        internal decimal LeaseWatts, MiningWatts;
        internal int Fees;
        internal decimal DeviceWatts => LeaseWatts + MiningWatts;
    }
    internal sealed class DayResult
    {
        internal ServerRoomState.Change Change { get; }
        internal int Fees { get; }
        internal int CashAfter { get; }
        internal IReadOnlyList<string> PaidRecords { get; }
        internal IReadOnlyList<string> SkippedRecords { get; }
        internal IReadOnlyList<string> UnavailableRecords { get; }
        internal IReadOnlyList<string> ExpiredContracts { get; }
        internal OperatingPlan Plan { get; }
        internal DayResult(ServerRoomState.Change change, int cashAfter, OperatingPlan plan, List<string> expired)
        {
            Change = change; CashAfter = cashAfter; Fees = plan.Fees; Plan = plan;
            PaidRecords = plan.Machines.Where(m => m.Runs).Select(m => m.RecordId).ToArray();
            SkippedRecords = plan.Machines.Where(m => m.Reason == "cash").Select(m => m.RecordId).ToArray();
            UnavailableRecords = plan.Machines.Where(m => !m.Runs && m.Reason != "cash").Select(m => m.RecordId).ToArray();
            ExpiredContracts = expired.AsReadOnly();
        }
    }

    internal static int PowerLimit(ServerRoomState.Document source) =>
        source.Enabled ? PowerLimits[source.Upgrades.Count(u => u.Power)] : 0;
    internal static int HeatLimit(ServerRoomState.Document source) =>
        source.Enabled ? HeatLimits[source.Upgrades.Count(u => u.Cooling)] : 0;
    private static bool Fits(ServerRoomState.Document source, decimal watts) =>
        watts * 1.15m <= PowerLimit(source) && watts <= HeatLimit(source);
    internal static decimal Health(ServerRoomState.Document source, int uniqueId) =>
        source.Maintenance.SingleOrDefault(item => item.UniqueId == uniqueId)?.Health ?? 100m;
    internal static int MaintenanceFee(int tier) => ServiceFees[tier - 1];
    internal static int MaintenanceMaterials(int tier) => ServiceMaterials[tier - 1];

    internal static OperatingPlan Plan(ServerRoomState.Document source, IReadOnlyList<MachineFact> machines, int cash, bool settlement = false)
    {
        ServerRoomState.Validate(source);
        if (cash < 0 || source.Network.Day == null) throw new InvalidOperationException("日结余额或网络状态无效。");
        return PlanCore(source, RequireFacts(source, machines), cash, settlement, 0,
            source.Maintenance.ToDictionary(m => m.UniqueId, m => m.Health), source.Network.ElectricityFactor);
    }
    private static OperatingPlan PlanCore(ServerRoomState.Document source, Dictionary<string, MachineFact> facts,
        int cash, bool settlement, int future, Dictionary<int, decimal> health, decimal electricity)
    {
        var result = new OperatingPlan { LeaseWatts = source.Leases.Where(l => l.RemainingDays > future).Sum(l => l.Watts) };
        if (!Fits(source, result.LeaseWatts)) throw new InvalidOperationException("已签租约超出机房保留容量。");
        var quantities = source.Coins.Select(c => c.MicroUnits).ToArray();
        var residues = source.Coins.Select(c => c.ResidueCoin).ToArray();
        foreach (var rack in source.Racks.Where(r => r.Kind == "mining" && r.PoweredOn).OrderBy(r => r.Priority).ThenBy(r => r.RackIndex))
        {
            var fact = facts[rack.RecordId]; var index = CoinIndex(rack.CoinId);
            var item = new PlannedMachine { RecordId = rack.RecordId, UniqueId = rack.UniqueId, Coin = index, Mode = rack.Mode };
            result.Machines.Add(item);
            if (!fact.CanRun) { item.Reason = "configuration"; continue; }
            item.Capacity = ServerRoomMiningModel.Evaluate(fact.Tier, fact.Cards, index, rack.Mode, health[rack.UniqueId]);
            if (!item.Capacity.CanRun) { item.Reason = item.Capacity.Reason; continue; }
            while (!Fits(source, result.DeviceWatts + item.Capacity.Watts) && rack.AutoDownshift && item.Mode > 0)
            {
                item.Mode--;
                item.Capacity = ServerRoomMiningModel.Evaluate(fact.Tier, fact.Cards, index, item.Mode, health[rack.UniqueId]);
            }
            if (!Fits(source, result.DeviceWatts + item.Capacity.Watts)) { item.Reason = "capacity"; continue; }
            var fee = ServerRoomMiningModel.ElectricityCost(result.MiningWatts + item.Capacity.Watts, electricity);
            if (fee > cash) { item.Reason = "cash"; continue; }
            var coin = source.Coins[index];
            var luck = settlement ? ServerRoomEconomyRandom.PoolLuck(source.Run, source.Slot, index, source.Network.Day!.Value) : 1m;
            item.Production = ServerRoomMiningModel.Produce(item.Capacity, index, source.Network.D[index], coin.Pool, luck, residues[index]);
            if (item.Production.Microcoins > long.MaxValue - quantities[index])
            { item.Reason = "wallet-capacity"; continue; }
            quantities[index] += item.Production.Microcoins; residues[index] = item.Production.ResidueCoin;
            result.MiningWatts += item.Capacity.Watts; result.Fees = fee;
        }
        return result;
    }
    internal static int ForecastBudget(ServerRoomState.Document source, IReadOnlyList<MachineFact> machines)
    {
        ServerRoomState.Validate(source); var facts = RequireFacts(source, machines);
        var health = source.Maintenance.ToDictionary(m => m.UniqueId, m => m.Health); var total = 0;
        for (var day = 0; day < 3; day++)
        {
            var network = source.Network;
            var factor = day == 0 ? network.ElectricityFactor : network.ElectricityNextDuration > 0 ?
                network.ElectricityNextFactor : network.ElectricityDaysRemaining > day ? network.ElectricityFactor : 1m;
            var plan = PlanCore(source, facts, int.MaxValue, false, day, health, factor);
            total = checked(total + plan.Fees);
            foreach (var machine in plan.Machines.Where(m => m.Runs))
                health[machine.UniqueId] = Math.Max(0m, health[machine.UniqueId] - machine.Capacity.Wear);
        }
        return total;
    }

    internal static ServerRoomState.Change PreparePower(ServerRoomState.Document source, string recordId, bool on)
    {
        var rack = MiningRack(source, recordId); if (rack.PoweredOn == on) return NoChange(source);
        var next = ServerRoomState.Advance(source); next.Racks.Single(r => r.RecordId == recordId).PoweredOn = on;
        return CheckedChange(next, 0);
    }
    internal static ServerRoomState.Change PrepareCoin(ServerRoomState.Document source, string recordId, string coinId)
    {
        var rack = MiningRack(source, recordId); CoinIndex(coinId); if (rack.CoinId == coinId) return NoChange(source);
        var next = ServerRoomState.Advance(source); next.Racks.Single(r => r.RecordId == recordId).CoinId = coinId;
        return CheckedChange(next, 0);
    }
    internal static ServerRoomState.Change PrepareAllCoins(ServerRoomState.Document source, string coinId)
    {
        RequireEnabled(source); CoinIndex(coinId);
        if (source.Racks.Where(r => r.Kind == "mining").All(r => r.CoinId == coinId)) return NoChange(source);
        var next = ServerRoomState.Advance(source); foreach (var rack in next.Racks.Where(r => r.Kind == "mining")) rack.CoinId = coinId;
        return CheckedChange(next, 0);
    }
    internal static ServerRoomState.Change PreparePriority(ServerRoomState.Document source, string recordId, int priority)
    {
        var rack = MiningRack(source, recordId); if (priority is < 0 or >= 10) throw new InvalidOperationException("运行优先级必须为0至9。");
        if (rack.Priority == priority) return NoChange(source);
        var next = ServerRoomState.Advance(source); next.Racks.Single(r => r.RecordId == recordId).Priority = priority;
        return CheckedChange(next, 0);
    }
    internal static ServerRoomState.Change PrepareMode(ServerRoomState.Document source, string recordId, int mode, bool downshift)
    {
        var rack = MiningRack(source, recordId);
        if (mode is < 0 or > 2 || mode == 2 && Health(source, rack.UniqueId) < 30)
            throw new InvalidOperationException("模式无效，或维护状态不足以超频。");
        if (rack.Mode == mode && rack.AutoDownshift == downshift) return NoChange(source);
        var next = ServerRoomState.Advance(source); var target = next.Racks.Single(r => r.RecordId == recordId);
        target.Mode = mode; target.AutoDownshift = downshift; return CheckedChange(next, 0);
    }
    internal static ServerRoomState.Change PreparePool(ServerRoomState.Document source, string coinId, int pool)
    {
        RequireEnabled(source); var index = CoinIndex(coinId);
        if (pool is < 0 or > 1) throw new InvalidOperationException("矿池无效。");
        if (source.Coins[index].NextPool == pool) return NoChange(source);
        var next = ServerRoomState.Advance(source); next.Coins[index].NextPool = pool; return CheckedChange(next, 0);
    }
    internal static ServerRoomState.Change PreparePolicy(ServerRoomState.Document source, ServerRoomState.AutoSalePolicy policy)
    {
        RequireEnabled(source);
        if (policy == null) throw new InvalidOperationException("自动出售策略缺失。");
        var next = ServerRoomState.Advance(source);
        next.AutoSale = new ServerRoomState.AutoSalePolicy { Kind = policy.Kind, Percent = policy.Percent,
            CoinOrder = new(policy.CoinOrder), MinimumNetPrices = new(policy.MinimumNetPrices) };
        return CheckedChange(next, 0);
    }
    internal static ServerRoomState.Change PrepareMaintenance(ServerRoomState.Document source, string recordId)
    {
        var rack = MiningRack(source, recordId);
        if (rack.PoweredOn) throw new InvalidOperationException("请先关闭矿机再维护。");
        var next = ServerRoomState.Advance(source); var item = next.Maintenance.Single(m => m.UniqueId == rack.UniqueId);
        if (item.Health == 100m) throw new InvalidOperationException("矿机无需维护。");
        item.Health = 100m;
        ServerRoomLedger.Add(next, next.Market.QuoteDay!.Value, a =>
        { a.MaintenanceCash = MaintenanceFee(item.Tier); a.MaintenanceMaterials = MaintenanceMaterials(item.Tier); });
        return CheckedChange(next, -MaintenanceFee(item.Tier));
    }
    internal static LeaseQuote QuoteLease(ServerRoomState.Document source, MachineFact machine, int days, int currentDay)
    {
        RequireEnabled(source); RequireFact(source, machine); RequireCurrentDay(source, currentDay);
        if (machine.Kind != "server" || !machine.CanRun || source.Leases.Any(l => l.RecordId == machine.RecordId))
            throw new InvalidOperationException("服务器不可运行或仍在出租中。");
        if (!Fits(source, source.Leases.Sum(l => l.Watts) + machine.Watts))
            throw new InvalidOperationException("供电或散热不足以承诺新租约。");
        var quote = new LeaseQuote(machine, days, currentDay, source.Network.ElectricityFactor);
        if (quote.NetIncome <= 0) throw new InvalidOperationException("租约净收入不足1。");
        return quote;
    }
    internal static ServerRoomState.Change PrepareLease(ServerRoomState.Document source, MachineFact machine, int days, int currentDay, string contractId)
    {
        RequireEnabled(source); RequireFact(source, machine);
        if (!Token(contractId)) throw new InvalidOperationException("租约身份无效。");
        var existing = source.Leases.SingleOrDefault(l => l.ContractId == contractId);
        if (existing != null)
        {
            if (existing.RecordId != machine.RecordId || existing.UniqueId != machine.UniqueId ||
                existing.DurationDays != days || existing.StartedDay != currentDay || existing.Configuration != machine.Configuration)
                throw new InvalidOperationException("同一租约身份不能用于不同请求。");
            return NoChange(source);
        }
        var quote = QuoteLease(source, machine, days, currentDay);
        var next = ServerRoomState.Advance(source);
        next.Leases.Add(new ServerRoomState.Lease { RecordId = machine.RecordId, UniqueId = machine.UniqueId, ContractId = contractId,
            RemainingDays = days, StartedDay = currentDay, DurationDays = days, DueDay = quote.DueDay,
            EffectiveCompute = quote.EffectiveCompute, Watts = quote.Watts, SigningElectricity = quote.SigningElectricity,
            Configuration = machine.Configuration, DailyFee = quote.DailyFee, GrossRent = quote.GrossRent,
            PrepaidFees = quote.PrepaidFees, NetIncome = quote.NetIncome });
        ServerRoomLedger.Add(next, currentDay, a => a.LeaseAdvanceCash = quote.NetIncome);
        return CheckedChange(next, quote.NetIncome);
    }
    internal static decimal UnearnedRent(ServerRoomState.Document source) =>
        source.Leases.Sum(l => ServerRoomLeaseContract.Remaining(l.GrossRent, l.RemainingDays, l.DurationDays));

    internal static void RequireSameMachines(IReadOnlyList<MachineFact> frozen, IReadOnlyList<MachineFact> live)
    {
        if (frozen.Count != live.Count) throw new InvalidOperationException("睡前机架数量发生变化。");
        foreach (var machine in frozen)
        {
            var current = live.SingleOrDefault(item => item.RecordId == machine.RecordId);
            if (current == null || current.UniqueId != machine.UniqueId || current.Kind != machine.Kind ||
                current.Tier != machine.Tier || current.Configuration != machine.Configuration ||
                current.Watts != machine.Watts || current.CanRun != machine.CanRun || current.EffectiveCompute != machine.EffectiveCompute)
                throw new InvalidOperationException("睡前机器身份或配置发生变化。");
        }
    }

    internal static DayResult PrepareDay(ServerRoomState.Document source, IReadOnlyList<MachineFact> machines,
        int fromDay, int toDay, int availableCash, ServerRoomState.MarketState marketAfter)
    {
        ServerRoomState.Validate(source);
        if (fromDay < 0 || (long)toDay != (long)fromDay + 1 || availableCash < 0)
            throw new InvalidOperationException("日结须成功跨一日，可用现金不可为负。");
        if (source.LastSettledDay == toDay)
            return new DayResult(NoChange(source), availableCash, new OperatingPlan(), new());
        if (source.Market.QuoteDay != fromDay || source.Network.Day != fromDay ||
            source.LastSettledDay != null && source.LastSettledDay != fromDay)
            throw new InvalidOperationException("日结来源日期回退或跳过。");
        ServerRoomMarket.Validate(marketAfter);
        if (!marketAfter.Initialized || marketAfter.QuoteDay != toDay) throw new InvalidOperationException("下一日行情尚未准备。");
        var facts = RequireFacts(source, machines);
        foreach (var lease in source.Leases)
        {
            var fact = facts[lease.RecordId];
            if (!fact.CanRun || fact.Configuration != lease.Configuration || fact.EffectiveCompute != lease.EffectiveCompute ||
                fact.Watts != lease.Watts || lease.StartedDay > fromDay || lease.DueDay - fromDay != lease.RemainingDays)
                throw new InvalidOperationException("出租期间机器配置或租约日期变化。");
        }
        var plan = Plan(source, machines, availableCash, true);
        var next = ServerRoomState.Advance(source); var fresh = new long[5];
        // Publish all dates together before adding next-day cash entries.
        next.Market = CloneMarket(marketAfter); next.LastSettledDay = toDay;
        next.Network = ServerRoomNetworkModel.Advance(source.Network, source.Run, source.Slot, fromDay,
            source.Market.Prices, ServerRoomState.CompletedStages(source) >= 1);
        foreach (var coin in next.Coins) { coin.DailyNetMicro = 0; coin.Pool = coin.NextPool; }
        foreach (var machine in plan.Machines)
        {
            if (!machine.Runs)
            {
                ServerRoomLedger.Add(next, toDay, _ => { }, machine.RecordId + ":" + machine.Reason); continue;
            }
            var coin = next.Coins[machine.Coin]; var production = machine.Production;
            coin.MicroUnits = checked(coin.MicroUnits + production.Microcoins);
            coin.MinedMicro = checked(coin.MinedMicro + production.Microcoins);
            coin.ResidueCoin = production.ResidueCoin; fresh[machine.Coin] = checked(fresh[machine.Coin] + production.Microcoins);
            var maintenance = next.Maintenance.Single(m => m.UniqueId == machine.UniqueId);
            maintenance.Health = Math.Max(0m, maintenance.Health - machine.Capacity.Wear);
            ServerRoomLedger.Add(next, toDay, a =>
            { a.ProducedCoins[machine.Coin] = production.ProducedCoin; a.PoolFees[machine.Coin] = production.PoolFeeCoin; });
        }
        var expired = new List<string>();
        foreach (var lease in next.Leases)
        {
            var before = lease.RemainingDays; lease.RemainingDays = lease.DueDay - toDay;
            if (lease.RemainingDays < 0) throw new InvalidOperationException("租约越过到期日。");
            var gross = ServerRoomLeaseContract.Remaining(lease.GrossRent, before, lease.DurationDays) -
                ServerRoomLeaseContract.Remaining(lease.GrossRent, lease.RemainingDays, lease.DurationDays);
            var rounding = lease.GrossRent - lease.PrepaidFees - lease.NetIncome;
            var roundingEarned = ServerRoomLeaseContract.Remaining(rounding, before, lease.DurationDays) -
                ServerRoomLeaseContract.Remaining(rounding, lease.RemainingDays, lease.DurationDays);
            ServerRoomLedger.Add(next, toDay, a =>
            { a.RentEarned = gross; a.LeaseFeesEarned = lease.DailyFee; a.LeaseRoundingEarned = roundingEarned; });
            if (lease.RemainingDays == 0) expired.Add(lease.ContractId);
        }
        next.Leases.RemoveAll(l => l.RemainingDays == 0);
        ServerRoomLedger.Add(next, toDay, a => a.ElectricityCash = plan.Fees);
        var dayEntry = next.Ledger.Days[^1];
        dayEntry.ProductionFromDay = fromDay; dayEntry.MiningWatts = plan.MiningWatts;
        dayEntry.ElectricityFactor = source.Network.ElectricityFactor;
        var cash = availableCash - plan.Fees;
        cash = AutoSell(next, machines, fresh, cash);
        return new DayResult(CheckedChange(next, cash - availableCash), cash, plan, expired);
    }
    private static int AutoSell(ServerRoomState.Document next, IReadOnlyList<MachineFact> facts, long[] fresh, int cash)
    {
        if (next.AutoSale.Kind == 0) return cash;
        var target = next.AutoSale.Kind == 1 ? ForecastBudget(next, facts) : 0;
        foreach (var index in next.AutoSale.CoinOrder)
        {
            if (cash == int.MaxValue || next.AutoSale.Kind == 1 && cash >= target) break;
            var allowed = next.AutoSale.Kind == 2 ? (long)(fresh[index] * (next.AutoSale.Percent / 100m)) : next.Coins[index].MicroUnits;
            if (allowed == 0) continue;
            var coin = next.Coins[index]; var price = next.Market.Prices[index];
            var room = next.AutoSale.Kind == 1 ? target - cash : int.MaxValue - cash;
            var quantity = ServerRoomTradeProtocol.MaxSell(price, ServerRoomTradeMath.Depth(index),
                allowed, coin.DailyNetMicro, room);
            if (quantity == 0)
            {
                ServerRoomLedger.Add(next, next.Market.QuoteDay!.Value, _ => { }, coin.Id + ":auto-no-fill"); continue;
            }
            var quote = ServerRoomTradeMath.Preview(next, index, quantity, false);
            if (next.AutoSale.Kind == 1)
            {
                // Find the smallest order giving this affordable cash receipt, avoiding needless rounded-away sales.
                long lo = 1, hi = quantity;
                while (lo < hi)
                {
                    var mid = lo + (hi - lo) / 2; var enough = false;
                    try { enough = ServerRoomTradeMath.Preview(next, index, mid, false).CashDelta >= quote.CashDelta; }
                    catch (InvalidOperationException) { }
                    if (enough) hi = mid; else lo = mid + 1;
                }
                quantity = lo; quote = ServerRoomTradeMath.Preview(next, index, quantity, false);
            }
            var minimumReceipt = ServerRoomTradeProtocol.Ratio.Of(next.AutoSale.MinimumNetPrices[index]) *
                ServerRoomTradeProtocol.Ratio.Of(quote.Quantity);
            if (quote.CashDelta * minimumReceipt.D < minimumReceipt.N)
            { ServerRoomLedger.Add(next, next.Market.QuoteDay!.Value, _ => { }, coin.Id + ":auto-minimum-price"); continue; }
            cash = checked(cash + ServerRoomTradeMath.Apply(next, index, quantity, false, cash));
            if (next.AutoSale.Kind == 2 && quantity < allowed || next.AutoSale.Kind == 1 && cash < target)
                ServerRoomLedger.Add(next, next.Market.QuoteDay!.Value, _ => { }, coin.Id + ":auto-partial");
        }
        return cash;
    }
    private static Dictionary<string, MachineFact> RequireFacts(ServerRoomState.Document source, IReadOnlyList<MachineFact> machines)
    {
        if (machines == null || machines.Count != source.Racks.Count) throw new InvalidOperationException("机架真实配置事实不完整。");
        var result = new Dictionary<string, MachineFact>(StringComparer.Ordinal);
        foreach (var machine in machines)
        {
            RequireFact(source, machine);
            if (!result.TryAdd(machine.RecordId, machine)) throw new InvalidOperationException("机器事实重复。");
            if (machine.Kind == "mining" && source.Maintenance.Single(m => m.UniqueId == machine.UniqueId).Tier != machine.Tier)
                throw new InvalidOperationException("箱体维护型号不匹配。");
        }
        return result;
    }
    private static void RequireFact(ServerRoomState.Document source, MachineFact machine)
    {
        if (machine == null) throw new InvalidOperationException("机器配置事实缺失。");
        var rack = source.Racks.SingleOrDefault(r => r.RecordId == machine.RecordId);
        if (rack == null || rack.UniqueId != machine.UniqueId || rack.Kind != machine.Kind)
            throw new InvalidOperationException("真实机器事实与机架身份不一致。");
    }
    private static ServerRoomState.RackReference MiningRack(ServerRoomState.Document source, string recordId)
    {
        RequireEnabled(source);
        return source.Racks.SingleOrDefault(r => r.RecordId == recordId && r.Kind == "mining")
            ?? throw new InvalidOperationException("请选择已上架的矿机。");
    }
    private static void RequireEnabled(ServerRoomState.Document source)
    { ServerRoomState.Validate(source); if (!source.Enabled) throw new InvalidOperationException("机房尚未启用。"); }
    private static void RequireCurrentDay(ServerRoomState.Document source, int day)
    {
        if (day < 0 || source.Market.QuoteDay != day || source.Network.Day != day ||
            source.LastSettledDay != null && source.LastSettledDay != day)
            throw new InvalidOperationException("租约报价日期已变化。");
    }
    private static int CoinIndex(string id) => ServerRoomMarket.CoinIndex(id);
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
