using System;
using HarmonyLib;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>0504 电话召来的装机佬：复用原版电话联系人冷却和现有电脑收购规则。</summary>
internal static class PhoneAssemblerNpc
{
    internal const string Id = "pcrepair.phone_assembler";
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
            displayName = LanguageText.Get("装机佬", "PC Builder"),
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
            existing.displayName = LanguageText.Get("装机佬", "PC Builder");
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
            SpriteAssets.SetPortraitReference(LowerAssemblerNpc.SpriteKey, reference);

        client.identifier = Id;
        client.displayName = LanguageText.Get("装机佬", "PC Builder");
        client.realName = client.displayName;
        client.spriteName = LowerAssemblerNpc.SpriteKey;
        client.clientFaction = StoreClient.FACTION_LOWER;
        client.clientIntent = StoreClient.ClientIntent.SELLNBUY;
        client.isMainDialogueStarted = false;
        client.isNoContributeToEvidence = true;
        client.AddTag(ClientTag);
        LowerAssemblerNpc.ConfigurePurchaseRules(client);
        LowerAssemblerNpc.ConfigureTradeDialogue(client);
        LowerAssemblerNpc.ApplyAssemblerBudget(client, Budget);

        var greeting = new Dialogue().SetText(client.displayName, LanguageText.Get("有啥好货都拿出来看看。", "Let's see what you've got.") );
        var offerLine = new Dialogue().SetText(client.displayName,
            LanguageText.Get("哥们我预算充足，我这里有几台机器你看看感不感兴趣。",
                "I've got cash to spend and a few towers to sell. Anything catch your eye?") );
        greeting.isMainDialog = true;
        greeting.SetNextDialogue(offerLine);
        client.mainDialogue = greeting;
        return client;
    }

    private static Dialogue GetCallDialog(StorePhoneClient contact)
    {
        if (contact.phoneState == StorePhoneClient.PhoneState.Cooldown)
            return PhoneDialogList.GenericCooldown(LanguageText.Get("装机佬", "PC Builder"));
        return new Dialogue().SetText(LanguageText.Get("装机佬", "PC Builder"),
            LanguageText.Get("行，我这就带几台机器过去，咱们店里聊。", "Sure, I'll bring a few rigs over. See you at the shop.") );
    }

    private static bool HasContactCard(PlayerStore? store)
    {
        try { return store != null && store.IsPlayerOwnThisItem(ContactCard.Id); }
        catch (Exception ex)
        {
            Core.Log?.Warning("检查 0504 名片持有状态失败：" + ex.Message);
            return false;
        }
    }

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
            if (!HasContactCard(store))
            {
                StoreUIManager.Instance?.Notify(LanguageText.Get("需要持有 0504 名片才能联系装机佬。",
                    "You need the 0504 business card to call the PC Builder."), "#FFFFFF");
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

            manager.AddNextClient(CreateClient());
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
            if (HasExistingStock()) return;

            for (var i = 0; i < 2; i++)
            {
                // T4+ parts cannot be repaired; keep both broken case offers in the repairable T1–T3 range.
                var brokenTier = random.Next(1, 4);
                AddOffer("pcrepair.computer_case_t" + brokenTier + "_broken");
            }

            var intactTier = random.Next(1, Components.TierCount + 1);
            AddOffer("pcrepair.computer_case_t" + intactTier);
            if (!store.IsPlayerOwnThisItem(UnboxTool.Id)) AddOffer(UnboxTool.Id);

            Core.Log?.Msg("[深空装机] 电话装机佬库存已上柜：2 个 T1–T3 可维修破损机箱、1 个完好机箱" +
                          (store.IsPlayerOwnThisItem(UnboxTool.Id) ? "。" : "、未持有的拆机螺丝刀。"));
        }
        catch (Exception ex)
        {
            Core.Log?.Error("[深空装机] 电话装机佬库存上柜失败：" + ex);
        }
    }

    private static void AddOffer(string id)
    {
        var item = DirectoryMaster.Item(id, true);
        if (item == null)
        {
            Core.Log?.Warning("电话装机佬库存物品尚未注册：" + id);
            return;
        }
        item.EnableTag(StockTag, false);
        PlayerStore.Instance?.AddDirectSellingItemToTable(item, false, false, false, 0);
    }

    private static bool HasExistingStock()
    {
        try
        {
            var items = EmporiumEntry.Instance?.GetAllNonOwnedItem();
            if (items == null) return false;
            foreach (var item in items)
                if (item != null && item.IsTag(StockTag)) return true;
        }
        catch (Exception ex) { Core.Log?.Warning("检查电话装机佬现有库存失败：" + ex.Message); }
        return false;
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
        private static void Postfix(PlayerStore __instance)
        {
            try { EnsurePhoneContact(__instance.PhoneClientDict); }
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
            __result = LanguageText.Get("装机佬", "PC Builder");
            return false;
        }
    }

    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.ShownInPhoneBook))]
    internal static class PhoneBookCardGatePatch
    {
        private static void Postfix(StorePhoneClient __instance, ref bool __result)
        {
            if (IsPhoneContact(__instance)) __result = HasContactCard(PlayerStore.Instance);
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
            if (number != PhoneNumber || HasContactCard(PlayerStore.Instance)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(PhoneUIManager), nameof(PhoneUIManager.StartPhoneDialog))]
    internal static class PhoneCallPatch
    {
        private static bool Prefix(long currentNumber)
        {
            if (currentNumber != PhoneNumber || HasContactCard(PlayerStore.Instance)) return true;
            StoreUIManager.Instance?.Notify(LanguageText.Get("需要持有 0504 名片才能联系装机佬。",
                "You need the 0504 business card to call the PC Builder."), "#FFFFFF");
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
