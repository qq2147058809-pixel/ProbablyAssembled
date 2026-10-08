using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PCExpansion;

/// <summary>快照只保留一棵内部件树；原生标签在捕获时脱离，在恢复前重建。</summary>
internal static class WorkroomAssemblyData
{
    private const string MachineReference = "@pc.snapshot.machine.v3";
    private const string CompositionReference = "@pc.snapshot.composition.v3";
    private const string CrateReference = "@pc.snapshot.crate.v1";
    private const string PackedPrefix = "PCGZ3:";
    // T5 supports at most 88 current item snapshots (case/body, board with
    // two 8-part GPUs, four 7-part RAMs, two 6-part SSDs, cooler, PSU and fans).
    // At the existing 65,536-cell shape ceiling, two serialized shapes per
    // snapshot occupy under 48 MiB even using four bytes per cell. 256 MiB
    // UTF-8 leaves over 5x that envelope for scalar/tag/feature data, the
    // original whole-item provenance on component bodies, and is
    // also far above the observed pre-compaction 8.8M-character case record.
    // Unknown instance strings remain supported within this explicit budget.
    internal const int MaxDecodedBytes = 256 * 1024 * 1024;
    private static readonly string[] Tags = { WorkroomMachineAssembly.Tag, WorkroomComponentParts.CompositionTag, WorkroomCrateContents.Tag };

