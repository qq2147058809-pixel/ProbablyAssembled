namespace PCExpansion;

/// <summary>中文风味文案由本体提供，英文风味文案独立存放在英文补丁。</summary>
internal static class ComponentFlavorText
{
    internal static string Get(Components.Item item) => LanguageText.Get(
        "flavor." + item.Owner.Stem + ".t" + item.Tier + (item.Broken ? ".broken" : ".intact"));
}
