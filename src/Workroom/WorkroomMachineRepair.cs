using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Il2Cpp;
using UnityEngine;

namespace PCExpansion;

/// <summary>主板/机箱空本体维修；所有候选清理完毕后一次提交本体和材料。</summary>
internal static class WorkroomMachineRepair
{
    private sealed class Material
    {
        internal string Id = "";
    }
    private sealed class Plan
    {
        internal IntPtr Owner;
        internal string? Run;
        internal int SaveSlot;
        internal long Epoch, Revision, RetryAt;
        internal bool Chinese, Allowed;
        internal string Family = "", SourceId = "", IntactId = "", Text = "", Refusal = "repair_unavailable";
        internal long IntactValue;
        internal List<Material> Materials = new();
    }
    private static readonly Dictionary<string, Plan> cached = new(StringComparer.Ordinal);
    static WorkroomMachineRepair() => LanguageText.LanguageChanged += () => cached.Clear();
    private static string Text(string key, params object[] args) => WorkroomMachineAssembly.Text(key, args);
    private static void Notice(string key) => WorkroomStorage.NotifyText(Text(key));

    internal static string Preview(string family) => GetPlan(family)?.Text ?? Text("repair_unavailable");
    internal static bool CanExecute(string family) => GetPlan(family)?.Allowed == true;

    internal static void Execute(string family, long epoch)
    {
        if (!ValidFamily(family) || !WorkroomMachineAssembly.Ready(epoch)) return;
        var plan = GetPlan(family);
        if (plan == null || !plan.Allowed) { Notice(plan?.Refusal ?? "repair_unavailable"); return; }
        WorkroomStorage.Run(() =>
        {
            RequireCurrent(plan);
            var source = WorkroomMachineAssembly.At(WorkroomMachineAssembly.MainSlot(family))
                ?? throw new InvalidOperationException("本体维修目标已转移。");
            var next = new List<WorkroomStorageState.Record>(WorkroomStorage.State.Items);
            foreach (var material in plan.Materials)
            {
                if (!WorkroomComponentParts.ConsumeMaterial(next, material.Id, 1))
                { Notice("repair_missing_materials"); return; }
            }
            var repaired = RepairBody(source.Item, plan);
            next[next.FindIndex(record => record.Id == source.Id)] = Copy(source, repaired);
            RequireCurrent(plan);
            WorkroomStorage.State.Replace(next);
            Notice("repair_complete");
        });
    }

    private static bool ValidFamily(string family) => family == WorkroomMachineAssembly.Motherboard || family == WorkroomMachineAssembly.Case;
    private static Plan? GetPlan(string family)
    {
        var state = WorkroomStorage.State;
        if (!ValidFamily(family) || !state.Ready || WorkroomStorage.Busy || WorkroomItemCodec.CleanupPending ||
            !PlayerStore.IsInstanceExist() || PlayerStore.instance == null) return null;
        var owner = PlayerStore.instance;
        if (cached.TryGetValue(family, out var prior) && prior.Owner == owner.Pointer && prior.Run == owner.runID &&
            prior.SaveSlot == owner.saveSlotId && prior.Epoch == state.Epoch && prior.Revision == state.Revision &&
            prior.Chinese == LanguageText.IsChinese &&
            (prior.RetryAt == 0 || Environment.TickCount64 < prior.RetryAt)) return prior;
        var plan = new Plan { Owner = owner.Pointer, Run = owner.runID, SaveSlot = owner.saveSlotId,
            Epoch = state.Epoch, Revision = state.Revision, Family = family, Chinese = LanguageText.IsChinese };
        var source = WorkroomMachineAssembly.At(WorkroomMachineAssembly.MainSlot(family));
        plan.SourceId = source?.Id ?? "";
        try { BuildPlan(plan, source); RequireCurrent(plan); }
        catch (Exception ex)
        {
            Refuse(plan, "repair_unavailable");
            plan.RetryAt = Environment.TickCount64 + 2000;
            Core.Log?.Warning("工作间本体维修预览不可用，保留本体及材料：" + ex.Message);
        }
        // Retain facts only, never native entities. A failed quote retries after
        // two seconds; successful quotes are exact-owner/exact-revision scoped.
        cached[family] = plan;
        return plan;
    }

