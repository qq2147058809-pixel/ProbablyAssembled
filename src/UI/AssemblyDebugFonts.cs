#nullable disable
using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppTMPro;

namespace PCExpansion;

/// <summary>只为装机调试控件选取原版简体字体；不修改全局回退。</summary>
internal static class AssemblyDebugFonts
{
    private static TMP_FontAsset prepared;
    private static string preparedText;

    internal static void Reset()
    {
        prepared = null;
        preparedText = null;
    }

    internal static TMP_FontAsset Prepare(string title, string notice)
    {
        var loader = FontLoader.Instance;
        var font = loader == null ? null : loader.simplifiedChineseFont;
        if (font == null) throw new InvalidOperationException("原版简体字体尚未就绪。");
        var required = title + notice + "×";
        if (prepared != null && prepared.Pointer == font.Pointer && preparedText == required &&
            font.material != null && font.material.mainTexture != null) return font;
        var characters = new HashSet<char>();
        foreach (var c in required) if (!char.IsWhiteSpace(c)) characters.Add(c);
        var ordered = new List<char>(characters);
        ordered.Sort();
        var all = new string(ordered.ToArray());
        var missing = Missing(font, all);
        if (missing.Length > 0) font.TryAddCharacters(all, out string _, true);
        missing = Missing(font, all);
        if (missing.Length > 0) throw new InvalidOperationException("装机调试字体仍缺字：" + missing);
        if (font.material == null || font.material.mainTexture == null)
            throw new InvalidOperationException("原版简体字体图集材质尚未就绪。");
        // Failed preparation never populates this cache: later UI updates may retry.
        prepared = font;
        preparedText = required;
        return font;
    }

    private static string Missing(TMP_FontAsset font, string text)
    {
        var missing = new List<string>();
        foreach (var c in text)
            if (!font.HasCharacter(c, false, false)) missing.Add("U+" + ((int)c).ToString("X4"));
        return string.Join(",", missing);
    }
}
