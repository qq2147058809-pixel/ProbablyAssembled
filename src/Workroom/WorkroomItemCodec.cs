using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Il2Cpp;
using UnityEngine;

namespace PCExpansion;

/// <summary>隔离原版全局编码堆；保存原版物品图及原版存档之外的实例数据。</summary>
internal static class WorkroomItemCodec
{
    internal sealed class Shape
    {
        public Shape() { }
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[] Cells { get; set; } = Array.Empty<byte>();
    }

    internal sealed class Snapshot
    {
        public Snapshot() { }
        public int Version { get; set; }
        [JsonConverter(typeof(WorkroomPayloadConverter))]
        public string Payload { get; set; } = "";
        public string Name { get; set; } = "";
        public string Identifier { get; set; } = "";
        public int Count { get; set; }
        public long Value { get; set; }
        public string Sprite { get; set; } = "";
        public string Atlas { get; set; } = "";
        public bool Broken { get; set; }
        public int Orientation { get; set; }
        public bool Flipped { get; set; }
        public Shape Footprint { get; set; } = new();
        public List<Node> Nodes { get; set; } = new();
    }

    internal sealed class Node
    {
        public Node() { }
        public long Uuid { get; set; }
        public int UniqueId { get; set; }
        public string Identifier { get; set; } = "";
        public Dictionary<string, JsonElement> Scalars { get; set; } = new();
        public Dictionary<string, Dictionary<string, JsonElement>> State { get; set; } = new();
        public Dictionary<string, Dictionary<string, JsonElement>> ModifiedState { get; set; } = new();
        public List<Feature> Features { get; set; } = new();
        public List<Snapshot> SlotItems { get; set; } = new();
        public List<Snapshot>? Composition { get; set; }
        public WorkroomComponentParts.PartInfo? Part { get; set; }
        public uint? DamageSeed { get; set; }
        public WorkroomMachineAssembly.MachineInfo? Machine { get; set; }
        // Current records require an explicit effects list, including empty lists.
        public List<Effect?>? Effects { get; set; }
        public bool EffectsNull { get; set; }
    }

    internal sealed class Effect
    {
        public Effect() { }
        public string Identifier { get; set; } = "";
        public Dictionary<string, JsonElement> Scalars { get; set; } = new();
        public Dictionary<string, Dictionary<string, JsonElement>> Tags { get; set; } = new();
        public bool Callback { get; set; }
        public bool InitCallback { get; set; }
        public bool EndCallback { get; set; }
    }

    internal sealed class Feature
    {
        public Feature() { }
        public Dictionary<string, JsonElement> Scalars { get; set; } = new();
        public Dictionary<string, JsonElement>? Fake { get; set; }
        public Dictionary<string, JsonElement>? Real { get; set; }
    }

    internal sealed class Candidate
    {
        private readonly IntPtr owner = PlayerStore.IsInstanceExist() && PlayerStore.instance != null ? PlayerStore.instance.Pointer : IntPtr.Zero;
        private readonly string? run = PlayerStore.IsInstanceExist() ? PlayerStore.instance?.runID : null;
        private readonly int slot = PlayerStore.IsInstanceExist() && PlayerStore.instance != null ? PlayerStore.instance.saveSlotId : 0;
        internal bool SameOwner => PlayerStore.IsInstanceExist() && PlayerStore.instance != null &&
            PlayerStore.instance.Pointer == owner && PlayerStore.instance.runID == run && PlayerStore.instance.saveSlotId == slot;
        internal GameItem Root = null!;
        internal List<GameItem> Nodes = new();
        internal bool Cleaned;
    }

    [ThreadStatic] private static int restoring;
    internal static bool Restoring => restoring > 0;
    private static readonly List<Candidate> failedCleanup = new();
    private static readonly HashSet<string> repairMaterialIds = new(ComponentRepair.GetRepairMaterialIds(), StringComparer.Ordinal);
    internal static bool IsRepairMaterial(string identifier) => repairMaterialIds.Contains(identifier);
    internal static bool CleanupPending => failedCleanup.Count != 0;
    internal static void RetryCleanup()
    {
        foreach (var candidate in failedCleanup.ToArray())
            try { Destroy(candidate); } catch (Exception ex) { Core.Debug("重试物品清理：" + ex.Message); }
    }
    internal static void EndSessionCleanup()
    {
        RetryCleanup();
        if (failedCleanup.Count != 0) Core.Log?.Warning("离开旧存档时仍有实体清理失败；记录已保留，旧对象不在新档继续操作。");
        failedCleanup.Clear();
    }

    private sealed class HeapLease : IDisposable
    {
        private readonly long uuid = SaveManager.currentUUID;
        private readonly Il2CppSystem.Collections.Generic.Dictionary<GameItem, long> heap = SaveManager.itemHeap;
        private readonly Il2CppSystem.Collections.Generic.Dictionary<long, GameItem> dict = SaveManager.itemDict;
        private readonly string json = SaveManager.saveJson;
        private readonly PlayerStore store;
        private readonly Il2CppSystem.Collections.Generic.List<ItemFeature> features;
        internal HeapLease()
        {
            store = WorkroomGameAdapter.ExistingStore() ?? throw new InvalidOperationException("店铺不可用。");
            features = store.savedItemFeatureList;
            var isolatedFeatures = new Il2CppSystem.Collections.Generic.List<ItemFeature>();
            var isolatedHeap = new Il2CppSystem.Collections.Generic.Dictionary<GameItem, long>();
            var isolatedDict = new Il2CppSystem.Collections.Generic.Dictionary<long, GameItem>();
            // InitHeap clears an existing dictionary in place. Keep the native
            // save operation's dictionaries untouched, including nested captures.
            try
            {
                store.savedItemFeatureList = isolatedFeatures;
                SaveManager.currentUUID = 0;
                SaveManager.itemHeap = isolatedHeap;
                SaveManager.itemDict = isolatedDict;
                SaveManager.saveJson = "";
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            SaveManager.currentUUID = uuid; SaveManager.itemHeap = heap; SaveManager.itemDict = dict;
            SaveManager.saveJson = json;
            store.savedItemFeatureList = features;
        }
    }

