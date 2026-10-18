using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;

namespace PCExpansion;

// Story publication and physical transfer share a save barrier. A native
// callback may throw after moving an item; only read-back ownership is proof.
internal static class WorkroomStoryTransfer
{
    private sealed class Ticket
    {
        internal IntPtr Owner;
        internal string Run = "";
        internal int Slot;
        internal bool Consume, Removed;
        internal WorkroomItemCodec.Snapshot[] Batch = Array.Empty<WorkroomItemCodec.Snapshot>();
        internal Action Committed = null!;
        internal List<WorkroomStorageState.Record>? ConsumptionNext;
        internal readonly List<WorkroomItemCodec.Candidate> Candidates = new();
    }
    private static Ticket? pending;
    private static bool executing;
    private static long retryAt;
    internal static bool Pending => pending != null || executing;
    internal static bool BlocksStorage => pending != null && !executing;
    internal static bool BeforeSave() { WakeRecovery(); return !Pending; }
    internal static bool AllowSessionChange() { WakeRecovery(); return !Pending; }

    private static string Text(string key) => LanguageText.Get("luoxi.transfer." + key);
    private static bool Same(Ticket t) => PlayerStore.IsInstanceExist() && PlayerStore.instance != null &&
        PlayerStore.instance.Pointer == t.Owner && PlayerStore.instance.runID == t.Run &&
        PlayerStore.instance.saveSlotId == t.Slot && WorkroomStorage.State.SameOwner();
    private static int Id(WorkroomItemCodec.Snapshot s) => s.Nodes[0].UniqueId;
    private static Ticket Create(IReadOnlyList<WorkroomItemCodec.Snapshot> batch, Action committed, bool consume)
    {
        var store = WorkroomGameAdapter.ExistingStore() ?? throw new InvalidOperationException("店铺不可用。");
        if (!WorkroomStorage.State.Ready || batch.Count is < 1 or > 6 || committed == null)
            throw new InvalidOperationException("剧情交接尚未就绪。");
        var identities = new HashSet<int>();
        foreach (var item in batch)
        {
            WorkroomItemCodec.Validate(item);
            if (item.Count != 1 || !item.Footprint.Cells.All(cell => cell != 0) ||
                WorkroomItemCodec.Identities(item).Any(id => !identities.Add(id)))
                throw new InvalidOperationException("剧情货单身份或形状无效。");
        }
        return new Ticket { Owner = store.Pointer, Run = store.runID, Slot = store.saveSlotId,
            Batch = batch.ToArray(), Committed = committed, Consume = consume };
    }

    private static bool Available => !Pending && WorkroomStorage.State.Ready && !WorkroomStorage.Busy &&
        WorkroomStorage.State.PendingRecords.Count == 0 && !WorkroomItemCodec.CleanupPending &&
        !NpcStockOffers.Pending && !ComputerSupplierNpcs.StockPending;

    internal static bool CanReduceUnissuedBatch(IReadOnlyList<WorkroomItemCodec.Snapshot> batch) =>
        Available && WorkroomStorage.State.SameOwner() &&
        !AllOwnedIdentities().Overlaps(batch.SelectMany(WorkroomItemCodec.Identities));

