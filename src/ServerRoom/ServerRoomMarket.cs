using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PCExpansion;

/// <summary>Pure, replayable market with persistent latent trends. No native cash or clock access.</summary>
internal static class ServerRoomMarket
{
    private static readonly decimal[] Bases = { 50m, 200m, 800m, 100m, 1500m };
    private static readonly double[] Ordinary = { .025, .045, .065, .09, .11 };
    private static readonly double[] ExtremeProbability = { .01, .015, .025, .035, .04 };
    private static readonly double[] UpLow = { .05, .08, .12, .15, .18 };
    private static readonly double[] UpHigh = { .10, .15, .22, .30, .33 };
    private static readonly double[] DownLow = { .05, .08, .12, .15, .18 };
    private static readonly double[] DownHigh = { .10, .15, .22, .30, .33 };
    private static readonly decimal[] Jump = { .15m, .25m, .35m, .45m, .60m };
    private static readonly double[] OrdinaryUpProbability = BalancedProbabilities(false);
    private static readonly double[] ExtremeUpProbability = BalancedProbabilities(true);
    // Representation guards, not economic support/resistance. In particular the old
    // base * .0001 floor no longer changes a coin's chance of rising.
    private const decimal MinimumPrice = .000000000001m;

    private static double[] BalancedProbabilities(bool extreme)
    {
        var result = new double[5];
        for (var i = 0; i < result.Length; i++)
        {
            // For the sampled arcsine magnitude, E[log(a+b*sin²(theta))]
            // is 2*log((sqrt(a)+sqrt(a+b))/2). Balance gains against losses
            // in log space so ordinary percentage moves do not systematically
            // destroy value. This has no dependence on the current/initial price.
            var upLow = extreme ? UpLow[i] : 0;
            var upHigh = extreme ? UpHigh[i] : Ordinary[i];
            var downLow = extreme ? DownLow[i] : 0;
            var downHigh = extreme ? DownHigh[i] : Ordinary[i];
            var gain = 2 * Math.Log((Math.Sqrt(1 + upLow) + Math.Sqrt(1 + upHigh)) / 2);
            var loss = 2 * Math.Log((Math.Sqrt(1 - downLow) + Math.Sqrt(1 - downHigh)) / 2);
            result[i] = -loss / (gain - loss);
        }
        return result;
    }

    internal static decimal BasePrice(int index) { RequireIndex(index); return Bases[index]; }
    internal static decimal LowerPrice(int index) { RequireIndex(index); return MinimumPrice; }
    internal static decimal UpperPrice(int index) => BasePrice(index) * 10000m;
    internal static int CoinIndex(string id)
    {
        for (var i = 0; i < ServerRoomState.CoinIds.Count; i++)
            if (ServerRoomState.CoinIds[i] == id) return i;
        throw new InvalidOperationException("币种无效。");
    }
    internal static ServerRoomState.MarketState Initialize(ServerRoomState.MarketState source,
        string run, int slot, int day, bool boomActive, string boomIdentity)
    {
        RequireContext(run, slot, day, boomActive, boomIdentity);
        Validate(source);
        if (source.Initialized)
        {
            if (source.QuoteDay != day) throw new InvalidOperationException("行情已初始化，请按日推进。");
            return Copy(source);
        }
        var result = new ServerRoomState.MarketState
        {
            Initialized = true, QuoteDay = day, Prices = new List<decimal>(Bases),
            RandomState = Context("initialize", run, slot, day, boomActive, boomIdentity),
            BoomMarker = boomActive ? boomIdentity : ""
        };
        // An already active event opens at base plus its first jump, without inventing history.
        if (boomActive) ApplyFirstJump(result.Prices);
        AppendHistory(result, day);
        Validate(result);
        return result;
    }

