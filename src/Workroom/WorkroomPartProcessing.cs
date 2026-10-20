using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Il2Cpp;

namespace PCExpansion;

/// <summary>单格处理的预计算计划；临时实体清理完成后才提交唯一库存模型。</summary>
internal static class WorkroomPartProcessing
{
    internal enum Mode { Disassemble, Repair, Recycle, Discard }

    private sealed class Material
    {
        internal string Id = "";
        internal int Count;
        internal long Value;
    }
    private sealed class Plan
    {
        internal IntPtr Owner;
        internal string? Run;
        internal int SaveSlot;
        internal long Epoch, Revision;
        internal long RetryAt;
        internal Mode Mode;
        internal bool Chinese;
        internal string SourceId = "", Text = "", Refusal = "processing_unavailable";
        internal bool Allowed;
        internal int Cash;
        internal long Fee, TotalValue, Budget;
        internal int RecoveryPercent;
        internal bool CashRepair;
        internal WorkroomStorageState.Record? Source;
        internal List<Material> Materials = new();
    }
    private static Plan? cached;
    internal sealed class DiscardConfirmation
    {
        internal IntPtr Owner;
        internal string? Run;
        internal int SaveSlot;
        internal long Epoch, Revision;
        internal bool Chinese;
        internal string SourceId = "";
        internal WorkroomItemCodec.Snapshot Target = null!;
    }
    static WorkroomPartProcessing() => LanguageText.LanguageChanged += () => cached = null;
    private static string Text(string key, params object[] args) => WorkroomComponentTemplates.Text(key, args);
    private static void Notice(string key) => WorkroomComponentAssembly.Notice(key);

    internal static string Preview(Mode mode) => WorkroomCashRepair.Pending ? Text("processing_cash_recovery") :
        GetPlan(mode)?.Text ?? Text("processing_unavailable");
    internal static bool CanExecute(Mode mode) => !WorkroomCashRepair.Pending && GetPlan(mode)?.Allowed == true;

    internal static void Execute(Mode mode, long epoch)
    {
        // Destructive disposal has its own target-bound two-click API.
        if (mode == Mode.Discard) { Notice("processing_discard_confirm_required"); return; }
        if (WorkroomCashRepair.Pending) { Notice("processing_cash_recovery"); return; }
        if (!WorkroomComponentAssembly.Ready(epoch)) return;
        var plan = GetPlan(mode);
        if (plan == null || !plan.Allowed) { Notice(plan?.Refusal ?? "processing_unavailable"); return; }
        if (mode == Mode.Disassemble) { WorkroomComponentAssembly.Disassemble(epoch); return; }
        var repaired = false;
        WorkroomStorage.Run(() =>
        {
            try
            {
            RequireCurrent(plan);
            var source = WorkroomComponentAssembly.At(WorkroomComponentAssembly.Disassembly)
                ?? throw new InvalidOperationException("处理目标已转移。");
            if (mode == Mode.Repair) repaired = Repair(source, plan);
            }
            finally { cached = null; }
        });
        if (repaired && WorkroomSelection.AutoReturn)
            WorkroomSelection.ReturnSlots(new[] { WorkroomComponentAssembly.Disassembly }, epoch);
    }

    internal static DiscardConfirmation? PrepareDiscard(long epoch)
    {
        if (!WorkroomComponentAssembly.Ready(epoch) || WorkroomCashRepair.Pending) return null;
        try
        {
        var plan = GetPlan(Mode.Discard);
        if (plan?.Allowed != true) { Notice(plan?.Refusal ?? "processing_unavailable"); return null; }
        RequireCurrent(plan);
        var target = WorkroomComponentAssembly.At(WorkroomComponentAssembly.Disassembly)!;
        // Keep an independent complete snapshot, not a reference that a later
        // same-record replacement or accidental in-place change can modify.
        var snapshot = JsonSerializer.Deserialize<WorkroomItemCodec.Snapshot>(
            JsonSerializer.Serialize(target.Item, WorkroomStorageState.JsonOptions), WorkroomStorageState.JsonOptions)
            ?? throw new InvalidOperationException("报废确认快照无法建立。");
        WorkroomItemCodec.Validate(snapshot);
        return new DiscardConfirmation { Owner = plan.Owner, Run = plan.Run, SaveSlot = plan.SaveSlot,
            Epoch = plan.Epoch, Revision = plan.Revision, Chinese = plan.Chinese, SourceId = plan.SourceId, Target = snapshot };
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("报废确认未建立，目标保留：" + ex.Message);
            Notice("processing_unavailable"); return null;
        }
    }

