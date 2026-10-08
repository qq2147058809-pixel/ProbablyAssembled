using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>库存交接单线程执行；失败记录和候选实体有唯一保管者。</summary>
internal static class WorkroomStorage
{
    internal static readonly WorkroomStorageState State = new();
    private static readonly Dictionary<string, WorkroomItemCodec.Candidate> cleanup = new();
    private static readonly Dictionary<string, WorkroomItemCodec.Candidate> delivery = new();
    private static bool busy, deferredSave;
    private static long lastSlowLog;
    internal static bool Busy => busy || WorkroomCashRepair.Pending;
    internal static string Text(string name, params object[] args) => LanguageText.Get("workroom.storage." + name, args);
    internal static string Message { get; private set; } = "";
    internal static long MessageUntil { get; private set; }

    internal static void Notify(string name)
    { Message = Text(name); MessageUntil = Environment.TickCount64 + 5000; }

    internal static void NotifyText(string text)
    { Message = text; MessageUntil = Environment.TickCount64 + 5000; }

    internal static void Update(PlayerStore store)
    {
        if (!State.SameOwner())
        {
            cleanup.Clear(); delivery.Clear();
            deferredSave = false;
            State.Bind(store);
            Run(ReconcileLoadedRecords);
        }
        if (!State.Ready || busy) return;
        WorkroomItemCodec.RetryCleanup();
        foreach (var pair in cleanup.ToArray())
            Run(() =>
            {
                WorkroomItemCodec.Destroy(pair.Value);
                State.Move(pair.Key, "box"); cleanup.Remove(pair.Key);
            });
        foreach (var pair in delivery.ToArray())
            Run(() =>
            {
                if (PlayerTargets().Any(target => IsPlaced(pair.Value.Root, target, State.Find(pair.Key)?.Item.Count ?? 0)))
                { State.Remove(pair.Key); delivery.Remove(pair.Key); }
                else if (pair.Value.Cleaned || pair.Value.Root.parentInventory == null)
                {
                    WorkroomItemCodec.Destroy(pair.Value);
                    State.Move(pair.Key, "box"); delivery.Remove(pair.Key);
                }
            });
        if (State.PendingRecords.Any(item => (item.Place == "cleanup" && !cleanup.ContainsKey(item.Id)) ||
            (item.Place == "delivery" && !delivery.ContainsKey(item.Id)))) Run(ReconcileLoadedRecords);
        if (deferredSave && cleanup.Count == 0 && delivery.Count == 0 && !WorkroomItemCodec.CleanupPending && !WorkroomCashRepair.Pending && !ComputerSupplierNpcs.StockPending && !NpcStockOffers.Pending &&
            State.PendingRecords.Count == 0)
        { deferredSave = false; store.SaveGame(); }
    }

    internal static IEnumerable<GameGridInventory> Sources()
    {
        var entry = EmporiumEntry.Instance;
        if (entry == null) yield break;
        var seen = new HashSet<IntPtr>();
        // invElement is the native main inventory, distinct from the four
        // counter/back/showcase grids. Its items must also be transferable.
        foreach (var grid in new[] { entry.invElement, entry.frontInvinvElement, entry.backInvinvElement,
            entry.backInvinvElementCounter, entry.showcaseElement })
            if (grid != null && seen.Add(grid.Pointer)) yield return grid;
    }

    internal static IEnumerable<GameGridInventory> PlayerTargets()
    {
        var entry = EmporiumEntry.Instance;
        if (entry == null) yield break;
        // Native TryAddToPlayerInv starts at back (player counter), then main.
        // Its third showcase fallback is deliberately outside this delivery.
        if (entry.backInvinvElement != null) yield return entry.backInvinvElement;
        if (entry.invElement != null && entry.invElement.Pointer != entry.backInvinvElement?.Pointer)
            yield return entry.invElement;
    }

    internal static SlotMarker? PlayerDeliverySlot(GameItem item)
    {
        foreach (var target in PlayerTargets())
        {
            if (target.IsInsertLocked() || !GeneralHelper.MayPlayerInsertInto(target)) continue;
            var marker = target.TryFindOneValidInventorySlot(item, true);
            if (marker != null && marker.inventory != null && marker.inventory.Pointer == target.Pointer &&
                marker.targetItem == null && marker.numTransfer == item.unitCount && marker.IsValid() &&
                GeneralHelper.MayOwnershipAcceptItem(item, target) &&
                target.CheckInventorySlotStillValid(item, marker.itemGridShape, item.modifiedState)) return marker;
        }
        return null;
    }