    internal static string Encode<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, WorkroomStorageState.JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaxDecodedBytes) throw new InvalidOperationException("内部件记录超过支持大小。");
        if (json.Length < 4096) return json;
        using var output = new MemoryStream();
        using (var zip = new GZipStream(output, CompressionLevel.Fastest, true))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            zip.Write(bytes, 0, bytes.Length);
        }
        var packed = PackedPrefix + Convert.ToBase64String(output.ToArray());
        return packed.Length < json.Length ? packed : json;
    }

    internal static string DecodeJson(string raw)
    {
        if (!raw.StartsWith(PackedPrefix, StringComparison.Ordinal))
        {
            if (Encoding.UTF8.GetByteCount(raw) > MaxDecodedBytes) throw new InvalidOperationException("内部件记录超过支持大小。");
            return raw;
        }
        if (raw.Length - PackedPrefix.Length > (long)MaxDecodedBytes * 4 / 3 + 4)
            throw new InvalidOperationException("压缩记录输入超过支持大小。");
        using var input = new MemoryStream(Convert.FromBase64String(raw[PackedPrefix.Length..]));
        using var zip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = zip.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (output.Length + read > MaxDecodedBytes) throw new InvalidOperationException("解压记录超过支持大小，原文保留。");
            output.Write(buffer, 0, read);
        }
        return new UTF8Encoding(false, true).GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    internal static T? Decode<T>(string raw) => JsonSerializer.Deserialize<T>(DecodeJson(raw), WorkroomStorageState.JsonOptions);

    private static string Reference(string tag) => tag == WorkroomMachineAssembly.Tag ? MachineReference :
        tag == WorkroomComponentParts.CompositionTag ? CompositionReference : CrateReference;
    private static bool HasRecord(WorkroomItemCodec.Node node, string tag) =>
        tag == WorkroomMachineAssembly.Tag ? node.Machine != null :
        tag == WorkroomComponentParts.CompositionTag ? node.Composition != null : node.SlotItems.Count != 0;
    private static string Record(WorkroomItemCodec.Node node, string tag) => tag == WorkroomMachineAssembly.Tag ?
        Encode(node.Machine ?? throw new InvalidOperationException("机器引用缺少数据。")) :
        tag == WorkroomComponentParts.CompositionTag ? Encode(node.Composition ?? throw new InvalidOperationException("配件引用缺少数据。")) :
        Encode(WorkroomCrateContents.Envelope(node.SlotItems));

    private static bool Active(Dictionary<string, JsonElement> fields) =>
        fields.TryGetValue("valueEnabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;
    private static string Value(Dictionary<string, JsonElement> fields) =>
        fields.TryGetValue("internalValueString", out var raw) && raw.ValueKind == JsonValueKind.String ?
            raw.GetString()! : throw new InvalidOperationException("装配标签缺少字符串字段。");

    internal static void ValidateNode(WorkroomItemCodec.Node node)
    {
        foreach (var tag in Tags)
        {
            var has = HasRecord(node, tag);
            var active = node.State.TryGetValue(tag, out var fields) && Active(fields);
            if (has != active || has && Value(fields!) != Reference(tag))
                throw new InvalidOperationException("装配引用与内部件记录不一致。");
            if (node.ModifiedState.TryGetValue(tag, out var modified) && Active(modified) &&
                (!has || Value(modified) != Reference(tag)))
                throw new InvalidOperationException("装配有效标签与内部件记录不一致。");
        }
    }

    // Verify the live record before removing its redundant copies from the snapshot.
    // Unknown tag fields and all non-assembly state remain byte-for-byte JSON values.
    internal static void Detach(WorkroomItemCodec.Snapshot snapshot)
    {
        var payload = JsonNode.Parse(snapshot.Payload) as JsonObject ?? throw new InvalidOperationException("物品图无效。");
        var saved = PayloadNodes(payload, snapshot);
        for (var i = 0; i < snapshot.Nodes.Count; i++)
        {
            var node = snapshot.Nodes[i];
            foreach (var tag in Tags)
            {
                if (!HasRecord(node, tag)) continue;
                var expected = tag == WorkroomMachineAssembly.Tag ?
                    JsonSerializer.Serialize(node.Machine, WorkroomStorageState.JsonOptions) :
                    tag == WorkroomComponentParts.CompositionTag ? JsonSerializer.Serialize(node.Composition, WorkroomStorageState.JsonOptions) :
                    JsonSerializer.Serialize(WorkroomCrateContents.Envelope(node.SlotItems), WorkroomStorageState.JsonOptions);
                string DetachValue(string actual)
                {
                    // JSON whitespace is not part of the record, but all actual fields are.
                    var canonical = tag == WorkroomMachineAssembly.Tag ?
                        JsonSerializer.Serialize(Decode<WorkroomMachineAssembly.MachineInfo>(actual), WorkroomStorageState.JsonOptions) :
                        tag == WorkroomComponentParts.CompositionTag ? JsonSerializer.Serialize(Decode<List<WorkroomItemCodec.Snapshot>>(actual), WorkroomStorageState.JsonOptions) :
                        JsonSerializer.Serialize(Decode<WorkroomCrateContents.Document>(actual), WorkroomStorageState.JsonOptions);
                    if (canonical != expected) throw new InvalidOperationException("捕获期间装配记录不一致。");
                    return Reference(tag);
                }
                foreach (var state in new[] { node.State, node.ModifiedState })
                    if (state.TryGetValue(tag, out var fields) && Active(fields))
                        fields["internalValueString"] = JsonSerializer.SerializeToElement(DetachValue(Value(fields)));
                RewriteNative(saved[i], tag, DetachValue, true);
            }
            ValidateNode(node);
        }
        snapshot.Payload = payload.ToJsonString(WorkroomStorageState.JsonOptions);
    }

    internal static string ExpandPayload(WorkroomItemCodec.Snapshot snapshot)
    {
        var payload = JsonNode.Parse(snapshot.Payload) as JsonObject ?? throw new InvalidOperationException("物品图无效。");
        var saved = PayloadNodes(payload, snapshot);
        for (var i = 0; i < snapshot.Nodes.Count; i++)
            foreach (var tag in Tags)
                if (HasRecord(snapshot.Nodes[i], tag))
                {
                    var record = Record(snapshot.Nodes[i], tag);
                    RewriteNative(saved[i], tag, actual => actual == Reference(tag) ? record :
                        throw new InvalidOperationException("原生图装配引用无效。"), true);
                }
        return payload.ToJsonString(WorkroomStorageState.JsonOptions);
    }

    internal static Dictionary<string, Dictionary<string, JsonElement>> ExpandState(
        WorkroomItemCodec.Node node, Dictionary<string, Dictionary<string, JsonElement>> source)
    {
        var result = new Dictionary<string, Dictionary<string, JsonElement>>(source, StringComparer.Ordinal);
        foreach (var tag in Tags)
            if (source.TryGetValue(tag, out var fields) && Active(fields))
            {
                if (!HasRecord(node, tag) || Value(fields) != Reference(tag)) throw new InvalidOperationException("装配状态引用无效。");
                result[tag] = new Dictionary<string, JsonElement>(fields)
                { ["internalValueString"] = JsonSerializer.SerializeToElement(Record(node, tag)) };
            }
        return result;
    }

    internal static void ValidateNative(JsonElement native, WorkroomItemCodec.Node node)
    {
        if (!native.TryGetProperty("_keys", out var keys) || !native.TryGetProperty("_values", out var values) ||
            keys.ValueKind != JsonValueKind.Array || values.ValueKind != JsonValueKind.Array || keys.GetArrayLength() != values.GetArrayLength())
        {
            if (Tags.Any(tag => HasRecord(node, tag))) throw new InvalidOperationException("原生图标签列表无效。");
            return;
        }
        foreach (var tag in Tags)
        {
            var count = 0;
            for (var i = 0; i < keys.GetArrayLength(); i++)
                if (keys[i].ValueKind == JsonValueKind.String && keys[i].GetString() == tag &&
                    values[i].TryGetProperty("valueEnabled", out var enabled) && enabled.ValueKind == JsonValueKind.True)
                {
                    count++;
                    if (!HasRecord(node, tag) || !values[i].TryGetProperty("internalValueString", out var raw) ||
                        raw.ValueKind != JsonValueKind.String || raw.GetString() != Reference(tag))
                        throw new InvalidOperationException("原生图内部件引用与记录不一致。");
                }
            if (count != (HasRecord(node, tag) ? 1 : 0)) throw new InvalidOperationException("原生图装配引用数量无效。");
        }
    }

    private static JsonObject[] PayloadNodes(JsonObject payload, WorkroomItemCodec.Snapshot snapshot)
    {
        if (payload["saveItems"] is not JsonArray array || array.Count != snapshot.Nodes.Count)
            throw new InvalidOperationException("物品图节点数量不符。");
        var nodes = array.Select(n => n as JsonObject ?? throw new InvalidOperationException("物品图节点无效。")).ToArray();
        for (var i = 0; i < nodes.Length; i++)
            if (nodes[i]["uuid"]?.GetValue<long>() != snapshot.Nodes[i].Uuid)
                throw new InvalidOperationException("物品图节点身份不符。");
        return nodes;
    }

    private static void RewriteNative(JsonObject native, string tag, Func<string, string> rewrite, bool required)
    {
        if (native["_keys"] is not JsonArray keys || native["_values"] is not JsonArray values || keys.Count != values.Count)
            throw new InvalidOperationException("原生图标签列表无效。");
        var count = 0;
        for (var i = 0; i < keys.Count; i++)
            if (keys[i]?.GetValue<string>() == tag && values[i] is JsonObject fields &&
                fields["valueEnabled"]?.GetValue<bool>() == true)
            {
                fields["internalValueString"] = rewrite(fields["internalValueString"]?.GetValue<string>() ?? "");
                count++;
            }
        if (count != (required ? 1 : 0)) throw new InvalidOperationException("原生图装配标签数量无效。");
    }
}

// Payload remains a string only at the Unity JsonUtility boundary. In saved
// snapshots it is an object, so nesting cannot repeatedly escape its contents.
internal sealed class WorkroomPayloadConverter : JsonConverter<string>
{
    public WorkroomPayloadConverter() { }
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("仅支持结构化物品图。");
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
    }
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        using var document = JsonDocument.Parse(value);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("物品图必须是对象。");
        document.RootElement.WriteTo(writer);
    }
}
