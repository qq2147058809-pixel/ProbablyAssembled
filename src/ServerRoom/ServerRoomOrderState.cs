using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PCExpansion;

/// <summary>预订只保存采购意图；不持有实物、不扣款，不属于资产 Pending。</summary>
internal static class ServerRoomOrderState
{
    internal const string SaveKey = "pcexpansion.server-room.orders.v2";
    internal const int Version = 2, Limit = 5, OriginalLimit = 3;
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 16 };

    internal sealed class Document
    {
        public Document() { }
        public int Version { get; set; } = ServerRoomOrderState.Version;
        public string Run { get; set; } = "";
        public int Slot { get; set; }
        public long Revision { get; set; }
        public string LastVisitId { get; set; } = "";
        public int LastVisitDay { get; set; } = -1;
        public bool LastVisitArrived { get; set; }
        public List<string> DispatchedVisits { get; set; } = new();
        public List<Order> Orders { get; set; } = new();
        // Tombstones survive removal from Orders and prevent late native purchase replay.
        public List<Purchase> Purchased { get; set; } = new();
        public int CatalogDay { get; set; } = -1;
        public List<ServerRoomOrderCatalog.Model> Catalog { get; set; } = new();
        internal ServerRoomState.Binding? RuntimeBinding { get; set; }
    }

    internal sealed class Order
    {
        public Order() { }
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "";
        public int Tier { get; set; }
        public string VisitId { get; set; } = "";
        public int DueDay { get; set; }
        public bool Placed { get; set; }
        public int OrderDay { get; set; } = -1;
    }

    internal sealed class Purchase
    {
        public Purchase() { }
        public string OrderId { get; set; } = "";
        public string VisitId { get; set; } = "";
        public string Kind { get; set; } = "";
        public int Tier { get; set; }
        public int OrderDay { get; set; } = -1;
    }

    internal sealed class Change
    {
        internal Document Next { get; }
        internal bool Changed { get; }
        internal Change(Document next, bool changed = true) { Next = next; Changed = changed; }
    }

    internal static Document New(string run, int slot)
    {
        var value = new Document { Run = run, Slot = slot };
        Validate(value);
        return value;
    }

    internal static Document? Load(bool present, string? raw, string run, int slot)
    {
        if (!Token(run) || slot < 0) throw new InvalidOperationException("预订存档会话无效。");
        if (!present) return null;
        if (string.IsNullOrEmpty(raw)) throw new InvalidOperationException("预订记录为空，保留原文。");
        return Parse(raw!, run, slot);
    }

    internal static Document Parse(string raw, string run, int slot)
    {
        using var parsed = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 16 });
        var root = parsed.RootElement;
        // Two explicit current-v2 layouts: the existing machine-only envelope,
        // or the complete extended envelope. Partial extensions are rejected.
        var extended = root.TryGetProperty("CatalogDay", out _);
        var fields = new[] { "Version", "Run", "Slot", "Revision", "LastVisitId", "LastVisitDay", "LastVisitArrived", "DispatchedVisits", "Orders", "Purchased" };
        CheckObject(root, extended ? fields.Concat(new[] { "CatalogDay", "Catalog" }).ToArray() : fields);
        foreach (var visit in Array(root, "DispatchedVisits"))
            if (visit.ValueKind != JsonValueKind.String) throw new InvalidOperationException("预订来访身份不是字符串。");
        foreach (var order in Array(root, "Orders")) CheckObject(order, extended ? new[] { "Id", "Kind", "Tier", "VisitId", "DueDay", "Placed", "OrderDay" } : new[] { "Id", "Kind", "Tier", "VisitId", "DueDay", "Placed" });
        foreach (var purchase in Array(root, "Purchased")) CheckObject(purchase, extended ? new[] { "OrderId", "VisitId", "Kind", "Tier", "OrderDay" } : new[] { "OrderId", "VisitId" });
        if (extended) foreach (var model in Array(root, "Catalog")) CheckObject(model, "Kind", "Tier");
        var value = JsonSerializer.Deserialize<Document>(raw, JsonOptions)
            ?? throw new InvalidOperationException("预订记录不是对象。");
        Validate(value);
        if (!extended && (value.Orders.Count > OriginalLimit || value.Orders.Any(order => ServerRoomOrderCatalog.IsComponent(order.Kind)) ||
            value.Purchased.GroupBy(purchase => purchase.VisitId).Any(group => group.Count() > OriginalLimit) ||
            value.Purchased.Count(purchase => purchase.VisitId == value.LastVisitId) + value.Orders.Count(order => order.VisitId != "") > OriginalLimit))
            throw new InvalidOperationException("原机箱预订封套包含扩展订单。");
        if (value.Run != run || value.Slot != slot)
            throw new InvalidOperationException("预订记录不属于当前 run 或档槽。");
        return value;
    }

    internal static string Serialize(Document value)
    { Validate(value); return JsonSerializer.Serialize(value, JsonOptions); }

    internal static Document Clone(Document value)
    {
        var result = Parse(Serialize(value), value.Run, value.Slot);
        result.RuntimeBinding = value.RuntimeBinding;
        return result;
    }

    internal static Change PrepareRefreshCatalog(Document source, int today, IReadOnlyList<ServerRoomOrderCatalog.Model> selection)
    {
        Validate(source); RequireDay(today);
        if (today < source.CatalogDay || today < source.LastVisitDay) throw new InvalidOperationException("配件目录日期倒退。");
        if (today == source.CatalogDay) return new Change(Clone(source), false);
        if (selection == null) throw new InvalidOperationException("每日配件目录缺失。");
        var next = Advance(source); next.CatalogDay = today;
        next.Catalog = selection.Select(model => new ServerRoomOrderCatalog.Model { Kind = model.Kind, Tier = model.Tier }).ToList();
        return Checked(next);
    }

    internal static bool Available(Document source, string kind, int tier, int today, string exceptOrder = "") =>
        source.CatalogDay == today && source.Catalog.Any(model => model.Kind == kind && model.Tier == tier) &&
        !source.Orders.Any(order => order.Id != exceptOrder && order.Kind == kind && order.Tier == tier && order.OrderDay == today) &&
        !source.Purchased.Any(purchase => purchase.Kind == kind && purchase.Tier == tier && purchase.OrderDay == today);

    internal static Change PrepareAdd(Document source, string orderId, string kind, int tier, int today, int limit = OriginalLimit)
    {
        Validate(source); Model(kind, tier);
        RequireDay(today);
        if (today < source.LastVisitDay || today < source.CatalogDay) throw new InvalidOperationException("预订日期早于最新来访或配件目录。");
        if (!Id(orderId) || source.Orders.Any(order => order.Id == orderId) ||
            source.Purchased.Any(purchase => purchase.OrderId == orderId))
            throw new InvalidOperationException("预订身份无效或重复。");
        if (limit != OriginalLimit && limit != Limit || source.Orders.Count >= limit) throw new InvalidOperationException("未购买订单已达共享上限。");
        if (ServerRoomOrderCatalog.IsComponent(kind) && !Available(source, kind, tier, today)) throw new InvalidOperationException("该配件今日不可预订。");
        var next = Advance(source);
        next.Orders.Add(new Order { Id = orderId, Kind = kind, Tier = tier, DueDay = checked(today + 1), OrderDay = ServerRoomOrderCatalog.IsComponent(kind) ? today : -1 });
        return Checked(next);
    }

    internal static Change PrepareReplace(Document source, string orderId, string kind, int tier, int today = -1)
    {
        Validate(source); Model(kind, tier);
        var order = Find(source, orderId);
        Editable(order);
        if (today >= 0 && (today < source.LastVisitDay || today < source.CatalogDay)) throw new InvalidOperationException("修改订单日期倒退。");
        if (order.Kind == kind && order.Tier == tier) return new Change(Clone(source), false);
        if (ServerRoomOrderCatalog.IsComponent(kind) && !Available(source, kind, tier, today, orderId)) throw new InvalidOperationException("该配件今日不可预订。");
        var next = Advance(source); var replacement = Find(next, orderId);
        replacement.Kind = kind; replacement.Tier = tier;
        replacement.OrderDay = ServerRoomOrderCatalog.IsComponent(kind) ? today : -1;
        if (replacement.OrderDay >= 0) replacement.DueDay = checked(today + 1);
        return Checked(next);
    }

    internal static Change PrepareCancel(Document source, string orderId)
    {
        Validate(source); Editable(Find(source, orderId));
        var next = Advance(source); next.Orders.RemoveAll(order => order.Id == orderId);
        return Checked(next);
    }

    // Caller proves enqueue success with the exact persisted visitor snapshot.
    internal static Change PrepareDispatch(Document source, string visitId, int visitDay, IReadOnlyCollection<string> orderIds)
    {
        Validate(source); RequireVisit(visitId); RequireDay(visitDay);
        var intended = Snapshot(orderIds);
        if (source.LastVisitId == visitId)
        {
            if (source.LastVisitDay != visitDay) throw new InvalidOperationException("重试来访日期不一致。");
            if (source.Orders.Any(order => order.VisitId == visitId))
            {
                var frozen = source.Orders.Where(order => order.VisitId == visitId).Select(order => order.Id)
                    .Concat(source.Purchased.Where(purchase => purchase.VisitId == visitId).Select(purchase => purchase.OrderId));
                if (!intended.SetEquals(frozen)) throw new InvalidOperationException("重试来访订单快照不一致。");
            }
            return new Change(Clone(source), false);
        }
        if (source.DispatchedVisits.Contains(visitId, StringComparer.Ordinal))
            throw new InvalidOperationException("不能重放此前来访的订货批次。");
        if (visitDay <= source.LastVisitDay || source.Orders.Any(order => order.VisitId != ""))
            throw new InvalidOperationException("当天已派送或上一批来访尚未结束。");
        var due = source.Orders.Where(order => order.DueDay <= visitDay && order.VisitId == "")
            .Select(order => order.Id).ToHashSet(StringComparer.Ordinal);
        if (intended.Count == 0 || !intended.SetEquals(due))
            throw new InvalidOperationException("订货批次须恰好包含全部到期未锁定订单。");
        var next = Advance(source);
        next.LastVisitId = visitId; next.LastVisitDay = visitDay;
        next.LastVisitArrived = false; next.DispatchedVisits.Add(visitId);
        foreach (var order in next.Orders.Where(order => intended.Contains(order.Id))) order.VisitId = visitId;
        return Checked(next);
    }

    internal static Change PrepareMarkArrived(Document source, string visitId, IReadOnlyCollection<string> orderIds)
    {
        Validate(source); RequireVisit(visitId);
        var intended = Snapshot(orderIds);
        if (source.LastVisitId != visitId) throw new InvalidOperationException("到店来访与预订快照不一致。");
        var expected = source.Orders.Where(order => order.VisitId == visitId).Select(order => order.Id)
            .Concat(source.Purchased.Where(purchase => purchase.VisitId == visitId).Select(purchase => purchase.OrderId))
            .ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(intended)) throw new InvalidOperationException("到店时冻结订单与原访客请求不一致。");
        if (source.LastVisitArrived) return new Change(Clone(source), false);
        if (!source.Orders.Any(order => order.VisitId == visitId))
            throw new InvalidOperationException("该来访的订单已结束。");
        var next = Advance(source); next.LastVisitArrived = true;
        return Checked(next);
    }

    internal static Change PrepareMarkPlaced(Document source, string orderId, string visitId)
    {
        Validate(source); RequireVisit(visitId);
        if (!Id(orderId)) throw new InvalidOperationException("预订身份无效。");
        var receipt = source.Purchased.SingleOrDefault(purchase => purchase.OrderId == orderId);
        if (receipt != null)
        {
            if (receipt.VisitId != visitId) throw new InvalidOperationException("上柜回调来访身份与成交凭据不一致。");
            return new Change(Clone(source), false);
        }
        var order = Find(source, orderId);
        if (order.VisitId != visitId || !source.LastVisitArrived)
            throw new InvalidOperationException("上柜订单未绑定当前已到店来访。");
        if (order.Placed) return new Change(Clone(source), false);
        var next = Advance(source); Find(next, orderId).Placed = true;
        return Checked(next);
    }

    // The caller proves departure or definitive disappearance. Unseen visitors
    // cannot cancel orders; items without a counter place are retried tomorrow.
    internal static Change PrepareFinishVisit(Document source, string visitId, int currentDay)
    {
        Validate(source); RequireVisit(visitId); RequireDay(currentDay);
        if (!source.DispatchedVisits.Contains(visitId, StringComparer.Ordinal))
            throw new InvalidOperationException("不能结束未登记的来访。");
        if (source.LastVisitId != visitId) return new Change(Clone(source), false);
        if (currentDay < source.LastVisitDay) throw new InvalidOperationException("离店日期早于派送日期。");
        if (!source.Orders.Any(order => order.VisitId == visitId)) return new Change(Clone(source), false);
        var next = Advance(source);
        if (source.LastVisitArrived) next.Orders.RemoveAll(order => order.VisitId == visitId && order.Placed);
        foreach (var order in next.Orders.Where(order => order.VisitId == visitId))
        {
            order.VisitId = ""; order.Placed = false;
            order.DueDay = Math.Max(order.DueDay, checked(currentDay + 1));
        }
        return Checked(next);
    }

    // This records external native ownership/payment proof; it never creates an item.
    internal static Change PrepareCompletePurchased(Document source, string orderId, string visitId)
    {
        Validate(source); RequireVisit(visitId);
        if (!Id(orderId)) throw new InvalidOperationException("预订身份无效。");
        var done = source.Purchased.SingleOrDefault(purchase => purchase.OrderId == orderId);
        if (done != null)
        {
            if (done.VisitId != visitId) throw new InvalidOperationException("成交回调来访身份与凭据不一致。");
            return new Change(Clone(source), false);
        }
        var order = Find(source, orderId);
        if (order.VisitId != visitId || !order.Placed || !source.LastVisitArrived)
            throw new InvalidOperationException("该订单未在成交回调的来访中上柜。");
        var next = Advance(source);
        next.Orders.RemoveAll(entry => entry.Id == orderId);
        next.Purchased.Add(new Purchase { OrderId = orderId, VisitId = visitId, Kind = order.Kind, Tier = order.Tier, OrderDay = order.OrderDay });
        return Checked(next);
    }

    internal static void Validate(Document value)
    {
        if (value == null || value.Version != Version || !Token(value.Run) || value.Slot < 0 || value.Revision < 0 ||
            value.LastVisitId == null || value.DispatchedVisits == null || value.Orders == null || value.Purchased == null ||
            value.Orders.Count > Limit || value.LastVisitDay < -1 || value.Catalog == null || value.CatalogDay < -1)
            throw new InvalidOperationException("预订封套字段无效。");
        var visits = new HashSet<string>(StringComparer.Ordinal);
        foreach (var visit in value.DispatchedVisits)
            if (!Token(visit) || !visits.Add(visit)) throw new InvalidOperationException("预订来访身份无效或重复。");
        if (value.DispatchedVisits.Count == 0 ? value.LastVisitId != "" || value.LastVisitArrived || value.LastVisitDay != -1 : value.LastVisitId != value.DispatchedVisits[^1] || value.LastVisitDay < 1)
            throw new InvalidOperationException("最新订货来访与历史不一致。");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var dailyModels = new HashSet<string>(StringComparer.Ordinal);
        if (value.CatalogDay == -1 ? value.Catalog.Count != 0 : value.Catalog.Count != ServerRoomOrderCatalog.DailyCount)
            throw new InvalidOperationException("每日配件目录数量无效。");
        foreach (var model in value.Catalog)
            if (model == null || !ServerRoomOrderCatalog.IsComponent(model.Kind) || model.Tier is < 1 or > 5 || !dailyModels.Add(model.Key))
                throw new InvalidOperationException("每日配件目录型号无效或重复。");
        var reservations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var order in value.Orders)
        {
            if (order == null || !Id(order.Id) || !identities.Add(order.Id) || order.VisitId == null ||
                order.DueDay < 1 || order.VisitId != "" && (order.VisitId != value.LastVisitId || order.DueDay > value.LastVisitDay) ||
                order.Placed && (order.VisitId == "" || !value.LastVisitArrived))
                throw new InvalidOperationException("预订身份或冻结批次无效。");
            Model(order.Kind, order.Tier);
            Reservation(order.Kind, order.Tier, order.OrderDay, order.DueDay, value, reservations);
        }
        var purchasedPerVisit = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var purchase in value.Purchased)
        {
            if (purchase == null || !Id(purchase.OrderId) || !identities.Add(purchase.OrderId) || !visits.Contains(purchase.VisitId))
                throw new InvalidOperationException("预订成交凭据无效或与未购买订单重复。");
            purchasedPerVisit.TryGetValue(purchase.VisitId, out var count);
            if (count >= Limit) throw new InvalidOperationException("同一来访的预订成交数量超出五笔。");
            if (purchase.Kind == "")
            { if (purchase.Tier != 0 || purchase.OrderDay != -1) throw new InvalidOperationException("既有成交凭据含不完整型号。"); }
            else
            {
                Model(purchase.Kind, purchase.Tier);
                Reservation(purchase.Kind, purchase.Tier, purchase.OrderDay, int.MaxValue, value, reservations);
            }
            purchasedPerVisit[purchase.VisitId] = count + 1;
        }
        purchasedPerVisit.TryGetValue(value.LastVisitId, out var latestPurchased);
        if (latestPurchased > 0 && !value.LastVisitArrived)
            throw new InvalidOperationException("最新来访成交缺少到店凭据。");
        if (latestPurchased + value.Orders.Count(order => order.VisitId != "") > Limit)
            throw new InvalidOperationException("同一来访的冻结订单与成交数量超出五笔。");
    }

    private static Document Advance(Document source)
    { var next = Clone(source); next.Revision = checked(source.Revision + 1); return next; }
    private static Change Checked(Document value) { Validate(value); return new Change(value); }
    private static Order Find(Document source, string id) => source.Orders.SingleOrDefault(order => order.Id == id)
        ?? throw new InvalidOperationException("预订已不存在或身份已变化。");
    private static void Editable(Order order)
    { if (order.VisitId != "") throw new InvalidOperationException("洛夕已携带该订单来访，本访结束前不能修改或取消。"); }
    private static void Model(string kind, int tier)
    { _ = ServerRoomOrderCatalog.Identifier(kind, tier); }
    private static void Reservation(string kind, int tier, int day, int dueDay, Document value, HashSet<string> reservations)
    {
        if (!ServerRoomOrderCatalog.IsComponent(kind))
        { if (day != -1) throw new InvalidOperationException("机器订单不应占用每日配件库存。"); return; }
        if (day < 0 || day > value.CatalogDay || dueDay <= day || !reservations.Add(day + ":" + kind + ":" + tier) ||
            day == value.CatalogDay && !value.Catalog.Any(model => model.Kind == kind && model.Tier == tier))
            throw new InvalidOperationException("每日配件预订凭据无效或重复。");
    }
    private static void RequireVisit(string visit)
    { if (!Token(visit)) throw new InvalidOperationException("预订来访身份无效。"); }
    private static void RequireDay(int day)
    { if (day < 0) throw new InvalidOperationException("预订游戏日无效。"); }
    private static HashSet<string> Snapshot(IReadOnlyCollection<string> orderIds)
    {
        if (orderIds == null || orderIds.Count > Limit) throw new InvalidOperationException("订货快照最多五笔。");
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in orderIds)
            if (!Id(id) || !result.Add(id)) throw new InvalidOperationException("订货快照身份无效或重复。");
        if (result.Count != orderIds.Count) throw new InvalidOperationException("订货快照在读取时变化。");
        return result;
    }
    private static bool Id(string? value) => value != null && Guid.TryParseExact(value, "N", out var guid) &&
        guid != Guid.Empty && guid.ToString("N") == value;
    private static bool Token(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 &&
        value == value.Trim() && !value.Any(char.IsControl);
    private static JsonElement.ArrayEnumerator Array(JsonElement owner, string name)
    {
        var value = owner.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("预订列表字段无效：" + name);
        return value.EnumerateArray();
    }
    private static void CheckObject(JsonElement value, params string[] required)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("预订记录缺少对象。");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in value.EnumerateObject())
            if (!names.Add(field.Name) || !required.Contains(field.Name, StringComparer.Ordinal))
                throw new InvalidOperationException("预订字段重复或不支持：" + field.Name);
        if (names.Count != required.Length || required.Any(name => !names.Contains(name)))
            throw new InvalidOperationException("预订记录缺少必需字段。");
    }
}