    // Rendering only observes invalidation tokens. Complete snapshot validation
    // remains at the destructive action boundary, including in-place changes.
    internal static bool IsDiscardConfirmationPending(DiscardConfirmation confirmation)
    {
        try
        {
        var owner = PlayerStore.IsInstanceExist() ? PlayerStore.instance : null;
        var state = WorkroomStorage.State;
        var target = WorkroomComponentAssembly.At(WorkroomComponentAssembly.Disassembly);
        return state.Ready && owner != null && owner.Pointer == confirmation.Owner && owner.runID == confirmation.Run &&
            owner.saveSlotId == confirmation.SaveSlot && state.Epoch == confirmation.Epoch && state.Revision == confirmation.Revision &&
            confirmation.Chinese == LanguageText.IsChinese && target != null && target.Id == confirmation.SourceId;
        }
        catch { return false; }
    }

    internal static bool IsDiscardConfirmationCurrent(DiscardConfirmation confirmation)
    {
        try
        {
            if (!IsDiscardConfirmationPending(confirmation)) return false;
            var target = WorkroomComponentAssembly.At(WorkroomComponentAssembly.Disassembly);
            return target != null && SameSnapshot(confirmation.Target, target.Item);
        }
        catch { return false; }
    }

    internal static void ConfirmDiscard(DiscardConfirmation confirmation, long epoch)
    {
        if (!WorkroomComponentAssembly.Ready(epoch) || WorkroomCashRepair.Pending || !IsDiscardConfirmationCurrent(confirmation))
        { Notice("processing_discard_changed"); return; }
        // Requote single-item recycling now. A price change must not discard
        // something that has gained a usable material output since preview.
        cached = null;
        var plan = GetPlan(Mode.Discard);
        if (plan?.Allowed != true) { Notice(plan?.Refusal ?? "processing_unavailable"); return; }
        WorkroomStorage.Run(() =>
        {
            try
            {
                RequireCurrent(plan);
                if (!IsDiscardConfirmationCurrent(confirmation)) throw new InvalidOperationException("报废目标或确认会话已变化。");
                var source = WorkroomComponentAssembly.At(WorkroomComponentAssembly.Disassembly)!;
                RequireDiscardSource(plan, source);
                var next = new List<WorkroomStorageState.Record>(WorkroomStorage.State.Items);
                if (next.RemoveAll(record => record.Id == confirmation.SourceId) != 1)
                    throw new InvalidOperationException("报废目标不唯一。");
                WorkroomStorage.State.ValidateReplacement(next);
                RequireCurrent(plan);
                if (!IsDiscardConfirmationCurrent(confirmation)) throw new InvalidOperationException("报废确认已失效。");
                RequireDiscardSource(plan, source);
                WorkroomStorage.State.Replace(next);
                Notice("processing_discarded");
            }
            finally { cached = null; }
        });
    }

