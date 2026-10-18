using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCExpansion;

/// <summary>One immutable, validated content snapshot per process. No player progress is stored here.</summary>
internal static class StoryContent
{
    internal sealed class Line
    {
        public string Id { get; set; } = "";
        public string? Next { get; set; }
        public string[] Choices { get; set; } = Array.Empty<string>();
        public string? Command { get; set; }
        public bool Player { get; set; }
        public bool Complete { get; set; }
    }
    internal sealed class Content
    {
        public int schemaVersion { get; set; }
        public Dictionary<string, string> texts { get; set; } = new();
        public Dictionary<string, int> parameters { get; set; } = new();
        public Dictionary<string, Line> smallGraph { get; set; } = new();
    }
    private static readonly Lazy<string> DefaultJson = new(() =>
    {
        using var stream = typeof(StoryContent).Assembly.GetManifestResourceStream("PCExpansion.Stories.npc-stories.json")
            ?? throw new InvalidOperationException("缺少内嵌剧情配置。");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    });
    private static readonly Lazy<Content> Defaults = new(() => ParseShape(DefaultJson.Value));
    private static Content? current;
    private static bool initialized;
    internal static string? LoadedPath { get; private set; }
    internal static bool UsingFallback { get; private set; }
    internal static IReadOnlyDictionary<string, Line> SmallGraph => (current ?? Defaults.Value).smallGraph;

    internal static void Initialize(string directory, Action<string>? warning = null)
    {
        if (initialized) return;
        initialized = true;
        // Validate even the embedded baseline before using it for a fallback.
        var baseline = Validate(DefaultJson.Value);
        current = baseline;
        LoadedPath = Path.Combine(directory, "npc-stories.json");
        try
        {
            CreateIfMissing(LoadedPath, DefaultJson.Value);
            var file = new FileInfo(LoadedPath);
            if (file.Length > 1024 * 1024) throw new InvalidOperationException("剧情配置超过1 MiB。");
            current = Validate(File.ReadAllText(LoadedPath, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            UsingFallback = true;
            warning?.Invoke("剧情配置无效或无法读取，保留原文件，本次整套使用内嵌默认配置；修改后需重启：" + LoadedPath + "；" + ex.Message);
        }
    }

    internal static string Text(string key, string fallback) =>
        (current ?? Defaults.Value).texts.TryGetValue(key, out var value) ? value : fallback;
    internal static int Number(string key, int fallback) =>
        (current ?? Defaults.Value).parameters.TryGetValue(key, out var value) ? value : fallback;

    // Pure-data entry point used by offline fault injection; never replaces an existing user file.
    internal static bool CreateIfMissing(string path, string json, Action<string>? beforeMove = null)
    {
        if (File.Exists(path)) return false;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".npc-stories-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var bytes = new UTF8Encoding(false, true).GetBytes(json);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            beforeMove?.Invoke(temporary);
            try { File.Move(temporary, path, false); return true; }
            catch (IOException) when (File.Exists(path)) { return false; }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static Content Validate(string json)
    {
        if (json.Length > 1024 * 1024) throw new InvalidOperationException("剧情配置超过限制。");
        var result = ParseShape(json);
        var baseline = Defaults.Value;
        if (result.schemaVersion != 1) throw new InvalidOperationException("未知剧情配置版本。");
        ExactKeys(result.texts, baseline.texts.Keys);
        ExactKeys(result.parameters, baseline.parameters.Keys);
        ExactKeys(result.smallGraph, baseline.smallGraph.Keys);
        foreach (var entry in result.texts)
        {
            if (string.IsNullOrWhiteSpace(entry.Value) || entry.Value.Length > 8192 || entry.Value.Contains('\0') || entry.Value.Contains('\uFFFD') ||
                !Arguments(entry.Value).SetEquals(Arguments(baseline.texts[entry.Key])))
                throw new InvalidOperationException("剧情文本或模板参数无效：" + entry.Key);
        }
        foreach (var entry in result.parameters)
        {
            var maximum = entry.Key.EndsWith("delayDays", StringComparison.Ordinal) ? 365 : 100000000;
            if (entry.Value < 1 || entry.Value > maximum) throw new InvalidOperationException("剧情触发参数越界：" + entry.Key);
        }
        foreach (var entry in result.smallGraph)
        {
            var line = entry.Value; var original = baseline.smallGraph[entry.Key];
            // Command locations and completion semantics are part of the persisted transaction contract.
            if (line == null || line.Id != entry.Key || line.Player != original.Player || line.Complete != original.Complete ||
                line.Command != original.Command || line.Choices == null || line.Choices.Length > 8 ||
                line.Choices.Distinct(StringComparer.Ordinal).Count() != line.Choices.Length ||
                line.Next != null && line.Choices.Length != 0 || line.Player && line.Choices.Length != 0)
                throw new InvalidOperationException("剧情节点身份、命令或完成标记无效：" + entry.Key);
            foreach (var target in line.Choices.Concat(line.Next == null ? Array.Empty<string>() : new[] { line.Next }))
            {
                if (!result.smallGraph.ContainsKey(target) || target.Substring(0, 2) != entry.Key.Substring(0, 2))
                    throw new InvalidOperationException("剧情引用越界：" + entry.Key);
            }
            foreach (var choice in line.Choices)
                if (!result.smallGraph[choice].Player) throw new InvalidOperationException("剧情选项不是玩家台词：" + choice);
            if (!result.texts.ContainsKey("smallstory." + entry.Key)) throw new InvalidOperationException("剧情节点缺少正文。");
        }
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidOperationException("剧情图含循环：" + id);
            var line = result.smallGraph[id];
            if (line.Next != null) Visit(line.Next);
            foreach (var choice in line.Choices) Visit(choice);
            visiting.Remove(id); visited.Add(id);
        }
        foreach (var id in result.smallGraph.Keys) Visit(id);
        return result;
    }

    private static HashSet<string> Arguments(string value)
    {
        var arguments = new object[16]; Array.Fill(arguments, "0");
        _ = string.Format(CultureInfo.InvariantCulture, value, arguments);
        return Regex.Matches(value, @"(?<!\{)\{(\d+)(?:[,}:])").Cast<Match>()
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }
    private static void ExactKeys<T>(Dictionary<string, T>? values, IEnumerable<string> keys)
    {
        if (values == null || !new HashSet<string>(values.Keys, StringComparer.Ordinal).SetEquals(keys))
            throw new InvalidOperationException("剧情配置缺少必要键或含未知键。");
    }
    private static Content ParseShape(string json)
    {
        using var document = JsonDocument.Parse(json);
        void Unique(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                { if (!names.Add(property.Name)) throw new InvalidOperationException("剧情配置字段重复。"); Unique(property.Value); }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
        }
        void Fields(JsonElement value, params string[] names)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(names))
                throw new InvalidOperationException("剧情配置字段不完整或含未知字段。");
        }
        Unique(document.RootElement);
        Fields(document.RootElement, "schemaVersion", "texts", "parameters", "smallGraph");
        foreach (var line in document.RootElement.GetProperty("smallGraph").EnumerateObject())
            Fields(line.Value, "Id", "Next", "Choices", "Command", "Player", "Complete");
        return JsonSerializer.Deserialize<Content>(json) ?? throw new InvalidOperationException("剧情配置为空。");
    }
}