    internal static Snapshot Capture(GameItem item) => Capture(item, out _);

    internal static Snapshot Capture(GameItem item, out List<GameItem> liveNodes)
    {
        using var timing = new WorkroomFeedbackDiagnostics.Operation("Capture");
        if (item == null || item.unitCount <= 0) throw new InvalidOperationException("无效物品。");
        using var lease = new HeapLease();
        var native = SaveManager.EncodeNodes(item.Cast<GraphNodeStorage>());
        if (native == null || native.Count == 0) throw new InvalidOperationException("物品图为空。");
        liveNodes = new List<GameItem>();
        var rootUuid = SaveManager.itemHeap[item];
        var rootIndex = -1;
        for (var i = 0; i < native.Count; i++) if (native[i].uuid == rootUuid) rootIndex = i;
        if (rootIndex < 0) throw new InvalidOperationException("物品图根缺失。");
        if (rootIndex != 0) { var root = native[rootIndex]; native.RemoveAt(rootIndex); native.Insert(0, root); }
        var result = new Snapshot
        {
            Version = 2,
            Payload = JsonUtility.ToJson(new SaveState { saveItems = native }),
            Identifier = Core.Clean(item.identifier), Name = item.GetDisplayName(), Count = item.unitCount,
            Value = item.unitValue, Sprite = item.spritePath ?? "", Atlas = item.spriteAtlasPath ?? "",
            Broken = item.IsTag(Components.BrokenTag) || item.IsTag("BROKEN_MACHINE"),
            Orientation = (item.modifiedShape ?? item.shape).orientation,
            Flipped = (item.modifiedShape ?? item.shape).flipped,
            Footprint = Footprint(item.modifiedShape ?? item.shape),
        };
        var byUuid = new Dictionary<long, GameItem>();
        foreach (var pair in SaveManager.itemHeap) byUuid.Add(pair.Value, pair.Key);
        foreach (var node in native)
        {
            if (!byUuid.TryGetValue(node.uuid, out var live) || Core.Clean(live.identifier) != Core.Clean(node.identifier))
                throw new InvalidOperationException("物品图身份不一致。");
            // ability is a native interface view of this GameItem, not an
            // optional effect descriptor. Its scalar data is captured below.
            liveNodes.Add(live);
            result.Nodes.Add(new Node
            {
                Uuid = node.uuid, UniqueId = live.uniqueId, Identifier = Core.Clean(live.identifier),
                Scalars = Scalars(live), State = Tags(live.state), ModifiedState = Tags(live.modifiedState),
                Features = CaptureFeatures(live),
                SlotItems = WorkroomCrateContents.Read(live),
                Composition = WorkroomComponentParts.ReadComposition(live),
                Part = WorkroomComponentParts.ReadPart(live), DamageSeed = WorkroomComponentParts.ReadSeed(live),
                Machine = WorkroomMachineAssembly.Read(live),
                Effects = CaptureEffects(live), EffectsNull = live.effects == null,
            });
        }
        WorkroomAssemblyData.Detach(result);
        timing.Mark("encode-and-read");
        Validate(result);
        timing.Mark("validate");
        var readBack = JsonUtility.FromJson<SaveState>(result.Payload);
        ValidateGraph(readBack, result);
        timing.Mark("native-readback");
        return result;
    }

    internal static Snapshot Reorient(Snapshot snapshot, int orientation)
    {
        Validate(snapshot);
        if (orientation < 0 || orientation > 3) throw new InvalidOperationException("物品方向无效。");
        if (snapshot.Orientation == orientation) return snapshot;
        var native = JsonUtility.FromJson<SaveState>(snapshot.Payload);
        ValidateGraph(native, snapshot);
        var shape = native.saveItems[0].itemModifiedShape;
        if (shape.orientation != snapshot.Orientation || shape.flipped != snapshot.Flipped)
            throw new InvalidOperationException("保存方向与物品记录不一致。");
        var expected = snapshot.Footprint;
        for (var current = snapshot.Orientation; current != orientation; current = WorkroomItemPose.Turn(current, false))
            expected = WorkroomItemPose.Rotate(expected, false);
        // Use the native save DTO: its serialized backing-field names must not
        // be guessed. Only modifiedShape changes; base shape and origins stay.
        shape.orientation = orientation;
        var footprint = Footprint(shape.Build());
        if (!SameShape(expected, footprint)) throw new InvalidOperationException("旋转后的原版占格不一致。");
        var result = JsonSerializer.Deserialize<Snapshot>(JsonSerializer.Serialize(snapshot, WorkroomStorageState.JsonOptions),
            WorkroomStorageState.JsonOptions) ?? throw new InvalidOperationException("物品记录复制失败。");
        result.Payload = JsonUtility.ToJson(native);
        result.Orientation = orientation;
        result.Footprint = footprint;
        Validate(result);
        var readBack = JsonUtility.FromJson<SaveState>(result.Payload);
        ValidateGraph(readBack, result);
        var restoredShape = readBack.saveItems[0].itemModifiedShape;
        if (restoredShape.orientation != orientation || restoredShape.flipped != result.Flipped ||
            !SameShape(footprint, Footprint(restoredShape.Build())))
            throw new InvalidOperationException("旋转方向保存回读失败。");
        return result;
    }

    private static bool SameShape(Shape a, Shape b) =>
        a.Width == b.Width && a.Height == b.Height && a.Cells.SequenceEqual(b.Cells);

