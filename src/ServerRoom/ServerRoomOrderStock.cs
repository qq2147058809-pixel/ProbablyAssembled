using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>预订与原生来客货单之间的桥；订单只在原生购买证实后核销。</summary>
internal static class ServerRoomOrderStock
{
    private const string DeliveryTag = "PCREPAIR_SERVER_ORDER_DELIVERY";
    private const string VisitPrefix = "PCREPAIR_SERVER_ORDER_VISIT_";
    private const string VisitDayPrefix = "PCREPAIR_SERVER_ORDER_DAY_";
    private const string ClientOrderPrefix = "PCREPAIR_SERVER_ORDER_REQUEST_";
    private const string ItemOrderPrefix = "PCREPAIR_SERVER_ORDER_ITEM_";
    private const string ItemVisitPrefix = "PCREPAIR_SERVER_ORDER_SOURCE_";
    private const string AttemptPrefix = "PCREPAIR_SERVER_ORDER_ATTEMPT_";
    private const string NoRoomPrefix = "PCREPAIR_SERVER_ORDER_NO_ROOM_";
    private const string StockTag = "PCREPAIR_SERVER_ORDER_STOCK";
    private static long retryAt, warnAt;
    private static readonly HashSet<string> unresolvedPurchases = new(StringComparer.Ordinal);

    internal static bool IsDelivery(StoreClient? client) =>
        PhoneAssemblerNpc.IsPhoneAssembler(client) && client!.IsTag(DeliveryTag);

    internal enum GreetingStatus { Offered, CounterFull, Failed }

    // The greeting is chosen from the native offer result, not the intended
    // order count. A recovered visit makes the same decision from saved tags.
    internal static GreetingStatus Greeting(PlayerStore store, StoreClient client)
    {
        if (!IsDelivery(client) || !store.isClientArrived || !ServerRoomOrders.Ready)
            return GreetingStatus.Failed;
        try { Stock(store, client); }
        catch (Exception ex) { Warn("洛夕送货开场前上柜未完成：", ex); }
        var current = ServerRoomOrders.Current;
        var token = Visit(client);
        if (current == null || current.LastVisitId != token || !current.LastVisitArrived) return GreetingStatus.Failed;
        var requests = Requests(client);
        if (requests.Count == 0) return GreetingStatus.Failed;
        var allNoRoom = !NpcStockOffers.Pending;
        foreach (var id in requests)
        {
            if (current.Purchased.Any(purchase => purchase.OrderId == id && purchase.VisitId == token))
                return GreetingStatus.Offered;
            var order = current.Orders.FirstOrDefault(entry => entry.Id == id && entry.VisitId == token);
            if (order == null) { allNoRoom = false; continue; }
            var offered = NpcStockOffers.Probe(item => item.IsTag(ItemOrderPrefix + id) &&
                item.IsTag(ItemVisitPrefix + token));
            if (order.Placed || offered == NpcStockOffers.StockState.Present) return GreetingStatus.Offered;
            if (offered != NpcStockOffers.StockState.Empty || !client.IsTag(NoRoomPrefix + id)) allNoRoom = false;
        }
        return allNoRoom ? GreetingStatus.CounterFull : GreetingStatus.Failed;
    }

    internal static bool CanEdit(ServerRoomOrderState.Order order)
    {
        if (order.VisitId.Length != 0) return false;
        var queue = PlayerStore.Instance?.storeClientManager?.clientStack;
        if (queue != null)
            foreach (var client in queue)
                if (IsDelivery(client) && Requests(client).Contains(order.Id)) return false;
        return true;
    }

    // The native visitor saves the exact due-order snapshot and delivery date.
    // A failed initial state write can therefore bind the same batch on arrival.
    internal static void PrepareVisitor(StoreClient client, int day, IReadOnlyList<string> orderIds)
    {
        if (day < 0 || orderIds.Count == 0 || orderIds.Count > ServerRoomOrderState.Limit)
            throw new InvalidOperationException("送货日期或订单数量无效。");
        var token = Guid.NewGuid().ToString("N");
        client.AddTag(DeliveryTag);
        client.AddTag(VisitPrefix + token);
        client.AddTag(VisitDayPrefix + day.ToString(CultureInfo.InvariantCulture));
        foreach (var id in orderIds) client.AddTag(ClientOrderPrefix + id);
        PhoneAssemblerNpc.ApplyDeliveryBudget(client);
        PhoneAssemblerNpc.PrepareDeliveryDialogue(client);
    }

