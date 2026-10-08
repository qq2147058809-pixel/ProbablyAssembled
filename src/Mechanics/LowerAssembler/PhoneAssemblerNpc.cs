using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>0504 电话召来的装机佬：复用原版电话联系人冷却和现有电脑收购规则。</summary>
internal static class PhoneAssemblerNpc
{
    internal const string Id = "pcrepair.phone_assembler";
    internal const string SpriteKey = "pcrepair.phone_assembler_sprite";
    internal const string ClientTag = "PCREPAIR_PHONE_ASSEMBLER";
    internal const string StockTag = "PCREPAIR_PHONE_ASSEMBLER_STOCK";
    internal const long PhoneNumber = 504;
    internal const int Budget = 15000;

    private const string DialogId = "pcrepair_phone_assembler";
    private static readonly Random random = new();

    internal static bool IsPhoneAssembler(StoreClient? client) =>
        client != null && (client.IsTag(ClientTag) || Core.Clean(client.identifier) == Id);

    private static bool IsPhoneContact(StorePhoneClient? client) =>
        client != null && Core.Clean(client.locID) == Id;

    private static StorePhoneClient CreatePhoneContact()
    {
        var contact = new StorePhoneClient
        {
            phoneClientType = StorePhoneClient.PhoneClientType.Supplier,
            phoneState = StorePhoneClient.PhoneState.Regular,
            displayName = LanguageText.Get("text.e419ca83983b"),
            locID = Id,
            dialogFuncId = DialogId,
            cooldownDuration = 3,
            currentCooldown = 0,
            dialedBefore = false,
        };
        return contact;
    }

    private static StorePhoneClient EnsurePhoneContact(
        Il2CppSystem.Collections.Generic.Dictionary<long, StorePhoneClient>? contacts)
    {
        if (contacts == null) throw new InvalidOperationException("原版电话联系人字典尚未初始化。");
        if (contacts.ContainsKey(PhoneNumber))
        {
            var existing = contacts[PhoneNumber];
            if (!IsPhoneContact(existing))
                throw new InvalidOperationException("0504 已被原版或其他联系人占用，未覆盖既有电话联系人。");
            existing.displayName = LanguageText.Get("text.e419ca83983b");
            existing.dialogFuncId = DialogId;
            existing.cooldownDuration = 3;
            return existing;
        }

        var contact = CreatePhoneContact();
        contacts.Add(PhoneNumber, contact);
        Core.Log?.Msg("[深空装机] 已将装机佬注册到原版电话簿（0504，冷却 3 天）。");
        return contact;
    }

    internal static StoreClient CreateClient()
    {
        var client = StoreClientList.CreateUpperLowerVisitor();
        if (client == null) throw new InvalidOperationException("原版访客模板创建失败。");

        var reference = SpriteDict.Instance?.GetSprite("wanted3");
        if (reference != null)
            SpriteAssets.SetPortraitReference(SpriteKey, reference);

        client.identifier = Id;
        client.displayName = LanguageText.Get("text.e419ca83983b");
        client.realName = client.displayName;
        client.spriteName = SpriteKey;
        client.clientFaction = StoreClient.FACTION_LOWER;
        client.clientIntent = StoreClient.ClientIntent.SELLNBUY;
        client.isMainDialogueStarted = false;
        client.isNoContributeToEvidence = true;
        client.AddTag(ClientTag);
        LowerAssemblerNpc.ConfigurePurchaseRules(client);
        LowerAssemblerNpc.ConfigureTradeDialogue(client);
        LowerAssemblerNpc.ApplyAssemblerBudget(client, Budget);

        var greeting = new Dialogue().SetText(client.displayName, LanguageText.Get("text.13aa4b2a3621") );
        var offerLine = new Dialogue().SetText(client.displayName,
            LanguageText.Get("text.a3ec80c70209") );
        greeting.isMainDialog = true;
        greeting.SetNextDialogue(offerLine);
        client.mainDialogue = greeting;
        return client;
    }

    private static Dialogue GetCallDialog(StorePhoneClient contact)
    {
        if (contact.phoneState == StorePhoneClient.PhoneState.Cooldown)
            return PhoneDialogList.GenericCooldown(LanguageText.Get("text.e419ca83983b"));
        var greeting = new Dialogue().SetText(LanguageText.Get("text.e419ca83983b"),
            LanguageText.Get("text.8b2de9772e65") );
        var reply = new Dialogue().SetText(LanguageText.Get("text.e419ca83983b"),
            LanguageText.Get("text.8bf853ae1d76") );
        greeting.SetNextDialogue(reply);
        return greeting;
    }