    internal static Candidate Restore(Snapshot snapshot)
    {
        using var timing = new WorkroomFeedbackDiagnostics.Operation("Restore");
        Validate(snapshot);
        var native = JsonUtility.FromJson<SaveState>(WorkroomAssemblyData.ExpandPayload(snapshot));
        ValidateGraph(native, snapshot);
        using var lease = new HeapLease();
        timing.Mark("validate-and-native-read");
        var candidate = new Candidate();
        restoring++;
        try
        {
            candidate.Root = SaveManager.DecodeNodes(native.saveItems);
            foreach (var saved in native.saveItems)
            {
                var item = saved.tempLink;
                if (item == null && SaveManager.itemDict.ContainsKey(saved.uuid)) item = SaveManager.itemDict[saved.uuid];
                if (item == null || Core.Clean(item.identifier) != Core.Clean(saved.identifier))
                    throw new InvalidOperationException("物品定义缺失或子图不完整。");
                candidate.Nodes.Add(item);
            }
            if (candidate.Root == null || candidate.Root.Pointer != candidate.Nodes[0].Pointer)
                throw new InvalidOperationException("物品图根不一致。");
            timing.Mark("decode");
            foreach (var item in candidate.Nodes)
            {
                MachineHelper.LoadUpgrade(item);
                MachineHydroponic.LoadHydroponicModuleInvData(item);
                SpriteHelper.UpdateWaterContainerSprite(item);
                GeneralHelper.LoadLockItem(item);
                GeneralHelper.LoadNonOwnedToolTag(item);
                GeneralHelper.LoadHazardousWasteTag(item);
                LockHelper.LoadLockedContainer(item);
                MachineBrokenHelper.LoadBrokenMachine(item);
                MachineHelper.OnLoad(item);
                MachineTurboBoosterAdv.LoadTurboBoostEligibleTag(item);
                item.onLoaded?.Invoke(item);
            }
            for (var i = 0; i < candidate.Nodes.Count; i++)
            {
                ApplyShapeAndTypes(candidate.Nodes[i], native.saveItems[i]);
                Apply(candidate.Nodes[i], snapshot.Nodes[i]);
            }
            // Verify the whole native graph again, including edges and inventory
            // indices; DecodeNodes may log missing edges and still return a root.
            timing.Mark("load-and-apply");
            var check = Capture(candidate.Root);
            timing.Mark("capture-check");
            if (!Equivalent(snapshot, check)) throw new InvalidOperationException("恢复后的物品信息不一致。");
            timing.Mark("equivalent");
            return candidate;
        }
        catch
        {
            // Decode can fail after allocating some nodes. Collect every node
            // still available from its isolated heap before restoring globals.
            foreach (var saved in native.saveItems)
                if (saved.tempLink != null && !candidate.Nodes.Any(item => item.Pointer == saved.tempLink.Pointer))
                    candidate.Nodes.Add(saved.tempLink);
            if (SaveManager.itemDict != null)
                foreach (var pair in SaveManager.itemDict)
                    if (pair.Value != null && !candidate.Nodes.Any(item => item.Pointer == pair.Value.Pointer)) candidate.Nodes.Add(pair.Value);
            Destroy(candidate);
            throw;
        }
        finally { restoring--; }
    }

    internal static void RestoreLeafOnto(Snapshot snapshot, GameItem item)
    {
        Validate(snapshot);
        var native = JsonUtility.FromJson<SaveState>(WorkroomAssemblyData.ExpandPayload(snapshot));
        ValidateGraph(native, snapshot);
        if (snapshot.Nodes.Count != 1 || snapshot.Identifier != Core.Clean(item.identifier))
            throw new InvalidOperationException("槽位物品图不支持。");
        var saved = native.saveItems[0];
        ApplyShapeAndTypes(item, saved);
        Apply(item, snapshot.Nodes[0]);
    }

    private static void ApplyShapeAndTypes(GameItem item, SaveItemNode saved)
    {
        item.SetShape(saved.itemShape.Build());
        item.modifiedShape = saved.itemModifiedShape.Build();
        item.RemoveAllGameItemType();
        foreach (var type in saved.itemTypes) item.SetGameItemType(type);
    }

    internal static bool Equivalent(Snapshot a, Snapshot b)
    {
        if (a.Identifier != b.Identifier || a.Count != b.Count || a.Nodes.Count != b.Nodes.Count) return false;
        // Canonicalize native JSON object ordering; array ordering is meaningful.
        using var left = JsonDocument.Parse(a.Payload);
        using var right = JsonDocument.Parse(b.Payload);
        if (!Equal(left.RootElement, right.RootElement)) return false;
        for (var i = 0; i < a.Nodes.Count; i++)
        {
            var x = JsonSerializer.SerializeToElement(a.Nodes[i], WorkroomStorageState.JsonOptions);
            var y = JsonSerializer.SerializeToElement(b.Nodes[i], WorkroomStorageState.JsonOptions);
            if (!Equal(x, y)) return false;
        }
        return true;
    }

    internal static bool StackLeaf(Snapshot item)
    {
        if (item.Nodes == null || item.Nodes.Count != 1 || item.Nodes[0] == null) return false;
        var node = item.Nodes[0]; var effects = node.Effects;
        var part = WorkroomComponentTemplates.TryPart(item.Identifier, out _, out _, out _);
        if (!part && !IsRepairMaterial(item.Identifier)) return false;
        return node.SlotItems != null && node.SlotItems.Count == 0 && node.Composition == null &&
            node.Identifier == item.Identifier && node.Machine == null && (part ? node.Part != null : node.Part == null) &&
            node.DamageSeed == null && effects != null && effects.Count == 0;
    }

    internal static bool CanUseOne(Snapshot snapshot) => snapshot.Count == 1;

