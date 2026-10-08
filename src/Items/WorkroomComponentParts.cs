using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace PCExpansion;

/// <summary>叶子件及组成记录。元数据随原版标签保存，快照副本供纯数据预览使用。</summary>
internal static class WorkroomComponentParts
{
    internal const string CompositionTag = "PCREPAIR_COMPONENT_COMPOSITION_V3";
    internal const string PartTag = "PCREPAIR_COMPONENT_PART_STATE_V1";
    internal const string SeedTag = "PCREPAIR_COMPONENT_DAMAGE_SEED_V1";
    // 这些数字是当前独立协议版本；稳定标签不随发布版本改名。
    internal const string OriginTag = "PCREPAIR_COMPONENT_ORIGIN_V2";
    internal const string ValueTag = "PCREPAIR_COMPONENT_PART_VALUE_V2";
    internal const int PartVersion = 3;
    internal const string ClaimTag = "PCREPAIR_COMPONENT_CLAIM_V1";
    internal sealed class ClaimInfo
    {
        public int Version { get; set; }
        public string Source { get; set; } = "actual";
        public string Model { get; set; } = "";
        public int Tier { get; set; }
        public long CapacityGB { get; set; }
    }
    internal sealed class PartInfo
    {
        public PartInfo() { }
        public int Version { get; set; }
        public string Family { get; set; } = "";
        public string Kind { get; set; } = "";
        public int Tier { get; set; }
        public int CapacityGB { get; set; }
        public string Specification { get; set; } = "";
        public long Intact { get; set; }
        public long Current { get; set; }
        public bool Broken { get; set; }
    }
    private static readonly Dictionary<string, Sprite> sprites = new();
    private static bool IndependentItem(string identifier) => Components.Find(identifier) != null ||
        identifier is "pcrepair.sign_computer" or "pcrepair.contact_card_0504" or "pcrepair.computer_build_guide";
    private static bool PartItem(string identifier) => WorkroomComponentTemplates.TryPart(identifier, out _, out _, out _);
    private static bool HandlesStack(GameItem item, GameItem? other) =>
        IndependentItem(Core.Clean(item.identifier)) || PartItem(Core.Clean(item.identifier)) ||
        other != null && (IndependentItem(Core.Clean(other.identifier)) || PartItem(Core.Clean(other.identifier)));
    // All Mod items remain independent. Keep both concrete targets because
    // native inventory code can inline MayStack before obtaining a stack slot.
    [HarmonyPatch(typeof(GameItem), nameof(GameItem.MayStack), new[] { typeof(GameItem) })]
    internal static class PartStackPatch
    {
        private static bool Prefix(GameItem __instance, GameItem otherItem, ref bool __result)
        {
            if (!HandlesStack(__instance, otherItem)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetStackSlot), new[] { typeof(GameItem) })]
    internal static class PartStackSlotPatch
    {
        private static bool Prefix(GameItem __instance, GameItem itemToBeStacked, ref SlotMarker __result)
        {
            if (!HandlesStack(__instance, itemToBeStacked)) return true;
            __result = null!;
            return false;
        }
    }
    internal static long BrokenValue(long intact) => intact == 0 ? 0 : Math.Max(1, intact / 5);
    internal static PartInfo? ReadPart(GameItem item)
    {
        if (!item.IsTag(PartTag)) return null;
        var part = JsonSerializer.Deserialize<PartInfo>(item.state.GetTag(PartTag).valueString, WorkroomStorageState.JsonOptions)
            ?? throw new InvalidOperationException("部件价格记录为空。");
        if (part.Version != PartVersion) throw new InvalidOperationException("部件保存版本不支持。");
        return part;
    }
    internal static uint? ReadSeed(GameItem item)
    {
        if (!item.IsTag(SeedTag)) return null;
        if (!uint.TryParse(item.state.GetTag(SeedTag).valueString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
            throw new InvalidOperationException("损坏种子无效。");
        return seed;
    }
    internal static uint StableSeed(string family, int identity)
    {
        uint hash = 2166136261;
        foreach (var c in "component.damage.v1/" + family + "/" + identity.ToString(CultureInfo.InvariantCulture))
            hash = unchecked((hash ^ c) * 16777619);
        return hash == 0 ? 1 : hash;
    }
    internal static void EnsureSeed(GameItem item, string family)
    {
        if (WorkroomItemCodec.Restoring || item.IsTag(SeedTag)) return;
        item.EnableTag(SeedTag, false);
        item.state.GetTag(SeedTag).SetString(StableSeed(family, item.uniqueId).ToString(CultureInfo.InvariantCulture));
    }
    private static void WritePart(GameItem item, PartInfo info)
    {
        item.EnableTag(PartTag, false);
        item.state.GetTag(PartTag).SetString(JsonSerializer.Serialize(info, WorkroomStorageState.JsonOptions));
        SetPartValue(item, info.Current);
    }
    private static void SetPartValue(GameItem item, long value)
    {
        item.EnableTag(ValueTag, false);
        item.state.GetTag(ValueTag).SetString(value.ToString(CultureInfo.InvariantCulture));
        item.SetValue(value);
    }
    internal static void ValidatePart(WorkroomItemCodec.Node node)
    {
        var part = node.Part;
        if (part == null)
        {
            if (WorkroomComponentTemplates.TryPart(node.Identifier, out _, out _, out _))
                throw new InvalidOperationException("新规格部件缺少元数据。");
            return;
        }
        if (part.Version != PartVersion || part.Intact < 0 || part.Current < 0 ||
            !WorkroomComponentTemplates.TryPart(node.Identifier, out var family, out var kind, out var tier) ||
            family.Key != part.Family || kind != part.Kind || tier != part.Tier)
            throw new InvalidOperationException("部件元数据与身份不符。");
        var definition = WorkroomComponentTemplates.Definition(node.Identifier);
        if (part.CapacityGB != definition.CapacityGB || part.Specification != definition.Specification)
            throw new InvalidOperationException("部件容量或规格与身份不符。");
        if (node.Identifier != WorkroomComponentTemplates.ItemId(definition, part.Broken))
            throw new InvalidOperationException("部件身份与保存状态不符。");
    }
    internal static GameItem Create(WorkroomComponentTemplates.Template template, string kind, int tier,
        bool broken = false, long? intactValue = null, long? actualValue = null) =>
        Create(WorkroomComponentTemplates.DefaultPart(template, kind, tier), broken, intactValue, actualValue);
    internal static GameItem Create(WorkroomComponentTemplates.PartDefinition definition,
        bool broken = false, long? intactValue = null, long? actualValue = null)
    {
        var template = definition.Family; var kind = definition.Kind; var tier = definition.Tier;
        var item = DirectoryMaster.Item("common_electronic", true) ?? throw new InvalidOperationException("电子零件模板缺失。");
        try
        {
            var intact = intactValue ?? definition.FixedValue ?? WorkroomComponentTemplates.PartValue(template, kind, tier,
                WorkroomComponentTemplates.BaseValue(template, tier));
            WritePart(item, new PartInfo { Version = PartVersion, Family = template.Key, Kind = kind, Tier = tier,
                CapacityGB = definition.CapacityGB, Specification = definition.Specification, Intact = intact,
                Broken = broken, Current = actualValue ?? (broken ? BrokenValue(intact) : intact) });
            Apply(item, definition);
            item.onLoaded = (Il2CppSystem.Action<GameItem>)(loaded => Apply(loaded, definition, true));
            return item;
        }
        catch { WorkroomItemCodec.Destroy(new WorkroomItemCodec.Candidate { Root = item, Nodes = new List<GameItem> { item } }); throw; }
    }
    private static void Apply(GameItem item, WorkroomComponentTemplates.PartDefinition definition, bool loaded = false)
    {
        var savedShape = loaded ? item.shape : null;
        var savedModifiedShape = loaded ? item.modifiedShape : null;
        var template = definition.Family; var kind = definition.Kind; var tier = definition.Tier;
        var info = ReadPart(item) ?? throw new InvalidOperationException("新规格部件缺少元数据。");
        // Loading must retain a current identity matching the saved damage state.
        var identifier = loaded ? Core.Clean(item.identifier) : WorkroomComponentTemplates.ItemId(definition, info.Broken);
        ValidatePart(new WorkroomItemCodec.Node { Identifier = identifier, Part = info });
        var broken = info.Broken;
        var value = info.Current;
        item.identifier = identifier;
        UpdateText(item, definition, broken, info.Intact);
        // Preserve the native saved count so single-item validation can reject
        // unsupported quantities instead of silently converting the record.
        SetPartValue(item, value);
        var w = kind == "body" ? template.BoardWidth : 1;
        var h = kind == "body" ? template.BoardHeight : 1;
        item.SetShape(new GridShapeBuilder(w, h).SetDataFill(1).Build());
        if (savedShape != null) item.SetShape(savedShape);
        if (savedModifiedShape != null) item.modifiedShape = savedModifiedShape;
        item.spritePath = WorkroomComponentTemplates.ItemId(definition, broken); item.spriteAtlasPath = ""; item.spriteChanged = true;
        item.RemoveAllGameItemType(); item.SetGameItemType("MATERIAL"); item.SetGameItemType(ComputerSuppliesType.Identifier);
        item.EnableTag("PCREPAIR_COMPONENT_PART", false);
        if (template.Key == "gpu") { item.EnableTag("PCREPAIR_GPU_PART", false); item.EnableTag("PCREPAIR_GPU_PART_" + kind.ToUpperInvariant(), false); }
        foreach (var tag in Components.TierTags) item.DisableTag(tag, false);
        if (tier is >= 1 and <= 5) item.EnableTag(Components.TierTags[tier - 1], false);
        if (broken) item.EnableTag(Components.BrokenTag, false); else item.DisableTag(Components.BrokenTag, false);
        item.DisableTag("BROKEN_MACHINE", false);
        item.mayThisTargetItemFunc = null; item.canThisTargetItemFunc = null; item.onThisTargetItemFunc = null;
        item.mayActivateSlotItemFunc = null; item.canActivateSlotItemFunc = null; item.onActivateSlotItemFunc = null;
        item.soundDragStart = "module_drag"; item.soundDragEnd = "module_drop";
    }
    internal static string PartName(WorkroomComponentTemplates.PartDefinition definition, bool broken = false)
    {
        var name = definition.Family.PartName(definition.Kind);
        if (definition.CapacityGB > 0) name = WorkroomComponentTemplates.Text("capacity_part_name", name, FormatCapacity(definition.CapacityGB));
        else if (definition.Specification != "") name = WorkroomComponentTemplates.Text("spec_part_name", name,
            WorkroomComponentTemplates.Text("spec_" + definition.Specification));
        else if (definition.Tier > 0) return WorkroomComponentTemplates.Text(broken ? "part_broken_name" : "part_name", name, definition.Tier);
        return broken ? WorkroomComponentTemplates.Text("broken_spec_name", name) : name;
    }
    private static void UpdateText(GameItem item, WorkroomComponentTemplates.PartDefinition definition, bool broken, long intact)
    {
        var name = PartName(definition, broken);
        item.identifierName = name; item.SetName(name);
        item.shortDescription = WorkroomComponentTemplates.Text("part_description", definition.Family.Name, intact);
        if (definition.CapacityGB > 0) item.shortDescription += "\n" + WorkroomComponentTemplates.Text("capacity_part_description", FormatCapacity(definition.CapacityGB));
        else if (definition.Specification != "") item.shortDescription += "\n" + WorkroomComponentTemplates.Text("common_part_description");
        if (definition.Family.Key == "psu" && definition.Kind == "power")
            item.shortDescription += "\n" + WorkroomComponentTemplates.Text("power_integrated");
        if (definition.Family.Key == "fan" && definition.Kind == "rotor") item.shortDescription += "\n" + WorkroomComponentTemplates.Text("rotor_integrated");
        item.longDescription = item.shortDescription; item.flavorText = "";
    }
    internal static int ProcessingTier(WorkroomItemCodec.Snapshot part) => WorkroomComponentTemplates.Definition(part.Identifier).ProcessingTier;
    internal static string FormatCapacity(long capacity) => capacity >= 1024 && capacity % 1024 == 0 ? capacity / 1024 + "TB" : capacity + "GB";
    internal static string CapacityText(IEnumerable<WorkroomItemCodec.Snapshot> parts)
    {
        var capacitive = parts.Where(part => WorkroomComponentTemplates.TryPart(part.Identifier, out _, out var kind, out _) &&
            kind is "memory" or "flash").ToList();
        if (capacitive.Count == 0) return "";
        var total = capacitive.Sum(part => (long)(part.Nodes[0].Part?.CapacityGB ?? 0));
        var family = WorkroomComponentTemplates.Definition(capacitive[0].Identifier).Family.Key;
        var label = WorkroomComponentTemplates.Text(family == "gpu" ? "capacity_vram" : family == "ram" ? "capacity_ram" : "capacity_storage");
        var intact = capacitive.Where(part => !part.Broken).Sum(part => (long)(part.Nodes[0].Part?.CapacityGB ?? 0));
        return WorkroomComponentTemplates.Text("capacity_health_summary", label, FormatCapacity(total), FormatCapacity(intact));
    }
    internal static WorkroomItemCodec.Snapshot CaptureFactory(Func<GameItem> factory)
    {
        if (WorkroomItemCodec.CleanupPending) throw new InvalidOperationException("临时物品尚未清理。");
        var native = factory();
        var candidate = new WorkroomItemCodec.Candidate { Root = native, Nodes = new List<GameItem> { native } };
        WorkroomItemCodec.Snapshot snapshot;
        try { snapshot = WorkroomItemCodec.Capture(native, out var nodes); candidate.Nodes = nodes; }
        finally { WorkroomItemCodec.Destroy(candidate); }
        if (WorkroomItemCodec.CleanupPending) throw new InvalidOperationException("临时物品回收尚未完成。");
        return snapshot;
    }
    internal static long IntactValue(WorkroomItemCodec.Snapshot snapshot) => snapshot.Nodes[0].Part?.Intact ?? snapshot.Value;
    internal static WorkroomItemCodec.Snapshot Repair(WorkroomItemCodec.Snapshot snapshot)
    {
        if (!WorkroomComponentTemplates.TryPart(snapshot.Identifier, out var family, out var kind, out var tier))
            throw new InvalidOperationException("只允许修理叶子部件。");
        var candidate = WorkroomItemCodec.Restore(snapshot);
        WorkroomItemCodec.Snapshot result;
        try
        {
            var item = candidate.Root; var intact = IntactValue(snapshot);
            var original = snapshot.Nodes[0].Part ?? throw new InvalidOperationException("新规格部件缺少元数据。");
            var definition = WorkroomComponentTemplates.Definition(snapshot.Identifier);
            WritePart(item, new PartInfo { Version = PartVersion, Family = family.Key, Kind = kind, Tier = tier,
                CapacityGB = original.CapacityGB, Specification = original.Specification, Intact = intact, Current = intact });
            item.DisableTag(Components.BrokenTag, false); item.DisableTag("BROKEN_MACHINE", false);
            item.identifier = definition.Identifier;
            item.spritePath = definition.Identifier; item.spriteChanged = true;
            UpdateText(item, definition, false, intact);
            result = WorkroomItemCodec.Capture(item);
        }
        finally { WorkroomItemCodec.Destroy(candidate); }
        if (WorkroomItemCodec.CleanupPending) throw new InvalidOperationException("临时维修对象尚未清理。");
        return result;
    }
    internal static bool UsableMaterial(WorkroomStorageState.Record record, string id) =>
        (record.Place == "room" || record.Place == "box") && WorkroomItemCodec.IsRepairMaterial(id) &&
        record.Item.Identifier == id && record.Item.Count == 1 && !record.Item.Broken && WorkroomItemCodec.StackLeaf(record.Item);
    internal static bool ConsumeMaterial(List<WorkroomStorageState.Record> next, string id, int count)
    {
        if (count <= 0 || !WorkroomItemCodec.IsRepairMaterial(id)) throw new InvalidOperationException("维修耗材数量或类别无效。");
        // Edit only the private plan; consume independent single-item records.
        var selected = next.Where(record => UsableMaterial(record, id)).OrderBy(record => record.Place == "room" ? 0 : 1).ToArray();
        if (selected.Length < count) return false;
        var remaining = count;
        foreach (var record in selected)
        {
            if (remaining == 0) break;
            var index = next.FindIndex(value => value.Id == record.Id);
            next.RemoveAt(index);
            remaining--;
        }
        return remaining == 0;
    }
    internal static List<WorkroomItemCodec.Snapshot>? ReadComposition(GameItem item)
    {
        if (!item.IsTag(CompositionTag)) return null;
        var parts = WorkroomAssemblyData.Decode<List<WorkroomItemCodec.Snapshot>>(item.state.GetTag(CompositionTag).valueString)
            ?? throw new InvalidOperationException("组成记录为空。");
        ValidateComposition(parts);
        if (!WorkroomComponentTemplates.TryWhole(item.identifier, out var whole, out _, out _) ||
            !WorkroomComponentTemplates.TryPart(parts[0].Identifier, out var family, out _, out _) || family != whole)
            throw new InvalidOperationException("组成记录与成品类别不符。");
        return parts;
    }
    internal static void ValidateComposition(List<WorkroomItemCodec.Snapshot> parts)
    {
        if (parts.Count < 2 || parts.Count > 8) throw new InvalidOperationException("组成数量无效。");
        WorkroomComponentTemplates.Template? family = null; var kinds = new List<string>();
        foreach (var part in parts)
        {
            if (part == null || part.Count != 1 || part.Nodes.Count != 1 || part.Nodes[0].Composition != null ||
                part.Nodes[0].SlotItems.Count != 0 || part.Nodes[0].Identifier != part.Identifier ||
                !WorkroomComponentTemplates.TryPart(part.Identifier, out var spec, out var kind, out var tier))
                throw new InvalidOperationException("组成部件无效。");
            family ??= spec;
            if (family != spec) throw new InvalidOperationException("组成包含不同类别部件。");
            ValidatePart(part.Nodes[0]);
            kinds.Add(kind);
        }
        if (family == null || family.Kinds.Any(kind => kinds.Count(k => k == kind) < family.MinCount(kind) ||
            kinds.Count(k => k == kind) > family.MaxCount(kind)))
            throw new InvalidOperationException("组成数量与六类配方不符。");
    }
    internal static int EffectiveTier(List<WorkroomItemCodec.Snapshot> parts)
    {
        if (parts.Count == 0 || !WorkroomComponentTemplates.TryPart(parts[0].Identifier, out var family, out _, out _)) return 0;
        if (family.Key == "ram")
        {
            var capacity = parts.Sum(part => (long)(part.Nodes[0].Part?.CapacityGB ?? 0));
            return WorkroomComponentTemplates.RamTier(capacity);
        }
        return parts.Select(part => WorkroomComponentTemplates.TryPart(part.Identifier, out _, out var kind, out var tier) &&
            family.PerformanceKind(kind) ? tier : 0).Where(tier => tier > 0).DefaultIfEmpty(0).Min();
    }
    internal static int ConfigurationTier(GameItem item) => WorkroomMachineFacts.Read(item) is { } machine ?
        machine.ConfigurationTier : ReadComposition(item) is { } parts ? EffectiveTier(parts) : Components.TierOf(item);
    internal static void RefreshComposition(GameItem item)
    {
        var parts = ReadComposition(item); if (parts == null) return;
        var spec = Components.Find(item.identifier)!; var broken = parts.Any(part => part.Broken);
        if (spec.Broken != broken) throw new InvalidOperationException("成品状态与内部部件不符。");
        var tier = EffectiveTier(parts);
        if (tier != spec.Tier) throw new InvalidOperationException("成品身份与真实配置不符。");
        var capacityGb = parts.Sum(part => (long)(part.Nodes[0].Part?.CapacityGB ?? 0));
        var name = spec.DisplayName;
        if (capacityGb > 0)
        {
            var capacityName = FormatCapacity(capacityGb);
            name = System.Text.RegularExpressions.Regex.IsMatch(name, @"\d+(?:GB|TB)") ?
                System.Text.RegularExpressions.Regex.Replace(name, @"\d+(?:GB|TB)", capacityName) : name + " " + capacityName;
        }
        var claim = item.IsTag(ClaimTag) ? JsonSerializer.Deserialize<ClaimInfo>(item.state.GetTag(ClaimTag).valueString,
            WorkroomStorageState.JsonOptions) : null;
        claim ??= new ClaimInfo { Version = 1 };
        if (claim.Version != 1) throw new InvalidOperationException("商品标示版本无效。");
        if (claim.Source == "actual")
        {
            claim.Model = name; claim.Tier = tier; claim.CapacityGB = capacityGb;
            item.EnableTag(ClaimTag, false);
            item.state.GetTag(ClaimTag).SetString(JsonSerializer.Serialize(claim, WorkroomStorageState.JsonOptions));
        }
        else if (claim.Source == "manual" && !string.IsNullOrWhiteSpace(claim.Model)) name = claim.Model;
        else throw new InvalidOperationException("商品标示来源无效。");
        item.identifierName = name; item.SetName(name);
        var desc = spec.Desc + "\n" + WorkroomComponentTemplates.Text("configuration", spec.Tier, tier) +
            "\n" + WorkroomComponentTemplates.Text("composition_info", parts.Count);
        var capacity = CapacityText(parts); if (capacity != "") desc += "\n" + capacity;
        item.shortDescription = desc; item.longDescription = desc;
        Components.SetTradeProperties(item, !broken && Components.IsHighEndEligible(new Components.Item(spec.Owner, tier, false)), !broken && tier == 5);
        LowerAssemblerNpc.RefreshPurchaseEligibility(item);
        // Trade features must not change the stored base value of a composition.
        item.SetValue(parts.Aggregate(0L, (sum, part) => checked(sum + part.Value)));
    }
    internal static bool IsSpriteKey(string key) => WorkroomComponentTemplates.TryPart(key, out _, out _, out _) ||
        WorkroomComponentTemplates.TryWhole(key, out var family, out _, out _) && family.Key == "ssd";
    internal static Sprite? Sprite(string key)
    {
        var broken = key.EndsWith("_broken", StringComparison.Ordinal);
        WorkroomComponentTemplates.Template family; string kind; int tier;
        if (WorkroomComponentTemplates.TryWhole(key, out family, out tier, out _) && family.Key == "ssd") kind = "ssd_whole";
        else if (!WorkroomComponentTemplates.TryPart(key, out family, out kind, out tier)) return null;
        var definition = kind == "ssd_whole" ? null : WorkroomComponentTemplates.Definition(key);
        var advanced = tier >= 4 || family.Key == "ram" && definition?.CapacityGB == 16;
        var cacheKey = family.Key + "." + kind + (kind == "ssd_whole" ? ".t" + tier : advanced ? ".advanced" : "") + (broken ? ".broken" : "");
        if (sprites.TryGetValue(cacheKey, out var existing) && AssemblyDebugUi.Alive(existing)) return existing;
        var artwork = kind == "ssd_whole" ? "component_ssd_t" + tier : "part_" + family.Key + "_" + kind +
            (family.PerformanceKind(kind) || family.Key == "ram" && kind == "memory" ? advanced ? "_advanced" : "_ordinary" : "");
        var formal = SpriteAssets.PartArtwork(artwork + (broken ? "_broken" : ""),
            kind == "body" ? family.BoardWidth : kind == "ssd_whole" ? 3 : 1, kind == "body" ? family.BoardHeight : 1);
        if (formal != null) { sprites[cacheKey] = formal; return formal; }
        var width = kind == "body" ? family.BoardWidth * 16 : kind == "ssd_whole" ? 48 : 16;
        var height = kind == "body" ? family.BoardHeight * 16 : 16;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        { name = "PCExpansion.Part." + cacheKey, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var fill = broken ? new Color(.36f,.22f,.22f,1) : kind == "body" ? new Color(.12f,.42f,.30f,1) :
            advanced ? new Color(.42f,.34f,.18f,1) : new Color(.28f,.34f,.44f,1);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var color = x < 2 || y < 2 || x >= width - 2 || y >= height - 2 ? new Color(.68f,.70f,.66f,1) :
                (x + y) % 8 == 0 ? new Color(.67f,.58f,.26f,1) : fill;
            if (kind == "ssd_whole") color = x < 7 ? new Color(.74f,.65f,.30f,1) :
                y > 3 && y < 12 && x % 13 > 2 && x % 13 < 11 ? new Color(.14f,.16f,.18f,1) : fill;
            if (broken && x == y % width) color = new Color(.82f,.34f,.25f,1);
            texture.SetPixel(x, y, color);
        }
        // Generated parts also use the native inventory's alpha-hit-tested Image.
        texture.Apply(false, false); UnityEngine.Object.DontDestroyOnLoad(texture);
        var sprite = UnityEngine.Sprite.Create(texture, new Rect(0,0,width,height), new Vector2(.5f,.5f), 100);
        UnityEngine.Object.DontDestroyOnLoad(sprite); sprites[cacheKey] = sprite; return sprite;
    }
    [HarmonyPatch(typeof(ModItemDirectory), "InitDirectory")]
    internal static class DirectoryPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ModItemDirectory __instance)
        {
            var count = 0;
            foreach (var definition in WorkroomComponentTemplates.Definitions)
            {
                var captured = definition;
                __instance.Set(captured.Identifier,
                    (Il2CppSystem.Func<GameItem>)(Func<GameItem>)(() => Create(captured)));
                __instance.Set(WorkroomComponentTemplates.ItemId(captured, true),
                    (Il2CppSystem.Func<GameItem>)(Func<GameItem>)(() => Create(captured, true)));
                count += 2;
            }
            Core.Log?.Msg("六类叶子部件工厂注册完成：" + count + " 个（完好/破损各 " + WorkroomComponentTemplates.Definitions.Count + " 个，仅当前配方）。");
        }
    }
}
