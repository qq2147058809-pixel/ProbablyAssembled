using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;

namespace PCExpansion;

// User commands build plans on demand. No native item or snapshot cache is
// refreshed by a frame loop; the click rechecks the exact custody and session.
internal static class WorkroomSelection
{
    [Flags] internal enum Source { Room = 1, Box = 2, Shop = 4, All = 7 }
    internal sealed class Candidate
    {
        internal string Id = "";
        internal WorkroomItemCodec.Snapshot Item = null!;
        internal Source Origin;
        internal string SourceName = "";
        internal GameItem? Native;
        internal string Inventory = "";
        internal long Epoch;
        internal WorkroomStorageState.Origin? NativeLocation;
    }
    internal static bool AutoReturn = true;
    internal static bool CandidatesComplete { get; private set; } = true;
    internal static string Text(string key, params object[] args) => LanguageText.Get("workroom.convenience." + key, args);
    private static WorkroomStorageState State => WorkroomStorage.State;
    private static void Notice(string key) => WorkroomStorage.NotifyText(Text(key));

    // Stable scene roles survive reloads and an emptied inventory. Never save
    // native pointers, runtime names, discovery order or an installed parent.
    internal static IEnumerable<(string Key, GameGridInventory Grid)> SceneInventories()
    {
        var e = EmporiumEntry.Instance; if (e == null) yield break;
        var seen = new HashSet<IntPtr>();
        var store = PlayerStore.IsInstanceExist() ? PlayerStore.instance : null;
        foreach (var pair in new (string, GameGridInventory?)[] {
            ("main", e.invElement), ("front", e.frontInvinvElement), ("back", e.backInvinvElement),
            ("counter", e.backInvinvElementCounter), ("showcase", e.showcaseElement),
            ("hidden", e.hiddenElement), ("bazar", e.bazarLeftinvElement),
            ("afterhour", e.afterhourInventory), ("hireling", e.hirelingInv),
            ("sold", e.soldElement), ("response", e.responseInventory),
            ("response_closable", e.responseInventoryClosable), ("trash", e.trashcanInvElement),
            ("storage", store?.gridInv) })
            if (pair.Item2 != null && seen.Add(pair.Item2.Pointer) && pair.Item2.GetParentItem() == null)
                yield return (pair.Item1, pair.Item2);
    }
    // Storage is a capability, not a list of item identifiers. Native machines
    // also carry CONTAINER_TAG (some even have a single grid), so exclude their
    // purpose before traversing contents. Mod hardware inherits native templates.
    private static bool IsStorageContainer(GameItem item) =>
        item.IsTag("CONTAINER_TAG") && !item.IsGameItemType("MACHINE") &&
        !item.IsTag("STANDARD_MACHINE_TAG") && !item.IsTag("NESTABLE_DEVICE_TAG") &&
        !item.IsTag("CAGE_TAG") && !ComputerCase.IsCase(item) &&
        !ServerRoomHardware.IsHardware(item) && !Components.IsComponent(item);

    // Discover owned, ordinary storage grids even when empty or closed. Keep
    // durable container identities; never persist pointers or window names.
    internal static IEnumerable<(string Key, GameGridInventory Grid)> Inventories(bool includeLocked = false)
    {
        var pending = new Queue<(string Key, GameGridInventory Grid, int Depth)>();
        foreach (var pair in SceneInventories()) pending.Enqueue((pair.Key, pair.Grid, 0));
        var seen = new HashSet<IntPtr>();
        while (pending.Count != 0)
        {
            var pair = pending.Dequeue();
            if (!seen.Add(pair.Grid.Pointer) || !includeLocked && pair.Grid.IsRemoveLocked()) continue;
            yield return (pair.Key, pair.Grid);
            if (pair.Depth >= 16) continue;
            foreach (var item in pair.Grid.childItems)
            {
                if (item == null || item.unitCount != 1 || item.uniqueId == 0 || !IsStorageContainer(item) ||
                    !WorkroomStorage.Contains(pair.Grid, item) || !GeneralHelper.IsItemOwned(item)) continue;
                var children = item.contentWindow?.TryCast<GraphNodeStorage>()?.children;
                if (children == null) continue;
                GameGridInventory? content = null; var ambiguous = false;
                foreach (var node in children)
                {
                    var inventory = node?.TryCast<GameInventory>();
                    if (inventory == null) continue;
                    var grid = inventory.TryCast<GameGridInventory>();
                    // Mixed socket/grid windows are device interfaces. A plain
                    // container must have one unambiguous, correctly parented grid.
                    if (grid == null || grid.GetParentItem()?.Pointer != item.Pointer) { ambiguous = true; break; }
                    if (content != null && content.Pointer != grid.Pointer) { ambiguous = true; break; }
                    content = grid;
                }
                if (!ambiguous && content != null)
                    pending.Enqueue(("container:" + item.identifier + ":" + item.uniqueId.ToString(System.Globalization.CultureInfo.InvariantCulture), content, pair.Depth + 1));
            }
        }
    }
    private static GameGridInventory? Inventory(string key)
    {
        var matches = Inventories().Where(p => p.Key == key).Take(2).ToArray();
        return matches.Length == 1 ? matches[0].Grid : null;
    }
    private static string InventoryKey(GameInventory source)
    {
        var all = Inventories().ToArray();
        var key = all.FirstOrDefault(p => p.Grid.Pointer == source.Pointer).Key;
        return key != null && all.Count(p => p.Key == key) == 1 ? key : "";
    }