    internal static ServerRoomState.MarketState Advance(ServerRoomState.MarketState source,
        string run, int slot, int day, bool boomActive, string boomIdentity)
    {
        RequireContext(run, slot, day, boomActive, boomIdentity);
        Validate(source);
        if (!source.Initialized) return Initialize(source, run, slot, day, boomActive, boomIdentity);
        if (source.QuoteDay == day) return Copy(source);
        if ((long)source.QuoteDay!.Value + 1 != day)
            throw new InvalidOperationException("行情必须逐日推进，不能回退或补抽未知事件历史。");
        var next = Copy(source);
        next.QuoteDay = day;
        next.RandomState = Context(source.RandomState, run, slot, day, boomActive, boomIdentity);
        if (boomActive && source.BoomMarker != boomIdentity)
        {
            ApplyFirstJump(next.Prices);
            next.BoomMarker = boomIdentity;
        }
        else
        {
            var random = new ReplayRandom(next.RandomState);
            var extreme = new bool[5];
            for (var i = 0; i < 5; i++) extreme[i] = random.Uniform() < ExtremeProbability[i] * (boomActive ? 2 : 1);
            var commonNormal = random.Normal();
            var commonTrend = Trend(run, slot, day, -1);
            var directions = new bool[5];
            for (var i = 0; i < 5; i++)
            {
                // Public and coin-specific phases persist across several days,
                // but neither absolute price nor player trades affect direction.
                // Symmetric offsets preserve each branch's zero-log-drift baseline
                // over randomly drawn ordinary phases. Shocks can oppose a trend.
                var trend = .65 * commonTrend + .35 * Trend(run, slot, day, i);
                var balanced = extreme[i] ? ExtremeUpProbability[i] : OrdinaryUpProbability[i];
                var upProbability = boomActive ? .7 : balanced + .28 * trend;
                var direction = Math.Sqrt(.65) * commonNormal + Math.Sqrt(.35) * random.Normal();
                directions[i] = NormalCdf(direction) < upProbability;
            }
            for (var i = 0; i < 5; i++)
                next.Prices[i] = SamplePrice(i, source.Prices[i], extreme[i], directions[i], random);
        }
        // Ending an event keeps the current price and restores ordinary phases.
        AppendHistory(next, day);
        Validate(next);
        return next;
    }

    internal static void Validate(ServerRoomState.MarketState value)
    {
        if (value == null || value.Prices == null || value.History == null || value.RandomState == null || value.BoomMarker == null)
            throw new InvalidOperationException("行情字段无效。");
        if (!value.Initialized)
        {
            if (value.QuoteDay != null || value.Prices.Count != 0 || value.History.Count != 0 || value.RandomState != "" || value.BoomMarker != "")
                throw new InvalidOperationException("未初始化行情含有经营记录。");
            return;
        }
        if (value.QuoteDay == null || value.QuoteDay < 0 || value.History.Count is < 1 or > 30 ||
            value.RandomState.Length != 64 || value.RandomState.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')) ||
            value.BoomMarker != "" && !Token(value.BoomMarker))
            throw new InvalidOperationException("行情日期、历史或随机上下文无效。");
        ValidatePrices(value.Prices);
        for (var i = 0; i < value.History.Count; i++)
        {
            var quote = value.History[i];
            if (quote == null || quote.Day < 0 || i > 0 && (long)value.History[i - 1].Day + 1 != quote.Day)
                throw new InvalidOperationException("行情历史日期不连续。");
            ValidatePrices(quote.Prices);
        }
        var last = value.History[value.History.Count - 1];
        if (last.Day != value.QuoteDay || !last.Prices.SequenceEqual(value.Prices))
            throw new InvalidOperationException("当前行情与历史末日不一致。");
    }

    private static decimal SamplePrice(int i, decimal price, bool extreme, bool up, ReplayRandom random)
    {
        var upLow = extreme ? UpLow[i] : 0;
        var upHigh = extreme ? UpHigh[i] : Ordinary[i];
        var downLow = extreme ? DownLow[i] : 0;
        var downHigh = extreme ? DownHigh[i] : Ordinary[i];
        var low = up ? upLow : downLow;
        var high = up ? upHigh : downHigh;
        var sine = Math.Sin(Math.PI * random.Uniform() / 2);
        var magnitude = (decimal)(low + (high - low) * sine * sine);
        var proposed = price * (1m + (up ? magnitude : -magnitude));
        // At the remote representation guards only, saturate rather than force a
        // reversal or resample upward. Further down days can remain at the minimum;
        // no zero/delisting state, extra reward or guaranteed recovery is created.
        return Math.Min(UpperPrice(i), Math.Max(MinimumPrice, proposed));
    }