    internal static bool AcceptsIdentifier(string? identifier) => identifier != null &&
        (Components.Find(identifier) != null || WorkroomComponentTemplates.TryPart(identifier, out _, out _, out _) ||
        identifier is "pcrepair.sign_computer" or "pcrepair.contact_card_0504" or
            "pcrepair.computer_build_guide" or
            "common_electronic" or "wire" or "nuts_metal" or "printer_plastic" or
            "scrap_metal" or "metal_ingot" or "energy_credit");

    internal static bool Contains(GameInventory? inventory, GameItem item)
    {
        if (inventory == null || item.TryCast<GameItemElement>() is { } element && element.IsDestroyed() ||
            item.parentInventory == null || inventory.Pointer != item.parentInventory.Pointer) return false;
        var children = inventory.childItems;
        for (var i = 0; i < children.Count; i++) if (children[i] != null && children[i].Pointer == item.Pointer) return true;
        return false;
    }

    private static bool CheckStore(GameItem item)
    {
        var stage = "state";
        var sourcePointer = IntPtr.Zero;
        var count = 0;
        bool Refuse(string reason)
        {
            Notify(reason);
            // One boundary record per refused drop, never a per-frame probe.
            Core.Log?.Warning($"[储物拒收] reason={reason}; stage={stage}; item={item?.Pointer}; source={sourcePointer}; count={count}");
            return false;
        }
        try
        {
            if (!State.Ready) return Refuse(State.Error ?? "unavailable");
            if (busy || WorkroomItemCodec.CleanupPending) return Refuse("recovery");
            stage = "item";
            if (item == null) return Refuse("refused");
            count = item.unitCount;
            if (count <= 0) return Refuse("refused");
            if (count != 1) return Refuse("single_only");
            if (!AcceptsIdentifier(item.identifier)) return Refuse("unsupported_item");
            stage = "ownership";
            // Call the same native check used by GetAllOwnedItems. Do not
            // convert a native exception into a misleading "not owned" result.
            if (!GeneralHelper.IsItemOwned(item)) return Refuse("not_owned");
            stage = "source";
            var source = item.parentInventory;
            sourcePointer = source?.Pointer ?? IntPtr.Zero;
            if (source == null || !Contains(source, item)) return Refuse("source_changed");
            if (!Sources().Any(root => root.Pointer == sourcePointer)) return Refuse("source_unsupported");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log?.Error($"[储物拒收] 条件检查异常；stage={stage}; item={item?.Pointer}; source={sourcePointer}; count={count}：{ex}");
            Notify("failed");
            return false;
        }
    }

    internal static bool Store(GameItem item)
    {
        if (!CheckStore(item)) return false;
        var taken = false;
        Run(() =>
        {
            var source = item.parentInventory;
            var epoch = State.Epoch; var revision = State.Revision;
            WorkroomItemCodec.Snapshot snapshot;
            List<GameItem> nativeNodes;
            try { snapshot = WorkroomItemCodec.Capture(item, out nativeNodes); }
            catch (Exception ex)
            {
                // No record has been added and Expel has not been called.
                Core.Log?.Error("储物信息捕获失败，尚未移出来源库存：" + ex);
                Notify("capture_failed"); return;
            }
            if (!State.Ready || State.Epoch != epoch || State.Revision != revision ||
                !Contains(source, item) || !GeneralHelper.IsItemOwned(item) || !AcceptsIdentifier(item.identifier))
                throw new InvalidOperationException("捕获期间存档、记录或来源变化。");
            var record = new WorkroomStorageState.Record { Item = snapshot, Place = "cleanup" };
            var candidate = new WorkroomItemCodec.Candidate { Root = item, Nodes = nativeNodes };
            State.Add(record);
            Exception? failure = null;
            try { source!.Expel(item); } catch (Exception ex) { failure = ex; }
            if (Contains(source, item))
            { State.Remove(record.Id); throw new InvalidOperationException("移出库存失败。", failure); }
            if (item.parentInventory != null)
            { State.Remove(record.Id); throw new InvalidOperationException("物品已转往其他库存。"); }
            taken = true;
            cleanup[record.Id] = candidate;
            WorkroomItemCodec.Destroy(candidate);
            State.Move(record.Id, "box"); cleanup.Remove(record.Id);
            Notify("stored");
        });
        return taken;
    }

