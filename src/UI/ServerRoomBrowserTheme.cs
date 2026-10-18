using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>Local browser appearance only; never part of a player's save document.</summary>
internal static class ServerRoomBrowserTheme
{
    internal enum Mode { Light, Soft, Dark }
    internal enum Tone
    {
        Ink, Muted, Line, Paper, Back, Blue, Selected, Brown, OnAccent,
        Tabs, Subtle, Address, Sidebar, CatalogBack, CatalogLine, ArtworkGround, CatalogSelected, CatalogMuted,
        Desktop, Wallpaper, DesktopInk, ShortcutInk, Taskbar, TaskSelected, TaskInk, TaskMuted,
        Chart, ChartInk, ChartMuted, ChartGrid, ChartLine, Gain, Loss, Error, Shield
    }

    // Columns are standard light, low-luminance grey-green, and dark.
    // Each semantic role has its own entry even when two entries share a colour.
    private static readonly int[,] palette = {
        { 0x242B34, 0x202E28, 0xE0E8E3 }, // Ink
        { 0x5A6470, 0x39483F, 0xA5B7AD }, // Muted
        { 0xDFE3E8, 0x87998C, 0x475950 }, // Line
        { 0xFFFFFF, 0xBCC9BB, 0x28382F }, // Paper
        { 0xF4F5F7, 0xA9BAAA, 0x1F2C25 }, // Back
        { 0x11569E, 0x103841, 0x8CC8D5 }, // Blue
        { 0xE6EFFA, 0x98B3A6, 0x2D493E }, // Selected
        { 0x74471F, 0x493116, 0xDEBE87 }, // Brown
        { 0xFFFFFF, 0xF0F3E9, 0x172D27 }, // OnAccent
        { 0xE8E9EC, 0x96AA9B, 0x17251D }, // Tabs
        { 0xFAFBFC, 0xB2C2B2, 0x25352B }, // Subtle
        { 0xF0F1F3, 0xA9BAAA, 0x1B2B22 }, // Address
        { 0xF9FAFB, 0xB2C2B2, 0x223229 }, // Sidebar
        { 0xFBFAF7, 0xB2BFAC, 0x293329 }, // CatalogBack
        { 0xE3DBD1, 0x8C967E, 0x57634F }, // CatalogLine
        { 0xF2F2EF, 0x9DAC97, 0x33432F }, // ArtworkGround
        { 0xEEE2D3, 0xADB69A, 0x3D492A }, // CatalogSelected
        { 0x6B5E4D, 0x484730, 0xC3C3A4 }, // CatalogMuted
        { 0x20384B, 0x263D32, 0x14271E }, // Desktop
        { 0x385A72, 0x3F5C49, 0x264333 }, // Wallpaper
        { 0xABC2D1, 0x9DB8A3, 0x98B5A2 }, // DesktopInk
        { 0xDBF0FF, 0xD7E4D2, 0xD3E5D8 }, // ShortcutInk
        { 0x1C2833, 0x20372B, 0x101E17 }, // Taskbar
        { 0x334757, 0x3D5A48, 0x34513F }, // TaskSelected
        { 0xD6E6F5, 0xD4E2D4, 0xD4E5D8 }, // TaskInk
        { 0xADBFCE, 0xAAC3AF, 0xA3BDAA }, // TaskMuted
        { 0x18242F, 0x2E4438, 0x14251C }, // Chart
        { 0xD4E3F0, 0xD7E5D5, 0xD8E8DC }, // ChartInk
        { 0xBFCEDB, 0xB5CDB8, 0xAAC7B3 }, // ChartMuted
        { 0x33424F, 0x5B7460, 0x3E5C49 }, // ChartGrid
        { 0x66BAEB, 0xA0CDB5, 0x8BCDB0 }, // ChartLine
        { 0x9C542E, 0x623818, 0xF0BB88 }, // Gain
        { 0x21795D, 0x174A35, 0x8CD0A6 }, // Loss
        { 0xB83329, 0x852E27, 0xFFA49A }, // Error
        { 0x000000, 0x000000, 0x000000 }  // Shield (alpha applied below)
    };

    private static MelonPreferences_Category? preferences;
    private static MelonPreferences_Entry<int>? setting;
    private static Mode mode = Mode.Soft;
    internal static Mode Current => mode;
    internal static string Name => LanguageText.Get("serverroom.web.theme_" + mode.ToString().ToLowerInvariant());

    internal static void Initialize(MelonPreferences_Category category)
    {
        preferences = category;
        setting = category.CreateEntry("BrowserTheme", (int)Mode.Soft,
            LanguageText.Get("serverroom.web.theme_preference"), LanguageText.Get("serverroom.web.theme_description"));
        mode = setting.Value is >= 0 and <= 2 ? (Mode)setting.Value : Mode.Soft;
    }

    internal static void RefreshLabels()
    {
        if (setting == null) return;
        setting.DisplayName = LanguageText.Get("serverroom.web.theme_preference");
        setting.Description = LanguageText.Get("serverroom.web.theme_description");
    }

    internal static void Cycle()
    {
        mode = (Mode)(((int)mode + 1) % 3);
        if (setting == null) return;
        setting.Value = (int)mode;
        try { preferences?.SaveToFile(false); }
        catch (Exception ex) { Core.Log?.Warning("BrowserTheme: " + ex.Message); }
    }

    internal static Color ColorFor(Tone tone)
    {
        var value = palette[(int)tone, (int)mode];
        return new Color(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f,
            (value & 255) / 255f, tone == Tone.Shield ? .48f : 1);
    }

    internal static Color Selection => Current == Mode.Light
        ? new Color(.25f, .55f, .88f, .35f) : new Color(.23f, .50f, .39f, .55f);

    internal static ColorBlock Controls => new() {
        m_NormalColor = Color.white,
        m_HighlightedColor = Current == Mode.Dark ? new Color(1.08f, 1.08f, 1.08f, 1) : new Color(.96f, .96f, .96f, 1),
        m_PressedColor = new Color(.90f, .90f, .90f, 1), m_SelectedColor = Color.white,
        disabledColor = new Color(.86f, .86f, .86f, 1), colorMultiplier = 1, fadeDuration = .06f
    };
}