    internal static bool TryGrantStoryBatch(IReadOnlyList<WorkroomItemCodec.Snapshot> batch,
        Action committed, out string reason)
    {
        reason = Text("busy");
        if (!Available) return false;
        using var timing = new WorkroomFeedbackDiagnostics.Operation("LuoxiGrant");
        executing = true;
        Ticket? value = null;
        try
        {
            value = Create(batch, committed, false);
            Find(batch, out var missing);
            if (missing.Count == 0)
            {
                pending = value; Finish(value); reason = ""; return true;
            }
            // Never restore an ID which exists, even nested in another machine.
            var existingIds = AllOwnedIdentities();
            timing.Mark("validate-and-identities");
            if (batch.SelectMany(WorkroomItemCodec.Identities).Any(existingIds.Contains))
            { reason = Text("identity_conflict"); return false; }
            foreach (var item in batch) value.Candidates.Add(WorkroomItemCodec.Restore(item));
            timing.Mark("restore");
            if (batch.Count > 1)
            {
                var counter = EmporiumEntry.Instance?.frontInvinvElement;
                if (counter == null || !Plan(new[] { counter }, batch, out _, out _))
                { reason = Text("counter_full"); return false; }
            }
            var targets = WorkroomStorage.PlayerTargets().Where(g => !g.IsInsertLocked() &&
                GeneralHelper.MayPlayerInsertInto(g)).ToArray();
            if (!Plan(targets, batch, out var placements, out var origins))
            { reason = Text("inventory_full"); return false; }
            // Verify all native target constraints before the first accept.
            var markers = new SlotMarker[batch.Count];
            for (var i = 0; i < batch.Count; i++)
            {
                var p = placements[i]; var root = value.Candidates[i].Root;
                var target = targets[p.Board]; var shape = root.modifiedShape ?? root.shape;
                var proposed = new GridShapeBuilder(shape).SetOrientation((shape.orientation + (p.Rotated ? 1 : 0)) % 4)
                    .SetPosition(origins[p.Board].X + p.X, origins[p.Board].Y + p.Y).Build();
                if (!target.CheckInventorySlotStillValid(root, proposed, root.modifiedState) ||
                    !GeneralHelper.MayOwnershipAcceptItem(root, target))
                { reason = Text("inventory_full"); return false; }
                markers[i] = new SlotMarker { item = root, inventory = target,
                    itemGridShape = proposed, numTransfer = 1 };
            }
            pending = value;
            timing.Mark("plan");
            for (var i = 0; i < markers.Length; i++)
            {
                if (!Same(value)) throw new InvalidOperationException("剧情交接会话变化。");
                var root = value.Candidates[i].Root; var marker = markers[i];
                if (!marker.inventory.CheckInventorySlotStillValid(root, marker.itemGridShape, root.modifiedState))
                    throw new InvalidOperationException("整批交付期间接收位置变化。");
                try { marker.AcceptUnchecked(); }
                catch (Exception ex) { Core.Log?.Warning("剧情交付回调异常，回读实际归属：" + ex.Message); }
                if (!WorkroomStorage.Contains(marker.inventory, root) || !GeneralHelper.IsItemOwned(root))
                    throw new InvalidOperationException("剧情交付尚未确认。");
            }
            RecoverGrant(value);
            timing.Mark("accept-and-commit");
            reason = ""; return pending == null;
        }
        catch (Exception ex)
        {
            Core.Log?.Error("洛夕整批交付等待恢复：" + ex);
            reason = Text("busy"); return false;
        }
        finally
        {
            if (value != null && !ReferenceEquals(pending, value))
                foreach (var c in value.Candidates)
                    if (!c.Cleaned && c.Root.parentInventory == null)
                        try { WorkroomItemCodec.Destroy(c); } catch (Exception ex) { Core.Log?.Warning(ex.Message); }
            executing = false;
            WakeRecovery();
        }
    }

    internal static bool TryConsumeStoryBatch(IReadOnlyList<WorkroomItemCodec.Snapshot> batch,
        Action committed, out IReadOnlyList<string> missing)
    {
        missing = new[] { Text("busy") };
        if (!Available) return false;
        using var timing = new WorkroomFeedbackDiagnostics.Operation("LuoxiConsume");
        executing = true;
        try
        {
            var value = Create(batch, committed, true);
            var sources = Find(batch, out missing);
            timing.Mark("validate-originals");
            if (missing.Count != 0) return false;
            // The full batch was checked before any move. Native sources first enter
            // the established custody protocol; a failure keeps them owned.
            if (!WorkroomStorage.StoreStoryBatch(sources.Where(s => s.Native != null).Select(s => s.Native!).ToArray()))
            { missing = new[] { Text("busy") }; return false; }
            timing.Mark("custody");
            sources = Find(batch, out missing);
            if (missing.Count != 0 || sources.Any(s => s.Native != null)) return false;
            pending = value;
            RecoverConsume(value);
            timing.Mark("consume-and-commit");
            missing = Array.Empty<string>(); return pending == null;
        }
        catch (Exception ex)
        { Core.Log?.Error("洛夕整批归还等待恢复：" + ex); missing = new[] { Text("busy") }; return false; }
        finally { executing = false; WakeRecovery(); }
    }