    internal static void Take(string id, bool inRoom, long epoch)
    {
        if (!State.Ready || State.Epoch != epoch || busy || WorkroomItemCodec.CleanupPending) return;
        var record = State.Find(id);
        if (record == null || record.Place != "box" || cleanup.ContainsKey(id) || delivery.ContainsKey(id)) return;
        if (record.Item.Count != 1) { Notify("single_only"); return; }
        Run(() =>
        {
            if (NativeOwnedIds().Overlaps(WorkroomItemCodec.Identities(record.Item)))
            { Notify("recovery"); return; }
            if (inRoom)
            {
                if (!State.FindSpace(record, out var x, out var y)) { Notify("full"); return; }
                State.Move(id, "room", x, y); Notify("taken"); return;
            }
            if (!PlayerTargets().Any())
            { Notify("unavailable"); return; }
            WorkroomItemCodec.Candidate? candidate = null;
            try
            {
                candidate = WorkroomItemCodec.Restore(record.Item);
                var root = candidate.Root;
                var marker = PlayerDeliverySlot(root);
                if (marker == null)
                { Notify("full"); return; }
                var target = marker.inventory;
                State.Move(id, "delivery");
                delivery[id] = candidate;
                try { marker.AcceptUnchecked(); }
                catch (Exception ex) { Core.Log?.Warning("储物提取回调异常，回读实际归属：" + ex.Message); }
                if (IsPlaced(root, target, record.Item.Count))
                {
                    State.Remove(id); delivery.Remove(id); candidate = null; Notify("taken");
                }
                else if (root.parentInventory == null)
                {
                    WorkroomItemCodec.Destroy(candidate); candidate = null;
                    State.Move(id, "box"); delivery.Remove(id); Notify("full");
                }
                else { candidate = null; Notify("recovery"); }
            }
            finally
            {
                if (candidate != null && !delivery.ContainsKey(id)) WorkroomItemCodec.Destroy(candidate);
            }
        });
    }

    internal static void MoveRoom(string id, int x, int y, long epoch, int? orientation = null)
    {
        if (!State.Ready || State.Epoch != epoch || busy) return;
        var item = State.Find(id);
        if (item == null || item.Place != "room") return;
        Run(() =>
        {
            var snapshot = WorkroomItemCodec.Reorient(item.Item, orientation ?? item.Item.Orientation);
            if (State.Fits(snapshot.Footprint, x, y, id)) State.Move(id, "room", x, y, snapshot);
            else Notify("invalid_position");
        });
    }

    internal static void StoreRoom(string id, long epoch, int? orientation = null)
    {
        if (!State.Ready || State.Epoch != epoch || busy || State.Find(id)?.Place != "room") return;
        Run(() =>
        {
            var item = State.Find(id)!;
            if (item.Item.Count != 1) { Notify("single_only"); return; }
            if (!AcceptsIdentifier(item.Item.Identifier)) { Notify("unsupported_item"); return; }
            var snapshot = WorkroomItemCodec.Reorient(item.Item, orientation ?? item.Item.Orientation);
            State.Move(id, "box", snapshot: snapshot); Notify("stored");
        });
    }

    // Deliberately one deterministic source: the active main inventory at the
    // shop, or room records inside. Drag/drop Sources remains its own contract.
    private static GameGridInventory? CurrentInventory()
    {
        var entry = EmporiumEntry.Instance;
        var grid = entry?.invElement;
        var render = grid?._handler_k__BackingField;
        if (grid == null || render == null || !AssemblyDebugUi.Alive(render) || !render.gameObject.activeInHierarchy) return null;
        var window = entry?.invWindow;
        return window != null && window.IsVisible() ? grid : null;
    }

