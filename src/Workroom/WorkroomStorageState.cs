using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Il2Cpp;

namespace PCExpansion;

/// <summary>存档中的唯一保管记录；视图和Unity对象均不是库存。</summary>
internal sealed class WorkroomStorageState
{
    internal const string SaveKey = "pcrepair.workroom.storage.v2";
    internal const int Columns = 32, Rows = 8;
    internal static readonly JsonSerializerOptions JsonOptions = new()
    { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    internal sealed class Document
    {
        public Document() { }
        public int Version { get; set; } = 2;
        public List<Record> Items { get; set; } = new();
    }

    internal sealed class Record
    {
        public Record() { }
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Place { get; set; } = "box";
        public string Slot { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public WorkroomItemCodec.Snapshot Item { get; set; } = new();
    }

    private PlayerStore? owner;
    private string? run;
    private int slot;
    private Document document = new();
    private readonly Dictionary<string, Record> slotIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Record> recordIndex = new(StringComparer.Ordinal);
    private readonly List<Record> roomItems = new();
    private readonly List<Record> pendingRecords = new();
    private bool[] roomOccupancy = new bool[Columns * Rows];
    private bool[]? excludedOccupancy;
    private string? excludedId;
    private long indexedRevision = -1;
    // Last successfully published catalog contents, detached from mutable DTOs.
    private byte[] catalogProjection = new byte[] { (byte)'[', (byte)']' };
    internal long Epoch { get; private set; }
    internal long Revision { get; private set; }
    internal long CatalogRevision { get; private set; }
    internal string? Error { get; private set; }
    internal bool Ready => owner != null && Error == null && SameOwner();
    internal IReadOnlyList<Record> Items => document.Items;
    internal IReadOnlyList<Record> RoomItems
    {
        get { EnsureIndex(); return roomItems; }
    }
    internal IReadOnlyList<Record> PendingRecords
    {
        get { EnsureIndex(); return pendingRecords; }
    }

    internal bool SameOwner() => owner != null && PlayerStore.IsInstanceExist() &&
        PlayerStore.instance != null && PlayerStore.instance.Pointer == owner.Pointer &&
        owner.runID == run && owner.saveSlotId == slot && SaveGeneration.IsSupported(owner);

    // A possession query must not bind the UI model, advance unique IDs or
    // replace unreadable save data. Incomplete transfers retain custody here.
    internal bool HasStoredItem(PlayerStore store, string identifier)
    {
        if (!SaveGeneration.IsSupported(store))
            throw new InvalidOperationException("当前存档不是受支持的1.0新档，不能检查储物记录。");
        if (!PlayerStore.IsInstanceExist() || PlayerStore.instance == null || PlayerStore.instance.Pointer != store.Pointer)
            throw new InvalidOperationException("储物持有检查不属于当前存档。");
        Document? saved = null;
        if (store.modData != null && store.modData.ContainsKey(SaveKey))
        {
            // DTO defaults are useful when creating a new model, but must not
            // turn a truncated/unknown saved envelope into an empty inventory.
            using var raw = JsonDocument.Parse(store.modData[SaveKey]);
            var root = raw.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Version", out var version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 2 ||
                !root.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("储物记录封套不支持，不能判定物品丢失。");
            saved = JsonSerializer.Deserialize<Document>(store.modData[SaveKey], JsonOptions)
                ?? throw new InvalidOperationException("空储物记录，不能判定物品丢失。");
            Validate(saved);
        }
        Document source;
        if (SameOwner() && owner!.Pointer == store.Pointer)
        {
            if (Error != null) throw new InvalidOperationException("储物记录不可读取，不能判定物品丢失。");
            source = document;
        }
        else
        {
            if (saved == null) return false;
            source = saved;
        }
        Validate(source);
        return source.Items.Any(record => ContainsIdentifier(record.Item, identifier));
    }

    private static bool ContainsIdentifier(WorkroomItemCodec.Snapshot item, string identifier) =>
        item.Identifier == identifier || item.Nodes.Any(node => node.Identifier == identifier ||
            node.SlotItems.Any(part => ContainsIdentifier(part, identifier)) ||
            (node.Composition?.Any(part => ContainsIdentifier(part, identifier)) ?? false) ||
            (node.Machine?.Parts.Any(part => ContainsIdentifier(part.Item, identifier)) ?? false));

    internal void Bind(PlayerStore store)
    {
        if (!SaveGeneration.IsSupported(store))
            throw new InvalidOperationException("当前存档不是受支持的1.0新档，工作间不读取或改写记录。");
        if (owner != null && owner.Pointer == store.Pointer && store.runID == run && store.saveSlotId == slot) return;
        owner = store; run = store.runID; slot = store.saveSlotId;
        Epoch++; Revision++; Error = null; document = new();
        ResetCatalog();
        try
        {
            if (store.modData == null || !store.modData.ContainsKey(SaveKey)) return;
            using var raw = JsonDocument.Parse(store.modData[SaveKey]);
            if (raw.RootElement.ValueKind != JsonValueKind.Object ||
                !raw.RootElement.TryGetProperty("Version", out var version) || !version.TryGetInt32(out var versionNumber) || versionNumber != 2 ||
                !raw.RootElement.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("储物记录封套不支持，保留原文。");
            var parsed = JsonSerializer.Deserialize<Document>(store.modData[SaveKey], JsonOptions)
                ?? throw new InvalidOperationException("空储物记录");
            Validate(parsed);
            var serialized = JsonSerializer.Serialize(parsed, JsonOptions);
            var catalog = CreateCatalogProjection(serialized);
            var highestId = parsed.Items.SelectMany(item => WorkroomItemCodec.Identities(item.Item))
                .DefaultIfEmpty(store.currentUniqueId).Max();
            if (store.currentUniqueId < highestId) store.currentUniqueId = highestId;
            document = parsed; indexedRevision = -1;
            PublishCatalog(catalog);
        }
        catch (Exception ex)
        {
            Error = "data_error";
            Core.Log?.Error("工作间储物记录未加载；保留原文并禁止覆盖：" + ex.Message);
        }
    }

    internal void Unbind()
    {
        owner = null; run = null; slot = 0; document = new(); Error = null;
        Epoch++; Revision++;
        ResetCatalog();
    }

    private void EnsureIndex()
    {
        if (indexedRevision == Revision) return;
        slotIndex.Clear(); recordIndex.Clear(); roomItems.Clear(); pendingRecords.Clear();
        roomOccupancy = new bool[Columns * Rows];
        excludedOccupancy = null; excludedId = null;
        foreach (var item in document.Items)
        {
            recordIndex.Add(item.Id, item);
            if (item.Place == "slot") slotIndex.Add(item.Slot, item);
            if (item.Place == "cleanup" || item.Place == "delivery") pendingRecords.Add(item);
            if (item.Place == "room")
            {
                roomItems.Add(item); Fill(roomOccupancy, item.Item.Footprint, item.X, item.Y);
            }
        }
        indexedRevision = Revision;
    }

    internal Record? Find(string id)
    {
        EnsureIndex();
        return recordIndex.TryGetValue(id, out var record) ? record : null;
    }

    internal Record? FindSlot(string slotName)
    {
        if (!Ready) return null;
        EnsureIndex();
        return slotIndex.TryGetValue(slotName, out var record) ? record : null;
    }

    private bool[] RoomOccupancy(string? excluding)
    {
        EnsureIndex();
        if (excluding == null || !recordIndex.TryGetValue(excluding, out var item) || item.Place != "room") return roomOccupancy;
        if (excludedOccupancy != null && excludedId == excluding) return excludedOccupancy;
        excludedId = excluding;
        return excludedOccupancy = Occupancy(roomItems, excluding);
    }

    // Serialize and publish before replacing the live model. Failed publication
    // leaves the previous model and raw save entry intact.
    internal void Replace(List<Record> items)
    {
        using var timing = new WorkroomFeedbackDiagnostics.Operation("Replace");
        if (!Ready) throw new InvalidOperationException("储物存档上下文不可用。");
        var next = new Document { Items = items };
        Validate(next);
        timing.Mark("validate");
        var json = JsonSerializer.Serialize(next, JsonOptions);
        timing.Mark("serialize(chars=" + json.Length + ")");
        // Prepare before writing modData: parsing/allocation failures must not
        // leave a persisted document without its matching live version.
        var catalog = CreateCatalogProjection(json);
        timing.Mark("catalog-projection");
        if (owner!.modData == null) owner.modData = new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
        owner.modData[SaveKey] = json;
        document = next; Revision++;
        PublishCatalog(catalog);
        timing.Mark("publish");
    }

    private void ResetCatalog()
    {
        catalogProjection = new byte[] { (byte)'[', (byte)']' };
        CatalogRevision++;
    }

    private void PublishCatalog(byte[] next)
    {
        if (catalogProjection.AsSpan().SequenceEqual(next.AsSpan())) return;
        catalogProjection = next;
        CatalogRevision++;
    }

    // Reuse the already validated serialization. Exact ordered record bytes
    // include identity, custody and every snapshot field, so mutable references
    // or equal IDs cannot conceal changed item data. Skip nested item trees
    // without allocating another JsonDocument/DTO graph or validating again.
    private static byte[] CreateCatalogProjection(string json)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        using var output = new System.IO.MemoryStream();
        output.WriteByte((byte)'[');
        var found = false; var first = true;
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new InvalidOperationException("目录版本缺少储物封套。");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new InvalidOperationException("目录版本封套字段无效。");
            var items = reader.ValueTextEquals(nameof(Document.Items));
            if (!reader.Read()) throw new InvalidOperationException("目录版本封套不完整。");
            if (!items) { reader.Skip(); continue; }
            if (found || reader.TokenType != JsonTokenType.StartArray)
                throw new InvalidOperationException("目录版本物品列表无效。");
            found = true;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                    throw new InvalidOperationException("目录版本记录无效。");
                var start = checked((int)reader.TokenStartIndex);
                var included = false;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                        throw new InvalidOperationException("目录版本记录字段无效。");
                    var place = reader.ValueTextEquals(nameof(Record.Place));
                    if (!reader.Read()) throw new InvalidOperationException("目录版本记录不完整。");
                    if (place)
                        included = reader.TokenType == JsonTokenType.String &&
                            (reader.ValueTextEquals("box") || reader.ValueTextEquals("cleanup") || reader.ValueTextEquals("delivery"));
                    reader.Skip();
                }
                if (reader.TokenType != JsonTokenType.EndObject)
                    throw new InvalidOperationException("目录版本记录未结束。");
                if (!included) continue;
                if (!first) output.WriteByte((byte)',');
                first = false;
                output.Write(bytes.AsSpan(start, checked((int)reader.BytesConsumed) - start));
            }
            if (reader.TokenType != JsonTokenType.EndArray)
                throw new InvalidOperationException("目录版本物品列表未结束。");
        }
        if (!found || reader.TokenType != JsonTokenType.EndObject || reader.Read())
            throw new InvalidOperationException("目录版本封套未结束。");
        output.WriteByte((byte)']');
        return output.ToArray();
    }

    internal void ValidateReplacement(List<Record> items)
    {
        if (!Ready) throw new InvalidOperationException("储物存档上下文不可用。");
        Validate(new Document { Items = items });
    }

    internal void Add(Record item)
    {
        var next = new List<Record>(document.Items) { item };
        Replace(next);
    }

    internal void Remove(string id) => Replace(document.Items.Where(item => item.Id != id).ToList());

    internal void Move(string id, string place, int x = 0, int y = 0, WorkroomItemCodec.Snapshot? snapshot = null, string slotName = "")
    {
        var old = Find(id) ?? throw new InvalidOperationException("记录已转移。");
        var replacement = new Record { Id = old.Id, Place = place, Slot = slotName, X = x, Y = y, Item = snapshot ?? old.Item };
        Replace(document.Items.Select(item => item.Id == id ? replacement : item).ToList());
    }

    internal bool Fits(WorkroomItemCodec.Shape footprint, int x, int y, string? excluding = null)
    {
        return Fits(RoomOccupancy(excluding), footprint, x, y);
    }

    internal bool FindSpace(Record item, out int x, out int y)
    {
        var occupied = RoomOccupancy(item.Id);
        for (y = 0; y < Rows; y++)
            for (x = 0; x < Columns; x++)
                if (Fits(occupied, item.Item.Footprint, x, y)) return true;
        x = y = 0; return false;
    }

    // Plan the entire batch against a private list before publishing any item.
    internal static bool PlaceAll(List<Record> next, IEnumerable<Record> outputs)
    {
        var occupied = Occupancy(next);
        // Place large footprints first so small pieces do not consume their only gap.
        foreach (var item in outputs.OrderByDescending(record => record.Item.Footprint.Width * record.Item.Footprint.Height))
        {
            var found = false;
            for (var y = 0; y < Rows && !found; y++)
                for (var x = 0; x < Columns && !found; x++)
                {
                    var shape = item.Item.Footprint;
                    if (!Fits(occupied, shape, x, y)) continue;
                    item.Place = "room"; item.Slot = ""; item.X = x; item.Y = y;
                    Fill(occupied, shape, x, y);
                    next.Add(item); found = true;
                }
            if (!found) return false;
        }
        return true;
    }

    // One bit per actual occupied cell: bounding-box holes remain usable.
    private static bool[] Occupancy(IEnumerable<Record> items, string? excluding = null)
    {
        var occupied = new bool[Columns * Rows];
        foreach (var item in items)
            if (item.Place == "room" && item.Id != excluding) Fill(occupied, item.Item.Footprint, item.X, item.Y);
        return occupied;
    }

    private static bool Fits(bool[] occupied, WorkroomItemCodec.Shape shape, int x, int y)
    {
        if (x < 0 || y < 0 || x + shape.Width > Columns || y + shape.Height > Rows) return false;
        for (var iy = 0; iy < shape.Height; iy++)
            for (var ix = 0; ix < shape.Width; ix++)
                if (shape.Cells[iy * shape.Width + ix] != 0 && occupied[(y + iy) * Columns + x + ix]) return false;
        return true;
    }

    private static void Fill(bool[] occupied, WorkroomItemCodec.Shape shape, int x, int y)
    {
        if (!Fits(occupied, shape, x, y)) throw new InvalidOperationException("工作间物品越界或重叠。");
        for (var iy = 0; iy < shape.Height; iy++)
            for (var ix = 0; ix < shape.Width; ix++)
                if (shape.Cells[iy * shape.Width + ix] != 0) occupied[(y + iy) * Columns + x + ix] = true;
    }

    private static void Validate(Document value)
    {
        if (value.Version != 2 || value.Items == null) throw new InvalidOperationException("储物版本不支持。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var nativeIds = new HashSet<int>();
        var slots = new HashSet<string>(StringComparer.Ordinal);
        var occupied = new bool[Columns * Rows];
        foreach (var item in value.Items)
        {
            if (item == null || string.IsNullOrEmpty(item.Id) || !ids.Add(item.Id) ||
                (item.Place != "box" && item.Place != "room" && item.Place != "cleanup" && item.Place != "delivery" && item.Place != "slot"))
                throw new InvalidOperationException("储物记录身份或去向无效。");
            if (item.Place == "slot" && (!WorkroomComponentAssembly.ValidSlot(item.Slot) || !slots.Add(item.Slot)))
                throw new InvalidOperationException("装配槽位无效或重复。");
            WorkroomItemCodec.Validate(item.Item);
            if (item.Item.Count != 1) throw new InvalidOperationException("当前储物记录只支持独立单件。");
            foreach (var id in WorkroomItemCodec.Identities(item.Item))
                if (!nativeIds.Add(id)) throw new InvalidOperationException("重复物品身份。");
            if (item.Place == "room" && (item.X < 0 || item.Y < 0 ||
                item.X + item.Item.Footprint.Width > Columns || item.Y + item.Item.Footprint.Height > Rows))
                throw new InvalidOperationException("工作间库存位置无效。");
            if (item.Place == "room") Fill(occupied, item.Item.Footprint, item.X, item.Y);
        }
        WorkroomComponentAssembly.ValidateSlots(value.Items);
    }
}
