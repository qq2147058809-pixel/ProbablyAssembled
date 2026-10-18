using System;
using System.Numerics;

namespace PCExpansion;

/// <summary>Exact integral and cash rounding. No wallet, clock, or native side effects.</summary>
internal static class ServerRoomTradeProtocol
{
    internal sealed class TradeQuote
    {
        public long MicroQuantity { get; set; }
        public long AfterNetMicro { get; set; }
        public decimal Gross { get; set; }
        public int Cash { get; set; }
    }

    // A bounded rational used only during one calculation. Never persisted or accumulated.
    internal readonly struct Ratio
    {
        internal readonly BigInteger N, D;
        internal Ratio(BigInteger n, BigInteger d)
        {
            if (d <= 0) throw new ArgumentOutOfRangeException(nameof(d));
            var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(n), d);
            N = n / gcd; D = d / gcd;
        }
        internal static Ratio Of(decimal value)
        {
            var b = decimal.GetBits(value);
            var n = (BigInteger)(uint)b[0] + ((BigInteger)(uint)b[1] << 32) + ((BigInteger)(uint)b[2] << 64);
            if (b[3] < 0) n = -n;
            return new Ratio(n, BigInteger.Pow(10, (b[3] >> 16) & 255));
        }
        public static Ratio operator +(Ratio a, Ratio b) => new(a.N * b.D + b.N * a.D, a.D * b.D);
        public static Ratio operator -(Ratio a, Ratio b) => new(a.N * b.D - b.N * a.D, a.D * b.D);
        public static Ratio operator *(Ratio a, Ratio b) => new(a.N * b.N, a.D * b.D);
        public static Ratio operator /(Ratio a, Ratio b)
        {
            if (b.N <= 0) throw new ArgumentOutOfRangeException(nameof(b));
            return new Ratio(a.N * b.D, a.D * b.N);
        }
        internal BigInteger Floor() => N >= 0 ? N / D : (N - D + 1) / D;
        internal BigInteger Ceiling() => -new Ratio(-N, D).Floor();
        internal decimal Fixed18() => (decimal)((this * Of(1000000000000000000m)).Floor()) / 1000000000000000000m;
    }

    internal static TradeQuote Quote(decimal price, decimal depthAmount, long microQty, bool buy, long signedDailyNetMicro)
    {
        var gross = ExactGross(price, depthAmount, microQty, buy, signedDailyNetMicro, out var after);
        var withFee = gross * Ratio.Of(buy ? 1.01m : .99m);
        var cash = buy ? withFee.Ceiling() : withFee.Floor();
        if (cash < 1 || cash > int.MaxValue) throw new InvalidOperationException("成交现金为零或超出安全范围。");
        return new TradeQuote { MicroQuantity = microQty, AfterNetMicro = after, Gross = gross.Fixed18(), Cash = (int)cash };
    }

    private static Ratio ExactGross(decimal price, decimal depthAmount, long qty, bool buy, long x, out long after)
    {
        if (qty <= 0) throw new ArgumentOutOfRangeException(nameof(qty));
        ValidatePosition(price, depthAmount, x);
        var next = (BigInteger)x + (buy ? (BigInteger)qty : -(BigInteger)qty);
        if (next < -long.MaxValue || next > long.MaxValue) throw new InvalidOperationException("当日净成交数量超界。");
        after = (long)next;
        ValidatePosition(price, depthAmount, after);
        var p = Ratio.Of(price); var depth = Ratio.Of(depthAmount);
        var amount = new Ratio(qty, 1000000) * p;
        var now = new Ratio(x, 1000000) * p;
        return amount * (Ratio.Of(1m) + now / depth + (buy ? amount : Ratio.Of(-1m) * amount) / (depth * Ratio.Of(2m)));
    }

    private static bool LessOrEqual(Ratio a, Ratio b) => a.N * b.D <= b.N * a.D;
    /// <summary>Exact shared gate for persisted net positions, quotes and maximum quantities.</summary>
    internal static void ValidatePosition(decimal price, decimal depthAmount, long signedDailyNetMicro)
    {
        RequireMarket(price, depthAmount);
        if (signedDailyNetMicro == long.MinValue) throw new ArgumentOutOfRangeException(nameof(signedDailyNetMicro));
        var current = new Ratio(BigInteger.Abs(signedDailyNetMicro), 1) * Ratio.Of(price);
        if (!LessOrEqual(current, Ratio.Of(depthAmount) * Ratio.Of(250000m)))
            throw new InvalidOperationException("超过当日可成交深度。");
    }
    private static void RequireMarket(decimal price, decimal depth)
    {
        if (price < .000000000001m || price > 15000000m || depth <= 0 || depth > 100000m)
            throw new ArgumentOutOfRangeException(nameof(price), "价格或深度无效。");
    }

    /// <summary>Largest legal micro quantity within the caller's inventory/capacity and cash boundary.</summary>
    internal static long MaxSell(decimal price, decimal depthAmount, long walletMicro, long signedDailyNetMicro, int cashRoom)
        => Maximum(price, depthAmount, walletMicro, false, signedDailyNetMicro, cashRoom);
    internal static long MaxBuy(decimal price, decimal depthAmount, long walletRoomMicro, long signedDailyNetMicro, int cashAvailable)
        => Maximum(price, depthAmount, walletRoomMicro, true, signedDailyNetMicro, cashAvailable);

    private static long Maximum(decimal price, decimal depth, long limit, bool buy, long x, int cashLimit)
    {
        if (limit < 0 || cashLimit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
        // Validate the starting position even when the caller has no cash or inventory.
        ValidatePosition(price, depth, x);
        long low = 0, high = limit;
        while (low < high)
        {
            var mid = low + (high - low) / 2 + 1;
            bool legal;
            try
            {
                var g = ExactGross(price, depth, mid, buy, x, out _);
                var f = g * Ratio.Of(buy ? 1.01m : .99m);
                legal = (buy ? f.Ceiling() : f.Floor()) <= cashLimit;
            }
            catch (InvalidOperationException) { legal = false; }
            if (legal) low = mid; else high = mid - 1;
        }
        if (low == 0) return 0;
        try { return Quote(price, depth, low, buy, x).Cash <= cashLimit ? low : 0; }
        catch (InvalidOperationException) { return 0; }
    }
}
