using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;

namespace PCExpansion;

/// <summary>中文手册独立选取原版简体字体，不依赖未启用页面的本地化状态。</summary>
internal static class ComputerManualFont
{
    private static IntPtr observedBook;
    private static bool observedTitle;
    private static bool observedArrow;

    internal static TMP_FontAsset Prepare(ChineseComputerManual.Document content)
    {
        var loader = FontLoader.Instance;
        var font = loader == null ? null : loader.simplifiedChineseFont;
        if (font == null) throw new InvalidOperationException("原版简体中文手册字体尚未就绪。");

        var characters = new HashSet<char>();
        AddCharacters(characters, content.Title);
        foreach (var page in content.Pages)
        {
            AddCharacters(characters, page.Heading);
            foreach (var element in page.Elements)
                if (!element.IsIcon) AddCharacters(characters, element.Text);
        }
        var ordered = new List<char>(characters);
        ordered.Sort();
        var required = new string(ordered.ToArray());
        var before = Missing(font, required);
        // The real Chinese asset is dynamic; its source font supplies the glyphs
        // absent from the saved atlas, including arrows and several Chinese words.
        if (before.Count > 0) font.TryAddCharacters(required, out string _, true);
        var missing = Missing(font, required);
        if (missing.Count > 0)
            throw new InvalidOperationException("中文手册字体补字失败：" + Codepoints(missing));
        var material = font.material;
        if (material == null || material.mainTexture == null)
            throw new InvalidOperationException("原版简体中文手册字体图集材质尚未就绪。");
        Core.Log?.Msg("[电脑手册字体] 字体=" + font.name + "，材质=" + material.name +
            "，图集=" + material.mainTexture.name + "，模式=" + font.atlasPopulationMode +
            "，所需字符=" + required.Length + "，补字前缺字=" + before.Count + "，添加后缺字=0。");
        return font;
    }

    private static void AddCharacters(HashSet<char> characters, string value)
    {
        foreach (var character in value)
            if (!char.IsWhiteSpace(character)) characters.Add(character);
    }

    private static List<char> Missing(TMP_FontAsset font, string required)
    {
        var missing = new List<char>();
        foreach (var character in required)
            // Verify this asset after the batch addition; do not mask a failure
            // with another font or mutate global fallback tables.
            if (!font.HasCharacter(character, false, false)) missing.Add(character);
        return missing;
    }

    private static string Codepoints(List<char> characters)
    {
        var codes = new List<string>();
        foreach (var character in characters) codes.Add("U+" + ((int)character).ToString("X4"));
        return string.Join(",", codes);
    }

    internal static void ObserveVisiblePage(GameObject book, int pageIndex)
    {
        try
        {
            if (book == null || !book.activeInHierarchy || pageIndex < 0 || pageIndex >= book.transform.childCount) return;
            if (observedBook != book.Pointer)
            {
                observedBook = book.Pointer;
                observedTitle = observedArrow = false;
            }
            if (observedTitle && observedArrow) return;
            var page = book.transform.GetChild(pageIndex).gameObject;
            foreach (var text in page.GetComponentsInChildren<TextMeshProUGUI>(false))
            {
                var title = !observedTitle && text.gameObject.name == "Title";
                var arrow = !observedArrow && text.text.Contains('→');
                if (!title && !arrow) continue;
                text.ForceMeshUpdate(false, true);
                var info = text.textInfo;
                var visibleChinese = 0;
                var visibleArrows = 0;
                string glyphFont = "无", glyphMaterial = "无", glyphAtlas = "无";
                for (var i = 0; info != null && i < info.characterCount; i++)
                {
                    var character = info.characterInfo[i];
                    if (!character.isVisible) continue;
                    if (character.character >= '\u4e00' && character.character <= '\u9fff') visibleChinese++;
                    if (character.character == '→') visibleArrows++;
                    if (glyphFont != "无") continue;
                    glyphFont = character.fontAsset == null ? "无" : character.fontAsset.name;
                    var material = character.material;
                    glyphMaterial = material == null ? "无" : material.name;
                    glyphAtlas = material == null || material.mainTexture == null ? "无" : material.mainTexture.name;
                }
                Core.Log?.Msg("[电脑手册字形] 页=" + (pageIndex + 1) + "，元素=" + text.gameObject.name +
                    "，可见中文字形=" + visibleChinese + "，可见箭头=" + visibleArrows +
                    "，实际字形字体=" + glyphFont + "，材质=" + glyphMaterial + "，图集=" + glyphAtlas + "。");
                if (title) observedTitle = true;
                if (arrow) observedArrow = true;
            }
        }
        catch (Exception ex)
        {
            // A diagnostic must never interrupt the native reader's page switch.
            observedTitle = observedArrow = true;
            Core.Log?.Warning("电脑手册显示自检暂不可用：" + ex.Message);
        }
    }
}