    internal static void ConfirmQueued(StoreClient client)
    {
        if (!IsDelivery(client)) throw new InvalidOperationException("仅自动送货来访可绑定订单。");
        PhoneAssemblerNpc.ApplyDeliveryBudget(client);
        var token = Visit(client);
        if (token == "") throw new InvalidOperationException("洛夕来访缺少订货批次身份。");
        if (!ServerRoomOrders.Ready) throw new InvalidOperationException("机房预订记录尚未就绪。");
        ServerRoomOrders.Dispatch(token, VisitDay(client), Requests(client));
    }

    internal static void Stock(PlayerStore store, StoreClient client)
    {
        if (!IsDelivery(client) || !store.isClientArrived || !ServerRoomOrders.Ready ||
            ServerRoomRuntime.Current?.Enabled != true) return;
        var token = Visit(client);
        ConfirmQueued(client);
        ServerRoomOrders.MarkArrived(token, Requests(client));
        ReconcileOwned(store);
        var expected = Requests(client).ToHashSet(StringComparer.Ordinal);
        foreach (var order in ServerRoomOrders.List().Where(order => order.VisitId == token))
        {
            if (!expected.Contains(order.Id)) throw new InvalidOperationException("来访货单与机房订单快照不一致。");
            if (order.Placed) continue;
            var definition = ServerRoomMachine.DefinitionFor(order.Kind, order.Tier);
            var itemOrderTag = ItemOrderPrefix + order.Id;
            var itemVisitTag = ItemVisitPrefix + token;
            var offered = NpcStockOffers.Probe(item => item.IsTag(itemOrderTag) && item.IsTag(itemVisitTag));
            if (offered == NpcStockOffers.StockState.Present)
            { ServerRoomOrders.MarkPlaced(order.Id, token); continue; }
            if (offered == NpcStockOffers.StockState.Unknown)
                throw new InvalidOperationException("预订货物库存暂不可确认。");
            if (client.IsTag(AttemptPrefix + order.Id)) continue;
            // A full counter defers this order to tomorrow. Do not top up later
            // during the same visit when a purchase frees a slot.
            client.AddTag(AttemptPrefix + order.Id);
            bool? counterHadRoom = null;
            try
            {
                NpcStockOffers.Stock(store, client, StockTag, "DELIVERY_ORDER_" + order.Id,
                    () => new List<string> { definition.Identifier }, 1, 1,
                    id => id == definition.Identifier,
                    item =>
                    {
                        item.EnableTag(itemOrderTag, false);
                        item.EnableTag(itemVisitTag, false);
                        if (item.FindItemFeatureByCategory("retailMarkUp") == null)
                            item.AddItemFeature(ItemFeatureList.RetailMarkUp());
                    }, item => item.IsTag(itemOrderTag) && item.IsTag(itemVisitTag),
                    inspectBeforeSubmit: item => counterHadRoom = CounterHasRoom(store, item));
                if (NpcStockOffers.Probe(item => item.IsTag(itemOrderTag) && item.IsTag(itemVisitTag)) !=
                    NpcStockOffers.StockState.Present)
                    throw new InvalidOperationException("预订货物上柜结果尚未确认。");
                ServerRoomOrders.MarkPlaced(order.Id, token);
            }
            catch (Exception ex)
            {
                if (counterHadRoom == false && !NpcStockOffers.Pending &&
                    NpcStockOffers.Probe(item => item.IsTag(itemOrderTag) && item.IsTag(itemVisitTag)) ==
                        NpcStockOffers.StockState.Empty)
                    client.AddTag(NoRoomPrefix + order.Id);
                Warn("洛夕本次未能摆出预订货，留待次日：", ex);
            }
        }
    }