    internal static void CollectCurrent(bool inRoom, long epoch)
    {
        if (!State.Ready || State.Epoch != epoch || busy || WorkroomItemCodec.CleanupPending ||
            !WorkroomTrial.CanCollectCurrent(inRoom)) { Notify("recovery"); return; }
        var moved = 0; var failed = 0; var skipped = 0;
        if (inRoom)
        {
            Run(() =>
            {
                var room = State.RoomItems.Where(item => item.Item.Count == 1 && AcceptsIdentifier(item.Item.Identifier)).ToArray();
                skipped = State.RoomItems.Count - room.Length;
                var ids = room.Select(item => item.Id).ToArray();
                var selected = new HashSet<string>(ids, StringComparer.Ordinal);
                failed = ids.Length;
                var next = State.Items.Select(item => selected.Contains(item.Id) ? new WorkroomStorageState.Record
                { Id = item.Id, Item = item.Item, Place = "box" } : item).ToList();
                if (ids.Length != 0) State.Replace(next);
                moved = ids.Length; failed = 0;
            });
        }
        else
        {
            var source = CurrentInventory();
            if (source == null) { Notify("unavailable"); return; }
            var candidates = new List<GameItem>();
            var seen = new HashSet<IntPtr>();
            var children = source.childItems;
            for (var i = 0; i < children.Count; i++)
                if (children[i] != null && seen.Add(children[i].Pointer)) candidates.Add(children[i]);
            try
            {
                // Stable selection is allowed while pressing this command, but
                // its native references must be released before Store destroys
                // any selected wrapper. Never reset an unfinished gesture here.
                var selection = ItemMultiSelectHandler.current;
                if (selection != null)
                {
                    if ((int)selection.state == 3) selection.OnEventReset();
                    if (!WorkroomStorageGroupDragPatch.ResetComplete(selection))
                        throw new InvalidOperationException("一键收集的原版选中引用尚未完整释放。");
                }
            }
            catch (Exception ex)
            {
                Core.Log?.Error("一键收集已停止，物品仍在原库存；原版手势保留恢复：" + ex);
                Notify("recovery"); return;
            }
            for (var index = 0; index < candidates.Count; index++)
            {
                var selection = ItemMultiSelectHandler.current;
                if (!State.Ready || State.Epoch != epoch || CurrentInventory()?.Pointer != source.Pointer ||
                    WorkroomItemCodec.CleanupPending || State.PendingRecords.Count != 0 ||
                    (selection != null && !WorkroomStorageGroupDragPatch.ResetComplete(selection)) ||
                    !WorkroomTrial.CanCollectCurrent(false))
                { failed += candidates.Count - index; break; }
                // Read membership before instance data: an earlier native
                // callback may already have moved or destroyed another item.
                var item = candidates[index];
                if (!Contains(source, item)) { failed++; continue; }
                if (!AcceptsIdentifier(item.identifier) || !GeneralHelper.IsItemOwned(item)) { skipped++; continue; }
                if (Store(item)) moved++; else failed++;
            }
        }
        var pending = State.PendingRecords.Count;
        if (pending == 0 && WorkroomItemCodec.CleanupPending) pending = 1;
        NotifyText(Text("collect_result", moved, skipped, failed, pending));
        Core.Log?.Msg($"[工作间一键收纳] source={(inRoom ? "room" : "main")}; moved={moved}; skipped={skipped}; failed={failed}; pending={pending}; records={State.Items.Count}");
    }

    internal sealed class RoomDrop
    {
        internal readonly WorkroomStorageState.Record Record;
        internal readonly string Id;
        internal readonly int X, Y, Count;
        internal readonly WorkroomItemCodec.Snapshot Item;
        internal RoomDrop(WorkroomStorageState.Record record)
        { Record = record; Id = record.Id; X = record.X; Y = record.Y; Item = record.Item; Count = record.Item.Count; }
        internal bool Unchanged => Record.Id == Id && ReferenceEquals(State.Find(Id), Record) && Record.Place == "room" &&
            Record.X == X && Record.Y == Y && ReferenceEquals(Record.Item, Item) && Item.Count == Count;
    }