    // A catalog group is a view over actual Count=1 instances, never a stack.
    // Refuse unfamiliar graphs without rejecting or rewriting their storage.
    internal static string? CatalogKey(Snapshot item)
    {
        try
        {
            if (item.Count != 1 || item.Nodes == null || item.Nodes.Count != 1 || item.Nodes[0] == null ||
                (Components.Find(item.Identifier) == null &&
                 !WorkroomComponentTemplates.TryPart(item.Identifier, out _, out _, out _) && !IsRepairMaterial(item.Identifier))) return null;
            var original = item.Nodes[0];
            if (original.Composition != null || original.Machine != null || original.SlotItems == null ||
                original.SlotItems.Count != 0 || original.Effects == null) return null;
            // Current machine records require actual installed parts. Empty
            // motherboard/case bodies have Machine=null and are eligible here.
            Validate(item);
            if (JsonNode.Parse(JsonSerializer.Serialize(item, WorkroomStorageState.JsonOptions)) is not JsonObject copy ||
                copy[nameof(Snapshot.Payload)] is not JsonObject payload ||
                payload["saveItems"] is not JsonArray saved || saved.Count != 1 || saved[0] is not JsonObject root ||
                root["childItems"] is not JsonArray children || children.Count != 0 ||
                root["childItemInventoryNode"] is not JsonArray inventories || inventories.Count != 0 ||
                !StackInteger(root["uniqueId"], original.UniqueId) || !StackLong(root["uuid"], original.Uuid) ||
                !StackInteger(root["unitCount"], 1) || !NormalizeStackPosition(root, item)) return null;

            // The exact native shape, including unknown fields, remains in the
            // key. Validate the actual rotated footprint before ignoring rotation.
            var shape = (JsonObject)root["itemModifiedShape"]!;
            var actual = ValidatePayloadShape(JsonSerializer.SerializeToElement(shape, WorkroomStorageState.JsonOptions));
            if (!SameShape(actual, item.Footprint)) return null;
            shape["<orientation>k__BackingField"] = 0;
            var canonicalShape = ValidatePayloadShape(JsonSerializer.SerializeToElement(shape, WorkroomStorageState.JsonOptions));
            copy[nameof(Snapshot.Orientation)] = 0;
            copy[nameof(Snapshot.Footprint)] = JsonSerializer.SerializeToNode(canonicalShape, WorkroomStorageState.JsonOptions);
            root["uniqueId"] = 0; root["uuid"] = 0L;
            if (copy[nameof(Snapshot.Nodes)] is not JsonArray nodes || nodes[0] is not JsonObject node ||
                node[nameof(Node.Scalars)] is not JsonObject scalars || node[nameof(Node.Features)] is not JsonArray features ||
                node[nameof(Node.ModifiedState)] is not JsonObject modifiedState ||
                !StackInteger(scalars["uniqueId"], original.UniqueId) || !StackInteger(scalars["unitCount"], 1)) return null;
            node[nameof(Node.UniqueId)] = 0; node[nameof(Node.Uuid)] = 0L; scalars["uniqueId"] = 0;
            // Some native items carry disabled, empty tags while otherwise
            // identical items omit them. Ignore only these exact defaults in
            // the comparison copy; the saved snapshot stays untouched.
            IgnoreCatalogDefaultTag(modifiedState, "BOUGHT_PRICE_TAG");
            IgnoreCatalogDefaultTag(modifiedState, "WINE_SCORE_INT");
            foreach (var feature in features)
            {
                if (feature is not JsonObject featureObject || featureObject[nameof(Feature.Scalars)] is not JsonObject fields) return null;
                if (fields["parentItemUniqueId"] is { } parent)
                {
                    if (parent is not JsonValue value || !value.TryGetValue<int>(out var id)) return null;
                    if (id == original.UniqueId) fields["parentItemUniqueId"] = "@root";
                }
            }
            using var buffer = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
                WriteCatalogCanonical(writer, JsonSerializer.SerializeToElement(copy, WorkroomStorageState.JsonOptions));
            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is ArgumentException ||
            ex is FormatException || ex is OverflowException || ex is KeyNotFoundException || ex is NotSupportedException)
        { return null; }
    }

    private static void WriteCatalogCanonical(Utf8JsonWriter writer, JsonElement value, string path = "")
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var fields = value.EnumerateObject().OrderBy(field => field.Name, StringComparer.Ordinal).ToArray();
            var names = new HashSet<string>(StringComparer.Ordinal);
            writer.WriteStartObject();
            foreach (var field in fields)
            {
                // Unknown serializer references cannot be safely rebound.
                var fieldPath = path + "/" + field.Name;
                var identity = field.Name.IndexOf("uuid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    field.Name.IndexOf("uniqueId", StringComparison.OrdinalIgnoreCase) >= 0;
                var knownIdentity = fieldPath is "/Payload/saveItems[]/uuid" or "/Payload/saveItems[]/uniqueId" or
                    "/Nodes[]/Uuid" or "/Nodes[]/UniqueId" or "/Nodes[]/Scalars/uniqueId" or
                    "/Nodes[]/Features[]/Scalars/parentItemUniqueId";
                if (!names.Add(field.Name) || field.Name is "$ref" or "$id" || identity && !knownIdentity)
                    throw new InvalidOperationException("目录比较包含未知引用或重复字段。");
                writer.WritePropertyName(field.Name); WriteCatalogCanonical(writer, field.Value, fieldPath);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var element in value.EnumerateArray()) WriteCatalogCanonical(writer, element, path + "[]");
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }

    private static bool StackInteger(JsonNode? value, int expected) => value is JsonValue number &&
        number.TryGetValue<int>(out var actual) && actual == expected;

    private static bool StackLong(JsonNode? value, long expected) => value is JsonValue number &&
        number.TryGetValue<long>(out var actual) && actual == expected;

    private static void IgnoreCatalogDefaultTag(JsonObject modifiedState, string name)
    {
        if (modifiedState[name] is not JsonObject tag || tag.Count != 9 ||
            tag["identifier"] is not JsonValue identifier || !identifier.TryGetValue<string>(out var id) || id != name ||
            tag["identifierName"] is not JsonValue identifierName ||
            !identifierName.TryGetValue<string>(out var displayName) || displayName != "TYPE-STRING_" + name ||
            tag["valueEnabled"] is not JsonValue enabled || !enabled.TryGetValue<bool>(out var isEnabled) || isEnabled ||
            tag["internalValueString"] is not JsonValue text || !text.TryGetValue<string>(out var stringValue) || stringValue != "" ||
            !StackInteger(tag["valueInt"], 0) || !StackLong(tag["valueLong"], 0) ||
            tag["valueFloat"] is not JsonValue floatValue || !floatValue.TryGetValue<float>(out var single) || single != 0 ||
            tag["valueDouble"] is not JsonValue doubleValue || !doubleValue.TryGetValue<double>(out var twice) || twice != 0 ||
            tag["valueBool"] is not JsonValue boolean || !boolean.TryGetValue<bool>(out var boolValue) || boolValue) return;
        modifiedState.Remove(name);
    }

