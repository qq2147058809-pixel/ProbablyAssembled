using System;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>名片只负责首次解锁；记录跟随当前存档，不跟随实体物品。</summary>
internal static class AssemblerContactAccess
{
    internal const string UnlockKey = "pcrepair.phone_assembler.unlocked";

    internal static bool HasUnlocked(PlayerStore? store)
    {
        if (store == null) return false;
        try
        {
            if (store.modData != null && store.modData.ContainsKey(UnlockKey) &&
                store.modData[UnlockKey] == "1") return true;

            // 当前档实际持卡才补记；电话拨打历史不作为解锁证据。
            if (!store.IsPlayerOwnThisItem(ContactCard.Id)) return false;
            return Unlock(store);
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("读取 0504 永久解锁状态失败：" + ex.Message);
            return false;
        }
    }

    private static bool Unlock(PlayerStore? store)
    {
        if (store == null) return false;
        try
        {
            if (store.modData == null)
                store.modData = new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
            if (store.modData.ContainsKey(UnlockKey) && store.modData[UnlockKey] == "1") return true;
            store.modData[UnlockKey] = "1";
            Core.Log?.Msg("[深空装机] 当前存档已永久解锁 0504；名片丢失不再影响联系。");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log?.Error("保存 0504 永久解锁状态失败：" + ex);
            return false;
        }
    }

    [HarmonyPatch(typeof(GeneralHelper), nameof(GeneralHelper.SetItemOwned))]
    internal static class CardOwnershipPatch
    {
        // 工厂的 isOwned=true 也会触发此回调；须确认名片已进入玩家拥有物品列表。
        // 购买完成走独立回调，不依赖购买过程中所有权/入包的先后顺序。
        private static void Postfix(GameItem __0, bool __1)
        {
            if (__1 && ContactCard.IsCard(__0)) HasUnlocked(PlayerStore.Instance);
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.OnItemBought))]
    internal static class CardPurchasePatch
    {
        private static void Postfix(PlayerStore __instance, GameItem gameItem)
        {
            if (ContactCard.IsCard(gameItem)) Unlock(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.LoadGame))]
    internal static class CardLoadPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PlayerStore __instance, bool __runOriginal)
        { if (__runOriginal) HasUnlocked(__instance); }
    }
}
