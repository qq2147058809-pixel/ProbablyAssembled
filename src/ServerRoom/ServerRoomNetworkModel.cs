using System;
using System.Collections.Generic;

namespace PCExpansion;

/// <summary>Five bounded public networks. Only the completed day's known prices enter Advance.</summary>
internal static class ServerRoomNetworkModel
{
    private static readonly int[] Periods = { 4, 4, 5, 2, 3 };
    internal const decimal Minimum = .00000001m;
    internal const decimal Maximum = 100000000m;
    private const decimal AdjustmentFloor = .75m, AdjustmentCeiling = 1.35m;

    // Display only the known schedule and allowed bounds, never a future network draw.
    internal static int AdjustmentPeriod(int coin) => coin is >= 0 and < 5
        ? Periods[coin] : throw new ArgumentOutOfRangeException(nameof(coin));

    internal static int DaysUntilAdjustment(State state, int coin)
    {
        Validate(state);
        var period = AdjustmentPeriod(coin);
        var day = state.Day ?? throw new InvalidOperationException("网络尚未初始化。");
        return period - (day - state.InitialDay) % period;
    }

    internal static decimal AdjustmentLower(State state, int coin)
    {
        Validate(state); _ = AdjustmentPeriod(coin);
        return Math.Max(Minimum, state.D[coin] * AdjustmentFloor);
    }

    internal static decimal AdjustmentUpper(State state, int coin)
    {
        Validate(state); _ = AdjustmentPeriod(coin);
        return Math.Min(Maximum, state.D[coin] * AdjustmentCeiling);
    }

    internal sealed class State
    {
        public int? Day { get; set; }
        public int InitialDay { get; set; }
        public List<decimal> N { get; set; } = new() { 1.25m, 1.25m, 1.25m, 1.25m, 1.25m };
        public List<decimal> D { get; set; } = new() { 1.25m, 1.25m, 1.25m, 1.25m, 1.25m };
        // Per-coin chronological observations, not synthetic prehistory. J omits A on both sides of J/U.
        public List<List<decimal>> JHistory { get; set; } = EmptyHistories();
        public List<List<decimal>> NHistory { get; set; } = EmptyHistories();
        public List<decimal> IndustryShock { get; set; } = new() { 0m, 0m, 0m, 0m, 0m };
        public List<int> IndustryDaysRemaining { get; set; } = new() { 0, 0, 0, 0, 0 };
        public decimal ElectricityFactor { get; set; } = 1m;
        public int ElectricityDaysRemaining { get; set; }
        public decimal ElectricityNextFactor { get; set; } = 1m;
        public int ElectricityNextDuration { get; set; }
        public int? ElectricityPreparedDay { get; set; }
        public bool ElectricityEventsEnabled { get; set; }
        public long LowerGuardUpdates { get; set; }
        public long UpperGuardUpdates { get; set; }
    }

    internal static State New() => new();
    internal static State Initialize(int day)
    {
        if (day < 0) throw new ArgumentOutOfRangeException(nameof(day));
        return new State { Day = day, InitialDay = day };
    }
    internal static State Initialize(int day, string run, int slot, bool eventsEnabled)
        => PrepareElectricity(Initialize(day), run, slot, eventsEnabled);

    /// <summary>Freezes today's notice of tomorrow's electricity event; safe to call repeatedly.</summary>
    internal static State PrepareElectricity(State current, string run, int slot, bool eventsEnabled)
    {
        Validate(current); RequireContext(run, slot);
        var day = current.Day ?? throw new InvalidOperationException("网络尚未初始化。");
        var result = Copy(current);
        if (result.ElectricityPreparedDay == result.Day && result.ElectricityEventsEnabled == eventsEnabled) return result;
        result.ElectricityPreparedDay = result.Day; result.ElectricityEventsEnabled = eventsEnabled;
        if (!eventsEnabled)
        {
            result.ElectricityFactor = 1m; result.ElectricityDaysRemaining = 0;
            result.ElectricityNextDuration = 0; result.ElectricityNextFactor = 1m;
        }
        else if (result.ElectricityDaysRemaining == 0 &&
            ServerRoomEconomyRandom.Uniform(run, slot, "electricity-start", 0, day) < .10)
        {
            result.ElectricityNextFactor = Number(.85 + .30 * ServerRoomEconomyRandom.Uniform(run, slot, "electricity-rate", 0, day));
            result.ElectricityNextDuration = 3 + (int)Math.Floor(3 * ServerRoomEconomyRandom.Uniform(run, slot, "electricity-duration", 0, day));
        }
        Validate(result); return result;
    }