    private static bool NormalizeStackPosition(JsonObject root, Snapshot item)
    {
        // Native AcceptUnchecked writes the inventory placement into this shape.
        // Ignore only its translation in the comparison copy, keeping the saved
        // payload, base shape, shape offsets and all actual item state intact.
        const string x = "<minX>k__BackingField", y = "<minY>k__BackingField";
        if (root["itemModifiedShape"] is not JsonObject shape || shape.Count != 8 ||
            shape[x] is not JsonValue xValue || !xValue.TryGetValue<int>(out _) ||
            shape[y] is not JsonValue yValue || !yValue.TryGetValue<int>(out _) ||
            !StackInteger(shape["<orientation>k__BackingField"], item.Orientation) ||
            shape["<flipped>k__BackingField"] is not JsonValue flipped ||
            !flipped.TryGetValue<bool>(out var mirror) || mirror != item.Flipped ||
            shape["<width>k__BackingField"] is not JsonValue width ||
            !width.TryGetValue<int>(out var w) || w <= 0 ||
            shape["<height>k__BackingField"] is not JsonValue height ||
            !height.TryGetValue<int>(out var h) || h <= 0 ||
            shape["outside"] is not JsonValue outside || !outside.TryGetValue<int>(out _) ||
            shape["data"] is not JsonArray data || data.Count == 0 ||
            data.Any(cell => cell is not JsonValue value || !value.TryGetValue<int>(out _))) return false;
        shape[x] = 0; shape[y] = 0;
        return true;
    }

    private static bool Equal(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        if (a.ValueKind == JsonValueKind.Object)
        {
            var count = 0;
            foreach (var field in a.EnumerateObject())
            {
                count++;
                if (!b.TryGetProperty(field.Name, out var other)) return false;
                if (!Equal(field.Value, other)) return false;
            }
            return count == b.EnumerateObject().Count();
        }
        if (a.ValueKind == JsonValueKind.Array)
        {
            var x = a.EnumerateArray().ToArray(); var y = b.EnumerateArray().ToArray();
            return x.Length == y.Length && x.Zip(y, (left, right) => Equal(left, right)).All(equal => equal);
        }
        return a.GetRawText() == b.GetRawText();
    }

    internal static void Destroy(Candidate candidate)
    {
        if (candidate.Cleaned) return;
        if (!candidate.SameOwner)
        {
            // Destroy mutates the current PlayerStore's uniqueId cache. An old
            // session's wrappers must never be used against the new store.
            candidate.Cleaned = true; failedCleanup.Remove(candidate);
            Core.Log?.Warning("旧存档候选已解除引用，未对新存档调用实体销毁。");
            return;
        }
        if (candidate.Root != null && candidate.Root.parentInventory != null)
        {
            if (!failedCleanup.Contains(candidate)) failedCleanup.Add(candidate);
            throw new InvalidOperationException("候选仍有库存归属，保留实体等待确认，禁止销毁。");
        }
        Exception? error = null;
        for (var i = candidate.Nodes.Count - 1; i >= 0; i--)
            try
            {
                var item = candidate.Nodes[i];
                if (item.TryCast<GameItemElement>() is { } element && element.IsDestroyed()) continue;
                item.Destroy();
            }
            catch (Exception ex) { error ??= ex; }
        if (error != null)
        {
            if (!failedCleanup.Contains(candidate)) failedCleanup.Add(candidate);
            throw new InvalidOperationException("物品实体清理未完成。", error);
        }
        candidate.Cleaned = true;
        failedCleanup.Remove(candidate);
    }

    internal static Shape Footprint(GridShape shape)
    {
        if (shape == null || shape.globalWidth <= 0 || shape.globalHeight <= 0 ||
            (long)shape.globalWidth * shape.globalHeight > 65536) throw new InvalidOperationException("物品形状无效。");
        var result = new Shape { Width = shape.globalWidth, Height = shape.globalHeight,
            Cells = new byte[shape.globalWidth * shape.globalHeight] };
        for (var y = 0; y < result.Height; y++)
            for (var x = 0; x < result.Width; x++) result.Cells[y*result.Width+x] = shape.Get(x + shape.minX, y + shape.minY);
        return result;
    }

