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
    internal static bool Busy => busy || WorkroomCashRepair.Pending || ServerRoomRuntime.Pending || WorkroomStoryTransfer.BlocksStorage;
    internal static string Text(string name, params object[] args) => LanguageText.Get("workroom.storage." + name, args);
    internal static string Message { get; private set; } = "";
    internal static long MessageUntil { get; private set; }

    internal static void Notify(string name)
    { Message = Text(name); MessageUntil = Environment.TickCount64 + 5000; RoomAudio.Notice(name); }

    internal static void NotifyText(string text)
    { Message = text; MessageUntil = Environment.TickCount64 + 5000; }

    private static bool HasMaintenance => cleanup.Count != 0 || delivery.Count != 0 || deferredSave ||
        WorkroomItemCodec.CleanupPending || State.PendingRecords.Count != 0;

    internal static void Wake(PlayerStore store)
    {
        // Binding is part of a load/event, never a hidden UI's frame refresh.
        if (!State.SameOwner()) Update(store);
        if (!HasMaintenance) return;
        var pointer = store.Pointer; var run = store.runID; var slot = store.saveSlotId;
        BusinessWork.Schedule("workroom-storage", () =>
        {
            if (!PlayerStore.IsInstanceExist() || PlayerStore.instance == null ||
                PlayerStore.instance.Pointer != pointer || store.runID != run || store.saveSlotId != slot) return false;
            Update(store);
            return HasMaintenance;
        }, intervalMs: 500);
    }

    private static void WakeMaintenance()
    {
        if (!HasMaintenance || !PlayerStore.IsInstanceExist() || PlayerStore.instance == null) return;
        Wake(PlayerStore.instance);
    }

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
        WorkroomItemCodec.WakeCleanup();
        foreach (var pair in cleanup.ToArray())
            Run(() =>
            {
                WorkroomItemCodec.Destroy(pair.Value);
                State.Move(pair.Key, "box"); cleanup.Remove(pair.Key);
            });
        foreach (var pair in delivery.ToArray())
            Run(() =>
            {
                if (GeneralHelper.IsItemOwned(pair.Value.Root) &&
                    WorkroomSelection.Inventories(includeLocked: true).Any(target => IsPlaced(pair.Value.Root, target.Grid, State.Find(pair.Key)?.Item.Count ?? 0)))
                { State.Remove(pair.Key); delivery.Remove(pair.Key); }
                else if (pair.Value.Cleaned || pair.Value.Root.parentInventory == null)
                {
                    WorkroomItemCodec.Destroy(pair.Value);
                    State.Move(pair.Key, "box"); delivery.Remove(pair.Key);
                }
            });
        if (State.PendingRecords.Any(item => (item.Place == "cleanup" && !cleanup.ContainsKey(item.Id)) ||
            (item.Place == "delivery" && !delivery.ContainsKey(item.Id)))) Run(ReconcileLoadedRecords);
        if (deferredSave && cleanup.Count == 0 && delivery.Count == 0 && !WorkroomItemCodec.CleanupPending && !WorkroomCashRepair.Pending && !ServerRoomRuntime.Pending && ServerRoomDaily.AllowSave && !ComputerSupplierNpcs.StockPending && !NpcStockOffers.Pending && ServerRoomOrderStock.BeforeSave() &&
            State.PendingRecords.Count == 0)
        { if (WorkroomStoryTransfer.BeforeSave() && LuoxiStory.BeforeSave() && JiangbaiStory.BeforeSave() && MysteriousContact.BeforeSave() && NpcStoryProgress.BeforeSave() && NpcSmallStories.BeforeSave()) { deferredSave = false; store.SaveGame(); } }
    }

    internal static IEnumerable<GameGridInventory> Sources()
    {
        if (!WorkroomGameAdapter.InventoryTransferAllowed()) yield break;
        // Includes verified native storage containers; installed hardware and
        // any locked ancestor remain outside the transfer source graph.
        foreach (var pair in WorkroomSelection.Inventories()) yield return pair.Grid;
    }

    internal static IEnumerable<GameGridInventory> PlayerTargets()
    {
        if (!WorkroomGameAdapter.InventoryTransferAllowed()) yield break;
        var entry = EmporiumEntry.Instance;
        if (entry == null) yield break;
        // Native TryAddToPlayerInv starts at back (player counter), then main.
        // Its third showcase fallback is deliberately outside this delivery.
        if (entry.backInvinvElement != null) yield return entry.backInvinvElement;
        if (entry.invElement != null && entry.invElement.Pointer != entry.backInvinvElement?.Pointer)
            yield return entry.invElement;
    }

    // Story returns follow the native owned-item inventory list (including the
    // hidden compartment), rather than the workroom drag source whitelist.
    // Only scene inventories participate: a component installed in another
    // item must first be removed normally. Never traverse/expel its parent item.
    internal static IEnumerable<GameInventory> StorySources()
    {
        var owned = EmporiumEntry.Instance?.GetAllOwnedItems();
        if (owned == null) yield break;
        var seen = new HashSet<IntPtr>();
        foreach (var item in owned)
        {
            if (item == null || !GeneralHelper.IsItemOwned(item)) continue;
            var source = item.parentInventory;
            if (source == null || source.IsRemoveLocked() || source.GetParentItem() != null ||
                !Contains(source, item) || !seen.Add(source.Pointer)) continue;
            yield return source;
        }
    }

    private static bool IsStorySource(GameInventory? source, GameItem item, HashSet<IntPtr> sources) =>
        source != null && !source.IsRemoveLocked() && source.GetParentItem() == null &&
        Contains(source, item) && GeneralHelper.IsItemOwned(item) &&
        sources.Contains(source.Pointer);

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

    // Admission only. Existing records remain readable and withdrawable.
    internal static bool AcceptsIdentifier(string? identifier) => identifier != null &&
        (Components.Find(identifier) != null || ServerRoomMachine.TryDefinition(identifier, out _) ||
        WorkroomComponentTemplates.TryPart(identifier, out _, out _, out _) ||
        identifier is "common_electronic" or "wire" or "nuts_metal" or "printer_plastic" or
            "scrap_metal" or "metal_ingot");

    internal static bool Contains(GameInventory? inventory, GameItem item)
    {
        if (inventory == null || item.TryCast<GameItemElement>() is { } element && element.IsDestroyed() ||
            item.parentInventory == null || inventory.Pointer != item.parentInventory.Pointer) return false;
        var children = inventory.childItems;
        for (var i = 0; i < children.Count; i++) if (children[i] != null && children[i].Pointer == item.Pointer) return true;
        return false;
    }

    private static bool CheckStore(GameItem item, HashSet<IntPtr>? storySources = null)
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
            if (storySources == null && !WorkroomGameAdapter.InventoryTransferAllowed()) return Refuse("unavailable");
            if (!State.Ready) return Refuse(State.Error ?? "unavailable");
            if (Busy || WorkroomItemCodec.CleanupPending) return Refuse("recovery");
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
            if (source.IsRemoveLocked() ||
                (storySources != null ? !IsStorySource(source, item, storySources) : !Sources().Any(root => root.Pointer == sourcePointer)))
                return Refuse("source_unsupported");
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
        // Direct drops retain the same durable home used by quick transfers.
        if (!CheckStore(item)) return false;
        try { return Store(item, null, origin: WorkroomSelection.Remember(item)); }
        catch (Exception ex) { Core.Log?.Warning("工作间来源读取失败，保留原物：" + ex.Message); Notify("failed"); return false; }
    }

    // Retain each native item's durable cleanup receipt before expelling it.
    // Only the safe cleanup -> box promotions are coalesced; failures retain
    // the existing per-item recovery owners and save barrier.
    internal static bool StoreStoryBatch(IReadOnlyList<GameItem> items)
    {
        // Resolve the native source list once for this synchronous batch. Each
        // item still rechecks its live parent/ownership/lock before and after
        // Capture; avoid a whole owned-item graph scan for every item twice.
        var sources = StorySources().Select(source => source.Pointer).ToHashSet();
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var stored = true;
        try
        {
            foreach (var item in items)
                if (!Store(item, completed, sources)) { stored = false; break; }
        }
        finally
        {
            if (completed.Count != 0) Run(() =>
            {
                if (!State.Ready || completed.Any(id => State.Find(id)?.Place != "cleanup" ||
                    !cleanup.TryGetValue(id, out var candidate) || !candidate.Cleaned))
                    throw new InvalidOperationException("剧情收纳保管凭据变化。");
                State.Replace(State.Items.Select(record => completed.Contains(record.Id)
                    ? WorkroomStorageState.Copy(record, "box", "")
                    : record).ToList());
                foreach (var id in completed) cleanup.Remove(id);
            });
        }
        return stored && completed.All(id => State.Find(id)?.Place == "box" && !cleanup.ContainsKey(id));
    }

    internal static bool StoreSelection(GameItem item, out string id)
    {
        id = "";
        try
        {
            if (!WorkroomSelection.CanTransfer(item)) return false;
            var identity = item.uniqueId;
            var origin = WorkroomSelection.Remember(item);
            if (!Store(item, null, origin: origin)) return false;
            id = State.Items.FirstOrDefault(record => record.Place == "box" && record.Item.Nodes[0].UniqueId == identity)?.Id ?? "";
            return id.Length != 0;
        }
        catch (Exception ex)
        { Core.Log?.Warning("工作间候选交接未完成，保留当前保管凭据：" + ex.Message); Notify("failed"); return false; }
    }

    private static bool Store(GameItem item, HashSet<string>? completed, HashSet<IntPtr>? storySources = null,
        WorkroomStorageState.Origin? origin = null)
    {
        if (!CheckStore(item, storySources)) return false;
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
                !Contains(source, item) || !GeneralHelper.IsItemOwned(item) || !AcceptsIdentifier(item.identifier) ||
                source!.IsRemoveLocked() ||
                storySources == null && !Sources().Any(root => root.Pointer == source.Pointer) ||
                storySources != null && !IsStorySource(source, item, storySources) ||
                storySources == null && !WorkroomGameAdapter.InventoryTransferAllowed())
                throw new InvalidOperationException("捕获期间存档、记录或来源变化。");
            var record = new WorkroomStorageState.Record { Item = snapshot, Place = "cleanup", ReturnTo = origin };
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
            if (completed != null) completed.Add(record.Id);
            else { State.Move(record.Id, "box"); cleanup.Remove(record.Id); }
            Notify("stored");
        });
        return taken;
    }

    internal static void Take(string id, bool inRoom, long epoch)
    {
        if (!WorkroomGameAdapter.InventoryTransferAllowed() || !State.Ready || State.Epoch != epoch || Busy || WorkroomItemCodec.CleanupPending) return;
        var record = State.Find(id);
        if (record == null || record.Place != "box" || cleanup.ContainsKey(id) || delivery.ContainsKey(id)) return;
        if (record.Item.Count != 1) { Notify("single_only"); return; }
        Run(() =>
        {
            if (NativeOwnedIds().Overlaps(WorkroomItemCodec.Identities(record.Item)))
            { Notify("recovery"); return; }
            if (!WorkroomGameAdapter.InventoryTransferAllowed()) return;
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
                if (!WorkroomGameAdapter.InventoryTransferAllowed()) return;
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
        if (!State.Ready || State.Epoch != epoch || Busy) return;
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
        if (!State.Ready || State.Epoch != epoch || Busy || State.Find(id)?.Place != "room") return;
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
        using var audioBatch = RoomAudio.Batch();
        if (!State.Ready || State.Epoch != epoch || Busy || WorkroomItemCodec.CleanupPending ||
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
                { Id = item.Id, Item = item.Item, Place = "box", ReturnTo = item.ReturnTo } : item).ToList();
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
        RoomAudio.Play(pending > 0 ? "warning" : moved > 0 ? "store" : "warning");
        Core.Debug($"[工作间一键收纳] source={(inRoom ? "room" : "main")}; moved={moved}; skipped={skipped}; failed={failed}; pending={pending}; records={State.Items.Count}");
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
        using var audioBatch = RoomAudio.Batch();
        if (!State.Ready || State.Epoch != epoch || Busy || WorkroomItemCodec.CleanupPending ||
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
                { Id = item.Id, Item = item.Item, Place = "box", ReturnTo = item.ReturnTo } : item).ToList();
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
                if (!State.Ready || State.Epoch != epoch || Busy || WorkroomItemCodec.CleanupPending ||
                    State.PendingRecords.Count != 0 || selection == null || selection.Pointer != handlerPointer ||
                    !WorkroomStorageGroupDragPatch.ResetComplete(selection) || !WorkroomTrial.CanCollectCurrent(false))
                { failed += candidates.Count - index; break; }
                var candidate = candidates[index];
                if (!candidate.Unchanged) { failed++; continue; }
                if (!Sources().Any(source => source.Pointer == candidate.Home.Pointer) ||
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
        RoomAudio.Play(pending > 0 ? "warning" : moved > 0 ? "store" : "warning");
        Core.Debug($"[工作间群拖收纳] source={source}; moved={moved}; skipped={skipped}; failed={failed}; pending={pending}");
    }

    private static bool IsPlaced(GameItem item, GameInventory? target, int count) =>
        count > 0 && item.unitCount == count && Contains(target, item);

    // Called inside the storage command lock, with every requested return
    // already planned. Retain the existing delivery owner before native accept.
    internal static bool DeliverTo(WorkroomStorageState.Record record, WorkroomItemCodec.Candidate candidate, SlotMarker marker)
    {
        var root = candidate.Root; var target = marker.inventory;
        if (!ReferenceEquals(State.Find(record.Id), record) || target == null || !Sources().Any(source => source.Pointer == target.Pointer) ||
            target.IsInsertLocked() || !GeneralHelper.MayPlayerInsertInto(target) ||
            !GeneralHelper.MayOwnershipAcceptItem(root, target) ||
            !target.CheckInventorySlotStillValid(root, marker.itemGridShape, root.modifiedState) ||
            NativeOwnedIds().Overlaps(WorkroomItemCodec.Identities(record.Item)) ||
            !WorkroomGameAdapter.InventoryTransferAllowed()) return false;
        State.Move(record.Id, "delivery"); delivery[record.Id] = candidate;
        try { marker.AcceptUnchecked(); }
        catch (Exception ex) { Core.Log?.Warning("工作间原来源归还回调异常，回读实际归属：" + ex.Message); }
        if (IsPlaced(root, target, record.Item.Count) && GeneralHelper.IsItemOwned(root))
        { State.Remove(record.Id); delivery.Remove(record.Id); return true; }
        if (root.parentInventory == null)
        {
            WorkroomItemCodec.Destroy(candidate);
            var current = State.Find(record.Id)!;
            State.Replace(State.Items.Select(item => item.Id == current.Id ? record : item).ToList());
            delivery.Remove(record.Id);
        }
        // Unknown live ownership remains a delivery record and blocks saving.
        return false;
    }

    private static IEnumerable<GameItem> OwnedRoots()
    {
        var items = new Dictionary<IntPtr, GameItem>();
        var native = EmporiumEntry.Instance?.GetAllOwnedItems();
        if (native != null) foreach (var item in native)
            if (item != null) items[item.Pointer] = item;
        // The native owned list need not enumerate PlayerStore.gridInv. Include
        // every supported scene root at this transaction/recovery boundary,
        // including locked roots, so duplicate identities cannot be restored.
        foreach (var pair in WorkroomSelection.Inventories(includeLocked: true))
            foreach (var item in pair.Grid.childItems)
                if (item != null && GeneralHelper.IsItemOwned(item)) items[item.Pointer] = item;
        return items.Values;
    }

    private static HashSet<int> NativeOwnedIds()
    {
        var ids = new HashSet<int>();
        foreach (var item in OwnedRoots())
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
                    var items = OwnedRoots();
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
            WakeMaintenance();
            if (timer.ElapsedMilliseconds >= 12 && Environment.TickCount64 - lastSlowLog >= 5000)
            {
                lastSlowLog = Environment.TickCount64;
                Core.Debug($"[工作间储物耗时] action={action.Method.Name}; elapsedMs={timer.ElapsedMilliseconds}; records={State.Items.Count}; revision={State.Revision}");
            }
        }
    }

    internal static bool BeforeSave()
    {
        if (busy || !WorkroomStoryTransfer.BeforeSave() || !LuoxiStory.BeforeSave() || !JiangbaiStory.BeforeSave() || !MysteriousContact.BeforeSave() || !NpcStoryProgress.BeforeSave() || !NpcSmallStories.BeforeSave() || cleanup.Count != 0 || delivery.Count != 0 || WorkroomItemCodec.CleanupPending || WorkroomCashRepair.Pending || ServerRoomRuntime.Pending || !ServerRoomRuntime.ValidateCurrent() || !ServerRoomDaily.AllowSave || ComputerSupplierNpcs.StockPending || NpcStockOffers.Pending || !ServerRoomOrderStock.BeforeSave() ||
            State.PendingRecords.Count != 0)
        { deferredSave = true; WakeMaintenance(); BusinessRuntime.Wake(); Notify("recovery"); return false; }
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