    private sealed class Source
    {
        internal GameItem? Native;
        internal WorkroomStorageState.Record? Record;
        internal WorkroomItemCodec.Snapshot Item = null!;
        internal bool Original = true;
    }
    private static List<Source> Find(IReadOnlyList<WorkroomItemCodec.Snapshot> batch,
        out IReadOnlyList<string> missing)
    {
        var requested = batch.ToDictionary(Id);
        var all = new List<Source>(); var seen = new HashSet<IntPtr>();
        foreach (var grid in WorkroomStorage.StorySources())
        foreach (var item in grid.childItems)
            if (item != null && requested.TryGetValue(item.uniqueId, out var expected) && seen.Add(item.Pointer) &&
                WorkroomStorage.Contains(grid, item) && GeneralHelper.IsItemOwned(item))
                all.Add(new Source { Native = item, Item = expected, Original = WorkroomStoryIdentity.Matches(expected, item) });
        foreach (var record in WorkroomStorage.State.Items)
            if (record.Place is "box" or "room" && requested.ContainsKey(Id(record.Item)))
                all.Add(new Source { Record = record, Item = record.Item });
        var selected = new List<Source>(); var gaps = new List<string>();
        foreach (var expected in batch)
        {
            var hits = all.Where(s => Id(s.Item) == Id(expected)).ToArray();
            if (hits.Length != 1 || !hits[0].Original ||
                hits[0].Native == null && !LuoxiStoryGoods.SameOriginal(expected, hits[0].Item)) gaps.Add(expected.Name);
            else selected.Add(hits[0]);
        }
        missing = gaps; return selected;
    }

    private static HashSet<int> AllOwnedIdentities()
    {
        var found = WorkroomStoryIdentity.Owned();
        foreach (var item in WorkroomStorage.State.Items) found.UnionWith(WorkroomItemCodec.Identities(item.Item));
        return found;
    }

    private static bool Plan(IReadOnlyList<GameGridInventory> grids,
        IReadOnlyList<WorkroomItemCodec.Snapshot> batch,
        out WorkroomStoryPacking.Placement[] placements, out (int X, int Y)[] origins)
    {
        var boards = new List<WorkroomStoryPacking.Board>(); origins = new (int, int)[grids.Count];
        for (var i = 0; i < grids.Count; i++)
        {
            var grid = grids[i]; var shape = grid.inventoryShape;
            var w = shape.globalWidth; var h = shape.globalHeight;
            if (w <= 0 || h <= 0 || (long)w * h > 65536) throw new InvalidOperationException("柜台形状不可读取。");
            var free = new bool[w * h]; origins[i] = (shape.minX, shape.minY);
            // Native inventory masks are collision masks: ctor fills the
            // interior with 0 and sets outside to 1. Item masks use the inverse
            // meaning (nonzero is solid), so do not interpret both alike.
            for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
                free[y * w + x] = shape.Get(x + shape.minX, y + shape.minY) == 0;
            foreach (var item in grid.childItems)
            {
                if (item == null || !WorkroomStorage.Contains(grid, item)) continue;
                var occupied = item.modifiedShape ?? item.shape;
                for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
                {
                    var gx = x + shape.minX; var gy = y + shape.minY;
                    if (gx >= occupied.minX && gy >= occupied.minY &&
                        (long)gx < (long)occupied.minX + occupied.globalWidth &&
                        (long)gy < (long)occupied.minY + occupied.globalHeight && occupied.Get(gx, gy) != 0)
                        free[y * w + x] = false;
                }
            }
            boards.Add(new WorkroomStoryPacking.Board { Width = w, Height = h, Free = free });
        }
        return WorkroomStoryPacking.TryPlan(boards, batch.Select(s =>
            new WorkroomStoryPacking.Part(s.Footprint.Width, s.Footprint.Height)).ToArray(), out placements);
    }

