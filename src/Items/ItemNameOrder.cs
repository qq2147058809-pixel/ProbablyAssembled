using System.Text.RegularExpressions;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

// Display projection for current items whose saved label predates the text template.
// Never edit their native scalar/claim/identity data merely to change a label.
internal static class ItemNameOrder
{
    private static readonly Regex component = new(@"^(?<kind>[^·\r\n]+)·(?<model>[^\r\n]+) T(?<tier>[1-5])(?<tail>(?:（破损）)?(?:（已装配）)?(?: .*)?)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex dedicated = new(@"^T(?<tier>[1-5]) (?<kind>专用(?:矿机|服务器)箱)(?<tail>.*)$",
        RegexOptions.CultureInvariant);

    internal static string Normalize(string identifier, string name)
    {
        if (!LanguageText.IsChinese || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(identifier)) return name;
        if (Components.Find(identifier) != null)
        {
            var match = component.Match(name);
            if (!match.Success) return name;
            var kind = match.Groups["kind"].Value;
            // Already ordered labels can still contain a model ending in T1..T5.
            if (kind.Length >= 3 && kind[^3] == ' ' && kind[^2] == 'T' && kind[^1] is >= '1' and <= '5') return name;
            return kind + " T" + match.Groups["tier"].Value + "·" + match.Groups["model"].Value + match.Groups["tail"].Value;
        }
        if (ServerRoomMachine.TryDefinition(identifier, out _))
        {
            var match = dedicated.Match(name);
            if (match.Success) return match.Groups["kind"].Value + " T" + match.Groups["tier"].Value + match.Groups["tail"].Value;
        }
        return name;
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetDisplayName))]
    internal static class DisplayNamePatch
    {
        private static void Postfix(GameItem __instance, ref string __result)
        {
            if (!LanguageText.IsChinese || string.IsNullOrEmpty(__result) ||
                (!__result.Contains('·') && !__result.StartsWith("T", System.StringComparison.Ordinal))) return;
            __result = Normalize(Core.Clean(__instance.identifier), __result);
        }
    }
}
