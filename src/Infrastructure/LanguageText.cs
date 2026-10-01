using System;

namespace ProbablyAssembled;

/// <summary>
/// The main mod is Chinese by default. The optional English patch selects the
/// English strings explicitly; the game's current locale is never consulted.
/// </summary>
public static class LanguageText
{
    private static bool englishPatchEnabled;

    internal static bool IsChinese => !englishPatchEnabled;
    internal static string LocaleCode => englishPatchEnabled ? "English patch" : "Chinese base";

    /// <summary>Entry point used by ProbablyAssembledEnglishPatch when that patch is loaded.</summary>
    public static void EnableEnglishPatch()
    {
        englishPatchEnabled = true;
        Core.Log?.Msg("[深空装机] English patch detected; mod text is set to English.");
    }

    internal static string Get(string chineseText, string englishText) =>
        englishPatchEnabled ? englishText : chineseText;
}