    private static void RecoverConsume(Ticket value)
    {
        if (!Same(value)) throw new InvalidOperationException("归还恢复会话不一致。");
        if (!value.Removed)
        {
            if (value.ConsumptionNext == null)
            {
                var sources = Find(value.Batch, out var missing);
                if (missing.Count != 0 || sources.Any(s => s.Record == null))
                    throw new InvalidOperationException("归还保管凭据不完整。");
                var ids = sources.Select(s => s.Record!.Id).ToHashSet();
                value.ConsumptionNext = WorkroomStorage.State.Items.Where(s => !ids.Contains(s.Id)).ToList();
            }
            // Keep the exact candidate even if the native dictionary setter
            // throws after applying it; retrying publishes the same removal.
            WorkroomStorage.State.Replace(value.ConsumptionNext);
            value.Removed = true;
        }
        Finish(value);
    }

    private static void RecoverGrant(Ticket value)
    {
        if (!Same(value)) throw new InvalidOperationException("交付恢复会话不一致。");
        // If another native callback changed available space mid-batch, retain
        // each undelivered candidate in the player's storage, then finish the
        // same grant. Never recreate an already delivered physical identity.
        for (var index = 0; index < value.Candidates.Count; index++)
        {
            var candidate = value.Candidates[index];
            var expected = value.Batch[index];
            var root = candidate.Root;
            var recordId = "luoxi.delivery." + Id(expected).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (candidate.Cleaned)
            {
                if (WorkroomStorage.State.Find(recordId)?.Place == "cleanup") WorkroomStorage.State.Move(recordId, "box");
                continue;
            }
            if (root.parentInventory != null)
            {
                if (!WorkroomStorage.Sources().Any(g => WorkroomStorage.Contains(g, root)) || !GeneralHelper.IsItemOwned(root))
                    throw new InvalidOperationException("交付物已进入未知库存，禁止重建。");
                continue;
            }
            var record = WorkroomStorage.State.Find(recordId);
            if (record == null)
            {
                if (AllOwnedIdentities().Overlaps(WorkroomItemCodec.Identities(expected)))
                    throw new InvalidOperationException("交付恢复身份已在其他保管者中。");
                WorkroomStorage.State.Add(new WorkroomStorageState.Record { Id = recordId, Place = "cleanup", Item = expected });
            }
            WorkroomItemCodec.Destroy(candidate);
            WorkroomStorage.State.Move(recordId, "box");
        }
        Find(value.Batch, out var missing);
        if (missing.Count != 0) throw new InvalidOperationException("整批交付尚未完整归玩家。");
        Finish(value);
    }

    private static void Finish(Ticket value)
    {
        if (!Same(value)) throw new InvalidOperationException("剧情结算会话变化。");
        value.Committed();
        if (!ReferenceEquals(value, pending)) throw new InvalidOperationException("剧情交接被重入改变。");
        pending = null;
        BusinessRuntime.Wake();
    }
    internal static void RetryRecovery()
    {
        if (pending == null || executing || Environment.TickCount64 < retryAt) return;
        retryAt = Environment.TickCount64 + 500;
        executing = true;
        try { if (pending.Consume) RecoverConsume(pending); else RecoverGrant(pending); }
        catch (Exception ex) { Core.Debug("洛夕交接恢复等待：" + ex.Message); }
        finally { executing = false; }
    }

    internal static void WakeRecovery()
    {
        if (pending == null || executing) return;
        BusinessWork.Schedule("story-transfer", () =>
        {
            RetryRecovery();
            return pending != null;
        }, intervalMs: 500);
    }
    internal static void ResetSession()
    {
        if (Pending) throw new InvalidOperationException("洛夕交接尚未结束，不能切换存档。");
        retryAt = 0;
    }
}