    internal static bool CanTransfer(GameItem item)
    {
        try
        {
            var source = item?.parentInventory;
            return WorkroomGameAdapter.InventoryTransferAllowed() && State.Ready && item != null && item.unitCount == 1 && WorkroomStorage.AcceptsIdentifier(item.identifier) &&
                source != null && !source.IsRemoveLocked() &&
                WorkroomStorage.Contains(source, item) && GeneralHelper.IsItemOwned(item) &&
                WorkroomStorage.Sources().Any(root => root.Pointer == source.Pointer) && InventoryKey(source).Length != 0;
        }
        catch { return false; }
    }
    internal static List<Candidate> Candidates(Source sources)
    {
        var result = new List<Candidate>();
        CandidatesComplete = true;
        if (!WorkroomGameAdapter.InventoryTransferAllowed() || !State.Ready || WorkroomStorage.Busy || WorkroomItemCodec.CleanupPending) { CandidatesComplete = false; return result; }
        var epoch = State.Epoch;
        foreach (var record in State.Items)
        {
            var source = record.Place == "room" ? Source.Room : record.Place == "box" ? Source.Box : 0;
            if (source != 0 && (sources & source) != 0)
                result.Add(new Candidate { Id = record.Id, Item = record.Item, Origin = source,
                    SourceName = Text(source == Source.Room ? "source_room" : "source_box"), Epoch = epoch });
        }
        if ((sources & Source.Shop) == 0) return result;
        try
        {
            foreach (var source in WorkroomStorage.Sources())
            {
                var key = InventoryKey(source); if (key.Length == 0) { CandidatesComplete = false; continue; }
                foreach (var item in source.childItems)
                {
                    if (item == null || !CanTransfer(item)) continue;
                    try
                    {
                        var snapshot = WorkroomItemCodec.Capture(item);
                        if (!CanTransfer(item) || !WorkroomStorage.Contains(source, item) || State.Epoch != epoch) continue;
                        result.Add(new Candidate { Id = "native:" + snapshot.Nodes[0].UniqueId, Item = snapshot,
                            Origin = Source.Shop, Native = item, Inventory = key, Epoch = epoch,
                            NativeLocation = Remember(item),
                            SourceName = Text("source_shop") + " · " + Text("inventory_" + (key.StartsWith("container:", StringComparison.Ordinal) ? "container" : key)) });
                    }
                    catch (Exception ex) { CandidatesComplete = false; Core.Log?.Warning("工作间候选跳过不可读取物品：" + ex.Message); }
                }
            }
        }
        catch (Exception ex) { CandidatesComplete = false; Core.Log?.Warning("工作间店铺候选不可用：" + ex.Message); }
        return result;
    }