    private static Plan? GetPlan(Mode mode)
    {
        // Recycling belongs exclusively to the mixed-selection batch service.
        if (mode == Mode.Recycle) return null;
        var state = WorkroomStorage.State;
        if (!state.Ready || WorkroomStorage.Busy || WorkroomItemCodec.CleanupPending ||
            !PlayerStore.IsInstanceExist() || PlayerStore.instance == null) return null;
        var owner = PlayerStore.instance;
        var cash = owner.GetCash();
        if (cached != null && cached.Owner == owner.Pointer && cached.Run == owner.runID &&
            cached.SaveSlot == owner.saveSlotId && cached.Epoch == state.Epoch && cached.Revision == state.Revision &&
            cached.Mode == mode && cached.Cash == cash && cached.Chinese == LanguageText.IsChinese &&
            (cached.RetryAt == 0 || Environment.TickCount64 < cached.RetryAt)) return cached;
        var plan = new Plan { Owner = owner.Pointer, Run = owner.runID, SaveSlot = owner.saveSlotId,
            Epoch = state.Epoch, Revision = state.Revision, Mode = mode, Cash = cash, Chinese = LanguageText.IsChinese };
        var source = WorkroomComponentAssembly.At(WorkroomComponentAssembly.Disassembly);
        plan.SourceId = source?.Id ?? "";
        try
        {
            BuildPlan(plan, source);
            RequireCurrent(plan);
        }
        catch (Exception ex)
        {
            plan.Allowed = false; plan.Refusal = "processing_unavailable"; plan.Text = Text(plan.Refusal);
            plan.RetryAt = Environment.TickCount64 + 2000;
            Core.Log?.Warning("工作间处理预览不可用，保留源记录：" + ex.Message);
        }
        // Failed quotes can retry; successful facts are scoped to this exact
        // owner and revision. Never keep native entities or quote snapshots here.
        cached = plan;
        return plan;
    }

    private static void Refuse(Plan plan, string key)
    { plan.Refusal = key; plan.Text = Text(key); plan.Allowed = false; }

    private static void BuildPlan(Plan plan, WorkroomStorageState.Record? source)
    {
        if (source == null) { Refuse(plan, "processing_need_target"); return; }
        var item = source.Item;
        WorkroomItemCodec.Validate(item);
        var whole = WorkroomComponentTemplates.TryWhole(item.Identifier, out _, out _, out _);
        var extraWhole = WorkroomRecyclingRules.WholeTarget(item);
        if (plan.Mode == Mode.Disassemble)
        {
            if (!whole || item.Count != 1) { Refuse(plan, "processing_need_whole"); return; }
            plan.Text = WorkroomComponentAssembly.DisassemblyPreview(item);
            plan.Allowed = true; return;
        }
        if (whole) { Refuse(plan, "processing_split_first"); return; }
        var part = WorkroomComponentTemplates.TryPart(item.Identifier, out var template, out var kind, out _);
        if (!extraWhole && (!part || item.Count != 1 || item.Nodes.Count != 1 || item.Nodes[0].Machine != null ||
            item.Nodes[0].Composition != null || item.Nodes[0].SlotItems.Count != 0))
        { Refuse(plan, "processing_need_part"); return; }
        var wholeSpec = extraWhole ? Components.Find(item.Identifier)! : null;
        // Material requirements follow the current part's physical specification.
        var multiplier = Multiplier(wholeSpec?.Tier ?? WorkroomComponentParts.ProcessingTier(item));
        if (plan.Mode == Mode.Repair)
        {
            if (extraWhole) { Refuse(plan, "processing_unrepairable"); return; }
            if (!item.Broken) { Refuse(plan, "processing_already_intact"); return; }
            if (CashRepair(template.Key, kind))
            {
                var intact = WorkroomComponentParts.IntactValue(item);
                plan.CashRepair = true; plan.Fee = intact == 0 ? 0 : Math.Max(1, intact / 4);
                plan.Allowed = plan.Fee <= plan.Cash && plan.Fee <= int.MaxValue;
                plan.Refusal = plan.Fee > int.MaxValue ? "processing_cash_limit" : "processing_missing_cash";
                plan.Text = Text("processing_cash_header", intact, plan.Fee, plan.Cash) + "\n" +
                    Text(plan.Allowed ? "processing_cash_ready" : plan.Refusal);
                return;
            }
            var ids = RepairRecipe(template.Key, kind);
            if (ids.Length == 0) { Refuse(plan, "processing_unrepairable"); return; }
            foreach (var id in ids)
            {
                var material = Quote(id); material.Count = multiplier; plan.Materials.Add(material);
            }
            var total = plan.Materials.Aggregate(0L, (sum, material) => checked(sum + material.Value * material.Count));
            var lines = new List<string> { Text("processing_repair_header", WorkroomComponentParts.IntactValue(item), total) };
            var enough = true;
            foreach (var material in plan.Materials)
            {
                var owned = Owned(material.Id);
                lines.Add(Text("processing_repair_material", MaterialName(material.Id), material.Count, owned, material.Value));
                if (owned < material.Count) enough = false;
            }
            plan.Allowed = enough; plan.Refusal = "processing_missing_materials";
            lines.Add(Text(enough ? "processing_repair_ready" : plan.Refusal));
            plan.Text = string.Join("\n", lines); return;
        }
        if (plan.Mode != Mode.Discard) { Refuse(plan, "processing_unavailable"); return; }
        if (!item.Broken || !extraWhole &&
            (CashRepair(template.Key, kind) || RepairRecipe(template.Key, kind).Length != 0))
        { Refuse(plan, "processing_discard_unrepairable_only"); return; }
        plan.Source = CaptureDiscardSource(source);
        plan.TotalValue = item.Value;
        plan.RecoveryPercent = WorkroomRecyclingValue.CurrentPercent;
        plan.Budget = WorkroomRecyclingValue.Budget(plan.TotalValue, plan.RecoveryPercent);
        if (item.Value == 0) { AllowDiscard(plan, item); return; }
        var preferred = wholeSpec != null ? WorkroomRecyclingRules.WholeRecipe(wholeSpec) : RecycleRecipe(template.Key, kind);
        if (preferred.Length == 0) { Refuse(plan, "processing_need_part"); return; }
        var fallback = wholeSpec != null ? WorkroomRecyclingRules.WholeFallback(wholeSpec) : BaseMaterial(template.Key, kind);
        var quotes = preferred.Append(fallback).Distinct(StringComparer.Ordinal).Select(Quote).ToArray();
        // Only a single, unrepairable target can be discarded. The former
        // single-item recovery loop produced something iff any allowed material
        // fit this same budget; quantity caps are always positive for one item.
        if (quotes.Any(material => material.Value <= plan.Budget)) Refuse(plan, "processing_discard_recoverable");
        else AllowDiscard(plan, item);
    }

