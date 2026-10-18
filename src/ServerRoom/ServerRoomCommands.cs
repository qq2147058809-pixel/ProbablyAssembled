using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using System.Text.Json;

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
                reference.Kind, capacity.Tier, capacity.EffectiveCompute, capacity.Watts, capacity.CanRun,
                capacity.Gpus.Select(gpu => new ServerRoomMiningModel.Card { Tier = gpu.Tier,
                    VramGb = checked((int)gpu.MemoryGB), Intact = gpu.Intact }).ToArray(),
                ServerRoomTransaction.Fingerprint(JsonSerializer.Serialize(info, WorkroomStorageState.JsonOptions))));
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

    internal static void Mode(string recordId, int mode, bool downshift)
    { var current = Require(); ServerRoomRuntime.Commit(ServerRoomEconomy.PrepareMode(current, recordId, mode, downshift), null); }

    internal static void Pool(string coinId, int pool)
    { var current = Require(); ServerRoomRuntime.Commit(ServerRoomEconomy.PreparePool(current, coinId, pool), null); }

    internal static void AutoSale(ServerRoomState.AutoSalePolicy policy)
    { var current = Require(); ServerRoomRuntime.Commit(ServerRoomEconomy.PreparePolicy(current, policy), null); }

    internal static void Maintain(string recordId)
    {
        var current = Require(); var fact = MachineFact(current, recordId);
        var change = ServerRoomEconomy.PrepareMaintenance(current, recordId);
        var storage = WorkroomStorage.State.Items.ToList();
        if (!WorkroomComponentParts.ConsumeMaterial(storage, "common_electronic", ServerRoomEconomy.MaintenanceMaterials(fact.Tier)))
            throw new InvalidOperationException("工作间和储物箱内的电子零件不足。");
        ServerRoomRuntime.Commit(change, storage);
    }

    internal static long MaximumTrade(string coinId, bool buy)
    {
        var current = Require(); ServerRoomDaily.RequireToday(current);
        var store = PlayerStore.Instance ?? throw new InvalidOperationException("玩家现金不可用。");
        return ServerRoomTradeMath.Maximum(current, ServerRoomMarket.CoinIndex(coinId), buy, store.GetCash());
    }

    internal static ServerRoomTradeMath.TradeQuote TradeQuote(string coinId, long microUnits, bool buy)
    {
        var current = Require();
        ServerRoomDaily.RequireToday(current);
        var index = ServerRoomState.CoinIds.ToList().IndexOf(coinId);
        if (index < 0) throw new InvalidOperationException("币种无效。");
        return ServerRoomTradeMath.Preview(current, index, microUnits, buy);
    }

    internal static void TradeConfirmed(string coinId, long microUnits, bool buy, int expectedDay, decimal expectedPrice, long expectedRevision, long expectedNet)
    {
        var current = Require();
        ServerRoomDaily.RequireToday(current);
        var index = ServerRoomMarket.CoinIndex(coinId);
        if (current.Market.QuoteDay != expectedDay || current.Market.Prices[index] != expectedPrice ||
            current.Revision != expectedRevision || current.Coins[index].DailyNetMicro != expectedNet)
            throw new InvalidOperationException("确认后行情已变化，请重新查看订单。");
        var store = PlayerStore.Instance ?? throw new InvalidOperationException("玩家现金不可用。");
        ServerRoomRuntime.Commit(ServerRoomTradeMath.Prepare(current, coinId, microUnits, buy,
            store.GetCash(), expectedDay), null);
    }
}