    internal sealed class NativeDrop
    {
        internal readonly GameItemElement Item;
        internal readonly GameInventory Home;
        private readonly int count, identity, x, y, orientation;
        private readonly bool flipped;
        private readonly string identifier;
        internal NativeDrop(GameItemElement item, GameInventory home)
        {
            Item = item; Home = home; count = item.unitCount; identity = item.uniqueId; identifier = item.identifier;
            var shape = item.modifiedShape ?? item.shape;
            if (shape == null) throw new InvalidOperationException("群拖物品形状缺失。");
            x = shape.minX; y = shape.minY; orientation = shape.orientation; flipped = shape.flipped;
        }
        internal bool Unchanged
        {
            get
            {
                if (!Contains(Home, Item) || Item.unitCount != count || Item.uniqueId != identity || Item.identifier != identifier)
                    return false;
                var shape = Item.modifiedShape ?? Item.shape;
                return shape != null && shape.minX == x && shape.minY == y && shape.orientation == orientation && shape.flipped == flipped;
            }
        }
    }

    // Called only after the room view releases its gesture. All accepted room
    // records publish together; a failed Replace leaves every original record.
    internal static void StoreRoomGroup(IReadOnlyList<RoomDrop> candidates, long epoch)
    {
        if (!State.Ready || State.Epoch != epoch || busy || WorkroomItemCodec.CleanupPending ||
            State.PendingRecords.Count != 0 || !WorkroomTrial.CanCollectCurrent(true))
        { Notify("recovery"); return; }
        var moved = 0; var skipped = 0; var failed = 0;
        Run(() =>
        {
            var valid = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                if (!candidate.Unchanged) { failed++; continue; }
                if (candidate.Item.Count != 1 || !AcceptsIdentifier(candidate.Item.Identifier)) { skipped++; continue; }
                valid.Add(candidate.Id);
            }
            failed += valid.Count;
            var next = State.Items.Select(item => valid.Contains(item.Id) ? new WorkroomStorageState.Record
                { Id = item.Id, Item = item.Item, Place = "box" } : item).ToList();
            if (!State.Ready || State.Epoch != epoch) throw new InvalidOperationException("群拖存档已变化。");
            if (valid.Count != 0) State.Replace(next);
            moved = valid.Count; failed -= valid.Count;
        });
        NotifyGroupResult("room", moved, skipped, failed);
    }

    // Native reset has already restored all ghosts and released every original
    // selection/drop reference. Keep that proof and source custody current for
    // each item: native storage callbacks can invalidate the remaining scope.
    internal static void StoreShopGroup(IReadOnlyList<NativeDrop> candidates, long epoch, IntPtr handlerPointer)
    {
        var moved = 0; var skipped = 0; var failed = 0; var index = 0;
        try
        {
            for (; index < candidates.Count; index++)
            {
                var selection = ItemMultiSelectHandler.current;
                if (!State.Ready || State.Epoch != epoch || busy || WorkroomItemCodec.CleanupPending ||
                    State.PendingRecords.Count != 0 || selection == null || selection.Pointer != handlerPointer ||
                    !WorkroomStorageGroupDragPatch.ResetComplete(selection) || !WorkroomTrial.CanCollectCurrent(false))
                { failed += candidates.Count - index; break; }
                var candidate = candidates[index];
                if (!candidate.Unchanged) { failed++; continue; }
                var showcase = EmporiumEntry.Instance?.showcaseElement;
                if (candidate.Home.Pointer == showcase?.Pointer ||
                    !Sources().Any(source => source.Pointer == candidate.Home.Pointer) ||
                    !GeneralHelper.IsItemOwned(candidate.Item) || !AcceptsIdentifier(candidate.Item.identifier))
                { skipped++; continue; }
                if (Store(candidate.Item)) moved++; else failed++;
            }
        }
        catch (Exception ex)
        {
            failed += Math.Max(1, candidates.Count - index);
            Core.Log?.Error("群拖收纳中止，未处理物品保留原位：" + ex);
        }
        NotifyGroupResult("shop", moved, skipped, failed);
    }

    private static void NotifyGroupResult(string source, int moved, int skipped, int failed)
    {
        var pending = State.PendingRecords.Count;
        if (pending == 0 && WorkroomItemCodec.CleanupPending) pending = 1;
        NotifyText(Text("selected_result", moved, skipped, failed, pending));
        Core.Log?.Msg($"[工作间群拖收纳] source={source}; moved={moved}; skipped={skipped}; failed={failed}; pending={pending}");
    }

    private static bool IsPlaced(GameItem item, GameInventory? target, int count) =>
        count > 0 && item.unitCount == count && Contains(target, item);

    private static HashSet<int> NativeOwnedIds()
    {
        var ids = new HashSet<int>(); var items = EmporiumEntry.Instance?.GetAllOwnedItems();
        if (items != null) foreach (var item in items)
            if (item != null)
            {
                ids.Add(item.uniqueId);
                if (WorkroomMachineFacts.Read(item) is { } machine)
                    foreach (var id in machine.StoredIdentities) ids.Add(id);
                foreach (var content in WorkroomCrateContents.Read(item))
                    foreach (var id in WorkroomItemCodec.Identities(content)) ids.Add(id);
                foreach (var part in WorkroomComponentParts.ReadComposition(item) ?? new List<WorkroomItemCodec.Snapshot>())
                    foreach (var id in WorkroomItemCodec.Identities(part)) ids.Add(id);
            }
        return ids;
    }

    private static void ReconcileLoadedRecords()
    {
        if (!State.Ready) return;
        var owned = NativeOwnedIds();
        foreach (var record in State.PendingRecords.ToArray())
        {
            if (record.Place == "cleanup")
            {
                // A completed normal save should never contain a half operation.
                // Recover only an unambiguous graph: all live nodes => source kept;
                // no live nodes => the snapshot is its sole remaining custodian.
                var identities = WorkroomItemCodec.Identities(record.Item).ToArray();
                var hits = identities.Count(owned.Contains);
                if (hits == 0) State.Move(record.Id, "box");
                else if (hits == identities.Length)
                {
                    var items = EmporiumEntry.Instance?.GetAllOwnedItems();
                    GameItem? root = null;
                    if (items != null) foreach (var item in items)
                        if (item != null && item.uniqueId == record.Item.Nodes[0].UniqueId) { root = item; break; }
                    if (root != null && WorkroomItemCodec.Equivalent(record.Item, WorkroomItemCodec.Capture(root))) State.Remove(record.Id);
                    else Notify("recovery");
                }
                else Notify("recovery");
            }
            else if (record.Place == "delivery")
            {
                var hits = WorkroomItemCodec.Identities(record.Item).Count(owned.Contains);
                if (hits == 0) State.Move(record.Id, "box");
                // Never discard a record based on one ID alone; ambiguous
                // delivery remains locked for investigation rather than copying.
                else Notify("recovery");
            }
        }
    }

    internal static void Run(Action action)
    {
        if (Busy) return;
        var timer = Stopwatch.StartNew();
        busy = true;
        try { action(); }
        catch (Exception ex) { Core.Log?.Error("工作间储物操作保留未完成记录：" + ex); Notify("failed"); }
        finally
        {
            busy = false;
            if (timer.ElapsedMilliseconds >= 12 && Environment.TickCount64 - lastSlowLog >= 5000)
            {
                lastSlowLog = Environment.TickCount64;
                Core.Log?.Msg($"[工作间储物耗时] action={action.Method.Name}; elapsedMs={timer.ElapsedMilliseconds}; records={State.Items.Count}; revision={State.Revision}");
            }
        }
    }

    internal static bool BeforeSave()
    {
        if (busy || cleanup.Count != 0 || delivery.Count != 0 || WorkroomItemCodec.CleanupPending || WorkroomCashRepair.Pending || ComputerSupplierNpcs.StockPending || NpcStockOffers.Pending ||
            State.PendingRecords.Count != 0)
        { deferredSave = true; Notify("recovery"); return false; }
        return true;
    }

    internal static void ResetSession()
    {
        foreach (var pair in cleanup) try { WorkroomItemCodec.Destroy(pair.Value); } catch (Exception ex) { Core.Log?.Warning(ex.Message); }
        foreach (var pair in delivery)
            try
            {
                if (pair.Value.Cleaned || pair.Value.Root.parentInventory == null) WorkroomItemCodec.Destroy(pair.Value);
            }
            catch (Exception ex) { Core.Log?.Warning(ex.Message); }
        cleanup.Clear(); delivery.Clear(); State.Unbind(); deferredSave = false;
        WorkroomItemCodec.EndSessionCleanup();
    }
}

[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.SaveGame))]
internal static class WorkroomStorageSavePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix() => WorkroomStorage.BeforeSave();
}
