using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Il2Cpp;

namespace PCExpansion;

/// <summary>显式单件选单的混合回收；预览不转移保管，整批核对后只发布一次。</summary>
internal static class WorkroomBatchRecycling
{
    internal static readonly string[] MaterialIds =
        { "common_electronic", "wire", "scrap_metal", "nuts_metal", "printer_plastic" };

    internal sealed class Selection
    {
        internal readonly WorkroomStorageState.Record Record;
        internal Selection(WorkroomStorageState.Record record) => Record = record;
    }

    internal sealed class Plan
    {
        internal long Epoch, Revision;
        internal readonly List<Selection> Sources = new();
        internal string MaterialId = "";
        internal long TotalValue, Budget, MaterialValue, OutputCount, Spent, Remainder;
        internal int RecoveryPercent;
        internal bool Allowed;
        internal string Reason = "unavailable";
        internal IntPtr Owner;
        internal string? Run;
        internal int SaveSlot;
        internal WorkroomItemCodec.Shape Shape = new();
        internal bool Attempted;
        // An independent seal also rejects accidental edits of the exposed UI DTOs.
        internal string Seal = "";
    }

    private static bool operating;
    internal static string Text(string key, params object[] args) => LanguageText.Get("workroom.recycle." + key, args);
    private static void Notice(string key)
    {
        try { WorkroomStorage.NotifyText(Text(key)); RoomAudio.Notice(key); }
        catch (Exception ex) { Core.Log?.Warning("混合回收提示未显示：" + ex.Message); }
    }

    // Pure eligibility: no native factories, storage writes or source auto-fill.
    internal static bool Eligible(WorkroomItemCodec.Snapshot? item)
    {
        try
        {
            if (item == null || item.Count != 1 || item.Value < 0 || item.Nodes.Count != 1 ||
                item.Nodes[0].Machine != null || item.Nodes[0].Composition != null || item.Nodes[0].SlotItems.Count != 0 ||
                WorkroomComponentTemplates.TryWhole(item.Identifier, out _, out _, out _)) return false;
            return WorkroomRecyclingRules.WholeTarget(item) ||
                WorkroomComponentTemplates.TryPart(item.Identifier, out _, out _, out _) && item.Nodes[0].Part != null;
        }
        catch { return false; }
    }

    internal static bool Available(WorkroomStorageState.Record? record) => record != null &&
        (record.Place == "room" || record.Place == "box" ||
            record.Place == "slot" && record.Slot == WorkroomComponentAssembly.Disassembly) && Eligible(record.Item);