    internal static WorkroomStorageState.Origin Remember(WorkroomStorageState.Record record) =>
        record.Place == "slot" && record.ReturnTo != null ? record.ReturnTo : new()
        { Place = record.Place, X = record.X, Y = record.Y, Orientation = record.Item.Orientation, Flipped = record.Item.Flipped };
    internal static WorkroomStorageState.Origin Remember(GameItem item)
    {
        var source = item.parentInventory ?? throw new InvalidOperationException("物品来源变化。");
        var key = InventoryKey(source); var shape = item.modifiedShape ?? item.shape;
        if (key.Length == 0 || shape == null) throw new InvalidOperationException("来源库存或位置不可识别。");
        return new() { Place = "shop", Inventory = key, X = shape.minX, Y = shape.minY,
            Orientation = shape.orientation, Flipped = shape.flipped };
    }
    private static HashSet<string> Expanded(IEnumerable<string> slots)
    {
        var requested = slots.ToHashSet(StringComparer.Ordinal);
        foreach (var family in WorkroomComponentTemplates.All)
            if (requested.Contains(WorkroomComponentAssembly.Slot(family, "body")))
                requested.UnionWith(WorkroomComponentAssembly.Slots(family));
        foreach (var family in new[] { WorkroomMachineAssembly.Motherboard, WorkroomMachineAssembly.Case })
            if (requested.Contains(WorkroomMachineAssembly.MainSlot(family))) requested.UnionWith(WorkroomMachineAssembly.Slots(family));
        return requested;
    }
    private static string Target(string slot, WorkroomItemCodec.Snapshot item) => slot == WorkroomComponentAssembly.EntryBoard &&
        WorkroomComponentTemplates.TryPart(item.Identifier, out var family, out var kind, out _) && kind == "body"
        ? WorkroomComponentAssembly.Slot(family, "body") : slot;
    private static HashSet<string> Replaced(string slot, WorkroomItemCodec.Snapshot item)
    {
        if (slot == WorkroomComponentAssembly.EntryBoard)
            return WorkroomComponentTemplates.All.SelectMany(t => WorkroomComponentAssembly.Slots(t))
                .Append(WorkroomComponentAssembly.EntryBoard).ToHashSet(StringComparer.Ordinal);
        return Expanded(new[] { Target(slot, item) });
    }
    internal static bool Suitable(string slot, WorkroomItemCodec.Snapshot item, bool replacing = false, bool repair = false)
    {
        try
        {
            if (!State.Ready || !WorkroomItemCodec.CanUseOne(item)) return false;
            // Explicit body selection also serves disassembly/material repair.
            // Passive compatibility highlights remain intact-only (replacing=false).
            var repairBody = (repair || replacing) && (slot == WorkroomMachineAssembly.MainSlot("motherboard") || slot == WorkroomMachineAssembly.MainSlot("case"));
            if (slot != WorkroomComponentAssembly.Disassembly && !repairBody && !WorkroomComponentAssembly.Intact(item)) return false;
            var target = Target(slot, item);
            var excluded = replacing ? Replaced(slot, item) : new HashSet<string>();
            WorkroomStorageState.Record? Find(string s) => excluded.Contains(s) ? null : State.FindSlot(s);
            if (!WorkroomComponentAssembly.Accepts(target, item, Find)) return false;
            if (!WorkroomMachineAssembly.ValidSlot(target)) return true;
            var family = target.StartsWith("machine.motherboard.", StringComparison.Ordinal) ? "motherboard" : "case";
            var items = WorkroomMachineAssembly.Slots(family).Select(Find).Where(r => r != null).Select(r => r!.Item).Append(item).ToArray();
            var parts = new List<CaseEconomy.PartRecord>();
            foreach (var snapshot in items)
            {
                if (snapshot.Nodes[0].Machine is { } machine) parts.AddRange(WorkroomMachineAssembly.Flatten(machine));
                else if (Components.Find(snapshot.Identifier) is { } spec && !(family == "case" && spec.Owner.Tag == Components.CaseTag))
                    parts.Add(new CaseEconomy.PartRecord(spec.Owner.Tag, WorkroomComponentAssembly.Configuration(snapshot), snapshot.Broken));
            }
            return parts.Count == 0 || parts.Where(p => p.TypeTag == Components.RamTag).Select(p => p.Tier).Distinct().Count() <= 1 &&
                parts.Max(p => p.Tier) - parts.Min(p => p.Tier) <= 3;
        }
        catch { return false; }
    }
    private static bool Current(Candidate c)
    {
        if (!WorkroomGameAdapter.InventoryTransferAllowed() || !State.Ready || State.Epoch != c.Epoch) return false;
        if (c.Origin == Source.Shop) return c.Native != null && CanTransfer(c.Native) &&
            InventoryKey(c.Native.parentInventory) == c.Inventory &&
            c.Native.uniqueId == c.Item.Nodes[0].UniqueId && c.Native.identifier == c.Item.Identifier &&
            c.Native.unitCount == c.Item.Count && c.NativeLocation is { } origin &&
            (c.Native.modifiedShape ?? c.Native.shape) is { } shape && shape.minX == origin.X && shape.minY == origin.Y &&
            shape.orientation == origin.Orientation && shape.flipped == origin.Flipped;
        var record = State.Find(c.Id);
        return record != null && record.Place == (c.Origin == Source.Room ? "room" : "box") && ReferenceEquals(record.Item, c.Item);
    }
    internal static string Reason(string slot, Candidate candidate, bool repair = false)
    {
        try
        {
            if (!WorkroomComponentAssembly.Ready(candidate.Epoch) || !Current(candidate)) return Text("changed");
            if (!Suitable(slot, candidate.Item, true, repair)) return Text("incompatible");
            var selected = State.Items.Where(r => r.Place == "slot" && Replaced(slot, candidate.Item).Contains(r.Slot)).ToArray();
            using var plan = PlanReturns(selected, candidate.Origin == Source.Shop ? null : candidate.Id, native: false);
            return plan == null ? Text("return_full") : "";
        }
        catch { return Text("unavailable"); }
    }

