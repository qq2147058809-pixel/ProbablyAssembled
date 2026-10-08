using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Il2Cpp;

namespace PCExpansion;

/// <summary>各类会话共享唯一保管模型，所有批量动作预计算后一次提交。</summary>
internal static class WorkroomComponentAssembly
{
    internal const string Disassembly = "entry.disassembly", EntryBoard = "entry.body";
    internal static string Slot(WorkroomComponentTemplates.Template template, string kind, int index = 0) =>
        template.Key + "." + kind + (template.MaxCount(kind) > 1 ? "." + index : "");
    internal static WorkroomStorageState.Record? At(string slot) => WorkroomStorage.State.FindSlot(slot);
    // Retain the existing caller API as a session-presence flag; a body has no tier.
    internal static int BoardTier(string family) => At(family + ".body") is { } body &&
        WorkroomComponentTemplates.TryPart(body.Item.Identifier, out var template, out var kind, out _) && template.Key == family && kind == "body" ? 1 : 0;
    internal static int SlotCount(WorkroomComponentTemplates.Template template, string kind)
    {
        return BoardTier(template.Key) > 0 ? template.MaxCount(kind) : kind == "body" ? 1 : 0;
    }
    internal static IEnumerable<string> Slots(WorkroomComponentTemplates.Template template, bool visibleOnly = false)
    {
        foreach (var kind in template.Kinds)
            for (var i = 0; i < (visibleOnly ? SlotCount(template, kind) : template.MaxCount(kind)); i++)
                yield return Slot(template, kind, i);
    }
    internal static bool ValidSlot(string slot) => WorkroomMachineAssembly.ValidSlot(slot) || slot == Disassembly || slot == EntryBoard || WorkroomComponentTemplates.All.Any(template => Slots(template).Contains(slot));
    internal static bool Ready(long epoch) => WorkroomStorage.State.Ready && WorkroomStorage.State.Epoch == epoch && !WorkroomStorage.Busy && !WorkroomItemCodec.CleanupPending;
    internal static void Notice(string key, params object[] args) => WorkroomStorage.NotifyText(WorkroomComponentTemplates.Text(key, args));
    private static bool ProcessingItem(WorkroomItemCodec.Snapshot item) => WorkroomItemCodec.CanUseOne(item) &&
        (WorkroomComponentTemplates.TryWhole(item.Identifier, out _, out _, out _) || WorkroomComponentTemplates.TryPart(item.Identifier, out _, out _, out _) ||
            WorkroomRecyclingRules.WholeTarget(item));
    internal static bool Accepts(string slot, WorkroomItemCodec.Snapshot item)
        => slot == EntryBoard ? CanRouteBody(item) : Accepts(slot, item, WorkroomStorage.State.FindSlot);
    internal static bool Accepts(string slot, WorkroomItemCodec.Snapshot item, Func<string, WorkroomStorageState.Record?> find)
    {
        if (WorkroomMachineAssembly.ValidSlot(slot)) return WorkroomMachineAssembly.Accepts(slot, item, find);
        if (!ValidSlot(slot) || find(slot) != null || !WorkroomItemCodec.CanUseOne(item)) return false;
        if (slot == Disassembly) return ProcessingItem(item);
        if (!WorkroomComponentTemplates.TryPart(item.Identifier, out var family, out var kind, out _) || item.Nodes.Count != 1 || item.Nodes[0].SlotItems.Count != 0 || item.Nodes[0].Composition != null) return false;
        if (slot == EntryBoard) return kind == "body";
        if (slot == Slot(family, "body")) return kind == "body" && !Slots(family).Any(s => s != slot && find(s) != null);
        var body = find(Slot(family, "body"));
        return body != null && WorkroomComponentTemplates.TryPart(body.Item.Identifier, out var bf, out var bk, out _) && bf == family && bk == "body" &&
            Enumerable.Range(0, family.MaxCount(kind)).Any(i => slot == Slot(family, kind, i));
    }

    private static HashSet<string> AssemblyModeSlots() => WorkroomComponentTemplates.All
        .SelectMany(template => Slots(template)).Append(EntryBoard).ToHashSet(StringComparer.Ordinal);

