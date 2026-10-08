using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Il2Cpp;

namespace PCExpansion;

/// <summary>主板与机箱的分层安装记录；本体元数据和实际内部件分别保管。</summary>
internal static class WorkroomMachineAssembly
{
    internal const string Motherboard = "motherboard", Case = "case";
    internal const string Tag = "PCREPAIR_MACHINE_ASSEMBLY_V3";
    internal sealed class Installed
    {
        public Installed() { }
        public string Kind { get; set; } = "";
        public int Index { get; set; }
        public WorkroomItemCodec.Snapshot Item { get; set; } = new();
    }
    internal sealed class MachineInfo
    {
        public MachineInfo() { }
        public int Version { get; set; }
        public string Kind { get; set; } = "";
        public WorkroomItemCodec.Snapshot Body { get; set; } = new();
        public List<Installed> Parts { get; set; } = new();
    }
    internal enum DetectionStatus { Invalid, BodyBroken, InternalBroken, MissingParts, RamMismatch, TierSpan, Complete }
    internal sealed class Assessment
    {
        internal Assessment(DetectionStatus status, List<CaseEconomy.PartRecord>? parts = null, string[]? missing = null)
        { Status = status; Parts = parts ?? new(); Missing = missing ?? Array.Empty<string>(); }
        internal DetectionStatus Status { get; }
        internal IReadOnlyList<CaseEconomy.PartRecord> Parts { get; }
        internal IReadOnlyList<string> Missing { get; }
        internal bool Complete => Status == DetectionStatus.Complete;
        internal bool AllowsTierRange(int minimum, int maximum) => Complete && Parts.All(p => p.Tier >= minimum && p.Tier <= maximum);
        internal bool UpperShowcaseEligible => AllowsTierRange(3, 5) && Parts.Any(p => p.Tier >= 4);
        internal bool Intact => Status != DetectionStatus.Invalid && Status != DetectionStatus.BodyBroken && Status != DetectionStatus.InternalBroken;
        internal bool HighEnd => Intact && Parts.Any(p => p.Tier >= 5 || p.Tier == 4 &&
            (p.TypeTag == Components.GpuTag || p.TypeTag == Components.CpuTag));
        internal bool HighContraband => Intact && Parts.Any(p => p.Tier == 5);
    }
    internal static string Text(string key, params object[] args) => LanguageText.Get("workroom.machine." + key, args);
    private static void Notice(string key) => WorkroomStorage.NotifyText(Text(key));
    internal static string MainSlot(string family) => "machine." + family + ".body";
    internal static string Slot(string family, string kind, int index = 0) => "machine." + family + "." + kind + "." + index;
    internal static string[] Kinds(string family) => family == Motherboard ? new[] { "cpu", "cooler", "gpu", "ram", "ssd" } :
        family == Case ? new[] { "motherboard", "psu", "fan" } : Array.Empty<string>();
    internal static int Count(string family, string kind, int tier)
    {
        if (tier < 1 || tier > 5 || !Kinds(family).Contains(kind)) return 0;
        if (family == Motherboard) return kind switch
        { "gpu" => tier >= 4 ? 2 : 1, "ram" => tier >= 3 ? 4 : 2, "ssd" => tier >= 4 ? 2 : 1, _ => 1 };
        return kind switch { "fan" => Math.Max(1, tier - 1), _ => 1 };
    }
    internal static int MaxCount(string family, string kind) => Count(family, kind, 5);
    internal static WorkroomStorageState.Record? At(string slot) => WorkroomComponentAssembly.At(slot);
    internal static WorkroomItemCodec.Snapshot? Body(string family) => At(MainSlot(family))?.Item;
    internal static bool Ready(long epoch) => WorkroomComponentAssembly.Ready(epoch);
    private static bool IsBody(string family, WorkroomItemCodec.Snapshot item) => item.Count == 1 && item.Nodes.Count == 1 &&
        item.Nodes[0].Identifier == item.Identifier &&
        Components.Find(item.Identifier)?.Owner.Tag == (family == Motherboard ? Components.MotherboardTag : Components.CaseTag);
    internal static int BodyTier(string family) => Body(family) is { } body && IsBody(family, body) ? Components.Find(body.Identifier)!.Tier : 0;
    internal static IEnumerable<string> Slots(string family, bool visibleOnly = false)
    {
        yield return MainSlot(family);
        foreach (var kind in Kinds(family)) for (var i = 0; i < (visibleOnly ? Count(family, kind, BodyTier(family)) : MaxCount(family, kind)); i++)
            yield return Slot(family, kind, i);
    }
    internal static bool ValidSlot(string slot) => new[] { Motherboard, Case }.Any(family => Slots(family).Contains(slot));
    private static string? FamilyOfSlot(string slot) => new[] { Motherboard, Case }.FirstOrDefault(family => Slots(family).Contains(slot));
    private static bool Sealed(WorkroomItemCodec.Snapshot item) => item.Nodes[0].State.ContainsKey(CaseUnboxing.LockedTagName) &&
        ActiveTag(item.Nodes[0], CaseUnboxing.LockedTagName);
    internal static bool ActiveTag(WorkroomItemCodec.Node node, string key)
    {
        if (!node.State.TryGetValue(key, out var fields)) return false;
        return fields.TryGetValue("valueEnabled", out var active) && active.ValueKind == JsonValueKind.True;
    }
    private static bool Bare(WorkroomItemCodec.Snapshot item) => item.Nodes[0].Machine == null && item.Nodes[0].SlotItems.Count == 0 && !Sealed(item);
    private static bool Matches(string kind, WorkroomItemCodec.Snapshot item)
    {
        var spec = Components.Find(item.Identifier);
        if (spec == null || item.Count != 1 || item.Nodes.Count != 1 || item.Nodes[0].Identifier != item.Identifier) return false;
        var tag = kind switch { "cpu" => Components.CpuTag, "cooler" => Components.CoolerTag, "gpu" => Components.GpuTag,
            "ram" => Components.RamTag, "motherboard" => Components.MotherboardTag, "psu" => Components.PsuTag,
            "fan" => Components.FanTag, "ssd" => Components.HddTag, _ => "" };
        return spec.Owner.Tag == tag &&
            item.Nodes[0].SlotItems.Count == 0 && (kind == Motherboard || item.Nodes[0].Machine == null);
    }
    internal static bool Accepts(string slot, WorkroomItemCodec.Snapshot item)
        => Accepts(slot, item, WorkroomStorage.State.FindSlot);
    internal static bool Accepts(string slot, WorkroomItemCodec.Snapshot item, Func<string, WorkroomStorageState.Record?> find)
    {
        var family = FamilyOfSlot(slot); if (family == null || find(slot) != null) return false;
        if (slot == MainSlot(family)) return IsBody(family, item) && !Slots(family).Any(s => find(s) != null);
        if (find(MainSlot(family))?.Item is not { } body || !Bare(body)) return false;
        return Kinds(family).Any(kind => Matches(kind, item) && Enumerable.Range(0, Count(family, kind, Components.Find(body.Identifier)!.Tier)).Any(i => slot == Slot(family, kind, i)));
    }
    internal static void AutoFill(string family, long epoch)
    {
        if (!Ready(epoch) || Kinds(family).Length == 0) return;
        var revision = WorkroomStorage.State.Revision;
        WorkroomStorage.Run(() =>
        {
            var next = new List<WorkroomStorageState.Record>(WorkroomStorage.State.Items); var filled = 0;
            var staged = next.Where(r => r.Place == "slot").ToDictionary(r => r.Slot, StringComparer.Ordinal);
            WorkroomStorageState.Record? Find(string name) => staged.TryGetValue(name, out var value) ? value : null;
            var main = MainSlot(family);
            if (Find(main) == null)
            {
                var body = WorkroomComponentAssembly.Best(next, r => Bare(r.Item) && Accepts(main, r.Item, Find));
                if (body != null) { WorkroomComponentAssembly.FillSlot(next, staged, body, main); filled++; }
            }
            var missing = new List<string>();
            if (Find(main)?.Item is not { } selected) missing.Add(Text(family + "_body"));
            else if (Bare(selected))
            {
                foreach (var kind in Kinds(family)) for (var i = 0; i < Count(family, kind, Components.Find(selected.Identifier)!.Tier); i++)
                {
                    var slot = Slot(family, kind, i); if (Find(slot) != null) continue;
                    var candidate = WorkroomComponentAssembly.Best(next, r => Accepts(slot, r.Item, Find));
                    if (candidate != null) { WorkroomComponentAssembly.FillSlot(next, staged, candidate, slot); filled++; }
                }
                foreach (var kind in family == Motherboard ? new[] { "cpu", "cooler", "gpu", "ram" } : Kinds(family))
                    if (!Enumerable.Range(0, Count(family, kind, Components.Find(selected.Identifier)!.Tier)).Any(i => Find(Slot(family, kind, i)) != null))
                        missing.Add(Text(kind));
            }
            else { WorkroomComponentAssembly.Notice("auto_fill_none"); return; }
            Current(epoch, revision); if (filled > 0) WorkroomStorage.State.Replace(next);
            WorkroomComponentAssembly.FillNotice(filled, missing);
        });
    }
    internal static void ValidateSlots(List<WorkroomStorageState.Record> records)
    {
        var staged = records.Where(r => r.Place == "slot" && r.Slot.StartsWith("machine.", StringComparison.Ordinal)).ToList();
        foreach (var record in staged)
        {
            var family = FamilyOfSlot(record.Slot) ?? throw new InvalidOperationException("机器槽位无效。");
            var body = staged.FirstOrDefault(r => r.Slot == MainSlot(family));
            if (body == null || !IsBody(family, body.Item)) throw new InvalidOperationException("机器缺少对应本体。");
            if (record == body) continue;
            var tier = Components.Find(body.Item.Identifier)!.Tier;
            if (!Bare(body.Item) || !Kinds(family).Any(kind => Matches(kind, record.Item) &&
                Enumerable.Range(0, Count(family, kind, tier)).Any(i => record.Slot == Slot(family, kind, i))))
                throw new InvalidOperationException("机器安装件与本体槽位不符。");
        }
    }
    internal static bool Stage(string id, string slot, long epoch, int orientation)
    {
        var record = WorkroomStorage.State.Find(id); if (!Ready(epoch) || record?.Place != "room") return false;
        if (!Accepts(slot, record.Item)) { Notice("wrong_slot"); return false; }
        var moved = false; var revision = WorkroomStorage.State.Revision;
        WorkroomStorage.Run(() =>
        {
            var next = new List<WorkroomStorageState.Record>(WorkroomStorage.State.Items);
            var staged = next.Where(r => r.Place == "slot").ToDictionary(r => r.Slot, StringComparer.Ordinal);
            WorkroomComponentAssembly.FillSlot(next, staged, record, slot, orientation);
            Current(epoch, revision); WorkroomStorage.State.Replace(next); moved = true;
        });
        return moved;
    }
    internal static void ReturnSlots(IEnumerable<string> slots, long epoch)
    {
        if (!Ready(epoch)) return;
        var requested = slots.ToHashSet();
        foreach (var family in new[] { Motherboard, Case }) if (requested.Contains(MainSlot(family))) requested.UnionWith(Slots(family));
        var selected = requested.Select(At).Where(r => r != null).Select(r => r!).ToList();
        WorkroomStorage.Run(() =>
        {
            var ids = selected.Select(r => r.Id).ToHashSet();
            var next = WorkroomStorage.State.Items.Where(r => !ids.Contains(r.Id)).ToList();
            if (!WorkroomStorageState.PlaceAll(next, selected.Select(r => new WorkroomStorageState.Record { Id = r.Id, Item = r.Item }))) { Notice("full"); return; }
            WorkroomStorage.State.Replace(next);
        });
    }
    internal static bool CanAssemble(string family) => Body(family) is { } body && Bare(body) && Slots(family).Any(s => s != MainSlot(family) && At(s) != null);
    internal static bool CanDisassemble(string family) => Body(family) is { } body &&
        (body.Nodes[0].Machine != null || body.Nodes[0].SlotItems.Count > 0 || Sealed(body));
    private static void Current(long epoch, long revision)
    {
        if (!WorkroomStorage.State.Ready || epoch != WorkroomStorage.State.Epoch || revision != WorkroomStorage.State.Revision || WorkroomItemCodec.CleanupPending)
            throw new InvalidOperationException("机器操作上下文已变化。");
    }
    internal static MachineInfo? Read(GameItem item)
    {
        if (!item.IsTag(Tag)) return null;
        var info = WorkroomAssemblyData.Decode<MachineInfo>(item.state.GetTag(Tag).valueString)
            ?? throw new InvalidOperationException("机器组成记录为空。");
        ValidateInfo(info, Core.Clean(item.identifier), item.uniqueId); return info;
    }
    internal static void ValidateInfo(MachineInfo info, string identifier, int identity) => ValidateInfoCore(info, identifier, identity, false);
    private static void ValidateInfoCore(MachineInfo info, string identifier, int identity, bool allowEmpty)
    {
        if (info.Version != 3 || (info.Kind != Motherboard && info.Kind != Case) || info.Parts == null || !allowEmpty && info.Parts.Count == 0 ||
            !IsBody(info.Kind, info.Body) || !Bare(info.Body) || info.Body.Identifier != identifier || info.Body.Nodes[0].UniqueId != identity)
            throw new InvalidOperationException("机器本体记录无效。");
        WorkroomItemCodec.Validate(info.Body);
        var tier = Components.Find(identifier)!.Tier; var slots = new HashSet<string>(); var identities = new HashSet<int> { identity };
        foreach (var part in info.Parts)
        {
            if (part == null || part.Index < 0 || part.Index >= Count(info.Kind, part.Kind, tier) || !Matches(part.Kind, part.Item) ||
                !slots.Add(part.Kind + "." + part.Index)) throw new InvalidOperationException("机器内部安装槽无效。");
            WorkroomItemCodec.Validate(part.Item);
            foreach (var id in WorkroomItemCodec.Identities(part.Item)) if (!identities.Add(id)) throw new InvalidOperationException("机器内部身份重复。");
        }
    }
    internal static void ValidateNode(WorkroomItemCodec.Node node)
    {
        if (node.Machine == null) return;
        ValidateInfo(node.Machine, node.Identifier, node.UniqueId);
        if (node.Composition != null || node.SlotItems.Count != 0 || !ActiveTag(node, Tag))
            throw new InvalidOperationException("机器组成快照与标签不一致。");
        WorkroomAssemblyData.ValidateNode(node);
    }
    internal static int ConfigurationTier(MachineInfo info) => Flatten(info).Select(p => p.Tier).DefaultIfEmpty(0).Min();
    internal static List<CaseEconomy.PartRecord> Flatten(MachineInfo info)
    {
        var result = new List<CaseEconomy.PartRecord>();
        if (info.Kind == Motherboard) Add(info.Body, result, false);
        foreach (var part in info.Parts) Add(part.Item, result, true);
        return result;
    }
    private static void Add(WorkroomItemCodec.Snapshot item, List<CaseEconomy.PartRecord> result, bool nested)
    {
        if (nested && item.Nodes[0].Machine is { } machine) { result.AddRange(Flatten(machine)); return; }
        var spec = Components.Find(item.Identifier) ?? throw new InvalidOperationException("内部物品类别无效。");
        result.Add(new CaseEconomy.PartRecord(spec.Owner.Tag, item.Nodes[0].Composition is { } parts ? WorkroomComponentParts.EffectiveTier(parts) : spec.Tier,
            SnapshotBroken(item), item.Value));
    }
    internal static long Value(MachineInfo info) => info.Parts.Aggregate(info.Body.Value, (sum, part) => checked(sum + part.Item.Value));
    private static bool SnapshotBroken(WorkroomItemCodec.Snapshot item) => item.Broken || Components.Find(item.Identifier)?.Broken == true ||
        item.Nodes[0].Composition?.Any(SnapshotBroken) == true || item.Nodes[0].Machine is { } machine && Broken(machine);
    internal static bool Broken(MachineInfo info) => SnapshotBroken(info.Body) || info.Parts.Any(part => SnapshotBroken(part.Item));
    /// <summary>仅检查保存的实际组成；不创建实体，不刷新标签，也不依赖检测文案。</summary>
    internal static Assessment Assess(MachineInfo info) => AssessCore(info, false);
    internal static Assessment AssessDraft(MachineInfo info) => AssessCore(info, true);
    internal static MachineInfo? ReadAssessed(GameItem item, out Assessment? assessment)
    {
        var info = Read(item); // Full validation occurs here, for this call only.
        assessment = info == null ? null : AssessCore(info, false, false);
        return info;
    }
    private static Assessment AssessCore(MachineInfo info, bool allowEmpty, bool validate = true)
    {
        try
        {
            if (validate) ValidateInfoCore(info, info.Body.Identifier, info.Body.Nodes[0].UniqueId, allowEmpty);
            var parts = Flatten(info);
            if (SnapshotBroken(info.Body)) return new(DetectionStatus.BodyBroken, parts);
            if (parts.Any(p => p.Broken)) return new(DetectionStatus.InternalBroken, parts);
            var required = info.Kind == Motherboard ? new[] { "cpu", "cooler", "gpu", "ram" } :
                new[] { "motherboard", "cpu", "cooler", "gpu", "ram", "storage", "psu", "fan" };
            var tags = new[] { Components.MotherboardTag, Components.CpuTag, Components.CoolerTag, Components.GpuTag,
                Components.RamTag, Components.HddTag, Components.PsuTag, Components.FanTag };
            var names = new[] { "motherboard", "cpu", "cooler", "gpu", "ram", "storage", "psu", "fan" };
            var missing = required.Where(kind => !parts.Any(p => p.TypeTag == tags[Array.IndexOf(names, kind)])).ToArray();
            if (missing.Length > 0) return new(DetectionStatus.MissingParts, parts, missing);
            if (parts.Any(p => p.Tier is < 1 or > 5)) return new(DetectionStatus.Invalid, parts);
            if (parts.Where(p => p.TypeTag == Components.RamTag).Select(p => p.Tier).Distinct().Count() > 1)
                return new(DetectionStatus.RamMismatch, parts);
            if (parts.Max(p => p.Tier) - parts.Min(p => p.Tier) > 3) return new(DetectionStatus.TierSpan, parts);
            return new(DetectionStatus.Complete, parts);
        }
        catch { return new(DetectionStatus.Invalid); }
    }
    internal static bool AllowsBoardPurchase(MachineInfo? info, int bareTier, bool broken, int minimum, int maximum) =>
        !broken && (info == null ? bareTier is >= 1 and <= 5 && bareTier >= minimum && bareTier <= maximum :
            info.Kind == Motherboard && Assess(info).AllowsTierRange(minimum, maximum));
    internal static string Detection(MachineInfo info) => DetectionText(Assess(info));
    private static string DetectionText(Assessment assessment) => assessment.Status switch
    {
        DetectionStatus.BodyBroken => Text("body_broken"),
        DetectionStatus.InternalBroken => Text("internal_broken"),
        DetectionStatus.MissingParts => Text("missing", string.Join("、", assessment.Missing.Select(kind => Text(kind)))),
        DetectionStatus.RamMismatch => Text("ram_warning"),
        DetectionStatus.TierSpan => Text("tier_warning"),
        DetectionStatus.Complete => Text("complete"),
        _ => Text("wrong_slot")
    };
    internal static void Refresh(GameItem item)
    {
        MachineInfo? info;
        try { info = Read(item); }
        catch
        {
            // A failed load must revoke any old eligibility before reporting the bad record.
            Components.SetTradeProperties(item, false, false);
            item.DisableTag(LowerAssemblerNpc.PurchaseTag, false);
            item.DisableTag(UpperComputerBuyerNpc.PurchaseTag, false);
            throw;
        }
        if (info == null) return;
        var assessment = Assess(info);
        var broken = Broken(info); var spec = Components.Find(item.identifier)!;
        if (broken) item.EnableTag(Components.BrokenTag, false); else item.DisableTag(Components.BrokenTag, false);
        // Internal faults are not sealed loot crates or broken native machines.
        item.DisableTag("BROKEN_MACHINE", false); item.DisableTag(CaseUnboxing.LockedTagName, false);
        item.EnableTag("IGNORE_CHILD_VALUE", false); item.lateUnitValue = 0;
        var name = spec.DisplayName + Text("assembled_name"); item.identifierName = name; item.SetName(name);
        var desc = spec.Desc + "\n" + Text("contents_info", info.Parts.Count, ConfigurationTier(info)) + "\n" +
            DetectionText(assessment);
        item.shortDescription = desc; item.longDescription = desc;
        if (info.Kind == Case) CaseEconomy.EvaluateCase(item);
        else
        {
            Components.SetTradeProperties(item, assessment.HighEnd, assessment.HighContraband);
            item.SetValue(Value(info));
            LowerAssemblerNpc.RefreshPurchaseEligibility(item);
            UpperComputerBuyerNpc.RefreshPurchaseEligibility(item);
        }
    }
    internal static void Assemble(string family, long epoch)
    {
        if (!Ready(epoch) || !CanAssemble(family)) return;
        var body = At(MainSlot(family))!; var revision = WorkroomStorage.State.Revision;
        WorkroomStorage.Run(() =>
        {
            var info = new MachineInfo { Version = 3, Kind = family, Body = body.Item };
            foreach (var kind in Kinds(family)) for (var i = 0; i < Count(family, kind, BodyTier(family)); i++)
                if (At(Slot(family, kind, i)) is { } part) info.Parts.Add(new Installed { Kind = kind, Index = i, Item = part.Item });
            ValidateInfo(info, body.Item.Identifier, body.Item.Nodes[0].UniqueId);
            var selected = Slots(family).Select(At).Where(r => r != null).Select(r => r!).ToList();
            var ids = selected.Select(r => r.Id).ToHashSet(); var next = WorkroomStorage.State.Items.Where(r => !ids.Contains(r.Id)).ToList();
            var trial = new List<WorkroomStorageState.Record>(next);
            if (!WorkroomStorageState.PlaceAll(trial, new[] { new WorkroomStorageState.Record { Item = body.Item } })) { Notice("full"); return; }
            var candidate = WorkroomItemCodec.Restore(body.Item); WorkroomItemCodec.Snapshot snapshot;
            try
            {
                candidate.Root.EnableTag(Tag, false); candidate.Root.state.GetTag(Tag).SetString(WorkroomAssemblyData.Encode(info));
                Refresh(candidate.Root); snapshot = WorkroomItemCodec.Capture(candidate.Root);
            }
            finally { WorkroomItemCodec.Destroy(candidate); }
            Current(epoch, revision);
            if (!WorkroomStorageState.PlaceAll(next, new[] { new WorkroomStorageState.Record { Id = body.Id, Item = snapshot } })) { Notice("full"); return; }
            WorkroomStorage.State.Replace(next); Notice("assembled");
        });
    }
    private static WorkroomItemCodec.Snapshot EmptyCrate(WorkroomItemCodec.Snapshot source)
    {
        var candidate = WorkroomItemCodec.Restore(source); WorkroomItemCodec.Snapshot result;
        try
        {
            var root = candidate.Root;
            if (Sealed(source)) CaseUnboxing.ReleaseWorkroomLock(root);
            WorkroomCrateContents.Clear(root);
            root.DisableTag(Tag, false);
            Components.ApplySpec(root, source.Identifier);
            root.RemoveItemFeatureByID(CaseEconomy.MachinePriceFeatureId);
            result = WorkroomItemCodec.Capture(root);
        }
        finally { WorkroomItemCodec.Destroy(candidate); }
        return result;
    }
    internal static void Disassemble(string family, long epoch)
    {
        if (!Ready(epoch) || !CanDisassemble(family)) return;
        var body = At(MainSlot(family))!; var revision = WorkroomStorage.State.Revision;
        WorkroomStorage.Run(() =>
        {
            if (Slots(family).Any(s => s != MainSlot(family) && At(s) != null)) { Notice("occupied"); return; }
            var next = WorkroomStorage.State.Items.Where(r => r.Id != body.Id).ToList();
            if (body.Item.Nodes[0].Machine is { } info)
            {
                ValidateInfo(info, body.Item.Identifier, body.Item.Nodes[0].UniqueId);
                next.Add(new WorkroomStorageState.Record { Id = body.Id, Place = "slot", Slot = MainSlot(family), Item = info.Body });
                next.AddRange(info.Parts.Select(p => new WorkroomStorageState.Record { Place = "slot", Slot = Slot(family, p.Kind, p.Index), Item = p.Item }));
            }
            else
            {
                // Fixed crate contents already belong to this case. Capacity
                // refusal does not construct or reroll any output.
                if (!WorkroomStorageState.PlaceAll(next, body.Item.Nodes[0].SlotItems.Select(p => new WorkroomStorageState.Record { Item = p }))) { Notice("full"); return; }
                var empty = EmptyCrate(body.Item);
                next.Add(new WorkroomStorageState.Record { Id = body.Id, Place = "slot", Slot = MainSlot(family), Item = empty });
            }
            Current(epoch, revision); WorkroomStorage.State.Replace(next); Notice("disassembled");
        });
    }
    internal static MachineInfo CreateDraft(string family, WorkroomItemCodec.Snapshot body, IEnumerable<WorkroomStorageState.Record> records)
    {
        var draft = new MachineInfo { Version = 3, Kind = family, Body = body };
        foreach (var record in records)
        {
            var slots = Kinds(family).SelectMany(kind => Enumerable.Range(0, MaxCount(family, kind))
                .Select(index => (Kind: kind, Index: index))).Where(slot => Slot(family, slot.Kind, slot.Index) == record.Slot).ToList();
            if (slots.Count != 1) throw new InvalidOperationException("机器预览槽位无效。");
            draft.Parts.Add(new Installed { Kind = slots[0].Kind, Index = slots[0].Index, Item = record.Item });
        }
        return draft;
    }
    internal static string Summary(string family)
    {
        var body = Body(family); if (body == null) return Text("need_body", Text(family + "_body"));
        if (Sealed(body)) return Text("sealed_hint");
        if (body.Nodes[0].Machine is { } info) return Text("stored_summary", info.Parts.Count, Value(info)) + "\n" +
            Detection(info);
        if (body.Nodes[0].SlotItems.Count > 0) return Text("crate_hint", body.Nodes[0].SlotItems.Count);
        var parts = Slots(family).Where(s => s != MainSlot(family)).Select(At).Where(r => r != null).Select(r => r!).ToList();
        var draft = CreateDraft(family, body, parts);
        return Text("staged_summary", parts.Count, checked(body.Value + parts.Sum(p => p.Item.Value))) + "\n" + DetectionText(AssessDraft(draft));
    }
    internal static long MaximumMachineValue(int tier)
    {
        long sum = Components.ValueFor(Array.Find(Components.All, t => t.IsCase)!, tier, false) +
            Components.ValueFor(Array.Find(Components.All, t => t.Tag == Components.MotherboardTag)!, tier, false);
        foreach (var family in new[] { Motherboard, Case }) foreach (var kind in Kinds(family))
        {
            if (kind == Motherboard) continue;
            var tag = kind switch { "cpu" => Components.CpuTag, "cooler" => Components.CoolerTag, "gpu" => Components.GpuTag,
                "ram" => Components.RamTag, "ssd" => Components.HddTag, "psu" => Components.PsuTag, _ => Components.FanTag };
            var template = WorkroomComponentTemplates.All.FirstOrDefault(t => t.Key == kind);
            var maximumValue = template == null ? Components.ValueFor(Array.Find(Components.All, t => t.Tag == tag)!, tier, false) :
                WorkroomComponentTemplates.MaximumWholeValue(template, tier);
            sum = checked(sum + Count(family, kind, tier) * maximumValue);
        }
        return sum;
    }
}