    private static void Refuse(Plan plan, string key)
    { plan.Allowed = false; plan.Refusal = key; plan.Text = Text(key); }

    private static void BuildPlan(Plan plan, WorkroomStorageState.Record? source)
    {
        if (source == null) { Refuse(plan, "repair_need_body"); return; }
        var snapshot = source.Item;
        WorkroomItemCodec.Validate(snapshot);
        var spec = Components.Find(snapshot.Identifier);
        var tag = plan.Family == WorkroomMachineAssembly.Motherboard ? Components.MotherboardTag : Components.CaseTag;
        if (spec == null || spec.Owner.Tag != tag || snapshot.Count != 1)
        { Refuse(plan, "repair_need_body"); return; }
        if (!Bare(snapshot)) { Refuse(plan, "repair_split_first"); return; }
        var candidate = WorkroomItemCodec.Restore(snapshot);
        try
        {
            if (candidate.Root.IsTag(CaseUnboxing.LockedTagName) || CaseContents.HasAnyStoredContents(candidate.Root))
            { Refuse(plan, "repair_split_first"); return; }
            if (!spec.Broken || !snapshot.Broken) { Refuse(plan, "repair_already_intact"); return; }
            if (spec.Tier < 1 || spec.Tier > 3) { Refuse(plan, "repair_high_tier"); return; }
            plan.IntactId = new Components.Item(spec.Owner, spec.Tier, false).Id;
            plan.IntactValue = Components.Find(plan.IntactId)!.Value;
            foreach (var id in Recipe(plan.Family, spec.Tier))
                plan.Materials.Add(new Material { Id = id });
        }
        finally { WorkroomItemCodec.Destroy(candidate); }
        if (WorkroomItemCodec.CleanupPending) throw new InvalidOperationException("本体预览候选尚未清理。");
        var lines = new List<string> { Text("repair_header", plan.IntactValue) };
        var enough = true;
        foreach (var material in plan.Materials)
        {
            var name = LanguageText.Get("material." + material.Id);
            var owned = WorkroomStorage.State.Items.Where(record => UsableMaterial(record, material.Id))
                .Aggregate(0L, (sum, record) => checked(sum + record.Item.Count));
            lines.Add(Text("repair_material", name, owned));
            if (owned < 1) enough = false;
        }
        plan.Allowed = enough; plan.Refusal = "repair_missing_materials";
        lines.Add(Text(enough ? "repair_ready" : plan.Refusal));
        plan.Text = string.Join("\n", lines);
    }

    private static bool Bare(WorkroomItemCodec.Snapshot snapshot) => snapshot.Nodes.Count == 1 &&
        snapshot.Nodes[0].Machine == null && snapshot.Nodes[0].Composition == null && snapshot.Nodes[0].SlotItems.Count == 0;
    private static string[] Recipe(string family, int tier) => family == WorkroomMachineAssembly.Motherboard
        ? new[] { "common_electronic", "wire", "nuts_metal" }.Take(tier).ToArray()
        : new[] { "scrap_metal", "metal_ingot", "printer_plastic" }.Take(tier).ToArray();