    private static bool HasContactAccess(PlayerStore? store) => AssemblerContactAccess.HasUnlocked(store);

    private static bool IsAlreadyAtStore(PlayerStore? store)
    {
        var current = store?.currentClientInstance?.storeClient;
        if (IsPhoneAssembler(current)) return true;
        var queued = store?.storeClientManager?.clientStack;
        if (queued == null) return false;
        for (var i = 0; i < queued.Count; i++)
            if (IsPhoneAssembler(queued[i])) return true;
        return false;
    }

    private static void OnPhoneCallConnected(PhoneUIManager phone, long number)
    {
        try
        {
            if (number != PhoneNumber) return;
            var store = PlayerStore.Instance;
            if (!HasContactAccess(store))
            {
                StoreUIManager.Instance?.Notify(LanguageText.Get("text.34ccec0329de"), "#FFFFFF");
                return;
            }

            var contact = EnsurePhoneContact(store?.PhoneClientDict);
            if (contact.phoneState == StorePhoneClient.PhoneState.Cooldown) return;
            if (IsAlreadyAtStore(store))
            {
                contact.UseService();
                Core.Log?.Msg("[深空装机] 装机佬已在访客队列中，本次电话仍按原版规则进入三天冷却。");
                return;
            }

            var manager = store?.storeClientManager;
            if (manager == null)
            {
                Core.Log?.Warning("电话已接通，但访客队列尚未就绪；未消耗装机佬冷却。");
                return;
            }

            var owner = store!.Pointer; var run = store.runID; var saveSlot = store.saveSlotId;
            var client = CreateClient();
            manager.AddNextClient(client);
            var queued = false;
            var stack = manager.clientStack;
            if (stack != null)
                foreach (var candidate in stack)
                    if (candidate != null && candidate.Pointer == client.Pointer) { queued = true; break; }
            if (!queued || !PlayerStore.IsInstanceExist() || PlayerStore.instance == null ||
                PlayerStore.instance.Pointer != owner || PlayerStore.instance.runID != run ||
                PlayerStore.instance.saveSlotId != saveSlot)
            {
                Core.Log?.Warning("0504 来客未确认进入当前队列，未消耗三天冷却。");
                return;
            }
            contact.UseService();
            Core.Log?.Msg("[深空装机] 0504 电话已接通，装机佬已加入来客队列，原版三天冷却已启动。");
        }
        catch (Exception ex)
        {
            Core.Log?.Error("[深空装机] 处理 0504 电话来客失败：" + ex);
        }
    }

    private static void AddStock()
    {
        try
        {
            var store = PlayerStore.Instance;
            var client = store?.currentClientInstance?.storeClient;
            if (store == null || !IsPhoneAssembler(client)) return;

            LowerAssemblerNpc.ConfigurePurchaseRules(client!);
            LowerAssemblerNpc.ConfigureTradeDialogue(client!);
            LowerAssemblerNpc.ApplyAssemblerBudget(client!, Budget);
            NpcStockOffers.Stock(store, client!, StockTag, "PHONE", () => new List<string>
            {
                "pcrepair.computer_case_t" + random.Next(1, 4) + "_broken",
                "pcrepair.computer_case_t" + random.Next(1, 4) + "_broken",
                "pcrepair.computer_case_t" + random.Next(1, Components.TierCount + 1)
            }, 3, 3, id => Components.Find(id) is { } spec && spec.Owner.IsCase && (!spec.Broken || spec.Tier <= 3),
                item =>
                {
                    if (item.FindItemFeatureByCategory("retailMarkUp") == null)
                        item.AddItemFeature(ItemFeatureList.RetailMarkUp());
                }, validate: plan =>
                {
                    if (!Components.Find(plan[0])!.Broken || !Components.Find(plan[1])!.Broken || Components.Find(plan[2])!.Broken)
                        throw new InvalidOperationException("电话来客须供应两个低阶坏机箱和一个完好机箱。");
                });
        }
        catch (Exception ex)
        {
            Core.Log?.Error("[深空装机] 电话装机佬库存上柜失败：" + ex);
        }
    }