    private sealed class NativeReturn
    {
        internal WorkroomStorageState.Record Record = null!;
        internal WorkroomItemCodec.Candidate Candidate = null!;
        internal SlotMarker Marker = null!;
    }
    private sealed class ReturnPlan : IDisposable
    {
        internal List<WorkroomStorageState.Record> Managed = new();
        internal List<NativeReturn> Native = new();
        public void Dispose()
        {
            foreach (var part in Native)
                if (!part.Candidate.Cleaned && part.Candidate.Root.parentInventory == null)
                    WorkroomItemCodec.Destroy(part.Candidate);
        }
    }
    internal static bool CanReturn(IReadOnlyList<WorkroomStorageState.Record> selected, string? incoming = null)
    {
        // Hover/previews check managed capacity and native destination access.
        // Native restore/placement is deferred to the actual command boundary.
        using var plan = PlanReturns(selected, incoming, native: false);
        return plan != null;
    }
    private static ReturnPlan? PlanReturns(IReadOnlyList<WorkroomStorageState.Record> selected, string? incoming = null, bool native = true)
    {
        var plan = new ReturnPlan(); var success = false;
        try
        {
            var ids = selected.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            var next = State.Items.Where(r => !ids.Contains(r.Id) && r.Id != incoming).ToList();
            var deferredRoom = new List<WorkroomStorageState.Record>();
            foreach (var record in selected.OrderByDescending(r => r.Item.Footprint.Width * r.Item.Footprint.Height))
            {
                var origin = record.ReturnTo;
                if (origin?.Place == "shop")
                {
                    var target = Inventory(origin.Inventory);
                    if (target == null || target.IsInsertLocked() || target.IsRemoveLocked() || !GeneralHelper.MayPlayerInsertInto(target)) return null;
                    if (!native) continue;
                    var candidate = WorkroomItemCodec.Restore(record.Item);
                    var entry = new NativeReturn { Record = record, Candidate = candidate }; plan.Native.Add(entry);
                    var root = candidate.Root; var shape = root.modifiedShape ?? root.shape;
                    var reserved = selected.Where(other => other.Id != record.Id && other.ReturnTo?.Place == "shop" &&
                        other.ReturnTo.Inventory == origin.Inventory).Select(other => new {
                            Home = other.ReturnTo!, Shape = WorkroomItemCodec.Reorient(other.Item, other.ReturnTo!.Orientation).Footprint }).ToArray();
                    var preferred = new GridShapeBuilder(shape).SetTransform(origin.X, origin.Y, origin.Flipped, origin.Orientation).Build();
                    bool Valid(GridShape value, bool reserve = true) => target.CheckInventorySlotStillValid(root, value, root.modifiedState) &&
                        GeneralHelper.MayOwnershipAcceptItem(root, target) && !plan.Native.Any(other => other != entry &&
                        other.Marker != null && other.Marker.inventory.Pointer == target.Pointer && Overlap(value, other.Marker.itemGridShape)) &&
                        (!reserve || !reserved.Any(other => value.minX < other.Home.X + other.Shape.Width && other.Home.X <= value.maxX &&
                            value.minY < other.Home.Y + other.Shape.Height && other.Home.Y <= value.maxY));
                    GridShape? placement = Valid(preferred, reserve: false) ? preferred : null;
                    if (placement == null)
                    {
                        var grid = target.inventoryShape;
                        for (var y = grid.minY; y <= grid.maxY && placement == null; y++)
                            for (var x = grid.minX; x <= grid.maxX && placement == null; x++)
                            {
                                var proposed = new GridShapeBuilder(shape).SetTransform(x, y, origin.Flipped, origin.Orientation).Build();
                                if (Valid(proposed)) placement = proposed;
                            }
                    }
                    if (placement == null) return null;
                    entry.Marker = new SlotMarker { item = root, inventory = target, itemGridShape = placement, numTransfer = 1 };
                }
                else
                {
                    var copy = WorkroomStorageState.Copy(record, origin?.Place == "box" ? "box" : "room", "");
                    if (origin?.Place == "box") next.Add(copy);
                    else
                    {
                        copy.Item = WorkroomItemCodec.Reorient(copy.Item, origin?.Orientation ?? copy.Item.Orientation);
                        if (origin?.Place != "room" || !WorkroomStorageState.PlaceAt(next, copy, origin.X, origin.Y))
                            deferredRoom.Add(copy);
                    }
                    plan.Managed.Add(copy);
                }
            }
            if (!WorkroomStorageState.PlaceAll(next, deferredRoom)) return null;
            success = true; return plan;
        }
        finally { if (!success) plan.Dispose(); }
    }
    private static bool Overlap(GridShape a, GridShape b)
    {
        // Bounding rectangles are conservative for holes; no native occupancy
        // is changed merely to reserve the rest of this return batch.
        return a.minX <= b.maxX && b.minX <= a.maxX && a.minY <= b.maxY && b.minY <= a.maxY;
    }
    private static bool CommitReturns(ReturnPlan plan, long epoch)
    {
        if (!WorkroomGameAdapter.InventoryTransferAllowed()) return false;
        if (plan.Native.Count == 0)
        {
            if (State.Epoch != epoch || plan.Managed.Any(p => State.Find(p.Id)?.Place != "slot")) return false;
            var ids = plan.Managed.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            State.Replace(State.Items.Where(p => !ids.Contains(p.Id)).Concat(plan.Managed).ToList());
            return true;
        }
        var all = plan.Native.Select(p => p.Record).Concat(plan.Managed.Select(p => State.Find(p.Id)!))
            .OrderBy(p => p.Slot.EndsWith(".body", StringComparison.Ordinal) ? 1 : 0).ToArray();
        foreach (var record in all)
        {
            if (!WorkroomGameAdapter.InventoryTransferAllowed() || State.Epoch != epoch || !ReferenceEquals(State.Find(record.Id), record)) return false;
            var native = plan.Native.FirstOrDefault(p => p.Record.Id == record.Id);
            if (native != null)
            { if (!WorkroomStorage.DeliverTo(native.Record, native.Candidate, native.Marker)) return false; }
            else
            {
                var replacement = plan.Managed.Single(p => p.Id == record.Id);
                State.Replace(State.Items.Select(p => p.Id == record.Id ? replacement : p).ToList());
            }
        }
        return true;
    }
    internal static bool ReturnSlots(IEnumerable<string> slots, long epoch)
    {
        if (!WorkroomGameAdapter.InventoryTransferAllowed() || !WorkroomComponentAssembly.Ready(epoch)) return false;
        var requested = Expanded(slots);
        var selected = State.Items.Where(r => r.Place == "slot" && requested.Contains(r.Slot)).ToArray();
        if (selected.Length == 0) return true;
        var result = false;
        WorkroomStorage.Run(() =>
        {
            using var plan = PlanReturns(selected);
            if (plan == null) { Notice("return_full"); return; }
            result = CommitReturns(plan, epoch);
            Notice(result ? "returned" : "unavailable");
        });
        return result;
    }

