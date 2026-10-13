using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;

namespace PCExpansion;

/// <summary>经营命令只从机架实物取配置，并通过统一事务发布现金及模块状态。</summary>
internal static partial class ServerRoomOperations
{
    internal static IReadOnlyList<ServerRoomEconomy.MachineFact> MachineFacts(ServerRoomState.Document current)
    {
        var facts = new List<ServerRoomEconomy.MachineFact>(current.Racks.Count);
        foreach (var reference in current.Racks)
        {
            var record = WorkroomStorage.State.FindRack(reference.RackIndex);
            if (record == null || record.Id != reference.RecordId || record.Item.Nodes.Count == 0 ||
                record.Item.Nodes[0].UniqueId != reference.UniqueId ||
                !ServerRoomMachine.TryDefinition(record.Item.Identifier, out var definition) ||
                definition.Kind != reference.Kind)
                throw new InvalidOperationException("机位物品身份已变化，停止经营操作。");
            var info = record.Item.Nodes[0].Machine ?? new WorkroomMachineAssembly.MachineInfo
                { Version = 3, Kind = definition.Kind, Body = record.Item };
            var capacity = ServerRoomCapacity.Read(info);
            facts.Add(new ServerRoomEconomy.MachineFact(reference.RecordId, reference.UniqueId,
                reference.Kind, capacity.EffectiveCompute, capacity.DailyFee, capacity.CanRun));
        }
        return facts;
    }

    internal static ServerRoomEconomy.MachineFact MachineFact(ServerRoomState.Document current, string recordId) =>
        MachineFacts(current).Single(fact => fact.RecordId == recordId);

    internal static void Power(string recordId, bool on)
    {
        var current = Require();
        var fact = MachineFact(current, recordId);
        if (on && (fact.Kind != ServerRoomMachine.Mining || !fact.CanRun))
            throw new InvalidOperationException("矿机配件不完整或损坏，不能开机。");
        ServerRoomRuntime.Commit(ServerRoomEconomy.PreparePower(current, recordId, on), null);
    }

    internal static void MineCoin(string recordId, string coinId)
    {
        var current = Require();
        ServerRoomRuntime.Commit(ServerRoomEconomy.PrepareCoin(current, recordId, coinId), null);
    }

    internal static void MineAll(string coinId)
    {
        var current = Require();
        ServerRoomRuntime.Commit(ServerRoomEconomy.PrepareAllCoins(current, coinId), null);
    }

    internal static void Priority(string recordId, int priority)
    {
        var current = Require();
        ServerRoomRuntime.Commit(ServerRoomEconomy.PreparePriority(current, recordId, priority), null);
    }

    internal static ServerRoomEconomy.LeaseQuote LeaseQuote(string recordId, int days)
    {
        var current = Require();
        return ServerRoomEconomy.QuoteLease(current, MachineFact(current, recordId), days, StoreStation.GetDayCounter());
    }

    internal static void Lease(string recordId, int days)
    {
        var current = Require();
        var fact = MachineFact(current, recordId);
        ServerRoomRuntime.Commit(ServerRoomEconomy.PrepareLease(current, fact, days,
            StoreStation.GetDayCounter(), Guid.NewGuid().ToString("N")), null);
    }

    internal static ServerRoomTradeMath.TradeQuote TradeQuote(string coinId, long units10, bool buy)
    {
        var current = Require();
        ServerRoomDaily.RequireToday(current);
        var index = ServerRoomState.CoinIds.ToList().IndexOf(coinId);
        if (index < 0) throw new InvalidOperationException("币种无效。");
        return ServerRoomTradeMath.Preview(current.Market.Prices[index], units10, buy);
    }

    internal static void TradeConfirmed(string coinId, long units10, bool buy, int expectedDay, decimal expectedPrice)
    {
        var current = Require();
        ServerRoomDaily.RequireToday(current);
        var index = ServerRoomMarket.CoinIndex(coinId);
        if (current.Market.QuoteDay != expectedDay || current.Market.Prices[index] != expectedPrice)
            throw new InvalidOperationException("确认后行情已变化，请重新查看订单。");
        var store = PlayerStore.Instance ?? throw new InvalidOperationException("玩家现金不可用。");
        ServerRoomRuntime.Commit(ServerRoomTradeMath.Prepare(current, coinId, units10, buy,
            store.GetCash(), expectedDay), null);
    }
}