    private static void AllowDiscard(Plan plan, WorkroomItemCodec.Snapshot item)
    {
        plan.Allowed = true;
        plan.Text = Text("processing_discard_preview", item.Name);
    }

    private static bool Repair(WorkroomStorageState.Record source, Plan plan)
    {
        var next = new List<WorkroomStorageState.Record>(WorkroomStorage.State.Items);
        foreach (var material in plan.Materials)
        {
            if (!WorkroomComponentParts.ConsumeMaterial(next, material.Id, material.Count))
            { Notice("processing_missing_materials"); return false; }
        }
        var repaired = WorkroomComponentParts.Repair(source.Item);
        if (repaired.Broken || repaired.Count != source.Item.Count ||
            repaired.Identifier != WorkroomComponentTemplates.Definition(source.Item.Identifier).Identifier ||
            repaired.Value != WorkroomComponentParts.IntactValue(source.Item) ||
            repaired.Nodes[0].Part?.CapacityGB != source.Item.Nodes[0].Part?.CapacityGB ||
            repaired.Nodes[0].Part?.Specification != source.Item.Nodes[0].Part?.Specification ||
            !WorkroomItemCodec.Identities(repaired).SequenceEqual(WorkroomItemCodec.Identities(source.Item)))
            throw new InvalidOperationException("修后状态、价值或身份不符合原件记录。");
        next[next.FindIndex(record => record.Id == source.Id)] = Copy(source, repaired);
        RequireCurrent(plan);
        // The complete replacement is prepared, all temporary native objects
        // cleaned and the complete storage graph validated before paying.
        WorkroomStorage.State.ValidateReplacement(next);
        RequireCurrent(plan);
        if (plan.CashRepair)
        {
            var owner = PlayerStore.instance ?? throw new InvalidOperationException("现金所有者已离开。");
            if (owner.GetCash() != plan.Cash) throw new InvalidOperationException("现金余额已变化，请刷新预览。");
            WorkroomCashRepair.Commit(owner, checked((int)plan.Fee), () => RequireCurrent(plan),
                () => WorkroomStorage.State.Replace(next));
        }
        else WorkroomStorage.State.Replace(next);
        Notice("processing_repaired");
        return true;
    }