    // Called under WorkroomStorage.Run by selection and scene-mat commands.
    // Keep one return planner and one partial-native-return custody protocol.
    internal static bool StageWithReturns(WorkroomStorageState.Record record, string target,
        IReadOnlyList<WorkroomStorageState.Record> selected, long epoch, int? orientation,
        WorkroomStorageState.Origin returnTo, out string failure, Func<bool>? current = null)
    {
        failure = "changed";
        var revision = State.Revision;
        bool Fresh() => WorkroomGameAdapter.InventoryTransferAllowed() && State.Ready && State.Epoch == epoch &&
            State.Revision == revision && ReferenceEquals(State.Find(record.Id), record) &&
            record.Place is "room" or "box" && selected.All(p => p.Place == "slot" && ReferenceEquals(State.Find(p.Id), p)) &&
            (current == null || current());
        if (!Fresh()) return false;
        // Reorientation can reject malformed geometry. Do that before native
        // return callbacks or even the incoming box-custody publication.
        if (orientation is { } turn) WorkroomItemCodec.Reorient(record.Item, turn);
        using var plan = PlanReturns(selected, record.Id);
        if (plan == null) { failure = "return_full"; return false; }
        if (!Fresh()) return false;
        var returning = selected.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        if (!WorkroomComponentAssembly.Accepts(target, record.Item,
            slot => State.Items.FirstOrDefault(r => r.Place == "slot" && r.Slot == slot && !returning.Contains(r.Id))))
        { failure = "incompatible"; return false; }
        if (plan.Native.Count == 0)
        {
            var atomic = State.Items.Where(r => !returning.Contains(r.Id)).ToList();
            atomic.AddRange(plan.Managed);
            var slots = atomic.Where(r => r.Place == "slot").ToDictionary(r => r.Slot, StringComparer.Ordinal);
            WorkroomComponentAssembly.FillSlot(atomic, slots, record, target, orientation, returnTo);
            if (!Fresh()) return false;
            State.Replace(atomic); failure = ""; return true;
        }
        // Free the incoming footprint before native callbacks. If one fails,
        // this box record and each remaining slot keep their unique custody.
        var next = State.Items.ToList();
        var held = WorkroomStorageState.Copy(record, "box", "");
        held.ReturnTo = returnTo;
        next[next.FindIndex(r => r.Id == record.Id)] = held;
        State.Replace(next);
        failure = "unavailable";
        if (!CommitReturns(plan, epoch)) return false;
        if (!WorkroomGameAdapter.InventoryTransferAllowed() || State.Epoch != epoch ||
            !ReferenceEquals(State.Find(record.Id), held) || held.Place != "box" ||
            !WorkroomComponentAssembly.Accepts(target, held.Item, State.FindSlot)) return false;
        next = State.Items.ToList();
        var staged = next.Where(r => r.Place == "slot").ToDictionary(r => r.Slot, StringComparer.Ordinal);
        WorkroomComponentAssembly.FillSlot(next, staged, held, target, orientation, held.ReturnTo);
        State.Replace(next); failure = ""; return true;
    }
    internal static bool Select(Candidate candidate, string slot, long epoch, bool repair = false, int? orientation = null)
    {
        if (!WorkroomGameAdapter.InventoryTransferAllowed() || !WorkroomComponentAssembly.Ready(epoch) || epoch != candidate.Epoch) return false;
        var reason = Reason(slot, candidate, repair);
        if (reason.Length != 0) { WorkroomStorage.NotifyText(reason); return false; }
        // Native acquisition uses the existing cleanup owner. A later native
        // return failure keeps the incoming object safely in its box record.
        var id = candidate.Id;
        if (candidate.Origin == Source.Shop)
        {
            var verified = false;
            WorkroomStorage.Run(() =>
            {
                if (!Current(candidate) || !WorkroomItemCodec.Equivalent(candidate.Item, WorkroomItemCodec.Capture(candidate.Native!)))
                { Notice("changed"); return; }
                var selected = State.Items.Where(r => r.Place == "slot" && Replaced(slot, candidate.Item).Contains(r.Slot)).ToArray();
                using var preflight = PlanReturns(selected);
                if (preflight == null) { Notice("return_full"); return; }
                verified = true;
            });
            if (!verified || WorkroomItemCodec.CleanupPending) return false;
            if (!WorkroomStorage.StoreSelection(candidate.Native!, out id)) return false;
        }
        var result = false;
        WorkroomStorage.Run(() =>
        {
            var record = State.Find(id);
            if (!WorkroomGameAdapter.InventoryTransferAllowed() || record == null || State.Epoch != epoch || record.Place is not ("room" or "box") || !Suitable(slot, record.Item, true, repair)) return;
            var selected = State.Items.Where(r => r.Place == "slot" && Replaced(slot, record.Item).Contains(r.Slot)).ToArray();
            result = StageWithReturns(record, Target(slot, record.Item), selected, epoch, orientation,
                candidate.Origin == Source.Shop ? record.ReturnTo! : Remember(record), out var failure);
            Notice(result ? "selected" : failure);
        });
        return result;
    }
    internal static bool TransferToRoom(GameItem item, long epoch)
    {
        if (!WorkroomComponentAssembly.Ready(epoch) || !CanTransfer(item)) return false;
        try
        {
            var snapshot = WorkroomItemCodec.Capture(item);
            if (!State.FindSpace(new WorkroomStorageState.Record { Item = snapshot }, out _, out _)) { Notice("room_full"); return false; }
            if (!WorkroomStorage.StoreSelection(item, out var id)) return false;
            var result = false;
            WorkroomStorage.Run(() =>
            {
                var record = State.Find(id);
                if (!WorkroomGameAdapter.InventoryTransferAllowed() || State.Epoch != epoch || record?.Place != "box") return;
                if (!State.FindSpace(record, out var x, out var y)) { Notice("room_full"); return; }
                State.Move(id, "room", x, y); result = true; Notice("transferred");
            });
            return result;
        }
        catch (Exception ex) { Core.Log?.Warning("工作间快捷转移保留来源：" + ex.Message); Notice("unavailable"); return false; }
    }

