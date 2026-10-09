using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCExpansion;

/// <summary>版本受控的可选页面入口。本体没有英文正文或补丁引用。</summary>
public static class ComputerManualPages
{
    public const string ContractVersion = "pcexpansion-computer-manual-v13";
    internal const int PageCount = 10;
    private static Page[]? english;
    internal static int Revision { get; private set; }
    internal static event Action? PagesChanged;
    internal static bool UseEnglish => !LanguageText.IsChinese && english != null;

    internal sealed class Page
    {
        internal string Title = string.Empty;
        internal string Body = string.Empty;
        internal string[] Illustrations = Array.Empty<string>();
    }

    internal static Page EnglishPage(int index) => english![index];

    public static bool InstallEnglishManualPages(string contract, string catalogFingerprint, string json)
    {
        english = null;
        try
        {
            if (contract != ContractVersion || catalogFingerprint != LanguageText.CatalogFingerprint ||
                LanguageText.IsChinese) throw new InvalidOperationException("手册语言版本不匹配。");
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            RequireFields(root, "contract", "pages");
            if (root.GetProperty("contract").GetString() != ContractVersion)
                throw new InvalidOperationException("手册页面契约不匹配。");
            var pages = root.GetProperty("pages");
            if (pages.ValueKind != JsonValueKind.Array || pages.GetArrayLength() != PageCount)
                throw new InvalidOperationException("手册页面数量不匹配。");
            var accepted = new Page[PageCount];
            for (var i = 0; i < PageCount; i++)
            {
                var entry = pages[i];
                RequireFields(entry, "title", "body", "illustrations");
                var page = new Page
                {
                    Title = ReadText(entry.GetProperty("title"), 100),
                    Body = ReadText(entry.GetProperty("body"), 2600)
                };
                var illustrations = entry.GetProperty("illustrations");
                if (illustrations.ValueKind != JsonValueKind.Array || illustrations.GetArrayLength() > 6)
                    throw new InvalidOperationException("手册配图列表无效。");
                var ids = new List<string>();
                foreach (var illustration in illustrations.EnumerateArray())
                {
                    var id = illustration.GetString() ?? string.Empty;
                    if (Components.Find(id) == null)
                        throw new InvalidOperationException("手册配图物品未登记：" + id);
                    ids.Add(id);
                }
                page.Illustrations = ids.ToArray();
                accepted[i] = page;
            }
            english = accepted;
            return true;
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("电脑手册英文页面不可用，整本回退中文：" + ex.Message);
            return false;
        }
        finally
        {
            Revision++;
            PagesChanged?.Invoke();
        }
    }

    private static string ReadText(JsonElement element, int maximum)
    {
        var text = element.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Contains('\0') ||
            text.Contains('\uFFFD') || text.Contains('<') || text.Contains('>') ||
            Regex.IsMatch(text, @"[\u3400-\u9FFF]"))
            throw new InvalidOperationException("手册英文正文无效。");
        return text;
    }

    private static void RequireFields(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("手册字段类型错误。");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidOperationException("手册字段重复。");
        if (!names.SetEquals(expected)) throw new InvalidOperationException("手册字段不匹配。");
    }
}