    private static WorkroomStorageState.Record CaptureDiscardSource(WorkroomStorageState.Record record)
    {
        if (record.Item.Count != 1) throw new InvalidOperationException("报废只支持独立单件。");
        var snapshot = JsonSerializer.Deserialize<WorkroomItemCodec.Snapshot>(
            JsonSerializer.Serialize(record.Item, WorkroomStorageState.JsonOptions), WorkroomStorageState.JsonOptions)
            ?? throw new InvalidOperationException("报废来源快照无法建立。");
        WorkroomItemCodec.Validate(snapshot);
        return Copy(record, snapshot);
    }

    private static void RequireDiscardSource(Plan plan, WorkroomStorageState.Record source)
    {
        var expected = plan.Source;
        var actual = expected == null ? null : WorkroomStorage.State.Find(expected.Id);
        if (expected == null || actual == null || actual.Id != source.Id || actual.Place != "slot" ||
            actual.Slot != WorkroomComponentAssembly.Disassembly || actual.Place != expected.Place || actual.Slot != expected.Slot ||
            actual.X != expected.X || actual.Y != expected.Y || actual.Item.Count != 1 || !SameSnapshot(expected.Item, actual.Item) ||
            actual.Item.Value != plan.TotalValue || plan.RecoveryPercent != WorkroomRecyclingValue.CurrentPercent ||
            WorkroomRecyclingValue.Budget(actual.Item.Value, plan.RecoveryPercent) != plan.Budget)
            throw new InvalidOperationException("报废目标的状态、价格、位置或回收预算与预览不符。");
    }

    private static Material Quote(string id)
    {
        var snapshot = CaptureMaterial(id);
        return new Material { Id = id, Value = snapshot.Value };
    }

    private static WorkroomItemCodec.Snapshot CaptureMaterial(string id)
    {
        if (WorkroomItemCodec.CleanupPending) throw new InvalidOperationException("临时物品回收尚未完成。");
        var snapshot = WorkroomComponentParts.CaptureFactory(() =>
        {
            var native = DirectoryMaster.Item(id, true) ?? throw new InvalidOperationException("原版材料工厂缺失：" + id);
            // The known vanilla material factories produce individual units.
            // Leave factory data untouched and fail closed if that contract changes.
            return native;
        });
        if (WorkroomItemCodec.CleanupPending || snapshot.Identifier != id || snapshot.Count != 1 || snapshot.Value <= 0 ||
            snapshot.Broken || snapshot.Nodes.Count != 1 || snapshot.Nodes[0].Composition != null || snapshot.Nodes[0].SlotItems.Count != 0)
            throw new InvalidOperationException("原版材料价格或完整信息不可用：" + id);
        return snapshot;
    }

