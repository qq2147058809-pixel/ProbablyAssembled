using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>每周四队首的收缴物资商江白；只售两盒，原版交易计入治安部声望。</summary>
internal static class SecuritySeizureMerchant
{
    internal const string Id = "pcrepair.security_seizure_merchant";
    internal const string SpriteKey = "pcrepair.jiang_bai_sprite";
    internal const string ClientTag = "PCREPAIR_SECURITY_SEIZURE_MERCHANT";
    private const string StockTag = "PCREPAIR_SECURITY_SEIZURE_STOCK";
    private const string LastDayKey = "pcrepair.security_seizure.last_scheduled_day";
    private static Il2CppSystem.Action? introduceAction;

    internal static bool IsMerchant(StoreClient? client) => client != null &&
        (client.IsTag(ClientTag) || Core.Clean(client.identifier) == Id);

    private static StoreClient Create()
    {
        // 普通商贩模板提供交易行为；不继承治安官的盘查和抓捕动作。
        var client = StoreClientList.CreateScavGeneral()
            ?? throw new InvalidOperationException("江白商贩模板创建失败。");
        var portrait = SpriteDict.Instance?.GetSprite("wanted3");
        if (portrait != null) SpriteAssets.SetPortraitReference(SpriteKey, portrait);
        client.identifier = Id;
        client.displayName = LanguageText.Get("security.merchant.name");
        client.realName = client.displayName;
        client.spriteName = SpriteKey;
        client.clientFaction = StoreClient.FACTION_SECURITY;
        client.clientIntent = StoreClient.ClientIntent.SELL;
        client.AddTag(ClientTag);
        client.AddBasicDialog();
        ConfigureDialogue(client);
        client.isIntroduced = false;
        return client;
    }

    private static void ConfigureDialogue(StoreClient client)
    {
        var greeting = new Dialogue().SetText(client.displayName,
            LanguageText.Get("security.merchant.greeting"));
        var offer = new Dialogue().SetText(client.displayName,
            LanguageText.Get("security.merchant.offer"));
        var question = new Dialogue().SetText(client.displayName,
            LanguageText.Get("security.merchant.question"));
        greeting.isMainDialog = true;
        greeting.SetNextDialogue(offer);
        offer.SetNextDialogue(question);
        introduceAction ??= (Il2CppSystem.Action)(Action)IntroduceCurrent;
        question.SetEndAction(introduceAction);
        client.mainDialogue = greeting;
        client.isMainDialogueStarted = false;
    }

    private static void IntroduceCurrent()
    {
        try
        {
            var client = PlayerStore.Instance?.currentClientInstance?.storeClient;
            if (!IsMerchant(client)) return;
            AddStock();
            client!.OnIntroduced();
        }
        catch (Exception ex) { Core.Log?.Error("江白开始交易失败：" + ex); }
    }

    private static void EnsureRegistered()
    {
        var registry = StoreClientListDict.storeClientDict;
        if (registry == null)
        {
            registry = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Func<StoreClient>>();
            StoreClientListDict.storeClientDict = registry;
        }
        registry[Id] = (Il2CppSystem.Func<StoreClient>)(Func<StoreClient>)Create;
    }

    private static void AddStock()
    {
        try
        {
            var store = PlayerStore.Instance;
            var client = store?.currentClientInstance?.storeClient;
            if (store == null || !IsMerchant(client)) return;
            NpcStockOffers.Stock(store, client!, StockTag, "SECURITY_SEIZURE",
                () => new List<string> { SecuritySeizureBoxes.LargeId, SecuritySeizureBoxes.SmallId },
                2, 2, id => id is SecuritySeizureBoxes.LargeId or SecuritySeizureBoxes.SmallId,
                configure: SecuritySeizureBoxes.PrepareStock,
                existingFilter: item => SecuritySeizureBoxes.IsBox(item),
                validate: plan =>
                {
                    if (plan.Count != 2 || plan[0] != SecuritySeizureBoxes.LargeId ||
                        plan[1] != SecuritySeizureBoxes.SmallId)
                        throw new InvalidOperationException("江白货单必须为大、小盒各一。");
                });
        }
        catch (Exception ex) { Core.Log?.Error("江白两盒上柜失败：" + ex); }
    }

