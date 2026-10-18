using System;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Native perk catalogue and presentation; selection/points remain native.</summary>
internal static class StartingPerks
{
    internal const string EfficientRecycling = "pcexpansion.efficient_recycling";
    internal const string ComputerApprentice = "pcexpansion.computer_apprentice";
    internal const string CryptoExpert = "pcexpansion.crypto_expert";
    private static readonly string[] Names = { "efficient_recycling", "computer_apprentice", "crypto_expert" };

    internal static bool HasEfficientRecycling() => PlayerStore.IsInstanceExist() &&
        PlayerStore.instance != null && Has(PlayerStore.instance, EfficientRecycling);

    internal static bool Has(PlayerStore store, string id)
    {
        if (store.startingPerks == null) return false;
        foreach (var perk in store.startingPerks)
            if (perk != null && perk.id == id) return true;
        return false;
    }

    private static string? ShortName(string? id)
    {
        foreach (var name in Names) if (id == "pcexpansion." + name) return name;
        return null;
    }

    private static void EnsureIcons()
    {
        var icons = StartingPerkIconLoader.perkIcons;
        if (icons == null) return;
        foreach (var name in Names)
        {
            var sprite = SpriteAssets.Get("perk_" + name);
            if (sprite != null) icons["pcexpansion." + name] = sprite;
        }
    }

    [HarmonyPatch(typeof(StartingPerkList), nameof(StartingPerkList.InitStartingPerk))]
    internal static class CataloguePatch
    {
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix()
        {
            var perks = StartingPerkList.Perks ?? throw new InvalidOperationException("原版开局特性目录尚未初始化。");
            foreach (var name in Names)
            {
                var id = "pcexpansion." + name;
                var exists = false;
                foreach (var perk in perks) if (perk != null && perk.id == id) { exists = true; break; }
                if (exists) continue;
                perks.Add(new StartingPerk {
                    id = id, cost = name == "efficient_recycling" ? 2 : 1, maxSlot = 0,
                    incompatiblePerks = new Il2CppSystem.Collections.Generic.List<string>(),
                    type = StartingPerk.StartingPerkType.POSITIVE,
                    rawName = LanguageText.Get("starting_perk." + name + ".name"),
                    rawDescription = LanguageText.Get("starting_perk." + name + ".description")
                });
            }
        }
    }

    [HarmonyPatch(typeof(StartingPerk), nameof(StartingPerk.GetLocalizedDisplayName))]
    internal static class NamePatch
    {
        internal static bool Prefix(StartingPerk __instance, ref string __result)
        {
            var name = ShortName(__instance.id);
            if (name == null) return true;
            __result = LanguageText.Get("starting_perk." + name + ".name");
            return false;
        }
    }

    [HarmonyPatch(typeof(StartingPerk), nameof(StartingPerk.GetLocalizedDescription))]
    internal static class DescriptionPatch
    {
        internal static bool Prefix(StartingPerk __instance, ref string __result)
        {
            var name = ShortName(__instance.id);
            if (name == null) return true;
            __result = LanguageText.Get("starting_perk." + name + ".description");
            return false;
        }
    }

    [HarmonyPatch(typeof(StartingPerkIconLoader), nameof(StartingPerkIconLoader.Start))]
    internal static class IconLoaderPatch
    {
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix() => EnsureIcons();
    }

    [HarmonyPatch(typeof(StartingPerkElement), nameof(StartingPerkElement.Start))]
    internal static class ElementPatch
    {
        internal static void Prefix(StartingPerkElement __instance)
        { if (ShortName(__instance.perk?.id ?? __instance.id) != null) EnsureIcons(); }
    }

    [HarmonyPatch(typeof(StartingPerkElement), nameof(StartingPerkElement.SetTooltipContent))]
    internal static class TooltipPatch
    {
        internal static void Prefix(StartingPerk __0)
        {
            var name = __0 == null ? null : ShortName(__0.id);
            if (name == null || !LanguageText.IsChinese) return;
            var ui = PerkUIController.Instance;
            if (ui == null) return;
            var font = FontLoader.Instance?.simplifiedChineseFont ??
                throw new InvalidOperationException("开局特性简体字体尚未就绪。");
            var text = LanguageText.Get("starting_perk." + name + ".name") +
                LanguageText.Get("starting_perk." + name + ".description");
            // Native tooltips assign raw text; prepare only the custom tooltip's
            // characters at its actual event, without global fallback changes.
            var missing = false;
            foreach (var character in text)
                if (!char.IsWhiteSpace(character) && !font.HasCharacter(character, false, false)) { missing = true; break; }
            if (missing) font.TryAddCharacters(text, out string _, true);
            foreach (var character in text)
                if (!char.IsWhiteSpace(character) && !font.HasCharacter(character, false, false))
                    throw new InvalidOperationException("开局特性简体字体缺字：U+" + ((int)character).ToString("X4"));
            if (ui.startingPerkTooltipTitle != null) ui.startingPerkTooltipTitle.font = font;
            if (ui.startingPerkTooltipDescription != null) ui.startingPerkTooltipDescription.font = font;
        }
    }
}
