using System;

namespace PCExpansion;

internal static class ServerRoomLeaseContract
{
    // Analytical service revenue has 12 fractional credit digits. This keeps the
    // entire int32 game-day horizon exact for at most ten physical servers.
    private const decimal RevenueScale = 1000000000000m;
    internal static decimal Gross(decimal compute, int days) => Fixed(
        ServerRoomTradeProtocol.Ratio.Of(compute) * ServerRoomTradeProtocol.Ratio.Of(2.5m) *
        ServerRoomTradeProtocol.Ratio.Of(days) * ServerRoomTradeProtocol.Ratio.Of(1m + Bonus(days)));
    private static decimal Fixed(ServerRoomTradeProtocol.Ratio value) =>
        (decimal)(value * ServerRoomTradeProtocol.Ratio.Of(RevenueScale)).Floor() / RevenueScale;
    internal static decimal Remaining(decimal total, int remaining, int duration) => Fixed(
        ServerRoomTradeProtocol.Ratio.Of(total) * ServerRoomTradeProtocol.Ratio.Of(remaining) /
        ServerRoomTradeProtocol.Ratio.Of(duration));
    internal static decimal Bonus(int days) => days switch
    { 3 => 0m, 5 => .03m, 7 => .05m, _ => throw new InvalidOperationException("租期仅允许三、五、七日。") };
    internal static void Validate(ServerRoomState.Lease lease, int? lastSettledDay)
    {
        if (lease.UniqueId < 0 || lease.StartedDay < 0 || lease.DurationDays is not (3 or 5 or 7) ||
            (long)lease.DueDay != (long)lease.StartedDay + lease.DurationDays || lease.RemainingDays is <= 0 or > 7 ||
            lease.RemainingDays != (long)lease.DueDay - Math.Max(lease.StartedDay, lastSettledDay ?? lease.StartedDay) ||
            lease.EffectiveCompute <= 0 || lease.EffectiveCompute > 240m || lease.Watts <= 0 || lease.Watts > 821m ||
            lease.SigningElectricity < .85m || lease.SigningElectricity > 1.15m ||
            string.IsNullOrEmpty(lease.Configuration) || lease.Configuration.Length > 256)
            throw new InvalidOperationException("租约期限或冻结机器事实无效。");
        var gross = Gross(lease.EffectiveCompute, lease.DurationDays);
        var daily = ServerRoomMiningModel.ElectricityCost(lease.Watts, lease.SigningElectricity);
        var fees = checked(daily * lease.DurationDays);
        var net = checked((int)decimal.Floor(gross - fees));
        if (lease.GrossRent != gross || lease.DailyFee != daily || lease.PrepaidFees != fees || lease.NetIncome != net || net <= 0)
            throw new InvalidOperationException("租约冻结金额与净收入不一致。");
    }
}