    private static WorkroomItemCodec.Snapshot RepairBody(WorkroomItemCodec.Snapshot source, Plan plan)
    {
        if (!Bare(source)) throw new InvalidOperationException("本体仍包含内部物品。");
        var native = JsonUtility.FromJson<SaveState>(source.Payload);
        var saved = native.saveItems[0];
        var candidate = WorkroomItemCodec.Restore(source);
        WorkroomItemCodec.Snapshot result;
        try
        {
            var item = candidate.Root;
            if (item.IsTag(CaseUnboxing.LockedTagName) || CaseContents.HasAnyStoredContents(item))
                throw new InvalidOperationException("上锁或有内容的本体必须先拆出内部物品。");
            Components.ApplySpec(item, plan.IntactId);
            // ApplySpec resets shapes and scalar defaults. Restore both saved
            // shapes and all non-spec instance fields before the final capture.
            item.SetShape(saved.itemShape.Build());
            item.modifiedShape = saved.itemModifiedShape.Build();
            item.RemoveAllGameItemType();
            foreach (var type in saved.itemTypes)
                if (type != "CONTRABAND" && type != HighEndParts.Identifier) item.SetGameItemType(type);
            var merged = WorkroomItemCodec.Capture(item);
            var original = Clone(source).Nodes[0];
            var changed = merged.Nodes[0];
            foreach (var scalar in original.Scalars)
                if (!RepairScalar(scalar.Key)) changed.Scalars[scalar.Key] = scalar.Value;
            PreserveTags(original.State, changed.State);
            PreserveTags(original.ModifiedState, changed.ModifiedState);
            // Keep unknown features and their condition data exactly. The spec
            // owns only the established computer price/qualification features.
            changed.Features = original.Features.Where(feature => !TradeFeature(feature))
                .Concat(changed.Features.Where(TradeFeature)).ToList();
            changed.Effects = original.Effects; changed.EffectsNull = original.EffectsNull;
            WorkroomItemCodec.RestoreLeafOnto(merged, item);
            item.SetValue(plan.IntactValue);
            result = WorkroomItemCodec.Capture(item);
            VerifyPreserved(source.Nodes[0], result.Nodes[0]);
            if (result.Identifier != plan.IntactId || result.Broken || result.Value != plan.IntactValue ||
                result.Count != source.Count || !Bare(result) ||
                result.Orientation != source.Orientation || result.Flipped != source.Flipped ||
                !SameShape(result.Footprint, source.Footprint) ||
                !WorkroomItemCodec.Identities(result).SequenceEqual(WorkroomItemCodec.Identities(source)) ||
                result.Nodes[0].State.Keys.Concat(result.Nodes[0].ModifiedState.Keys).Any(RepairTag))
                throw new InvalidOperationException("修后本体、形状、身份或价值不符合原记录。");
        }
        finally { WorkroomItemCodec.Destroy(candidate); }
        if (WorkroomItemCodec.CleanupPending) throw new InvalidOperationException("本体维修候选尚未清理。");
        // Full restore verifies factory/onLoaded behavior against the completed
        // snapshot, before the original custody or any material is replaced.
        var verification = WorkroomItemCodec.Restore(result);
        try
        {
            if (!WorkroomItemCodec.Equivalent(result, WorkroomItemCodec.Capture(verification.Root)))
                throw new InvalidOperationException("维修本体完整回读不一致。");
        }
        finally { WorkroomItemCodec.Destroy(verification); }
        if (WorkroomItemCodec.CleanupPending) throw new InvalidOperationException("本体维修回读候选尚未清理。");
        return result;
    }