    private static bool? CounterHasRoom(PlayerStore store, GameItem item)
    {
        try
        {
            var counter = store.gridInv;
            if (counter == null || counter.IsInsertLocked()) return null;
            return counter.TryFindOneValidInventorySlot(item, true) != null;
        }
        catch (Exception ex) { Warn("洛夕送货柜台容量暂不可确认：", ex); return null; }
    }

    internal static void Update(PlayerStore store)
    {
        if (!ServerRoomOrders.Ready || Environment.TickCount64 < retryAt) return;
        retryAt = Environment.TickCount64 + 750;
        try
        {
            ReconcileOwned(store);
            ReleaseDeparted(store);
            var client = store.isClientArrived ? store.currentClientInstance?.storeClient : null;
            if (IsDelivery(client))
            {
                Stock(store, client!);
                PhoneAssemblerNpc.RefreshDisplayedDeliveryGreeting(client!);
            }
        }
        catch (Exception ex) { Warn("机房预订恢复或上柜未完成：", ex); }
    }

    internal static bool BeforeSave()
    {
        if (!ServerRoomOrders.BeforeSave()) return false;
        if (!ServerRoomOrders.Ready) return unresolvedPurchases.Count == 0;
        try
        {
            var store = PlayerStore.Instance;
            if (store != null) ReconcileOwned(store);
            return unresolvedPurchases.Count == 0 && ServerRoomOrders.BeforeSave();
        }
        catch (Exception ex) { Warn("机房预订成交待确认，暂缓保存：", ex); return false; }
    }

    internal static bool AllowSessionChange() => unresolvedPurchases.Count == 0 ?
        ServerRoomOrders.AllowSessionChange() : BeforeSave() && ServerRoomOrders.AllowSessionChange();

    internal static void ResetSession()
    {
        unresolvedPurchases.Clear(); retryAt = warnAt = 0;
        PhoneAssemblerNpc.ResetDisplayedDeliveryGreeting();
        ServerRoomDeliverySchedule.ResetSession();
    }

    private static void ReconcileOwned(PlayerStore store)
    {
        var receipts = ServerRoomOrders.Current?.Purchased;
        if (receipts != null)
            unresolvedPurchases.RemoveWhere(id => receipts.Any(purchase => purchase.OrderId == id));
        var orders = ServerRoomOrders.List();
        if (!orders.Any(order => order.VisitId != "")) return;
        var all = store.FindAllItem(true) ?? throw new InvalidOperationException("玩家物品暂不可读。");
        foreach (var order in orders)
        {
            if (order.VisitId == "") continue;
            GameItem? purchased = null;
            foreach (var item in all)
            {
                if (item == null) throw new InvalidOperationException("玩家物品列表含无效项。");
                if (!item.IsTag(ItemOrderPrefix + order.Id) || !GeneralHelper.IsItemOwned(item)) continue;
                if (purchased != null) throw new InvalidOperationException("同一预订出现多台已拥有机箱。");
                if (!item.IsTag(ItemVisitPrefix + order.VisitId) ||
                    Core.Clean(item.identifier) != ServerRoomMachine.DefinitionFor(order.Kind, order.Tier).Identifier)
                    throw new InvalidOperationException("已拥有预订机箱的型号或来访凭据不一致。");
                purchased = item;
            }
            if (purchased == null) continue;
            if (!order.Placed) ServerRoomOrders.MarkPlaced(order.Id, order.VisitId);
            ClearRetailOnOwned(purchased);
            ServerRoomOrders.CompletePurchased(order.Id, order.VisitId);
            unresolvedPurchases.Remove(order.Id);
        }
    }

