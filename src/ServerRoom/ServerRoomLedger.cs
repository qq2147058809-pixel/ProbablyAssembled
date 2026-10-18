using System;
using System.Collections.Generic;
using System.Linq;

namespace PCExpansion;

/// <summary>Cash movements and earned revenue are separate. Unknown acquisition costs stay unknown.</summary>
internal static class ServerRoomLedger
{
    internal sealed class Amounts
    {
        public decimal InvestmentCash { get; set; }
        public decimal MaintenanceCash { get; set; }
        public decimal MaintenanceMaterials { get; set; }
        public decimal ElectricityCash { get; set; }
        public decimal BuyCash { get; set; }
        public decimal MiningSaleCash { get; set; }
        public decimal TradingSaleCash { get; set; }
        public decimal TradingRealized { get; set; }
        public decimal LeaseAdvanceCash { get; set; }
        public decimal RentEarned { get; set; }
        public decimal LeaseFeesEarned { get; set; }
        public decimal LeaseRoundingEarned { get; set; }
        public decimal TradingFees { get; set; }
        public List<decimal> ProducedCoins { get; set; } = Zeros();
        public List<decimal> PoolFees { get; set; } = Zeros();
        public List<decimal> BoughtMicro { get; set; } = Zeros();
        public List<decimal> SoldBoughtMicro { get; set; } = Zeros();
        public List<decimal> SoldMinedMicro { get; set; } = Zeros();
        internal decimal CashDelta => LeaseAdvanceCash + MiningSaleCash + TradingSaleCash -
            InvestmentCash - MaintenanceCash - ElectricityCash - BuyCash;
    }
    internal sealed class Day
    {
        public int Date { get; set; }
        public Amounts Amount { get; set; } = new();
        public List<string> Notices { get; set; } = new();
        public int? ProductionFromDay { get; set; }
        public decimal MiningWatts { get; set; }
        public decimal ElectricityFactor { get; set; } = 1m;
        public List<decimal> NetworkN { get; set; } = new() { 1.25m, 1.25m, 1.25m, 1.25m, 1.25m };
        public List<decimal> Difficulty { get; set; } = new() { 1.25m, 1.25m, 1.25m, 1.25m, 1.25m };
    }
    internal sealed class State
    {
        // Inventory predates the ledger and can have gifts, repairs and barter. Do not infer its cost from sell value.
        public bool HardwareCostsComplete { get; set; }
        public Amounts Total { get; set; } = new();
        public List<Day> Days { get; set; } = new();
    }
    private static List<decimal> Zeros() => new() { 0m, 0m, 0m, 0m, 0m };
    internal static void Add(ServerRoomState.Document document, int date, Action<Amounts> fill, string notice = "")
    {
        var state = document.Ledger;
        if (date < 0 || state.Days.Count > 0 && date < state.Days[^1].Date)
            throw new InvalidOperationException("账本日期回退。");
        var delta = new Amounts(); fill(delta); ValidateAmounts(delta);
        if (state.Days.Count == 0 || state.Days[^1].Date != date) state.Days.Add(new Day { Date = date,
            NetworkN = new(document.Network.N), Difficulty = new(document.Network.D) });
        Accumulate(state.Total, delta); Accumulate(state.Days[^1].Amount, delta);
        if (notice.Length > 0 && !state.Days[^1].Notices.Contains(notice)) state.Days[^1].Notices.Add(notice);
        state.Days.RemoveAll(day => (long)day.Date < (long)date - 89);
    }
    private static void Accumulate(Amounts target, Amounts delta)
    {
        // This closed DTO contains only scalar decimals and five-coin decimal vectors.
        foreach (var property in typeof(Amounts).GetProperties())
            if (property.PropertyType == typeof(decimal))
                property.SetValue(target, checked((decimal)property.GetValue(target)! + (decimal)property.GetValue(delta)!));
            else
            {
                var a = (List<decimal>)property.GetValue(target)!; var b = (List<decimal>)property.GetValue(delta)!;
                for (var i = 0; i < 5; i++) a[i] = checked(a[i] + b[i]);
            }
    }
    internal static void Validate(State state)
    {
        if (state == null || state.HardwareCostsComplete || state.Total == null || state.Days == null || state.Days.Count > 90)
            throw new InvalidOperationException("经营账本无效。");
        ValidateAmounts(state.Total);
        var previous = -1;
        foreach (var day in state.Days)
        {
            if (day == null || day.Date <= previous || day.Notices == null ||
                day.Notices.Any(note => string.IsNullOrWhiteSpace(note) || note.Length > 512) ||
                day.MiningWatts < 0 || day.MiningWatts > 12500m || day.ElectricityFactor < .85m || day.ElectricityFactor > 1.15m ||
                day.ProductionFromDay != null && (day.ProductionFromDay < 0 || (long)day.ProductionFromDay + 1 != day.Date) ||
                day.ProductionFromDay == null && day.MiningWatts != 0 || !NetworkValues(day.NetworkN) || !NetworkValues(day.Difficulty))
                throw new InvalidOperationException("经营账本日期或状态无效。");
            previous = day.Date; ValidateAmounts(day.Amount);
        }
        if (state.Days.Count > 0 && state.Days[^1].Date - state.Days[0].Date > 89)
            throw new InvalidOperationException("经营明细超过90日。");
    }
    private static bool NetworkValues(List<decimal> values) => values != null && values.Count == 5 &&
        values.All(value => value >= ServerRoomNetworkModel.Minimum && value <= ServerRoomNetworkModel.Maximum);
    private static void ValidateAmounts(Amounts amount)
    {
        if (amount == null) throw new InvalidOperationException("经营金额缺失。");
        foreach (var property in typeof(Amounts).GetProperties())
            if (property.PropertyType == typeof(decimal))
            {
                if (property.Name != nameof(Amounts.TradingRealized) && (decimal)property.GetValue(amount)! < 0)
                    throw new InvalidOperationException("经营金额为负。");
            }
            else if (property.GetValue(amount) is not List<decimal> values || values.Count != 5 || values.Any(value => value < 0))
                throw new InvalidOperationException("经营币种向量无效。");
    }
}