    internal static void Validate(Snapshot value)
    {
        if (value == null || value.Version != 2 || string.IsNullOrEmpty(value.Payload) ||
            string.IsNullOrEmpty(value.Identifier) || value.Count <= 0 || value.Orientation < 0 || value.Orientation > 3 ||
            value.Nodes == null || value.Nodes.Count == 0 ||
            value.Footprint == null || value.Footprint.Width <= 0 || value.Footprint.Height <= 0 ||
            (long)value.Footprint.Width*value.Footprint.Height > 65536 || value.Footprint.Cells == null ||
            value.Footprint.Cells.Length != value.Footprint.Width*value.Footprint.Height || !value.Footprint.Cells.Any(cell => cell != 0))
            throw new InvalidOperationException("物品快照无效。");
        var uuids = new HashSet<long>(); var ids = new HashSet<int>();
        foreach (var node in value.Nodes)
            if (node == null || !uuids.Add(node.Uuid) || !ids.Add(node.UniqueId) || string.IsNullOrEmpty(node.Identifier) ||
                node.Scalars == null || node.State == null || node.ModifiedState == null || node.Features == null || node.SlotItems == null || node.Effects == null)
                throw new InvalidOperationException("物品图身份或字段无效。");
        if (value.Identifier != value.Nodes[0].Identifier ||
            !value.Nodes[0].Scalars.TryGetValue("unitCount", out var rootCount) || rootCount.GetInt32() != value.Count ||
            !value.Nodes[0].Scalars.TryGetValue("unitValue", out var rootValue) || rootValue.GetInt64() != value.Value)
            throw new InvalidOperationException("物品根型号、数量或价格与快照不符。");
        ValidatePayload(value);
        foreach (var node in value.Nodes)
        {
            WorkroomAssemblyData.ValidateNode(node);
            WorkroomCrateContents.ValidateNode(node);
            if (node.SlotItems.Count != 0)
            {
                if (Components.Find(node.Identifier)?.Owner.IsCase != true || node.Machine != null || node.Composition != null)
                    throw new InvalidOperationException("物资箱内容与快照本体不符。");
                WorkroomCrateContents.Validate(node.SlotItems);
            }
            WorkroomComponentParts.ValidatePart(node);
            WorkroomMachineAssembly.ValidateNode(node);
            if (node.Machine is { } machine)
            {
                foreach (var part in machine.Parts)
                    foreach (var id in Identities(part.Item))
                        if (!ids.Add(id)) throw new InvalidOperationException("机器内部物品身份重复。");
                if (node == value.Nodes[0] && (WorkroomMachineAssembly.Value(machine) != value.Value ||
                    WorkroomMachineAssembly.Broken(machine) != value.Broken))
                    throw new InvalidOperationException("机器价值或损坏状态与组成不符。");
            }
            if (node == value.Nodes[0] && node.Part is { } partInfo &&
                (partInfo.Current != value.Value || partInfo.Broken != value.Broken))
                throw new InvalidOperationException("子件实际状态或价格与保存元数据不一致。");
            if (node.EffectsNull && (node.Effects == null || node.Effects.Count != 0))
                throw new InvalidOperationException("物品效果列表状态无效。");
            if (node.Effects != null)
                foreach (var effect in node.Effects)
                    if (effect != null && (effect.Scalars == null || effect.Tags == null || effect.Identifier == null))
                        throw new InvalidOperationException("物品效果记录无效。");
            foreach (var part in node.SlotItems)
            {
                if (part == null || part.Nodes == null || part.Nodes.Count != 1 ||
                    part.Nodes[0].SlotItems == null || part.Nodes[0].SlotItems.Count != 0)
                    throw new InvalidOperationException("分离槽位图无效。");
                Validate(part);
                foreach (var id in Identities(part))
                    if (!ids.Add(id)) throw new InvalidOperationException("分离槽位物品身份重复。");
            }
            if (node.Composition != null)
            {
                if (!WorkroomComponentTemplates.TryWhole(node.Identifier, out var wholeTemplate, out _, out _) ||
                    node.Composition.Count == 0 || !WorkroomComponentTemplates.TryPart(node.Composition[0].Identifier, out var partTemplate, out _, out _) || wholeTemplate != partTemplate)
                    throw new InvalidOperationException("成品包含不匹配的组成记录。");
                WorkroomComponentParts.ValidateComposition(node.Composition);
                foreach (var part in node.Composition)
                {
                    Validate(part);
                    foreach (var id in Identities(part))
                        if (!ids.Add(id)) throw new InvalidOperationException("内部部件身份重复。");
                }
            }
        }
    }