    internal static State Advance(State current, string run, int slot, int fromDay, IReadOnlyList<decimal> currentPrices, bool eventsEnabled)
    {
        Validate(current); RequireContext(run, slot);
        if (current.Day != fromDay || fromDay < 0 || fromDay == int.MaxValue || currentPrices == null || currentPrices.Count != 5)
            throw new ArgumentException("网络跨日上下文无效。");
        var result = PrepareElectricity(current, run, slot, eventsEnabled);
        var elapsed = fromDay - result.InitialDay;
        var shared = -.015 + .03 * ServerRoomEconomyRandom.Uniform(run, slot, "industry-shared", 0, fromDay);
        for (var c = 0; c < 5; c++)
        {
            if (currentPrices[c] < .000000000001m || currentPrices[c] > ServerRoomMiningModel.BasePrice(c) * 10000m)
                throw new ArgumentException("网络输入价格无效。");
            var remaining = result.IndustryDaysRemaining[c] - 1;
            if (remaining <= 0)
            {
                result.IndustryShock[c] = Number(.2 * shared + .8 * (-.015 + .03 * ServerRoomEconomyRandom.Uniform(run, slot, "industry-coin", c, fromDay)));
                remaining = 3 + (int)Math.Floor(5 * ServerRoomEconomyRandom.Uniform(run, slot, "industry-duration", c, fromDay));
            }
            result.IndustryDaysRemaining[c] = remaining;
            Add(result.JHistory[c], Significant(currentPrices[c] / ServerRoomMiningModel.BasePrice(c) * 2.5m / current.D[c]), 3);
            decimal sum = 0; foreach (var j in result.JHistory[c]) sum += j;
            var ratio = (double)(sum / result.JHistory[c].Count / 2m);
            var response = Math.Clamp(.08 * Math.Log(Math.Max(ratio, 1e-300)) + (double)result.IndustryShock[c], -.10, .10);
            Add(result.NHistory[c], current.N[c], 5);
            var nextN = (double)current.N[c] * Math.Exp(response);
            if (nextN < (double)Minimum) result.LowerGuardUpdates = checked(result.LowerGuardUpdates + 1);
            if (nextN > (double)Maximum) result.UpperGuardUpdates = checked(result.UpperGuardUpdates + 1);
            result.N[c] = Number(Math.Clamp(nextN, (double)Minimum, (double)Maximum));
            if ((elapsed + 1) % Periods[c] == 0)
            {
                var count = Math.Min(Periods[c], result.NHistory[c].Count); double log = 0;
                for (var k = result.NHistory[c].Count - count; k < result.NHistory[c].Count; k++) log += Math.Log((double)result.NHistory[c][k]);
                var geom = Math.Exp(log / count);
                result.D[c] = Number(Math.Clamp(geom, (double)(current.D[c] * AdjustmentFloor), (double)(current.D[c] * AdjustmentCeiling)));
                result.D[c] = Math.Clamp(result.D[c], Minimum, Maximum);
            }
        }
        if (result.ElectricityNextDuration > 0)
        {
            result.ElectricityFactor = result.ElectricityNextFactor;
            result.ElectricityDaysRemaining = result.ElectricityNextDuration;
            result.ElectricityNextDuration = 0; result.ElectricityNextFactor = 1m;
        }
        else if (result.ElectricityDaysRemaining > 0)
        {
            result.ElectricityDaysRemaining--;
            if (result.ElectricityDaysRemaining == 0) result.ElectricityFactor = 1m;
        }
        result.Day = fromDay + 1;
        result.ElectricityPreparedDay = null;
        result = PrepareElectricity(result, run, slot, eventsEnabled);
        Validate(result); return result;
    }

    internal static State Copy(State value)
    {
        Validate(value);
        var result = new State { Day = value.Day, InitialDay = value.InitialDay, N = new(value.N), D = new(value.D),
            IndustryShock = new(value.IndustryShock), IndustryDaysRemaining = new(value.IndustryDaysRemaining),
            ElectricityFactor = value.ElectricityFactor, ElectricityDaysRemaining = value.ElectricityDaysRemaining,
            ElectricityNextFactor = value.ElectricityNextFactor, ElectricityNextDuration = value.ElectricityNextDuration,
            ElectricityPreparedDay = value.ElectricityPreparedDay, ElectricityEventsEnabled = value.ElectricityEventsEnabled,
            LowerGuardUpdates = value.LowerGuardUpdates, UpperGuardUpdates = value.UpperGuardUpdates };
        for (var c = 0; c < 5; c++) { result.JHistory[c] = new(value.JHistory[c]); result.NHistory[c] = new(value.NHistory[c]); }
        return result;
    }