    internal static void Arrange(PlayerStore store)
    {
        try
        {
            var day = StoreStation.GetDayCounter();
            if (day < 4 || day % 7 != 4) return;
            var manager = store.storeClientManager;
            var stack = manager?.clientStack;
            if (manager == null || stack == null) return;
            EnsureRegistered();
            var text = day.ToString(CultureInfo.InvariantCulture);
            var current = store.currentClientInstance?.storeClient;
            if (store.isClientArrived && IsMerchant(current)) { RecordDay(store, text); return; }
            StoreClient? existing = null;
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (!IsMerchant(stack[i])) continue;
                existing ??= stack[i];
                stack.RemoveAt(i);
            }
            if (existing != null)
            {
                stack.Insert(0, existing);
                RecordDay(store, text);
                return;
            }
            if (store.modData != null && store.modData.ContainsKey(LastDayKey) &&
                store.modData[LastDayKey] == text) return;
            var client = Create();
            manager.AddNextClient(client);
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (stack[i] == null || stack[i].Pointer != client.Pointer) continue;
                stack.RemoveAt(i);
                stack.Insert(0, client);
                RecordDay(store, text);
                Core.Log?.Msg("[治安收缴] 江白已排在周四队首：第 " + text + " 天。");
                return;
            }
            Core.Log?.Warning("江白入队未确认，保留次日重试资格。");
        }
        catch (Exception ex) { Core.Log?.Error("安排周四江白失败：" + ex); }
    }

    private static void RecordDay(PlayerStore store, string day)
    {
        store.modData ??= new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
        store.modData[LastDayKey] = day;
    }

    [HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.CreateStoreClient))]
    internal static class RestoreFactoryPatch
    {
        private static bool Prefix(string identifier, ref StoreClient __result)
        {
            if (Core.Clean(identifier) != Id) return true;
            try { __result = Create(); }
            catch (Exception ex) { Core.Log?.Error("恢复江白失败：" + ex); __result = null!; }
            return false;
        }
    }

    [HarmonyPatch(typeof(SpriteDict), nameof(SpriteDict.GetSprite))]
    internal static class PortraitPatch
    {
        private static bool Prefix(string key, ref UnityEngine.Sprite __result)
        {
            if (Core.Clean(key) != SpriteKey) return true;
            var portrait = SpriteAssets.Get(SpriteKey);
            if (portrait == null) return true;
            __result = portrait;
            return false;
        }
    }

    [HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.OnNextClientArrived))]
    internal static class ArrivalStockPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => AddStock();
    }

    [HarmonyPatch(typeof(StoreClient), nameof(StoreClient.OnIntroduced))]
    internal static class IntroductionStockPatch
    {
        private static void Prefix(StoreClient __instance)
        {
            if (IsMerchant(__instance)) AddStock();
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.LoadGame))]
    internal static class LoadPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PlayerStore __instance, bool __runOriginal)
        {
            if (!__runOriginal || !SaveGeneration.IsSupported(__instance)) return;
            try
            {
                EnsureRegistered();
                var current = __instance.currentClientInstance?.storeClient;
                if (IsMerchant(current))
                {
                    current!.AddBasicDialog();
                    if (!current.isIntroduced) ConfigureDialogue(current);
                    if (__instance.isClientArrived) AddStock();
                }
                var stack = __instance.storeClientManager?.clientStack;
                if (stack != null)
                    foreach (var client in stack)
                        if (IsMerchant(client) && !client.isIntroduced) ConfigureDialogue(client);
            }
            catch (Exception ex) { Core.Log?.Error("读档恢复江白失败：" + ex); }
        }
    }

    [HarmonyPatch(typeof(StoreReputation), nameof(StoreReputation.OnItemTraded))]
    internal static class ReputationPatch
    {
        private static void Prefix(ref string factionId, StoreClient storeClient, bool isPlayerSold,
            GameItem gameItem)
        {
            if (IsMerchant(storeClient) && !isPlayerSold && SecuritySeizureBoxes.IsBox(gameItem))
                factionId = StoreClient.FACTION_SECURITY;
        }
    }
}