    [HarmonyPatch(typeof(SpriteDict), nameof(SpriteDict.GetSprite))]
    internal static class PortraitSpritePatch
    {
        private static bool Prefix(string key, ref UnityEngine.Sprite __result)
        {
            if (Core.Clean(key) != SpriteKey) return true;
            try
            {
                var sprite = SpriteAssets.Get(SpriteKey);
                if (sprite == null) return true;
                __result = sprite;
                return false;
            }
            catch (Exception ex)
            {
                Core.Log?.Warning("加载洛夕立绘失败：" + ex.Message);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.InitPhoneClientDict))]
    internal static class PhoneClientRegistrationPatch
    {
        private static void Postfix(ref Il2CppSystem.Collections.Generic.Dictionary<long, StorePhoneClient> __result)
        {
            try { EnsurePhoneContact(__result); }
            catch (Exception ex) { Core.Log?.Error("注册 0504 电话联系人失败：" + ex); }
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.LoadGame))]
    internal static class PhoneClientLoadPatch
    {
        private static void Postfix(PlayerStore __instance, bool __runOriginal)
        {
            if (!__runOriginal) return;
            try
            {
                EnsurePhoneContact(__instance.PhoneClientDict);
                if (__instance.isClientArrived && IsPhoneAssembler(__instance.currentClientInstance?.storeClient)) AddStock();
            }
            catch (Exception ex) { Core.Log?.Error("读档后恢复 0504 电话联系人失败：" + ex); }
        }
    }

    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.GetCallDialog))]
    internal static class PhoneDialogPatch
    {
        private static bool Prefix(StorePhoneClient __instance, ref Dialogue __result)
        {
            if (!IsPhoneContact(__instance)) return true;
            __result = GetCallDialog(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.GetLocalizedDisplayName))]
    internal static class PhoneNamePatch
    {
        private static bool Prefix(StorePhoneClient __instance, ref string __result)
        {
            if (!IsPhoneContact(__instance)) return true;
            __result = LanguageText.Get("text.e419ca83983b");
            return false;
        }
    }

    // 夜间报告使用 StoreClient.identifier 作为 ClientName 本地化 key，
    // 不读取访客实例的 displayName / realName。为电话装机佬补上专用名称映射，
    // 让本体中文与独立英文补丁都能显示正确名称。
    [HarmonyPatch(typeof(LocHelper), nameof(LocHelper.GetLocalizedClientName))]
    internal static class PhoneClientReportNamePatch
    {
        private static bool Prefix(string key, ref string __result)
        {
            if (Core.Clean(key) != Id) return true;
            __result = LanguageText.Get("text.e419ca83983b");
            return false;
        }
    }

    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.ShownInPhoneBook))]
    internal static class PhoneBookCardGatePatch
    {
        private static void Postfix(StorePhoneClient __instance, ref bool __result)
        {
            if (IsPhoneContact(__instance)) __result = HasContactAccess(PlayerStore.Instance);
        }
    }

    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.IsPhoneClientAtStore))]
    internal static class PhoneVisitCheckPatch
    {
        private static bool Prefix(long number, ref bool __result)
        {
            if (number != PhoneNumber) return true;
            __result = IsAlreadyAtStore(PlayerStore.Instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(PhoneUIManager), nameof(PhoneUIManager.WillAnswerCall))]
    internal static class PhoneCardDialGatePatch
    {
        private static bool Prefix(long number, ref bool __result)
        {
            if (number != PhoneNumber || HasContactAccess(PlayerStore.Instance)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(PhoneUIManager), nameof(PhoneUIManager.StartPhoneDialog))]
    internal static class PhoneCallPatch
    {
        private static bool Prefix(long currentNumber)
        {
            if (currentNumber != PhoneNumber || HasContactAccess(PlayerStore.Instance)) return true;
            StoreUIManager.Instance?.Notify(LanguageText.Get("text.34ccec0329de"), "#FFFFFF");
            return false;
        }

        private static void Postfix(PhoneUIManager __instance, long currentNumber) =>
            OnPhoneCallConnected(__instance, currentNumber);
    }

    [HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.OnNextClientArrived))]
    internal static class ArrivalStockPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => AddStock();
    }

    [HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.CreateStoreClient))]
    internal static class ClientRestorePatch
    {
        private static bool Prefix(string identifier, ref StoreClient __result)
        {
            if (Core.Clean(identifier) != Id) return true;
            try
            {
                __result = CreateClient();
                return false;
            }
            catch (Exception ex)
            {
                Core.Log?.Error("恢复电话装机佬失败：" + ex);
                return true;
            }
        }
    }
}