    // Validate saved JSON before any DTO-only move can publish it. This allocates
    // no native item, changes no encoding heap and preserves unreadable raw data.
    private static void ValidatePayload(Snapshot snapshot)
    {
        using var document = JsonDocument.Parse(snapshot.Payload);
        var objects = new Stack<JsonElement>(); objects.Push(document.RootElement);
        while (objects.Count > 0)
        {
            var element = objects.Pop();
            if (element.ValueKind == JsonValueKind.Array)
                foreach (var value in element.EnumerateArray()) objects.Push(value);
            else if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new InvalidOperationException("物品Payload字段重复。");
                    objects.Push(property.Value);
                }
            }
        }
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("saveItems", out var items) ||
            items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != snapshot.Nodes.Count)
            throw new InvalidOperationException("物品Payload图数量无效。");
        var incoming = snapshot.Nodes.ToDictionary(node => node.Uuid, _ => 0);
        var edges = new Dictionary<long, List<long>>();
        for (var i = 0; i < snapshot.Nodes.Count; i++)
        {
            var saved = items[i]; var extra = snapshot.Nodes[i];
            if (saved.ValueKind != JsonValueKind.Object ||
                saved.GetProperty("uuid").GetInt64() != extra.Uuid ||
                saved.GetProperty("uniqueId").GetInt32() != extra.UniqueId ||
                Core.Clean(saved.GetProperty("identifier").GetString()) != extra.Identifier ||
                !extra.Scalars.TryGetValue("unitCount", out var count) ||
                saved.GetProperty("unitCount").GetInt32() != count.GetInt32())
                throw new InvalidOperationException("物品Payload身份或数量不一致。");
            WorkroomAssemblyData.ValidateNative(saved, extra);
            foreach (var field in new[] { "unitValue", "unitBaseValue", "lateUnitValue", "backupUnitValue" })
                if (!extra.Scalars.TryGetValue(field, out var value) || saved.GetProperty(field).GetInt64() != value.GetInt64())
                    throw new InvalidOperationException("物品Payload价格字段不一致：" + field);
            ValidatePayloadShape(saved.GetProperty("itemShape"));
            var modified = saved.GetProperty("itemModifiedShape");
            var footprint = ValidatePayloadShape(modified);
            if (i == 0 && (modified.GetProperty("<orientation>k__BackingField").GetInt32() != snapshot.Orientation ||
                modified.GetProperty("<flipped>k__BackingField").GetBoolean() != snapshot.Flipped ||
                !SameShape(footprint, snapshot.Footprint)))
                throw new InvalidOperationException("物品Payload方向、镜像或占格与快照不符。");
            var children = saved.GetProperty("childItems");
            var inventories = saved.GetProperty("childItemInventoryNode");
            if (children.ValueKind != JsonValueKind.Array || inventories.ValueKind != JsonValueKind.Array ||
                children.GetArrayLength() != inventories.GetArrayLength())
                throw new InvalidOperationException("物品Payload子节点归属记录无效。");
            foreach (var index in inventories.EnumerateArray()) _ = index.GetInt32();
            var types = saved.GetProperty("itemTypes");
            if (types.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("物品Payload类型列表无效。");
            foreach (var type in types.EnumerateArray())
                if (type.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException("物品Payload类型成员无效。");
            var targets = new List<long>();
            foreach (var child in children.EnumerateArray())
            {
                var uuid = child.GetInt64();
                if (!incoming.ContainsKey(uuid)) throw new InvalidOperationException("物品Payload子节点缺失。");
                incoming[uuid]++; targets.Add(uuid);
            }
            edges.Add(extra.Uuid, targets);
        }
        var rootId = snapshot.Nodes[0].Uuid;
        if (incoming[rootId] != 0 || incoming.Any(pair => pair.Key != rootId && pair.Value != 1))
            throw new InvalidOperationException("物品Payload包含重复归属。");
        var visited = new HashSet<long>(); var pending = new Stack<long>(); pending.Push(rootId);
        while (pending.Count > 0)
        {
            var uuid = pending.Pop();
            if (!visited.Add(uuid)) throw new InvalidOperationException("物品Payload包含循环。");
            foreach (var child in edges[uuid]) pending.Push(child);
        }
        if (visited.Count != snapshot.Nodes.Count) throw new InvalidOperationException("物品Payload图不完整。");
    }

    private static Shape ValidatePayloadShape(JsonElement shape)
    {
        if (shape.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("物品Payload形状无效。");
        var width = shape.GetProperty("<width>k__BackingField").GetInt32();
        var height = shape.GetProperty("<height>k__BackingField").GetInt32();
        var orientation = shape.GetProperty("<orientation>k__BackingField").GetInt32();
        var data = shape.GetProperty("data");
        if (width <= 0 || height <= 0 || (long)width * height > 65536 || orientation < 0 || orientation > 3 ||
            data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != (long)width * height)
            throw new InvalidOperationException("物品Payload形状尺寸或方向无效。");
        _ = shape.GetProperty("<minX>k__BackingField").GetInt32();
        _ = shape.GetProperty("<minY>k__BackingField").GetInt32();
        var flipped = shape.GetProperty("<flipped>k__BackingField").GetBoolean();
        _ = shape.GetProperty("outside").GetByte();
        var result = new Shape { Width = (orientation & 1) == 0 ? width : height,
            Height = (orientation & 1) == 0 ? height : width, Cells = new byte[width * height] };
        // Match GridShape.ForwardTransform: mirror local X, then rotate.
        // GetLocal reads row-major data; minX/minY cancel in Footprint's origin.
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var localX = flipped ? width - 1 - x : x;
                var (globalX, globalY) = orientation switch
                {
                    1 => (y, width - 1 - localX),
                    2 => (width - 1 - localX, height - 1 - y),
                    3 => (height - 1 - y, localX),
                    _ => (localX, y),
                };
                result.Cells[globalY * result.Width + globalX] = data[y * width + x].GetByte();
            }
        return result;
    }

    internal static IEnumerable<int> Identities(Snapshot value)
    {
        foreach (var node in value.Nodes)
        {
            yield return node.UniqueId;
            foreach (var part in node.SlotItems)
                foreach (var id in Identities(part)) yield return id;
            if (node.Composition != null)
                foreach (var part in node.Composition)
                    foreach (var id in Identities(part)) yield return id;
            if (node.Machine != null)
                foreach (var part in node.Machine.Parts)
                    foreach (var id in Identities(part.Item)) yield return id;
        }
    }

    private static void ValidateGraph(SaveState state, Snapshot snapshot)
    {
        if (state == null || state.saveItems == null || state.saveItems.Count != snapshot.Nodes.Count)
            throw new InvalidOperationException("物品图记录数量不一致。");
        var uuids = snapshot.Nodes.Select(node => node.Uuid).ToHashSet();
        var incoming = snapshot.Nodes.ToDictionary(node => node.Uuid, node => 0);
        var edges = new Dictionary<long, List<long>>();
        for (var i = 0; i < state.saveItems.Count; i++)
        {
            var node = state.saveItems[i]; var extra = snapshot.Nodes[i];
            if (node == null || node.uuid != extra.Uuid || node.uniqueId != extra.UniqueId ||
                Core.Clean(node.identifier) != extra.Identifier || node.itemShape == null || node.itemModifiedShape == null ||
                node.childItems == null || node.childItemInventoryNode == null || node.childItems.Count != node.childItemInventoryNode.Count)
                throw new InvalidOperationException("物品图结构不一致。");
            foreach (var child in node.childItems)
            {
                if (!uuids.Contains(child)) throw new InvalidOperationException("物品图子节点缺失。");
                incoming[child]++;
            }
            edges[node.uuid] = new List<long>();
            foreach (var child in node.childItems) edges[node.uuid].Add(child);
        }
        var root = snapshot.Nodes[0].Uuid;
        if (incoming[root] != 0 || incoming.Any(pair => pair.Key != root && pair.Value != 1))
            throw new InvalidOperationException("物品图包含重复归属。");
        var visited = new HashSet<long>(); var pending = new Stack<long>(); pending.Push(root);
        while (pending.Count > 0)
        {
            var uuid = pending.Pop();
            if (!visited.Add(uuid)) throw new InvalidOperationException("物品图包含循环。");
            foreach (var child in edges[uuid]) pending.Push(child);
        }
        if (visited.Count != snapshot.Nodes.Count) throw new InvalidOperationException("物品图不完整。");
    }

    private static Dictionary<string, JsonElement> Scalars(object value)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var valueType = value is GameItem ? typeof(GameItem) : value.GetType();
        foreach (var property in valueType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var type = property.PropertyType;
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0 ||
                property.Name == "valueString" ||
                property.Name == "spriteChanged") continue;
            if (property.Name.StartsWith("_", StringComparison.Ordinal) && property.Name.EndsWith("_k__BackingField", StringComparison.Ordinal))
            {
                var name = property.Name.Substring(1, property.Name.IndexOf("_k__BackingField", StringComparison.Ordinal)-1);
                var plain = valueType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                // Read-only/write-only aliases still need their native backing
                // fields (feature ID/category and item identifierName).
                if (plain != null && plain.CanRead && plain.CanWrite) continue;
            }
            if (type != typeof(string) && type != typeof(bool) && type != typeof(int) && type != typeof(long) &&
                type != typeof(float) && type != typeof(double) && !type.IsEnum) continue;
            fields.Add(property.Name, JsonSerializer.SerializeToElement(property.GetValue(value), type, WorkroomStorageState.JsonOptions));
        }
        return fields;
    }

    private static void ApplyScalars(object value, Dictionary<string, JsonElement> fields)
    {
        foreach (var field in fields)
        {
            var property = value.GetType().GetProperty(field.Key, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("物品字段不再支持：" + field.Key);
            property.SetValue(value, field.Value.Deserialize(property.PropertyType, WorkroomStorageState.JsonOptions));
        }
    }

    private static Dictionary<string, Dictionary<string, JsonElement>> Tags(TagSystem? state)
    {
        var result = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.Ordinal);
        if (state?.dict != null)
            foreach (var pair in state.dict)
                result.Add(pair.Key, Scalars(pair.Value ?? throw new InvalidOperationException("空标签。")));
        return result;
    }

    private static TagSystem RestoreTags(Dictionary<string, Dictionary<string, JsonElement>> fields)
    {
        var result = new TagSystem();
        foreach (var pair in fields)
        {
            var tag = new TagState(pair.Key, pair.Key);
            ApplyScalars(tag, pair.Value);
            result.dict[pair.Key] = tag;
        }
        return result;
    }

    private static List<Feature> CaptureFeatures(GameItem item)
    {
        var result = new List<Feature>();
        if (item.itemFeatures == null) return result;
        foreach (var feature in item.itemFeatures)
        {
            if (feature == null) throw new InvalidOperationException("空物品特性。");
            var saved = new Feature { Scalars = Scalars(feature),
                Fake = feature.fakeCondition == null ? null : Scalars(feature.fakeCondition),
                Real = feature.realCondition == null ? null : Scalars(feature.realCondition) };
            result.Add(saved);
        }
        return result;
    }

    private static ItemCondition? Condition(Dictionary<string, JsonElement>? fields)
    {
        if (fields == null) return null;
        var condition = new ItemCondition(); ApplyScalars(condition, fields); return condition;
    }

    private static List<Effect?> CaptureEffects(GameItem item)
    {
        var result = new List<Effect?>();
        if (item.effects == null) return result;
        foreach (var effect in item.effects)
        {
            if (effect == null) { result.Add(null); continue; }
            var saved = new Effect
            {
                Identifier = Core.Clean(effect.identifier), Scalars = Scalars(effect), Tags = Tags(effect),
                Callback = effect.callback != null, InitCallback = effect.initCallback != null, EndCallback = effect.endCallback != null,
            };
            // Never replace an unknown callback with a dummy factory effect.
            // Template callbacks are reconstructed from the registered ID.
            if (saved.Callback || saved.InitCallback || saved.EndCallback)
            {
                if (!DirectoryMaster.Has<CombatAbilityEffect>(saved.Identifier))
                    throw new InvalidOperationException("物品效果缺少可重建定义：" + saved.Identifier);
                var template = DirectoryMaster.CombatAbilityEffect(saved.Identifier);
                if (template == null || (saved.Callback && template.callback == null) ||
                    (saved.InitCallback && template.initCallback == null) || (saved.EndCallback && template.endCallback == null))
                    throw new InvalidOperationException("物品效果回调无法重建：" + saved.Identifier);
            }
            result.Add(saved);
        }
        return result;
    }

    private static void ApplyEffects(GameItem item, Node node)
    {
        if (node.Effects == null) throw new InvalidOperationException("当前快照缺少效果记录。");
        if (node.EffectsNull) { item.effects = null!; return; }
        var effects = new Il2CppSystem.Collections.Generic.List<CombatAbilityEffect>();
        foreach (var saved in node.Effects)
        {
            if (saved == null) { effects.Add(null!); continue; }
            var known = DirectoryMaster.Has<CombatAbilityEffect>(saved.Identifier);
            if (!known && (saved.Callback || saved.InitCallback || saved.EndCallback))
                throw new InvalidOperationException("物品效果定义不可用：" + saved.Identifier);
            var effect = known ? DirectoryMaster.CombatAbilityEffect(saved.Identifier) : new CombatAbilityEffect();
            if (effect == null || (saved.Callback && effect.callback == null) ||
                (saved.InitCallback && effect.initCallback == null) || (saved.EndCallback && effect.endCallback == null))
                throw new InvalidOperationException("物品效果回调无法恢复：" + saved.Identifier);
            ApplyScalars(effect, saved.Scalars);
            effect.dict = RestoreTags(saved.Tags).dict;
            if (!saved.Callback) effect.callback = null;
            if (!saved.InitCallback) effect.initCallback = null;
            if (!saved.EndCallback) effect.endCallback = null;
            effects.Add(effect);
        }
        item.effects = effects;
    }

    private static void Apply(GameItem item, Node node)
    {
        ApplyScalars(item, node.Scalars);
        item.state = RestoreTags(WorkroomAssemblyData.ExpandState(node, node.State));
        item.modifiedState = RestoreTags(WorkroomAssemblyData.ExpandState(node, node.ModifiedState));
        var features = new Il2CppSystem.Collections.Generic.List<ItemFeature>();
        foreach (var saved in node.Features)
        {
            var feature = new ItemFeature(); ApplyScalars(feature, saved.Scalars);
            feature.fakeCondition = Condition(saved.Fake); feature.realCondition = Condition(saved.Real);
            features.Add(feature);
        }
        item.itemFeatures = features; item.spriteChanged = true;
        ApplyEffects(item, node);
    }
}
