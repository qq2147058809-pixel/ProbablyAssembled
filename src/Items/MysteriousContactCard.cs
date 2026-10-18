using System;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>The single, ordinary 1×2 document handed out by Jiangbai.</summary>
internal static class MysteriousContactCard
{
    internal const string Id = "pcrepair.contact_card_27571";
    internal const string SpriteKey = "pcrepair.contact_card_27571_sprite";
    internal static bool IsCard(GameItem? item) =>
        item != null && Core.Clean(item.identifier) == Id;
    internal static GameItem Create()
    {
        var item = DirectoryMaster.Item("handnote", true)
            ?? throw new InvalidOperationException("找不到原版物品模板：handnote");
        Apply(item);
        item.onLoaded = (Il2CppSystem.Action<GameItem>)(loaded => Apply(loaded));
        return item;
    }
    private static void Apply(GameItem item)
    {
        item.identifier = Id;
        var name = LanguageText.Get("jiangbai.contact.card_name");
        item.identifierName = name;
        item.SetName(name);
        // Functional instructions must stay current even when an existing story
        // configuration retains or customizes the card's original narrative text.
        item.shortDescription = LanguageText.Get("jiangbai.contact.card_usage");
        item.longDescription = item.shortDescription;
        item.flavorText = LanguageText.Get("jiangbai.contact.card_description");
        item.unitCount = 1;
        item.SetValue(50);
        item.SetShape(new GridShapeBuilder(1, 2).SetDataFill(1).Build());
        item.spritePath = SpriteKey;
        item.spriteAtlasPath = string.Empty;
        item.spriteChanged = true;
        item.RemoveAllGameItemType();
        item.SetGameItemType("DOCUMENT");
    }
    [HarmonyPatch(typeof(ModItemDirectory), "InitDirectory")]
    internal static class DirectoryPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ModItemDirectory __instance)
        {
            try { __instance.Set(Id, (Il2CppSystem.Func<GameItem>)(Func<GameItem>)(() => Create())); }
            catch (Exception ex) { Core.Log?.Error("注册27571名片失败：" + ex); }
        }
    }
}
