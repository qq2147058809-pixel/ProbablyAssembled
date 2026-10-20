using System;
using System.Collections.Generic;

namespace PCExpansion;

/// <summary>Frozen hardware and pool economics, expressed only in copied facts.</summary>
internal static class ServerRoomMiningModel
{
    private static readonly decimal[] Core = { 10m, 16m, 25m, 39m, 60m };
    private static readonly decimal[] Memory = { 14m, 20m, 27m, 36m, 48m };
    private static readonly decimal[] Large = { 14m, 23m, 36m, 60m, 95m };
    private static readonly decimal[] GpuWatts = { 45m, 65m, 95m, 135m, 190m };
    private static readonly decimal[] BoxWatts = { 12m, 18m, 25m, 32m, 45m };
    private static readonly int[] Slots = { 1, 2, 3, 4, 6 };
    private static readonly decimal[] HashFactors = { .82m, 1m, 1.12m };
    private static readonly decimal[] PowerFactors = { .70m, 1m, 1.35m };
    private static readonly decimal[] RejectRates = { .002m, .005m, .02m };
    private static readonly decimal[] WearRates = { .6m, 1m, 2m };
    private static readonly decimal[] Bases = { 50m, 200m, 800m, 100m, 1500m };
    private static readonly decimal[] Algorithms = { 1m, .95m, .65m, .95m, 1m };

    internal sealed class Card
    {
        public int Tier { get; set; }
        public int VramGb { get; set; }
        public bool Intact { get; set; }
    }
    internal sealed class CardResult
    {
        public int Index { get; set; }
        public string Reason { get; set; } = "";
        public decimal Hash { get; set; }
        public decimal Watts { get; set; }
    }
    internal sealed class Capability
    {
        public bool CanRun { get; set; }
        public string Reason { get; set; } = "";
        public decimal NominalHash { get; set; }
        public decimal EffectiveHash { get; set; }
        public decimal Watts { get; set; }
        public decimal Wear { get; set; }
        public decimal RejectRate { get; set; }
        public int EligibleCards { get; set; }
        public List<CardResult> Cards { get; set; } = new();
    }
    internal sealed class Production
    {
        public long Microcoins { get; set; }
        // Coin units, quantized to 1e-18; always [0, 1e-6).
        public decimal ResidueCoin { get; set; }
        // After rejection, luck and pool fee; before joining the previous residue.
        public decimal ProducedCoin { get; set; }
        public decimal PoolFeeCoin { get; set; }
    }

    internal static decimal BasePrice(int coin) { RequireCoin(coin); return Bases[coin]; }
    internal static void RequireCoin(int coin)
    {
        if (coin < 0 || coin >= 5) throw new ArgumentOutOfRangeException(nameof(coin));
    }

    // Modes: 0 eco, 1 standard, 2 overclock. Pools: 0 stable, 1 floating.
    internal static Capability Evaluate(int boxTier, IReadOnlyList<Card> cards, int coin, int mode, decimal maintenance)
    {
        RequireCoin(coin);
        if (boxTier < 1 || boxTier > 5 || cards == null || cards.Count > Slots[boxTier - 1] ||
            mode < 0 || mode > 2 || maintenance < 0 || maintenance > 100)
            throw new ArgumentException("矿机事实无效。");
        var result = new Capability { Wear = WearRates[mode], RejectRate = RejectRates[mode] + (maintenance > 0 && maintenance < 30 ? .03m : 0m) };
        var minimumVram = coin == 2 ? 16 : coin == 1 || coin == 3 ? 8 : 4;
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            if (card == null || card.Tier < 1 || card.Tier > 5 || card.VramGb < 0 || card.VramGb > 1024)
                throw new ArgumentException("显卡事实无效。");
            var item = new CardResult { Index = i };
            result.Cards.Add(item);
            if (!card.Intact) { item.Reason = "broken"; continue; }
            if (card.VramGb < minimumVram) { item.Reason = "vram"; continue; }
            var t = card.Tier - 1;
            var h = coin == 2 ? Large[t] * (.6m + .4m * Math.Min((card.VramGb - 16m) / 16m, 1m)) :
                coin == 1 || coin == 3 ? Memory[t] * (.75m + .25m * Math.Min((card.VramGb - 8m) / 8m, 1m)) : Core[t];
            item.Hash = h * HashFactors[mode]; item.Watts = GpuWatts[t] * PowerFactors[mode];
            result.NominalHash += item.Hash; result.Watts += item.Watts; result.EligibleCards++;
        }
        result.Reason = maintenance == 0 ? "maintenance" : maintenance < 30 && mode == 2 ? "overclock-maintenance" : result.EligibleCards == 0 ? "no-eligible-card" : "";
        result.CanRun = result.Reason.Length == 0;
        result.EffectiveHash = result.NominalHash * (1m - result.RejectRate);
        result.Watts = result.CanRun ? result.Watts + BoxWatts[boxTier - 1] : 0m;
        if (!result.CanRun) { result.NominalHash = 0m; result.EffectiveHash = 0m; result.Wear = 0m; }
        return result;
    }

    internal static Production Produce(Capability capacity, int coin, decimal difficulty, int pool, decimal luck, decimal residueCoin)
    {
        RequireCoin(coin); ValidateResidue(residueCoin);
        if (capacity == null || difficulty < .00000001m || difficulty > 100000000m || pool < 0 || pool > 1 ||
            (luck != .70m && luck != .85m && luck != 1m && luck != 1.15m && luck != 1.30m) ||
            capacity.EffectiveHash < 0 || capacity.EffectiveHash > 1000m)
            throw new ArgumentException("产出计算输入无效。");
        if (!capacity.CanRun) return new Production { ResidueCoin = residueCoin };
        var raw = ServerRoomTradeProtocol.Ratio.Of(capacity.EffectiveHash) * ServerRoomTradeProtocol.Ratio.Of(Algorithms[coin]) *
            ServerRoomTradeProtocol.Ratio.Of(2.5m) / ServerRoomTradeProtocol.Ratio.Of(Bases[coin] * difficulty);
        if (pool == 1) raw *= ServerRoomTradeProtocol.Ratio.Of(luck);
        var payout = (raw * ServerRoomTradeProtocol.Ratio.Of(pool == 0 ? .98m : .995m)).Fixed18();
        var sum = payout + residueCoin;
        var micro = decimal.Floor(sum * 1000000m);
        if (micro > long.MaxValue) throw new InvalidOperationException("当日产币超过钱包表示范围。");
        return new Production { Microcoins = (long)micro, ResidueCoin = sum - micro / 1000000m,
            ProducedCoin = payout, PoolFeeCoin = (raw * ServerRoomTradeProtocol.Ratio.Of(pool == 0 ? .02m : .005m)).Fixed18() };
    }

    internal static void ValidateResidue(decimal residueCoin)
    {
        if (residueCoin < 0 || residueCoin >= .000001m || decimal.Truncate(residueCoin * 1000000000000000000m) != residueCoin * 1000000000000000000m)
            throw new ArgumentException("低于微币的余量无效。");
    }
    internal static int ElectricityCost(decimal deviceWatts, decimal electricityFactor)
    {
        if (deviceWatts < 0 || deviceWatts > 100000m || electricityFactor < .85m || electricityFactor > 1.15m)
            throw new ArgumentOutOfRangeException(nameof(deviceWatts));
        return (int)decimal.Ceiling(deviceWatts * 40m * 1.15m * electricityFactor / 1000m);
    }
}
