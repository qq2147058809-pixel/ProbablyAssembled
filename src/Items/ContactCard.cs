using System;
using HarmonyLib;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>0504 横版电脑配件名片：背包中的 2×1 文档物品。</summary>
internal static class ContactCard
{
    internal const string Id = "pcrepair.contact_card_0504";
    internal const string SpriteKey = "pcrepair.contact_card_0504_sprite";

    internal static bool IsCard(GameItem? item) =>
        item != null && Core.Clean(item.identifier) == Id;

    internal static GameItem Create()
    {
        var item = DirectoryMaster.Item("handnote", true);
        if (item == null) throw new InvalidOperationException("找不到原版物品模板：handnote");
        Apply(item);
        item.onLoaded = (Il2CppSystem.Action<GameItem>)(loaded => Apply(loaded));
        return item;
    }

    private static void Apply(GameItem item)
    {
        item.identifier = Id;
        var name = LanguageText.Get("0504 名片", "0504 Business Card");
        item.identifierName = name;
        item.SetName(name);
        item.shortDescription = LanguageText.Get(
            "下城区装机佬的电话：0504。他那儿时不时能淘到好东西。",
            "The lower-district builder's number: 0504. He usually has something worth a look.");
        item.longDescription = item.shortDescription;
        item.flavorText = "MONKEY · 0504";
        item.unitCount = 1;
        item.SetValue(50);
        item.SetShape(new GridShapeBuilder(2, 1).SetDataFill(1).Build());
        item.spritePath = SpriteKey;
        item.spriteAtlasPath = string.Empty;
        item.spriteChanged = true;
        item.RemoveAllGameItemType();
        item.SetGameItemType("DOCUMENT");
    }

    [HarmonyPatch(typeof(ModItemDirectory), "InitDirectory")]
    internal static class ContactCardDirectoryPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ModItemDirectory __instance)
        {
            try
            {
                Il2CppSystem.Func<GameItem> factory = (Func<GameItem>)(() => Create());
                __instance.Set(Id, factory);
                Core.Log?.Msg("已注册 0504 名片文档物品。");
            }
            catch (Exception ex)
            {
                Core.Log?.Error("注册 0504 名片失败：" + ex);
            }
        }
    }
}