    private static WorkroomItemCodec.Snapshot Clone(WorkroomItemCodec.Snapshot source) =>
        JsonSerializer.Deserialize<WorkroomItemCodec.Snapshot>(JsonSerializer.Serialize(source, WorkroomStorageState.JsonOptions),
            WorkroomStorageState.JsonOptions) ?? throw new InvalidOperationException("本体快照复制失败。");
    private static string Plain(string name) => name.StartsWith("_", StringComparison.Ordinal) && name.EndsWith("_k__BackingField", StringComparison.Ordinal)
        ? name.Substring(1, name.Length - 17) : name;
    private static bool RepairScalar(string name) => Plain(name) is "identifier" or "identifierName" or "name" or
        "shortDescription" or "longDescription" or "flavorText" or "spritePath" or "spriteAtlasPath" or
        "unitValue" or "unitBaseValue" or "lateUnitValue" or "backupUnitValue" or "soundContainerOpen" or "soundContainerClose" or "ITEM_TYPE_STRING";
    private static bool RepairTag(string tag) => tag == Components.BrokenTag || tag == "BROKEN_MACHINE";
    private static bool TradeTag(string tag) => tag == LowerAssemblerNpc.PurchaseTag || tag == UpperComputerBuyerNpc.PurchaseTag ||
        tag == "IGNORE_CHILD_VALUE" || tag == "CONTRABAND_ITEM_TAG" || tag == "CONTRABAND_LEVEL" ||
        tag is "CONTRABAND_LEVEL_NONE" or "CONTRABAND_LEVEL_LOW" or "CONTRABAND_LEVEL_MID" or "CONTRABAND_LEVEL_HIGH" or "CONTRABAND_LEVEL_CRITICAL";
    private static void PreserveTags(Dictionary<string, Dictionary<string, JsonElement>> original,
        Dictionary<string, Dictionary<string, JsonElement>> changed)
    {
        foreach (var pair in original) if (!RepairTag(pair.Key) && !TradeTag(pair.Key)) changed[pair.Key] = pair.Value;
        foreach (var tag in changed.Keys.Where(RepairTag).ToArray()) changed.Remove(tag);
    }
    private static bool TradeFeature(WorkroomItemCodec.Feature feature) => feature.Scalars.Any(pair =>
        (Plain(pair.Key) is "identifier" or "category") && pair.Value.ValueKind == JsonValueKind.String &&
        (pair.Value.GetString() is HighEndParts.FeatureId or "retailMarkUp" or "contrabandMarkUp" or
            "discountedHighContraband" or CaseEconomy.MachinePriceFeatureId));
    private static void VerifyPreserved(WorkroomItemCodec.Node source, WorkroomItemCodec.Node repaired)
    {
        foreach (var scalar in source.Scalars)
            if (!RepairScalar(scalar.Key) && (!repaired.Scalars.TryGetValue(scalar.Key, out var value) ||
                scalar.Value.GetRawText() != value.GetRawText()))
                throw new InvalidOperationException("维修改变了本体实例字段：" + scalar.Key);
        VerifyTags(source.State, repaired.State); VerifyTags(source.ModifiedState, repaired.ModifiedState);
        var before = source.Features.Where(feature => !TradeFeature(feature)).ToList();
        var after = repaired.Features.Where(feature => !TradeFeature(feature)).ToList();
        if (JsonSerializer.Serialize(before, WorkroomStorageState.JsonOptions) != JsonSerializer.Serialize(after, WorkroomStorageState.JsonOptions) ||
            (source.EffectsNull != repaired.EffectsNull ||
                JsonSerializer.Serialize(source.Effects, WorkroomStorageState.JsonOptions) != JsonSerializer.Serialize(repaired.Effects, WorkroomStorageState.JsonOptions)))
            throw new InvalidOperationException("维修改变了本体额外特性、条件或效果。");
    }
    private static void VerifyTags(Dictionary<string, Dictionary<string, JsonElement>> source,
        Dictionary<string, Dictionary<string, JsonElement>> repaired)
    {
        foreach (var tag in source)
        {
            if (RepairTag(tag.Key) || TradeTag(tag.Key)) continue;
            if (!repaired.TryGetValue(tag.Key, out var other) || tag.Value.Count != other.Count ||
                tag.Value.Any(field => !other.TryGetValue(field.Key, out var value) || field.Value.GetRawText() != value.GetRawText()))
                throw new InvalidOperationException("维修改变了本体永久标记：" + tag.Key);
        }
    }
    private static bool SameShape(WorkroomItemCodec.Shape a, WorkroomItemCodec.Shape b) =>
        a.Width == b.Width && a.Height == b.Height && a.Cells.SequenceEqual(b.Cells);
    private static bool UsableMaterial(WorkroomStorageState.Record record, string id) => WorkroomComponentParts.UsableMaterial(record, id);
    private static WorkroomStorageState.Record Copy(WorkroomStorageState.Record record, WorkroomItemCodec.Snapshot item) =>
        new() { Id = record.Id, Place = record.Place, Slot = record.Slot, X = record.X, Y = record.Y, Item = item };
    private static void RequireCurrent(Plan plan)
    {
        var state = WorkroomStorage.State;
        var owner = PlayerStore.IsInstanceExist() ? PlayerStore.instance : null;
        if (!state.Ready || owner == null || owner.Pointer != plan.Owner || owner.runID != plan.Run ||
            owner.saveSlotId != plan.SaveSlot || state.Epoch != plan.Epoch || state.Revision != plan.Revision ||
            (WorkroomMachineAssembly.At(WorkroomMachineAssembly.MainSlot(plan.Family))?.Id ?? "") != plan.SourceId ||
            WorkroomItemCodec.CleanupPending)
            throw new InvalidOperationException("本体维修上下文已变化或临时物品尚未回收。");
    }
}