    internal static void Validate(State state)
    {
        if (state == null || state.InitialDay < 0 || state.Day < state.InitialDay || state.LowerGuardUpdates < 0 || state.UpperGuardUpdates < 0 ||
            state.N == null || state.N.Count != 5 || state.D == null || state.D.Count != 5 ||
            state.JHistory == null || state.JHistory.Count != 5 || state.NHistory == null || state.NHistory.Count != 5 ||
            state.IndustryShock == null || state.IndustryShock.Count != 5 || state.IndustryDaysRemaining == null || state.IndustryDaysRemaining.Count != 5 ||
            state.ElectricityFactor < .85m || state.ElectricityFactor > 1.15m || state.ElectricityNextFactor < .85m || state.ElectricityNextFactor > 1.15m ||
            state.ElectricityDaysRemaining < 0 || state.ElectricityDaysRemaining > 5 ||
            (state.ElectricityNextDuration != 0 && (state.ElectricityNextDuration < 3 || state.ElectricityNextDuration > 5)) ||
            (state.ElectricityPreparedDay.HasValue && state.ElectricityPreparedDay != state.Day) ||
            (state.ElectricityDaysRemaining == 0 && state.ElectricityFactor != 1m) ||
            (state.ElectricityNextDuration == 0 && state.ElectricityNextFactor != 1m) ||
            (state.ElectricityNextDuration > 0 && (state.ElectricityDaysRemaining > 0 || state.ElectricityPreparedDay != state.Day || !state.ElectricityEventsEnabled)))
            throw new ArgumentException("网络状态无效。");
        var elapsed = state.Day.HasValue ? state.Day.Value - state.InitialDay : 0;
        if (state.LowerGuardUpdates > (long)elapsed * 5 || state.UpperGuardUpdates > (long)elapsed * 5 ||
            state.LowerGuardUpdates + state.UpperGuardUpdates > (long)elapsed * 5 ||
            (!state.ElectricityEventsEnabled && (state.ElectricityDaysRemaining > 0 || state.ElectricityNextDuration > 0)))
            throw new ArgumentException("网络保护计数或电价事件无效。");
        for (var c = 0; c < 5; c++)
        {
            if (state.N[c] < Minimum || state.N[c] > Maximum || state.D[c] < Minimum || state.D[c] > Maximum ||
                state.IndustryShock[c] < -.015m || state.IndustryShock[c] > .015m || state.IndustryDaysRemaining[c] < 0 || state.IndustryDaysRemaining[c] > 7 ||
                state.JHistory[c] == null || state.JHistory[c].Count != Math.Min(elapsed, 3) ||
                state.NHistory[c] == null || state.NHistory[c].Count != Math.Min(elapsed, 5)) throw new ArgumentException("币网络状态无效。");
            foreach (var j in state.JHistory[c]) if (j <= 0m || j > 1000000000000000m) throw new ArgumentException("收益历史无效。");
            foreach (var n in state.NHistory[c]) if (n < Minimum || n > Maximum) throw new ArgumentException("算力历史无效。");
            if (elapsed == 0 && (state.N[c] != 1.25m || state.D[c] != 1.25m || state.IndustryShock[c] != 0m || state.IndustryDaysRemaining[c] != 0))
                throw new ArgumentException("网络初始状态无效。");
            if (elapsed > 0 && state.IndustryDaysRemaining[c] == 0) throw new ArgumentException("行业阶段缺失。");
        }
        if (!state.Day.HasValue && (state.InitialDay != 0 || state.ElectricityPreparedDay.HasValue || state.ElectricityDaysRemaining != 0 ||
            state.ElectricityNextDuration != 0 || state.ElectricityEventsEnabled || state.LowerGuardUpdates != 0 || state.UpperGuardUpdates != 0))
            throw new ArgumentException("未初始化网络包含活动状态。");
    }
    private static void RequireContext(string run, int slot)
    {
        if (string.IsNullOrWhiteSpace(run) || slot < 0) throw new ArgumentException("网络身份无效。");
    }
    private static List<List<decimal>> EmptyHistories() => new() { new(), new(), new(), new(), new() };
    private static void Add(List<decimal> list, decimal value, int maximum) { list.Add(value); if (list.Count > maximum) list.RemoveAt(0); }
    // 14 significant decimal digits freeze the exp/log boundary; no double is used for cash or minting.
    private static decimal Number(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new InvalidOperationException("网络数值无效。");
        return Significant((decimal)value);
    }
    private static decimal Significant(decimal value)
    {
        if (value == 0) return 0m;
        var places = Math.Clamp(13 - (int)Math.Floor(Math.Log10(Math.Abs((double)value))), 0, 28);
        return decimal.Round(value, places, MidpointRounding.ToEven);
    }
}