    private static bool BodyFamily(WorkroomItemCodec.Snapshot item, out WorkroomComponentTemplates.Template family)
    {
        family = null!;
        if (!WorkroomItemCodec.CanUseOne(item) ||
            !WorkroomComponentTemplates.TryPart(item.Identifier, out family, out var kind, out _) || kind != "body" ||
            item.Nodes == null || item.Nodes.Count != 1 || item.Nodes[0] == null ||
            item.Nodes[0].SlotItems == null || item.Nodes[0].SlotItems.Count != 0 ||
            item.Nodes[0].Composition != null || item.Nodes[0].Machine != null) return false;
        WorkroomItemCodec.Validate(item);
        return true;
    }

    // Build detached replacement records only. Neither preflight nor a failed
    // placement changes the live slot records, incoming footprint or snapshots.
    private static bool PrepareBodyRoute(WorkroomStorageState.Record incoming, int? orientation,
        out List<WorkroomStorageState.Record> next, out string failure)
    {
        next = new(); failure = "wrong_slot";
        if (incoming.Place != "room" || !BodyFamily(incoming.Item, out var family)) return false;
        var target = Slot(family, "body");
        if (At(target) != null) return false;
        var modeSlots = AssemblyModeSlots();
        var returning = WorkroomStorage.State.Items.Where(record => record.Place == "slot" && modeSlots.Contains(record.Slot)).ToList();
        var ids = returning.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        next = WorkroomStorage.State.Items.Where(record => !ids.Contains(record.Id)).ToList();
        var staged = next.Where(record => record.Place == "slot").ToDictionary(record => record.Slot, StringComparer.Ordinal);
        WorkroomStorageState.Record? Find(string slot) => staged.TryGetValue(slot, out var record) ? record : null;
        if (!Accepts(target, incoming.Item, Find)) return false;
        // Free the incoming room footprint before arranging all old components.
        FillSlot(next, staged, incoming, target, orientation);
        if (!WorkroomStorageState.PlaceAll(next, returning.Select(record =>
            new WorkroomStorageState.Record { Id = record.Id, Item = record.Item })))
        { failure = "full"; return false; }
        return true;
    }

    internal static bool CanRouteBody(WorkroomItemCodec.Snapshot item)
    {
        var epoch = WorkroomStorage.State.Epoch;
        if (!Ready(epoch)) return false;
        var revision = WorkroomStorage.State.Revision;
        try
        {
            if (!BodyFamily(item, out _)) return false;
            // Drag previews may carry a reoriented copy, so match custody by the
            // unique root identity and model rather than Snapshot references.
            var sources = WorkroomStorage.State.Items.Where(record => record.Place == "room" &&
                record.Item.Identifier == item.Identifier && record.Item.Nodes.Count == 1 &&
                record.Item.Nodes[0].UniqueId == item.Nodes[0].UniqueId).Take(2).ToArray();
            if (sources.Length != 1 || !PrepareBodyRoute(sources[0], null, out _, out _)) return false;
            Current(epoch, revision);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is JsonException || ex is ArgumentException)
        { return false; }
    }