    private static void ReleaseDeparted(PlayerStore store)
    {
        var current = ServerRoomOrders.Current;
        if (current == null || current.LastVisitId == "" ||
            !current.Orders.Any(order => order.VisitId == current.LastVisitId) ||
            VisitorLive(store, current.LastVisitId) || NpcStockOffers.Pending) return;
        var day = StoreStation.GetDayCounter();
        // The native queue briefly removes a client before it reaches the
        // counter. An unseen batch is only deemed lost on a later game day.
        if (!current.LastVisitArrived && day <= current.LastVisitDay) return;
        foreach (var order in current.Orders.Where(order => order.VisitId == current.LastVisitId))
        {
            var state = NpcStockOffers.Probe(item => item.IsTag(ItemOrderPrefix + order.Id) &&
                item.IsTag(ItemVisitPrefix + current.LastVisitId));
            if (state != NpcStockOffers.StockState.Empty) return;
        }
        ServerRoomOrders.FinishVisit(current.LastVisitId, day);
    }

    private static bool VisitorLive(PlayerStore store, string token)
    {
        if (store.isClientArrived && IsDelivery(store.currentClientInstance?.storeClient) &&
            Visit(store.currentClientInstance!.storeClient) == token) return true;
        var queue = store.storeClientManager?.clientStack;
        if (queue != null)
            for (var index = 0; index < queue.Count; index++)
                if (IsDelivery(queue[index]) && Visit(queue[index]) == token) return true;
        return false;
    }

    private static string Visit(StoreClient client) => SingleGuid(client, VisitPrefix);

    private static int VisitDay(StoreClient client)
    {
        var day = SingleValue(client, VisitDayPrefix);
        if (!int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < 0)
            throw new InvalidOperationException("洛夕送货日期无效。");
        return result;
    }

    private static IReadOnlyList<string> Requests(StoreClient client)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (client.tag == null) return Array.Empty<string>();
        foreach (var raw in client.tag)
        {
            var tag = Core.Clean(raw);
            if (!tag.StartsWith(ClientOrderPrefix, StringComparison.Ordinal)) continue;
            var id = tag.Substring(ClientOrderPrefix.Length);
            if (!ValidGuid(id) || !found.Add(id)) throw new InvalidOperationException("洛夕订货请求身份无效或重复。");
        }
        if (found.Count > ServerRoomOrderState.Limit) throw new InvalidOperationException("洛夕订货请求超过三台。");
        return found.OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }

    private static string SingleGuid(StoreClient client, string prefix)
    {
        var found = SingleValue(client, prefix);
        if (found.Length != 0 && !ValidGuid(found)) throw new InvalidOperationException("洛夕来访身份无效。");
        return found;
    }

    private static string SingleValue(StoreClient client, string prefix)
    {
        string? found = null;
        if (client.tag != null)
            foreach (var raw in client.tag)
            {
                var tag = Core.Clean(raw);
                if (!tag.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var id = tag.Substring(prefix.Length);
                if (found != null) throw new InvalidOperationException("洛夕来访字段重复。");
                found = id;
            }
        return found ?? "";
    }

    private static bool ValidGuid(string value) => Guid.TryParseExact(value, "N", out var parsed) &&
        parsed != Guid.Empty && parsed.ToString("N") == value;

    private static void ClearRetailOnOwned(GameItem item)
    {
        item.RemoveFeatureByCategory("retailMarkUp");
        item.RemoveItemFeatureByID("retailMarkUp");
        item.DisableTag(StockTag, false);
    }

    private static void Warn(string prefix, Exception ex)
    {
        if (Environment.TickCount64 < warnAt) return;
        warnAt = Environment.TickCount64 + 5000;
        Core.Log?.Warning(prefix + ex);
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.OnItemBought))]
    internal static class OrderPurchasePatch
    {
        private static void Postfix(PlayerStore __instance, GameItem gameItem)
        {
            try
            {
                if (gameItem == null || !ServerRoomOrders.Ready || !GeneralHelper.IsItemOwned(gameItem)) return;
                var order = ServerRoomOrders.List().FirstOrDefault(entry => gameItem.IsTag(ItemOrderPrefix + entry.Id));
                if (order == null) return;
                unresolvedPurchases.Add(order.Id);
                ReconcileOwned(__instance);
            }
            catch (Exception ex) { Warn("机房预订成交回调未确认：", ex); }
        }
    }
}
