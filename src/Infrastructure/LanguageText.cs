using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCExpansion;

/// <summary>本体只保存中文；可选补丁通过版本校验后提供独立翻译表。</summary>
public static class LanguageText
{
    public const string ContractVersion = "pcexpansion-localization-v1";
    private static readonly Dictionary<string, string> Chinese;
    private static Dictionary<string, string>? english;
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    public static string CatalogFingerprint { get; }
    internal static event Action? LanguageChanged;

    static LanguageText()
    {
        using var stream = typeof(LanguageText).Assembly.GetManifestResourceStream(
            "PCExpansion.Localization.zh.json")
            ?? throw new InvalidOperationException("缺少中文文本资源。");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        CatalogFingerprint = Convert.ToHexString(SHA256.HashData(bytes));
        Chinese = JsonSerializer.Deserialize<Dictionary<string, string>>(bytes)
            ?? throw new InvalidOperationException("中文文本资源无法读取。");
    }

    internal static bool IsChinese => english == null;
    internal static string LocaleCode => IsChinese ? "中文本体" : "独立语言补丁";

    /// <summary>补丁仅传入 .NET 字典，不依赖补丁类型或注册游戏玩法。</summary>
    public static bool InstallEnglishTranslations(string contract, string fingerprint,
        Dictionary<string, string> translations)
    {
        try
        {
        english = null;
        if (contract != ContractVersion || fingerprint != CatalogFingerprint || translations == null)
        {
            WarnOnce("contract", "语言补丁版本不匹配，继续使用中文。");
            return false;
        }
        var accepted = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in translations)
        {
            if (!Chinese.TryGetValue(entry.Key, out var fallback) || string.IsNullOrWhiteSpace(entry.Value) ||
                entry.Value.Contains('\uFFFD') || entry.Value.Contains('\0') ||
                !HasMatchingArguments(fallback, entry.Value))
            {
                WarnOnce(entry.Key, "语言补丁含无效文本，相关内容回退中文：" + entry.Key);
                continue;
            }
            accepted.Add(entry.Key, entry.Value);
        }
        if (accepted.Count == 0)
        {
            WarnOnce("empty", "语言补丁没有有效译文，继续使用中文。");
            return false;
        }
        english = accepted;
        if (accepted.Count != Chinese.Count)
            WarnOnce("coverage", "语言补丁译文不完整，缺失内容将回退中文。");
        return true;
        }
        finally { LanguageChanged?.Invoke(); }
    }

    internal static string Get(string key, params object[] arguments)
    {
        if (!Chinese.TryGetValue(key, out var fallback))
        {
            WarnOnce(key, "未找到中文文本：" + key);
            return "文字暂不可用";
        }
        var useEnglish = english != null && english.ContainsKey(key);
        if (english != null && !useEnglish) WarnOnce(key, "缺少译文，回退中文：" + key);
        var template = Resolve(key, useEnglish);
        try { return Format(template, arguments, useEnglish); }
        catch (FormatException)
        {
            WarnOnce(key, "译文格式不匹配，回退中文：" + key);
            try { return Format(fallback, arguments, false); }
            catch (FormatException) { return "文字暂不可用"; }
        }
    }

    // 延迟解析嵌套名称和列表，使整条中文回退不会混入已提前翻译的英文参数。
    internal static object Argument(string key) => new TextArgument(key);
    internal static object List(IEnumerable<string> keys) => new TextList(keys);
    internal static object Template(string key, params object[] arguments) => new FormattedTextArgument(key, arguments);

    private static string Resolve(string key, bool useEnglish)
    {
        if (useEnglish && english != null && english.TryGetValue(key, out var translation)) return translation;
        if (useEnglish) WarnOnce(key, "缺少译文，回退中文：" + key);
        return Chinese.TryGetValue(key, out var text) ? text : "文字暂不可用";
    }

    private static string Format(string template, object[] arguments, bool useEnglish)
    {
        var values = new object[arguments.Length];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = arguments[index] switch
            {
                TextArgument text => Resolve(text.Key, useEnglish),
                FormattedTextArgument text => Format(Resolve(text.Key, useEnglish), text.Arguments,
                    useEnglish && english != null && english.ContainsKey(text.Key)),
                TextList list => string.Join(Resolve("list.separator", useEnglish),
                    Array.ConvertAll(list.Keys, key => Resolve(key, useEnglish))),
                _ => arguments[index]
            };
        }
        return string.Format(CultureInfo.InvariantCulture, template, values);
    }

    private static bool HasMatchingArguments(string fallback, string translation)
    {
        try
        {
            var slots = new object[16];
            Array.Fill(slots, "0");
            _ = string.Format(CultureInfo.InvariantCulture, fallback, slots);
            _ = string.Format(CultureInfo.InvariantCulture, translation, slots);
            return ArgumentIndexes(fallback).SetEquals(ArgumentIndexes(translation));
        }
        catch (FormatException) { return false; }
    }

    private static HashSet<string> ArgumentIndexes(string template)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(template, @"(?<!\{)\{(\d+)(?:[,}:])"))
            result.Add(match.Groups[1].Value);
        return result;
    }

    private static void WarnOnce(string key, string message)
    {
        if (Reported.Add(key)) Core.Log?.Warning(message);
    }

    private sealed class TextArgument
    {
        internal readonly string Key;
        internal TextArgument(string key) => Key = key;
    }

    private sealed class TextList
    {
        internal readonly string[] Keys;
        internal TextList(IEnumerable<string> keys) => Keys = new List<string>(keys).ToArray();
    }

    private sealed class FormattedTextArgument
    {
        internal readonly string Key;
        internal readonly object[] Arguments;
        internal FormattedTextArgument(string key, object[] arguments)
        {
            Key = key;
            Arguments = (object[])arguments.Clone();
        }
    }
}