    private static double Trend(string run, int slot, int day, int coin)
    {
        // Independent 256-day windows bound work even at int.MaxValue day. Each
        // window starts at a random phase age, then draws 6..20-day phases. Window
        // seams do not reset prices or force a direction/alternating cycle.
        var epoch = day / 256;
        var random = new ReplayRandom(Context("phase.v1." + coin.ToString(CultureInfo.InvariantCulture),
            run, slot, epoch, false, ""));
        var remaining = day % 256 + (int)(random.Uniform() * 20);
        while (true)
        {
            var duration = 6 + (int)(random.Uniform() * 15);
            var draw = random.Uniform();
            var trend = draw < .3 ? -1d : draw < .7 ? 0d : 1d;
            if (remaining < duration) return trend;
            remaining -= duration;
        }
    }

    private static void ApplyFirstJump(List<decimal> prices)
    {
        for (var i = 0; i < 5; i++) prices[i] = Math.Min(UpperPrice(i), prices[i] * (1m + Jump[i]));
    }
    private static void AppendHistory(ServerRoomState.MarketState state, int day)
    {
        state.History.Add(new ServerRoomState.Quote { Day = day, Prices = new List<decimal>(state.Prices) });
        if (state.History.Count > 30) state.History.RemoveAt(0);
    }
    private static ServerRoomState.MarketState Copy(ServerRoomState.MarketState state) => new()
    {
        Initialized = state.Initialized, QuoteDay = state.QuoteDay, Prices = new List<decimal>(state.Prices),
        History = state.History.Select(q => new ServerRoomState.Quote { Day = q.Day, Prices = new List<decimal>(q.Prices) }).ToList(),
        RandomState = state.RandomState, BoomMarker = state.BoomMarker
    };
    private static void ValidatePrices(List<decimal> prices)
    {
        if (prices == null || prices.Count != 5) throw new InvalidOperationException("五币行情不完整。");
        for (var i = 0; i < 5; i++)
            if (prices[i] < LowerPrice(i) || prices[i] > UpperPrice(i)) throw new InvalidOperationException("币价越界。");
    }
    private static void RequireIndex(int index)
    { if (index is < 0 or >= 5) throw new InvalidOperationException("币种无效。"); }
    private static bool Token(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 &&
        value == value.Trim() && !value.Any(char.IsControl);
    private static void RequireContext(string run, int slot, int day, bool active, string identity)
    {
        if (!Token(run) || slot < 0 || day < 0 || identity == null || active && !Token(identity))
            throw new InvalidOperationException("行情会话、日期或事件身份无效。");
    }
    private static string Context(string previous, string run, int slot, int day, bool active, string identity)
    {
        // Length-delimited tokens cannot collide through a separator embedded in a run/event ID.
        var payload = "PCExpansion.market.C.v1|" + previous.Length + ":" + previous + run.Length + ":" + run + "|" +
            slot.ToString(CultureInfo.InvariantCulture) + "|" + day.ToString(CultureInfo.InvariantCulture) + "|" +
            (active ? "1" : "0") + "|" + (active ? identity.Length + ":" + identity : "0:");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
    private static double NormalCdf(double value)
    {
        // A&S 26.2.17; absolute CDF error below 7.5e-8, used only for the Gaussian copula direction.
        var x = Math.Abs(value); var t = 1 / (1 + .2316419 * x);
        var tail = Math.Exp(-x * x / 2) / Math.Sqrt(2 * Math.PI) * t *
            (.319381530 + t * (-.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
        return value >= 0 ? 1 - tail : tail;
    }
    private sealed class ReplayRandom
    {
        private ulong state;
        internal ReplayRandom(string seed) => state = ulong.Parse(seed.Substring(0, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        internal double Uniform()
        {
            // SplitMix64, explicit unchecked arithmetic: independent of process hash/random implementations.
            unchecked
            {
                var z = (state += 0x9e3779b97f4a7c15UL);
                z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL;
                z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL;
                z ^= z >> 31;
                return ((z >> 12) + .5) / 4503599627370496d;
            }
        }
        internal double Normal() => Math.Sqrt(-2 * Math.Log(Uniform())) * Math.Cos(2 * Math.PI * Uniform());
    }
}