    private static bool UsableMaterial(WorkroomStorageState.Record record, string id) => WorkroomComponentParts.UsableMaterial(record, id);
    private static long Owned(string id) => WorkroomStorage.State.Items.Where(record => UsableMaterial(record, id))
        .Aggregate(0L, (sum, record) => checked(sum + record.Item.Count));
    private static string MaterialName(string id) => LanguageText.Get("material." + id);
    private static int Multiplier(int tier) => tier switch { 1 or 2 => 1, 3 or 4 => 2, 5 => 3,
        _ => throw new InvalidOperationException("子件档位无效。") };
    private static WorkroomStorageState.Record Copy(WorkroomStorageState.Record record, WorkroomItemCodec.Snapshot item) =>
        WorkroomStorageState.Copy(record, item: item);
    private static bool SameSnapshot(WorkroomItemCodec.Snapshot a, WorkroomItemCodec.Snapshot b) =>
        a.Version == b.Version && a.Name == b.Name && a.Value == b.Value && a.Sprite == b.Sprite && a.Atlas == b.Atlas &&
        a.Broken == b.Broken && a.Orientation == b.Orientation && a.Flipped == b.Flipped &&
        WorkroomItemCodec.SameShape(a.Footprint, b.Footprint) && WorkroomItemCodec.Equivalent(a, b) &&
        a.Nodes.Zip(b.Nodes, (left, right) => ExactJson(
            JsonSerializer.SerializeToElement(left, WorkroomStorageState.JsonOptions),
            JsonSerializer.SerializeToElement(right, WorkroomStorageState.JsonOptions))).All(equal => equal);

    // Destructive processing requires all preview facts. Object ordering is
    // harmless; no node field is omitted.
    private static bool ExactJson(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        if (a.ValueKind == JsonValueKind.Object)
        {
            var fields = a.EnumerateObject().ToArray();
            if (fields.Length != b.EnumerateObject().Count()) return false;
            foreach (var field in fields)
                if (!b.TryGetProperty(field.Name, out var other) || !ExactJson(field.Value, other)) return false;
            return true;
        }
        if (a.ValueKind == JsonValueKind.Array)
        {
            if (a.GetArrayLength() != b.GetArrayLength()) return false;
            for (var i = 0; i < a.GetArrayLength(); i++) if (!ExactJson(a[i], b[i])) return false;
            return true;
        }
        return a.ValueKind == JsonValueKind.String ? a.GetString() == b.GetString() : a.GetRawText() == b.GetRawText();
    }

    private static void RequireCurrent(Plan plan)
    {
        var state = WorkroomStorage.State;
        var owner = PlayerStore.IsInstanceExist() ? PlayerStore.instance : null;
        if (!state.Ready || owner == null || owner.Pointer != plan.Owner || owner.runID != plan.Run ||
            owner.saveSlotId != plan.SaveSlot || state.Epoch != plan.Epoch || state.Revision != plan.Revision ||
            (WorkroomComponentAssembly.At(WorkroomComponentAssembly.Disassembly)?.Id ?? "") != plan.SourceId ||
            WorkroomItemCodec.CleanupPending) throw new InvalidOperationException("处理上下文已变化或临时物品尚未回收。");
    }

    private static string[] RepairRecipe(string family, string kind)
    {
        if (family == "psu" && kind == "power")
            return new[] { "common_electronic", "wire" };
        return Array.Empty<string>();
    }

    private static bool CashRepair(string family, string kind) => kind == "body" ||
        family == "fan" && kind == "rotor" || family == "cooler" && (kind == "thermal" || kind == "fan");

    private static string[] RecycleRecipe(string family, string kind)
    {
        if (kind == "body") return new[] { "common_electronic", "wire", family == "fan" ? "printer_plastic" : "scrap_metal" };
        if (family == "psu" && kind == "power")
            return new[] { "common_electronic", "wire" };
        if (kind == "core" || kind == "memory" || kind == "controller" || kind == "flash")
            return new[] { "common_electronic" };
        if (family == "fan" && kind == "rotor") return new[] { "common_electronic", "nuts_metal", "printer_plastic" };
        if (family == "cooler" && kind == "fan") return new[] { "common_electronic", "nuts_metal" };
        if (kind == "thermal") return new[] { "scrap_metal", "nuts_metal" };
        return Array.Empty<string>();
    }

    private static string BaseMaterial(string family, string kind) =>
        family == "fan" && (kind == "body" || kind == "rotor") ? "printer_plastic" :
        kind == "body" || kind == "thermal" || family == "cooler" && kind == "fan" ? "scrap_metal" : "common_electronic";
}