    internal static Plan Preview(IReadOnlyList<string> ids, string materialId, long epoch)
    {
        var state = WorkroomStorage.State;
        var plan = new Plan { Epoch = epoch, Revision = state.Revision, MaterialId = materialId };
        if (operating || WorkroomStorage.Busy || !state.Ready || state.Epoch != epoch || WorkroomItemCodec.CleanupPending)
            return plan;
        operating = true;
        try
        {
            var owner = PlayerStore.IsInstanceExist() ? PlayerStore.instance : null;
            if (owner == null) return plan;
            plan.Owner = owner.Pointer; plan.Run = owner.runID; plan.SaveSlot = owner.saveSlotId;
            if (!Material(materialId)) { plan.Reason = "invalid_material"; return plan; }
            if (ids == null || ids.Count == 0) { plan.Reason = "empty"; return plan; }
            var selected = new HashSet<string>(StringComparer.Ordinal);
            var records = IndexRecords();
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id) || !selected.Add(id)) { plan.Reason = "invalid_source"; return plan; }
                records.TryGetValue(id, out var record);
                if (!Available(record)) { plan.Reason = "invalid_source"; return plan; }
                var copy = Clone(record!);
                WorkroomItemCodec.Validate(copy.Item);
                plan.Sources.Add(new Selection(copy));
                plan.TotalValue = checked(plan.TotalValue + copy.Item.Value);
            }
            plan.RecoveryPercent = WorkroomRecyclingValue.CurrentPercent;
            plan.Budget = WorkroomRecyclingValue.Budget(plan.TotalValue, plan.RecoveryPercent);
            var quote = CaptureMaterial(materialId);
            plan.MaterialValue = quote.Value;
            plan.Shape = CloneShape(quote.Footprint);
            plan.OutputCount = plan.Budget / plan.MaterialValue;
            plan.Spent = checked(plan.OutputCount * plan.MaterialValue);
            plan.Remainder = plan.Budget - plan.Spent;
            RequireCurrent(plan, false, false);
            if (plan.OutputCount == 0) { plan.Reason = "zero"; return plan; }
            // Every valid footprint occupies at least one room cell. Refuse huge
            // budgets before allocating a list or touching any native output.
            if (plan.OutputCount > WorkroomStorageState.Columns * WorkroomStorageState.Rows)
            { plan.Reason = "full"; return plan; }
            var next = Remainders(plan);
            var shapes = Enumerable.Range(0, (int)plan.OutputCount).Select(_ => new WorkroomStorageState.Record
                { Item = new WorkroomItemCodec.Snapshot { Footprint = plan.Shape } });
            if (!WorkroomStorageState.PlaceAll(next, shapes)) { plan.Reason = "full"; return plan; }
            RequireCurrent(plan, false, false);
            plan.Allowed = true; plan.Reason = "ready";
            plan.Seal = Facts(plan);
            return plan;
        }
        catch (Exception ex)
        {
            plan.Allowed = false; plan.Reason = "unavailable";
            Core.Log?.Warning("混合回收预览不可用，来源保留：" + ex.Message);
            return plan;
        }
        finally { operating = false; }
    }

    // Full snapshot checks belong to selection/confirmation boundaries, never a
    // per-frame UI predicate. Native quote factories are only used by Preview/Apply.
    internal static bool IsCurrent(Plan? plan)
    {
        if (plan == null || !plan.Allowed || plan.Attempted || operating || WorkroomStorage.Busy) return false;
        try { RequireCurrent(plan, false, true); return true; }
        catch { return false; }
    }

    internal static bool Apply(Plan? plan)
    {
        if (plan == null || !plan.Allowed) { Notice(plan?.Reason ?? "unavailable"); return false; }
        if (operating || WorkroomStorage.Busy || plan.Attempted) { Notice("changed"); return false; }
        // Consume the confirmation even on failure: retry requires a fresh quote.
        plan.Attempted = true;
        operating = true;
        var applied = false;
        var refusal = "changed";
        try
        {
            RequireCurrent(plan, false, true);
            WorkroomStorage.Run(() =>
            {
                try
                {
                    RequireCurrent(plan, true, true);
                    if (plan.OutputCount is <= 0 or > WorkroomStorageState.Columns * WorkroomStorageState.Rows)
                        throw new InvalidOperationException("回收产量超出有界工作间容量。");
                    var outputs = new List<WorkroomStorageState.Record>((int)plan.OutputCount);
                    for (var i = 0; i < plan.OutputCount; i++)
                    {
                        var output = CaptureMaterial(plan.MaterialId);
                        if (output.Value != plan.MaterialValue || !SameShape(output.Footprint, plan.Shape))
                            throw new InvalidOperationException("材料实价或占格已变化，请重新预览。");
                        outputs.Add(new WorkroomStorageState.Record { Item = output });
                        RequireContext(plan, true);
                    }
                    var next = Remainders(plan);
                    if (!WorkroomStorageState.PlaceAll(next, outputs)) { refusal = "full"; return; }
                    RequireCurrent(plan, true, true);
                    WorkroomStorage.State.ValidateReplacement(next);
                    RequireCurrent(plan, true, true);
                    WorkroomStorage.State.Replace(next);
                    applied = true;
                }
                catch (Exception ex)
                { Core.Log?.Warning("混合回收未提交，来源保留：" + ex.Message); }
            });
        }
        catch (Exception ex)
        { Core.Log?.Warning("混合回收确认已失效，来源保留：" + ex.Message); }
        finally { operating = false; }
        Notice(applied ? "done" : refusal);
        return applied;
    }

    private static bool Material(string id) => id is "common_electronic" or "wire" or "scrap_metal" or "nuts_metal" or "printer_plastic";

    // One transient index per operation boundary; selecting many box records
    // must not rescan the whole inventory once for every selected item.
    private static Dictionary<string, WorkroomStorageState.Record> IndexRecords()
    {
        var records = new Dictionary<string, WorkroomStorageState.Record>(StringComparer.Ordinal);
        foreach (var record in WorkroomStorage.State.Items)
            if (!records.TryAdd(record.Id, record)) throw new InvalidOperationException("来源记录身份重复。");
        return records;
    }

    private static void RequireCurrent(Plan plan, bool insideRun, bool sealedPlan)
    {
        RequireContext(plan, insideRun);
        if (sealedPlan && (plan.Seal.Length == 0 || plan.Seal != Facts(plan)))
            throw new InvalidOperationException("回收选单预览事实已变化。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var records = IndexRecords();
        long value = 0;
        foreach (var source in plan.Sources)
        {
            var expected = source.Record;
            records.TryGetValue(expected.Id, out var actual);
            if (!ids.Add(expected.Id) || !Available(actual) || actual!.Place != expected.Place || actual.Slot != expected.Slot ||
                actual.X != expected.X || actual.Y != expected.Y || SnapshotText(actual.Item) != SnapshotText(expected.Item))
                throw new InvalidOperationException("回收来源身份、位置、状态或完整内容已变化。");
            value = checked(value + actual.Item.Value);
        }
        if (ids.Count == 0 || value != plan.TotalValue || plan.RecoveryPercent != WorkroomRecyclingValue.CurrentPercent ||
            WorkroomRecyclingValue.Budget(value, plan.RecoveryPercent) != plan.Budget)
            throw new InvalidOperationException("回收选单或总价已变化。");
    }

    private static void RequireContext(Plan plan, bool insideRun)
    {
        var state = WorkroomStorage.State;
        var owner = PlayerStore.IsInstanceExist() ? PlayerStore.instance : null;
        if (!state.Ready || owner == null || owner.Pointer != plan.Owner || owner.runID != plan.Run ||
            owner.saveSlotId != plan.SaveSlot || state.Epoch != plan.Epoch || state.Revision != plan.Revision ||
            WorkroomItemCodec.CleanupPending || !insideRun && WorkroomStorage.Busy)
            throw new InvalidOperationException("回收会话或临时清理状态已变化。");
    }

    private static List<WorkroomStorageState.Record> Remainders(Plan plan)
    {
        var ids = new HashSet<string>(plan.Sources.Select(source => source.Record.Id), StringComparer.Ordinal);
        return WorkroomStorage.State.Items.Where(record => !ids.Contains(record.Id)).ToList();
    }

    private static WorkroomItemCodec.Snapshot CaptureMaterial(string id)
    {
        if (!Material(id) || WorkroomItemCodec.CleanupPending) throw new InvalidOperationException("材料选择或临时清理不可用。");
        var snapshot = WorkroomComponentParts.CaptureFactory(() => DirectoryMaster.Item(id, true)
            ?? throw new InvalidOperationException("原版材料工厂缺失：" + id));
        WorkroomItemCodec.Validate(snapshot);
        if (WorkroomItemCodec.CleanupPending || snapshot.Identifier != id || snapshot.Count != 1 || snapshot.Value <= 0 ||
            snapshot.Broken || snapshot.Nodes.Count != 1 || snapshot.Nodes[0].Part != null || snapshot.Nodes[0].Machine != null ||
            snapshot.Nodes[0].Composition != null || snapshot.Nodes[0].SlotItems.Count != 0)
            throw new InvalidOperationException("原版材料完整信息、价格或数量无效：" + id);
        return snapshot;
    }

    private static WorkroomStorageState.Record Clone(WorkroomStorageState.Record record) =>
        JsonSerializer.Deserialize<WorkroomStorageState.Record>(JsonSerializer.Serialize(record, WorkroomStorageState.JsonOptions),
            WorkroomStorageState.JsonOptions) ?? throw new InvalidOperationException("来源深快照未建立。");
    private static WorkroomItemCodec.Shape CloneShape(WorkroomItemCodec.Shape shape) =>
        new() { Width = shape.Width, Height = shape.Height, Cells = (byte[])shape.Cells.Clone() };
    private static bool SameShape(WorkroomItemCodec.Shape a, WorkroomItemCodec.Shape b) =>
        a.Width == b.Width && a.Height == b.Height && a.Cells.SequenceEqual(b.Cells);
    private static string SnapshotText(WorkroomItemCodec.Snapshot item) => JsonSerializer.Serialize(item, WorkroomStorageState.JsonOptions);
    private static string Facts(Plan plan) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        plan.Epoch, plan.Revision, Owner = plan.Owner.ToInt64(), plan.Run, plan.SaveSlot, plan.MaterialId,
        plan.TotalValue, plan.Budget, plan.RecoveryPercent, plan.MaterialValue, plan.OutputCount, plan.Spent, plan.Remainder,
        plan.Allowed, plan.Reason, plan.Shape, Sources = plan.Sources.Select(source => source.Record)
    }, WorkroomStorageState.JsonOptions)));
}
