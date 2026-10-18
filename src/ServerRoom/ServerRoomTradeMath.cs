using System;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace PCExpansion;

/// <summary>One quote protocol for preview, manual orders and automatic settlement.</summary>
internal static class ServerRoomTradeMath
{
    private static readonly decimal[] Depths = { 100000m, 80000m, 50000m, 30000m, 20000m };
    internal static decimal Depth(int coin) => Depths[coin];
    internal sealed class TradeQuote
    {
        internal decimal Price { get; }
        internal long MicroUnits { get; }
        internal decimal Quantity => MicroUnits / 1000000m;
        internal decimal Value { get; }
        internal decimal Fee => Value * .01m;
        internal int CashDelta { get; }
        internal bool Buy { get; }
        internal int Day { get; }
        internal long Revision { get; }
        internal long NetBefore { get; }
        internal long NetAfter { get; }
        internal TradeQuote(ServerRoomState.Document source, int index, long units, bool buy, ServerRoomTradeProtocol.TradeQuote quote)
        {
            Price = source.Market.Prices[index]; MicroUnits = units; Value = quote.Gross;
            CashDelta = buy ? -quote.Cash : quote.Cash; Buy = buy;
            Day = source.Market.QuoteDay!.Value; Revision = source.Revision;
            NetBefore = source.Coins[index].DailyNetMicro; NetAfter = quote.AfterNetMicro;
        }
    }
    internal static long ParseUnits(string input)
    {
        if (input == null) throw new InvalidOperationException("请输入交易数量。");
        var text = input.Trim(); var dot = text.IndexOf('.');
        if (text.Length == 0 || text.Length > 24 || text.Any(c => c != '.' && (c < '0' || c > '9')) ||
            dot == 0 || dot >= 0 && (dot != text.LastIndexOf('.') || text.Length - dot is < 2 or > 5) ||
            !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity) ||
            quantity <= 0 || quantity > long.MaxValue / 1000000m)
            throw new InvalidOperationException("数量须为正数，最多四位小数（0.0001枚步长）。");
        return checked((long)(quantity * 1000000m));
    }
    internal static TradeQuote Preview(ServerRoomState.Document source, int index, long units, bool buy)
    {
        if (!source.Enabled || !source.Market.Initialized || index is < 0 or > 4)
            throw new InvalidOperationException("行情尚未就绪。");
        return new TradeQuote(source, index, units, buy, ServerRoomTradeProtocol.Quote(
            source.Market.Prices[index], Depth(index), units, buy, source.Coins[index].DailyNetMicro));
    }
    internal static long Maximum(ServerRoomState.Document source, int index, bool buy, int cash)
    {
        var coin = source.Coins[index];
        if (cash < 0) return 0;
        return buy ? ServerRoomTradeProtocol.MaxBuy(source.Market.Prices[index], Depth(index),
                long.MaxValue - coin.MicroUnits, coin.DailyNetMicro, cash) :
            ServerRoomTradeProtocol.MaxSell(source.Market.Prices[index], Depth(index), coin.MicroUnits,
                coin.DailyNetMicro, int.MaxValue - cash);
    }
    internal static ServerRoomState.Change Prepare(ServerRoomState.Document source, string coinId,
        long units, bool buy, int cash, int expectedQuoteDay)
    {
        ServerRoomState.Validate(source);
        if (source.Market.QuoteDay != expectedQuoteDay || cash < 0)
            throw new InvalidOperationException("行情或可用现金已变化，请刷新交易确认。");
        var index = ServerRoomMarket.CoinIndex(coinId);
        var next = ServerRoomState.Advance(source);
        var delta = Apply(next, index, units, buy, cash);
        ServerRoomState.Validate(next);
        return new ServerRoomState.Change(next, delta);
    }
    // Only mutates a detached candidate. Caller owns its one revision and atomic cash commit.
    internal static int Apply(ServerRoomState.Document next, int index, long units, bool buy, int cash)
    {
        var quote = Preview(next, index, units, buy); var coin = next.Coins[index];
        if (cash < 0 || (long)cash + quote.CashDelta < 0 || (long)cash + quote.CashDelta > int.MaxValue ||
            !buy && units > coin.MicroUnits || buy && units > long.MaxValue - coin.MicroUnits)
            throw new InvalidOperationException("现金、持币或钱包容量不足。");
        if (buy)
        {
            coin.MicroUnits = checked(coin.MicroUnits + units); coin.BoughtMicro = checked(coin.BoughtMicro + units);
            coin.Cost = checked(coin.Cost - quote.CashDelta);
            ServerRoomLedger.Add(next, quote.Day, a =>
            { a.BuyCash = -quote.CashDelta; a.BoughtMicro[index] = units; a.TradingFees = quote.Fee; });
        }
        else
        {
            var bought = (long)((BigInteger)coin.BoughtMicro * units / coin.MicroUnits);
            var mined = units - bought;
            // Rounding of source attribution never changes the fungible wallet or total cash.
            if (mined > coin.MinedMicro) { bought += mined - coin.MinedMicro; mined = coin.MinedMicro; }
            var removedCost = bought == coin.BoughtMicro ? coin.Cost : coin.Cost * ((decimal)bought / coin.BoughtMicro);
            var tradingCash = quote.CashDelta * ((decimal)bought / units);
            coin.MicroUnits -= units; coin.BoughtMicro -= bought; coin.MinedMicro -= mined; coin.Cost -= removedCost;
            ServerRoomLedger.Add(next, quote.Day, a =>
            {
                a.TradingSaleCash = tradingCash; a.MiningSaleCash = quote.CashDelta - tradingCash;
                a.TradingRealized = tradingCash - removedCost; a.TradingFees = quote.Fee;
                a.SoldBoughtMicro[index] = bought; a.SoldMinedMicro[index] = mined;
            });
        }
        coin.DailyNetMicro = quote.NetAfter;
        return quote.CashDelta;
    }
}
