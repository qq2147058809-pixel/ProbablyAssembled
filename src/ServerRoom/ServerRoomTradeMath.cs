using System;
using System.Globalization;
using System.Linq;

namespace PCExpansion;

/// <summary>Whole-order fee rounding and wallet/cost candidates; publishing belongs to the transaction layer.</summary>
internal static class ServerRoomTradeMath
{
    internal sealed class TradeQuote
    {
        internal decimal Price { get; }
        internal long Units10 { get; }
        internal decimal Quantity => Units10 / 10m;
        internal decimal Value { get; }
        internal decimal Fee => Value * .01m;
        internal int CashDelta { get; }
        internal bool Buy { get; }
        internal TradeQuote(decimal price, long units10, decimal value, int delta, bool buy)
        { Price = price; Units10 = units10; Value = value; CashDelta = delta; Buy = buy; }
    }

    internal static long ParseUnits(string input)
    {
        if (input == null) throw new InvalidOperationException("请输入交易数量。");
        var text = input.Trim();
        var dot = text.IndexOf('.');
        if (text.Length == 0 || text.Length > 21 || text.Any(c => c != '.' && (c < '0' || c > '9')) ||
            dot == 0 || dot >= 0 && (dot != text.LastIndexOf('.') || text.Length - dot != 2))
            throw new InvalidOperationException("数量须为正数，且最多一位小数（0.1枚步长）。");
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity) ||
            quantity <= 0 || quantity * 10m > long.MaxValue)
            throw new InvalidOperationException("交易数量超出范围。");
        return checked((long)(quantity * 10m));
    }

    internal static TradeQuote Preview(decimal price, long units10, bool buy)
    {
        if (price <= 0 || units10 <= 0) throw new InvalidOperationException("单价和数量须为正数。");
        try
        {
            var value = checked(price * (units10 / 10m));
            var cash = buy ? decimal.Ceiling(checked(value * 1.01m)) : decimal.Floor(checked(value * .99m));
            if (cash < 1) throw new InvalidOperationException("卖出到账不足1，请增加数量。");
            if (cash > int.MaxValue) throw new InvalidOperationException("交易现金超出游戏整数范围。");
            return new TradeQuote(price, units10, value, buy ? -(int)cash : (int)cash, buy);
        }
        catch (OverflowException) { throw new InvalidOperationException("交易金额超出安全范围。"); }
    }

    internal static ServerRoomState.Change Prepare(ServerRoomState.Document source, string coinId,
        long units10, bool buy, int cash, int expectedQuoteDay)
    {
        ServerRoomState.Validate(source);
        if (!source.Enabled || !source.Market.Initialized || source.Market.QuoteDay != expectedQuoteDay || cash < 0)
            throw new InvalidOperationException("行情或可用现金已变化，请刷新交易确认。");
        var index = ServerRoomMarket.CoinIndex(coinId);
        var quote = Preview(source.Market.Prices[index], units10, buy);
        var old = source.Coins[index];
        if (buy && (long)cash + quote.CashDelta < 0) throw new InvalidOperationException("现金不足。");
        if (!buy && old.Units10 < units10) throw new InvalidOperationException("持币不足。");
        if ((long)cash + quote.CashDelta > int.MaxValue) throw new InvalidOperationException("交易后现金超出游戏整数范围。");
        try
        {
            var next = ServerRoomState.Advance(source);
            var coin = next.Coins[index];
            if (buy)
            {
                coin.Units10 = checked(old.Units10 + units10);
                coin.Cost = checked(old.Cost - quote.CashDelta);
            }
            else
            {
                coin.Units10 = old.Units10 - units10;
                // Divide the quantity first to avoid overflowing a large valid total cost.
                coin.Cost = coin.Units10 == 0 ? 0 : old.Cost * ((decimal)coin.Units10 / old.Units10);
            }
            ServerRoomState.Validate(next);
            return new ServerRoomState.Change(next, quote.CashDelta);
        }
        catch (OverflowException) { throw new InvalidOperationException("钱包数量、成本或版本超出安全范围。"); }
    }
}