    // Read-only ranking; quantities do not multiply a unit's capacity or tier.
    internal static bool Intact(WorkroomItemCodec.Snapshot item) => !item.Broken && Components.Find(item.Identifier)?.Broken != true &&
        item.Nodes.All(node => (node.Composition == null || node.Composition.All(Intact)) &&
            (node.Machine == null || Intact(node.Machine.Body) && node.Machine.Parts.All(part => Intact(part.Item))));
    internal static int Configuration(WorkroomItemCodec.Snapshot item) => item.Nodes[0].Machine is { } machine ?
        WorkroomMachineAssembly.ConfigurationTier(machine) : item.Nodes[0].Composition is { } parts ?
        WorkroomComponentParts.EffectiveTier(parts) : WorkroomComponentTemplates.TryPart(item.Identifier, out var family, out var kind, out var tier) ?
        (family.PerformanceKind(kind) ? tier : 0) : Components.Find(item.Identifier)?.Tier ?? 0;
    internal static long Capacity(WorkroomItemCodec.Snapshot item)
    {
        long capacity = item.Nodes.Sum((WorkroomItemCodec.Node node) => (long)(node.Part?.CapacityGB ?? 0) +
            (node.Composition?.Sum(Capacity) ?? 0L) +
            (node.Machine?.Parts.Sum((WorkroomMachineAssembly.Installed part) => Capacity(part.Item)) ?? 0L));
        // Untouched whole items have no explicit composition until first split.
        if (item.Nodes[0].Composition == null && item.Nodes[0].Machine == null &&
            WorkroomComponentTemplates.TryWhole(item.Identifier, out var family, out var tier, out _))
            capacity = family.Kinds.Sum((string kind) => (long)family.DefaultCount(kind, tier) * WorkroomComponentTemplates.DefaultPart(family, kind, tier).CapacityGB);
        return capacity;
    }
    internal static WorkroomStorageState.Record? Best(IEnumerable<WorkroomStorageState.Record> records, Func<WorkroomStorageState.Record, bool> compatible) =>
        records.Where(r => (r.Place == "room" || r.Place == "box") && Intact(r.Item) && compatible(r))
            .OrderBy(r => r.Place == "room" ? 0 : 1).ThenByDescending(r => Configuration(r.Item))
            .ThenByDescending(r => Capacity(r.Item)).FirstOrDefault();
    internal static void FillSlot(List<WorkroomStorageState.Record> next, Dictionary<string, WorkroomStorageState.Record> staged,
        WorkroomStorageState.Record record, string slot, int? orientation = null)
    {
        if (record.Item.Count != 1) throw new InvalidOperationException("装配只支持独立单件。");
        var item = record.Item;
        if (orientation is { } turn) item = WorkroomItemCodec.Reorient(item, turn);
        var replacement = new WorkroomStorageState.Record
            { Id = record.Id, Place = "slot", Slot = slot, Item = item };
        var index = next.FindIndex(r => r.Id == record.Id);
        if (index < 0 || !ReferenceEquals(next[index], record)) throw new InvalidOperationException("拆件来源记录已变化。");
        next[index] = replacement;
        staged[slot] = replacement;
    }
    internal static void FillNotice(int filled, IEnumerable<string> missing)
    {
        var names = missing.ToArray();
        Notice(names.Length == 0 ? "auto_fill_complete" : "auto_fill_result", filled, string.Join("、", names));
    }
    internal static void AutoFill(string familyKey, long epoch)
    {
        if (!Ready(epoch)) return;
        var family = WorkroomComponentTemplates.All.FirstOrDefault(t => t.Key == familyKey); if (family == null) return;
        var revision = WorkroomStorage.State.Revision;
        WorkroomStorage.Run(() =>
        {
            var next = new List<WorkroomStorageState.Record>(WorkroomStorage.State.Items); var filled = 0;
            var staged = next.Where(r => r.Place == "slot").ToDictionary(r => r.Slot, StringComparer.Ordinal);
            var boardSlot = Slot(family, "body");
            WorkroomStorageState.Record? Find(string name) => staged.TryGetValue(name, out var value) ? value : null;
            if (Find(boardSlot) == null)
            {
                var board = Best(next, r => Accepts(boardSlot, r.Item, Find));
                if (board != null) { FillSlot(next, staged, board, boardSlot); filled++; }
            }
            var missing = new List<string>();
            foreach (var kind in family.Kinds)
            for (var i = 0; i < (Find(boardSlot) == null ? kind == "body" ? 1 : 0 : family.MaxCount(kind)); i++)
            {
                var slot = Slot(family, kind, i); if (Find(slot) != null) continue;
                var candidate = Best(next, r => Accepts(slot, r.Item, Find));
                if (candidate != null) { FillSlot(next, staged, candidate, slot); filled++; }
            }
            foreach (var kind in family.Kinds)
                if (Enumerable.Range(0, family.MaxCount(kind)).Count(i => Find(Slot(family, kind, i)) != null) < family.MinCount(kind))
                    missing.Add(family.PartName(kind));
            Current(epoch, revision); if (filled > 0) WorkroomStorage.State.Replace(next);
            FillNotice(filled, missing);
        });
    }
    internal static void ValidateSlots(List<WorkroomStorageState.Record> records)
    {
        WorkroomMachineAssembly.ValidateSlots(records);
        var staged = records.Where(record => record.Place == "slot" && !WorkroomMachineAssembly.ValidSlot(record.Slot)).ToList();
        foreach (var record in staged)
        {
            if (!ValidSlot(record.Slot) || record.Item.Count != 1) throw new InvalidOperationException("装配槽记录无效。");
            if (record.Slot == Disassembly)
            { if (!ProcessingItem(record.Item)) throw new InvalidOperationException("处理槽类别无效。"); continue; }
            if (!WorkroomComponentTemplates.TryPart(record.Item.Identifier, out var family, out var kind, out _) ||
                record.Item.Nodes.Count != 1 || record.Item.Nodes[0].SlotItems.Count != 0 || record.Item.Nodes[0].Composition != null)
                throw new InvalidOperationException("装配槽只能保存叶子子件。");
            if (record.Slot == EntryBoard) { if (kind != "body") throw new InvalidOperationException("入口只能保存通用机体。"); continue; }
            var body = staged.FirstOrDefault(value => value.Slot == Slot(family, "body"));
            if (body == null || !WorkroomComponentTemplates.TryPart(body.Item.Identifier, out var bt, out var bk, out _) || bt != family || bk != "body" ||
                !Enumerable.Range(0, family.MaxCount(kind)).Any(i => record.Slot == Slot(family, kind, i)))
                throw new InvalidOperationException("部件类别或槽位数量与通用机体不符。");
        }
    }
    internal static bool Stage(string id, string slot, long epoch, int orientation)
    {
        if (WorkroomMachineAssembly.ValidSlot(slot)) return WorkroomMachineAssembly.Stage(id, slot, epoch, orientation);
        var record = WorkroomStorage.State.Find(id); if (!Ready(epoch) || record?.Place != "room") return false;
        if (slot == EntryBoard)
        {
            var routed = false; var routeRevision = WorkroomStorage.State.Revision;
            WorkroomStorage.Run(() =>
            {
                if (!PrepareBodyRoute(record, orientation, out var next, out var failure)) { Notice(failure); return; }
                Current(epoch, routeRevision); WorkroomStorage.State.Replace(next); routed = true; Notice("placed");
            });
            return routed;
        }
        if (!Accepts(slot, record.Item)) { Notice("wrong_slot"); return false; }
        var moved = false; var revision = WorkroomStorage.State.Revision;
        WorkroomStorage.Run(() =>
        {
            var next = new List<WorkroomStorageState.Record>(WorkroomStorage.State.Items);
            var staged = next.Where(r => r.Place == "slot").ToDictionary(r => r.Slot, StringComparer.Ordinal);
            FillSlot(next, staged, record, slot, orientation);
            Current(epoch, revision); WorkroomStorage.State.Replace(next); moved = true; Notice("placed");
        });
        return moved;
    }
    internal static bool ReturnModeSlots(bool assemblyMode, long epoch)
    {
        if (!Ready(epoch)) return false;
        var requested = assemblyMode ? AssemblyModeSlots() : new HashSet<string>(StringComparer.Ordinal) { Disassembly };
        var selected = WorkroomStorage.State.Items.Where(record => record.Place == "slot" && requested.Contains(record.Slot)).ToList();
        var revision = WorkroomStorage.State.Revision;
        if (selected.Count == 0) return Ready(epoch) && revision == WorkroomStorage.State.Revision;
        var returned = false;
        WorkroomStorage.Run(() =>
        {
            var ids = selected.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
            var next = WorkroomStorage.State.Items.Where(record => !ids.Contains(record.Id)).ToList();
            if (!WorkroomStorageState.PlaceAll(next, selected.Select(record =>
                new WorkroomStorageState.Record { Id = record.Id, Item = record.Item }))) { Notice("full"); return; }
            Current(epoch, revision); WorkroomStorage.State.Replace(next); returned = true; Notice("returned");
        });
        return returned;
    }
    internal static void ReturnSlots(IEnumerable<string> slots, long epoch)
    {
        var requestedSlots = slots.ToArray();
        if (requestedSlots.Any(WorkroomMachineAssembly.ValidSlot))
        { WorkroomMachineAssembly.ReturnSlots(requestedSlots, epoch); return; }
        if (!Ready(epoch)) return;
        var requested = requestedSlots.ToHashSet();
        foreach (var family in WorkroomComponentTemplates.All) if (requested.Contains(Slot(family, "body"))) requested.UnionWith(Slots(family));
        var selected = requested.Select(At).Where(record => record != null).Select(record => record!).ToList();
        if (selected.Count == 0) return;
        WorkroomStorage.Run(() =>
        {
            var ids = selected.Select(record => record.Id).ToHashSet();
            var next = WorkroomStorage.State.Items.Where(record => !ids.Contains(record.Id)).ToList();
            if (!WorkroomStorageState.PlaceAll(next, selected.Select(record => new WorkroomStorageState.Record { Id = record.Id, Item = record.Item }))) { Notice("full"); return; }
            WorkroomStorage.State.Replace(next); Notice("returned");
        });
    }
    internal sealed class PlannedPart
    {
        internal string Kind = "";
        internal int Tier;
        internal WorkroomComponentTemplates.PartDefinition Definition = null!;
        internal long Intact;
        internal bool Broken;
    }
    private struct FixedRandom
    {
        private uint state;
        internal FixedRandom(uint seed) { state = seed == 0 ? 1 : seed; }
        internal uint Next(uint maximum) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state % maximum; }
    }
    private static List<PlannedPart> Plan(WorkroomItemCodec.Snapshot source, out WorkroomComponentTemplates.Template family)
    {
        if (!WorkroomComponentTemplates.TryWhole(source.Identifier, out family, out var tier, out _) || source.Count != 1 ||
            source.Nodes.Count != 1 || source.Nodes[0].SlotItems.Count != 0)
            throw new InvalidOperationException("整件不适用于拆件。");
        if (source.Value < 0) throw new InvalidOperationException("成品价值无效。");
        var budget = source.Value;
        if (source.Broken)
        {
            var standardResidual = Components.Find(WorkroomComponentTemplates.WholeId(family, tier, true))!.Value;
            budget = checked((long)((decimal)WorkroomComponentTemplates.BaseValue(family, tier) * source.Value / standardResidual));
        }
        var result = new List<PlannedPart>();
        foreach (var kind in family.Kinds) for (var i = 0; i < family.DefaultCount(kind, tier); i++)
            result.Add(new PlannedPart { Kind = kind, Tier = tier, Definition = WorkroomComponentTemplates.DefaultPart(family, kind, tier),
                Intact = WorkroomComponentTemplates.PartValue(family, kind, tier, budget) });
        var remainder = checked(budget - result.Sum(part => part.Intact));
        if (remainder < 0) throw new InvalidOperationException("拆件分价超过来源完好预算。");
        var core = result.First(part => part.Definition.Family.Key == "ram" ? part.Kind == "memory" : part.Definition.Family.PerformanceKind(part.Kind));
        core.Intact = checked(core.Intact + remainder);
        if (!source.Broken) return result;
        var random = new FixedRandom(source.Nodes[0].DamageSeed ?? WorkroomComponentParts.StableSeed(family.Key, source.Nodes[0].UniqueId));
        var severity = random.Next(100); var fraction = severity < 50 ? 25 : severity < 85 ? 50 : 75;
        var count = Math.Clamp((result.Count * fraction + 99) / 100, 1, result.Count);
        var pool = Enumerable.Range(0, result.Count).ToList();
        var damageFamily = family;
        for (var i = 0; i < count; i++)
        {
            var cursor = (int)random.Next((uint)pool.Sum(index => damageFamily.DamageWeight(result[index].Kind)));
            var picked = pool[0];
            foreach (var index in pool) { cursor -= family.DamageWeight(result[index].Kind); if (cursor < 0) { picked = index; break; } }
            result[picked].Broken = true; pool.Remove(picked);
        }
        return result;
    }
    internal static string DisassemblyPreview(WorkroomItemCodec.Snapshot source)
    {
        var existing = source.Nodes[0].Composition;
        if (existing != null)
        {
            WorkroomComponentParts.ValidateComposition(existing);
            return WorkroomComponentTemplates.Text("split_header", existing.Count, existing.Sum(part => part.Value)) + "\n" + string.Join("\n", existing.Select(part =>
                WorkroomComponentTemplates.Text("split_row", part.Name, part.Broken ? WorkroomComponentTemplates.Text("broken") : WorkroomComponentTemplates.Text("intact"), part.Value)));
        }
        var plan = Plan(source, out var family);
        return WorkroomComponentTemplates.Text("split_header", plan.Count, plan.Sum(part => part.Broken ? WorkroomComponentParts.BrokenValue(part.Intact) : part.Intact)) + "\n" +
            string.Join("\n", plan.Select(part => WorkroomComponentTemplates.Text("split_row", WorkroomComponentParts.PartName(part.Definition),
                WorkroomComponentTemplates.Text(part.Broken ? "broken" : "intact"), part.Broken ? WorkroomComponentParts.BrokenValue(part.Intact) : part.Intact)));
    }
    private static void Current(long epoch, long revision)
    {
        if (!WorkroomStorage.State.Ready || epoch != WorkroomStorage.State.Epoch || revision != WorkroomStorage.State.Revision || WorkroomItemCodec.CleanupPending)
            throw new InvalidOperationException("工作间上下文已变化或临时物品尚未清理。");
    }
    internal static void Disassemble(long epoch)
    {
        if (!Ready(epoch)) return;
        var source = At(Disassembly); if (source == null) { Notice("processing_need_target"); return; }
        if (!WorkroomComponentTemplates.TryWhole(source.Item.Identifier, out _, out _, out _)) { Notice("processing_need_whole"); return; }
        var revision = WorkroomStorage.State.Revision;
        WorkroomStorage.Run(() =>
        {
            var parts = source.Item.Nodes[0].Composition;
            if (parts == null)
            {
                var plan = Plan(source.Item, out var family);
                // Check all footprint capacity before allocating candidate entities.
                var shapes = plan.Select(part => new WorkroomStorageState.Record { Item = new WorkroomItemCodec.Snapshot { Footprint = Shape(family, part.Kind) } }).ToList();
                var trial = WorkroomStorage.State.Items.Where(record => record.Id != source.Id).ToList();
                if (!WorkroomStorageState.PlaceAll(trial, shapes)) { Notice("full"); return; }
                parts = new List<WorkroomItemCodec.Snapshot>();
                foreach (var part in plan)
                    parts.Add(WorkroomComponentParts.CaptureFactory(() =>
                    {
                        var native = WorkroomComponentParts.Create(part.Definition, part.Broken, part.Intact);
                        try
                        {
                        if (part.Kind == "body")
                        {
                            native.EnableTag(WorkroomComponentParts.OriginTag, false);
                            native.state.GetTag(WorkroomComponentParts.OriginTag).SetString(JsonSerializer.Serialize(source.Item, WorkroomStorageState.JsonOptions));
                        }
                        return native;
                        }
                        catch { WorkroomItemCodec.Destroy(new WorkroomItemCodec.Candidate { Root = native, Nodes = new List<GameItem> { native } }); throw; }
                    }));
            }
            WorkroomComponentParts.ValidateComposition(parts);
            var next = WorkroomStorage.State.Items.Where(record => record.Id != source.Id).ToList();
            if (!WorkroomStorageState.PlaceAll(next, parts.Select(part => new WorkroomStorageState.Record { Item = part }))) { Notice("full"); return; }
            Current(epoch, revision); WorkroomStorage.State.Replace(next); Notice("disassembled");
        });
    }
    private static WorkroomItemCodec.Shape Shape(WorkroomComponentTemplates.Template family, string kind)
    {
        var width = kind == "body" ? family.BoardWidth : 1;
        var height = kind == "body" ? family.BoardHeight : 1;
        return new WorkroomItemCodec.Shape { Width = width, Height = height, Cells = Enumerable.Repeat((byte)1, width * height).ToArray() };
    }
    internal static bool CanAssemble(WorkroomComponentTemplates.Template family) => BoardTier(family.Key) > 0 && family.Kinds.All(kind =>
        Enumerable.Range(0, family.MaxCount(kind)).Count(i => At(Slot(family, kind, i)) != null) >= family.MinCount(kind));
    internal static void Assemble(string familyKey, long epoch)
    {
        if (!Ready(epoch)) return;
        var family = WorkroomComponentTemplates.All.FirstOrDefault(t => t.Key == familyKey);
        if (family == null || BoardTier(familyKey) == 0) { Notice("need_body"); return; }
        if (!CanAssemble(family)) { Notice("missing"); return; }
        var selected = Slots(family, true).Select(At).Where(record => record != null).Select(record => record!).ToList();
        var revision = WorkroomStorage.State.Revision;
        WorkroomStorage.Run(() =>
        {
            var parts = selected.Select(record => record!.Item).ToList(); WorkroomComponentParts.ValidateComposition(parts);
            var identifier = WorkroomComponentTemplates.WholeId(family, WorkroomComponentParts.EffectiveTier(parts), parts.Any(part => part.Broken));
            var ids = selected.Select(record => record!.Id).ToHashSet();
            var trial = WorkroomStorage.State.Items.Where(record => !ids.Contains(record.Id)).ToList();
            var type = Components.Find(identifier)!.Owner;
            var fake = new WorkroomStorageState.Record { Item = new WorkroomItemCodec.Snapshot { Footprint = new WorkroomItemCodec.Shape {
                Width = type.Width, Height = type.Height, Cells = Enumerable.Repeat((byte)1, type.Width * type.Height).ToArray() } } };
            if (!WorkroomStorageState.PlaceAll(trial, new[] { fake })) { Notice("full"); return; }
            var board = WorkroomItemCodec.Restore(parts.First(part => WorkroomComponentTemplates.TryPart(part.Identifier, out _, out var kind, out _) && kind == "body"));
            WorkroomItemCodec.Candidate? candidate = null; WorkroomItemCodec.Snapshot snapshot;
            try
            {
                if (board.Root.IsTag(WorkroomComponentParts.OriginTag))
                {
                    var origin = JsonSerializer.Deserialize<WorkroomItemCodec.Snapshot>(board.Root.state.GetTag(WorkroomComponentParts.OriginTag).valueString,
                        WorkroomStorageState.JsonOptions) ?? throw new InvalidOperationException("来源成品快照缺失。");
                    if (!WorkroomComponentTemplates.TryWhole(origin.Identifier, out var originFamily, out _, out _) || originFamily != family)
                        throw new InvalidOperationException("来源类别与通用机体不符。");
                    candidate = WorkroomItemCodec.Restore(origin);
                    // 来源成品的组成将在下方由实际安装部件替换。
                    candidate.Root.DisableTag(WorkroomComponentParts.CompositionTag, false);
                    Components.ApplySpec(candidate.Root, identifier);
                }
                else { var native = Components.Create(identifier); candidate = new WorkroomItemCodec.Candidate { Root = native, Nodes = new List<GameItem> { native } }; }
                var result = candidate.Root;
                result.EnableTag(WorkroomComponentParts.CompositionTag, false);
                result.state.GetTag(WorkroomComponentParts.CompositionTag).SetString(WorkroomAssemblyData.Encode(parts));
                WorkroomComponentParts.EnsureSeed(result, familyKey); WorkroomComponentParts.RefreshComposition(result);
                snapshot = WorkroomItemCodec.Capture(result);
            }
            finally { try { if (candidate != null) WorkroomItemCodec.Destroy(candidate); } finally { WorkroomItemCodec.Destroy(board); } }
            Current(epoch, revision);
            var next = WorkroomStorage.State.Items.Where(record => !ids.Contains(record.Id)).ToList();
            if (!WorkroomStorageState.PlaceAll(next, new[] { new WorkroomStorageState.Record { Item = snapshot } })) { Notice("full"); return; }
            WorkroomStorage.State.Replace(next); Notice("assembled");
        });
    }
}
