using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PCExpansion;

/// <summary>中文本体独立加载原生图文布局；不依赖英文补丁或整页贴图。</summary>
internal static class ChineseComputerManual
{
    internal const string ResourceName = "PCExpansion.Manual.zh.json";
    internal const float PaperWidth = 628;
    internal const float PaperHeight = 804;
    private static Document? cached;
    internal static Document Content => cached ??= Load();

    internal sealed class Document
    {
        internal string Title = string.Empty;
        internal Page[] Pages = Array.Empty<Page>();
    }

    internal sealed class Page
    {
        internal string Heading = string.Empty;
        internal Element[] Elements = Array.Empty<Element>();
    }

    internal sealed class Element
    {
        internal bool IsIcon;
        internal string Text = string.Empty;
        internal string Id = string.Empty;
        internal float X, Y, Width, Height, FontSize;
        internal string Style = "body";
        internal bool Center;
    }

    // JSON uses exported sprite names; native factories use these stable item IDs.
    internal static string? VanillaItemId(string id) => id switch
    {
        "vanilla:electronic1" => "common_electronic",
        "vanilla:wire" => "wire",
        "vanilla:nuts_metal_pile" => "nuts_metal",
        "vanilla:printer_plastic" => "printer_plastic",
        "vanilla:scrap_metal" => "scrap_metal",
        "vanilla:metal_ingot" => "metal_ingot",
        "vanilla:energy_credit" => "energy_credit",
        _ => null
    };

    internal static bool IsIconId(string id) =>
        VanillaItemId(id) != null || Components.Find(id) != null;

    private static Document Load()
    {
        using var stream = typeof(ChineseComputerManual).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("缺少中文电脑手册布局资源。");
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true));
        return Parse(reader.ReadToEnd());
    }

    // Also exercised directly by managed tests; Unity is deliberately outside parsing.
    internal static Document Parse(string json)
    {
        if (json == null || json.Length > 131072) throw new InvalidOperationException("中文手册资源大小无效。");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Fields(root, new[] { "contract", "title", "pages" });
        if (root.GetProperty("contract").GetString() != ComputerManualPages.ContractVersion)
            throw new InvalidOperationException("中文手册布局契约不匹配。");
        var title = Text(root.GetProperty("title"), 80);
        if (title != "电脑装机与交易手册") throw new InvalidOperationException("中文手册标题不匹配。");
        var pages = root.GetProperty("pages");
        if (pages.ValueKind != JsonValueKind.Array || pages.GetArrayLength() != ComputerManualPages.PageCount)
            throw new InvalidOperationException("中文手册页面数量不匹配。");
        var accepted = new Document { Title = title, Pages = new Page[ComputerManualPages.PageCount] };
        for (var i = 0; i < accepted.Pages.Length; i++)
        {
            var entry = pages[i];
            Fields(entry, new[] { "heading", "elements" });
            var elements = entry.GetProperty("elements");
            if (elements.ValueKind != JsonValueKind.Array || elements.GetArrayLength() < 1 || elements.GetArrayLength() > 160)
                throw new InvalidOperationException("中文手册页面元素数量无效。");
            var page = new Page
            {
                Heading = Text(entry.GetProperty("heading"), 80),
                Elements = new Element[elements.GetArrayLength()]
            };
            for (var j = 0; j < page.Elements.Length; j++)
            {
                var raw = elements[j];
                if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("type", out var kind))
                    throw new InvalidOperationException("中文手册元素类型缺失。");
                var type = kind.GetString();
                var icon = type == "icon";
                if (!icon && type != "text") throw new InvalidOperationException("中文手册元素类型未知。");
                Fields(raw, icon
                    ? new[] { "type", "id", "x", "y", "width", "height" }
                    : new[] { "type", "text", "x", "y", "width", "height", "fontSize", "style", "align" },
                    icon ? new[] { "fontSize", "style", "align" } : Array.Empty<string>());
                var element = new Element
                {
                    IsIcon = icon, X = Number(raw, "x", 0, 1), Y = Number(raw, "y", 0, 1),
                    Width = Number(raw, "width", .001f, 1), Height = Number(raw, "height", .001f, 1)
                };
                if (element.X + element.Width > 1.00001f || element.Y + element.Height > 1.00001f)
                    throw new InvalidOperationException("中文手册元素超出纸张边界。");
                if (icon)
                {
                    element.Id = Text(raw.GetProperty("id"), 120);
                    if (!IsIconId(element.Id)) throw new InvalidOperationException("中文手册配图未登记：" + element.Id);
                }
                else element.Text = Text(raw.GetProperty("text"), 3200);
                if (raw.TryGetProperty("fontSize", out _)) element.FontSize = Number(raw, "fontSize", 9, 80);
                if (raw.TryGetProperty("style", out var style))
                {
                    element.Style = style.GetString() ?? string.Empty;
                    if (element.Style != "body" && element.Style != "blue" && element.Style != "green" &&
                        element.Style != "caption" && element.Style != "arrow")
                        throw new InvalidOperationException("中文手册文字样式未知。");
                }
                if (raw.TryGetProperty("align", out var align))
                {
                    var alignment = align.GetString();
                    if (alignment != "left" && alignment != "center") throw new InvalidOperationException("中文手册文字对齐未知。");
                    element.Center = alignment == "center";
                }
                page.Elements[j] = element;
            }
            accepted.Pages[i] = page;
        }
        return accepted;
    }

    private static float Number(JsonElement element, string name, float min, float max)
    {
        if (!element.GetProperty(name).TryGetSingle(out var number) || !float.IsFinite(number) || number < min || number > max)
            throw new InvalidOperationException("中文手册布局数值无效：" + name);
        return number;
    }

    private static string Text(JsonElement element, int max)
    {
        if (element.ValueKind != JsonValueKind.String) throw new InvalidOperationException("中文手册文本类型无效。");
        var text = element.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || text.Length > max || text.Contains('\0') || text.Contains('\uFFFD') ||
            text.Contains('<') || text.Contains('>')) throw new InvalidOperationException("中文手册文本无效。");
        return text;
    }

    private static void Fields(JsonElement element, string[] required, string[]? optional = null)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("中文手册字段类型错误。");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var allowed = new HashSet<string>(required, StringComparer.Ordinal);
        if (optional != null) allowed.UnionWith(optional);
        foreach (var property in element.EnumerateObject())
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name)) throw new InvalidOperationException("中文手册字段重复或未知。");
        foreach (var name in required)
            if (!seen.Contains(name)) throw new InvalidOperationException("中文手册字段缺失：" + name);
    }
}