    internal static bool ReturnToShop(string id, long epoch)
    {
        if (!WorkroomGameAdapter.InventoryTransferAllowed() || !WorkroomComponentAssembly.Ready(epoch)) return false;
        var record = State.Find(id);
        if (record == null || record.Place is not ("room" or "box")) return false;
        var result = false;
        WorkroomStorage.Run(() =>
        {
            if (State.Epoch != epoch || !ReferenceEquals(State.Find(id), record)) return;
            var candidate = WorkroomItemCodec.Restore(record.Item);
            try
            {
                var root = candidate.Root;
                SlotMarker? marker = null;
                var home = record.ReturnTo;
                if (home?.Place == "shop" && Inventory(home.Inventory) is { } target &&
                    !target.IsInsertLocked() && !target.IsRemoveLocked() && GeneralHelper.MayPlayerInsertInto(target))
                {
                    var shape = new GridShapeBuilder(root.modifiedShape ?? root.shape)
                        .SetTransform(home.X, home.Y, home.Flipped, home.Orientation).Build();
                    if (GeneralHelper.MayOwnershipAcceptItem(root, target) &&
                        target.CheckInventorySlotStillValid(root, shape, root.modifiedState))
                        marker = new SlotMarker { item = root, inventory = target, itemGridShape = shape, numTransfer = 1 };
                }
                // A vanished/occupied home falls back only to the player's main grid.
                var main = EmporiumEntry.Instance?.invElement;
                if (marker == null && main != null && main.GetParentItem() == null &&
                    !main.IsInsertLocked() && !main.IsRemoveLocked() && GeneralHelper.MayPlayerInsertInto(main))
                {
                    var found = main.TryFindOneValidInventorySlot(root, true);
                    if (found != null && found.inventory != null && found.inventory.Pointer == main.Pointer &&
                        found.targetItem == null && found.numTransfer == 1 && found.IsValid() &&
                        GeneralHelper.MayOwnershipAcceptItem(root, main) &&
                        main.CheckInventorySlotStillValid(root, found.itemGridShape, root.modifiedState)) marker = found;
                }
                if (marker == null) { Notice("return_full"); return; }
                result = WorkroomStorage.DeliverTo(record, candidate, marker);
                Notice(result ? "returned" : "unavailable");
            }
            finally
            {
                if (!candidate.Cleaned && candidate.Root.parentInventory == null) WorkroomItemCodec.Destroy(candidate);
            }
        });
        return result;
    }
}
