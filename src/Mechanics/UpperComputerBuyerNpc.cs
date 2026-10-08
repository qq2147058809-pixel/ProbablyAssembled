using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>上城区电脑收购商：通过原版展示窗访客流程触发，收购完好电脑商品并出售高阶破损机箱。</summary>
internal static class UpperComputerBuyerNpc
{
    internal const string Id = "pcrepair.upper_computer_buyer";
    internal const string SpriteKey = "pcrepair.upper_computer_buyer_sprite";
    internal const string ClientTag = "PCREPAIR_UPPER_COMPUTER_BUYER";
    internal const string PurchaseTag = "PCREPAIR_UPPER_BUYER_PURCHASABLE";
    internal const string StockTag = "PCREPAIR_UPPER_BUYER_STOCK";
    private const string ShowcaseType = "PCREPAIR_COMPUTER_BUYER";
    private static readonly Random random = new();

    [HarmonyPatch(typeof(TypeHelper), nameof(TypeHelper.GetTypeDisplayName))]
    internal static class ShowcaseNamePatch
    {
        private static bool Prefix(string __0, ref string __result)
        {
            if (Core.Clean(__0) != ShowcaseType) return true;
            __result = LanguageText.Get("showcase.pc_buyer.reason");
            return false;
        }
    }

    [HarmonyPatch(typeof(TypeHelper), nameof(TypeHelper.GetTypeColor))]
    internal static class ShowcaseColorPatch
    {
        private static bool Prefix(string __0, ref UnityEngine.Color __result)
        {
            if (Core.Clean(__0) != ShowcaseType) return true;
            __result = new UnityEngine.Color(0.34f, 0.65f, 0.34f, 1f);
            return false;
        }
    }

    internal static bool IsBuyer(StoreClient? client) =>
        client != null && (client.IsTag(ClientTag) || Core.Clean(client.identifier) == Id);

    private static Il2CppSystem.Func<StoreClient> Factory() =>
        (Func<StoreClient>)CreateSafely;

    private static StoreClient CreateSafely()
    {
        try { return Create(); }
        catch (Exception ex)
        {
            Core.Log?.Error("创建上城区电脑收购商失败，回退原版上城区访客：" + ex);
            return StoreClientList.CreateUpperLowerVisitorLuxury();
        }
    }

    private static StoreClient Create()
    {
        var client = StoreClientList.CreateUpperLowerVisitorLuxury();
        if (client == null) throw new InvalidOperationException("原版上城区访客模板创建失败。");

        // 与装机佬使用同一固定原版尺寸和锚点，避免随机模板改变显示参照。
        var portraitReference = SpriteDict.Instance?.GetSprite("wanted3");
        if (portraitReference != null)
            SpriteAssets.SetPortraitReference(SpriteKey, portraitReference);

        client.identifier = Id;
        client.displayName = LanguageText.Get("text.e7abf8ee21e9");
        client.realName = client.displayName;
        client.spriteName = SpriteKey;
        client.clientFaction = StoreClient.FACTION_UPPER;
        client.clientIntent = StoreClient.ClientIntent.SELLNBUY;
        client.isMainDialogueStarted = false;
        client.AddTag(ClientTag);

        ConfigurePurchaseRules(client);
        ApplyBuyerBudget(client);
        ConfigureDialogue(client);
        return client;
    }

    private static void ConfigurePurchaseRules(StoreClient client)
    {
        // 只保留本 NPC 的隐藏收购标记，避免模板原有的奢侈品偏好混入电脑收购条件。
        client.clientBuyingTagList = new Il2CppSystem.Collections.Generic.List<string>();
        client.clientBuyingTagList.Add(PurchaseTag);
        client.clientBuyingIdList = new Il2CppSystem.Collections.Generic.List<string>();
        client.clientBlackTagList = new Il2CppSystem.Collections.Generic.List<string>();
        client.clientBlackIdList = new Il2CppSystem.Collections.Generic.List<string>();
        client.willAcceptItemPreference = (Func<GameItem, bool>)IsAllowedPurchase;
        client.noAcceptingContraband = false;
        client.AcceptingContrabandOverride = true;
        client.isRequireVariety = false;
        client.isMultiBuyDisabled = false;
        // 原版直接把该值传给 Discount 的价值修正；负数才会让玩家买价下降。
        client.wholesaleDiscount = -10;
    }

    private static void ApplyBuyerBudget(StoreClient client)
    {
        // 预算覆盖一台最高 T5 完整机的估值及小费，并留出额外配件收购空间。
        long maxMachineValue = WorkroomMachineAssembly.MaximumMachineValue(5);
        var budget = checked((int)Math.Ceiling(maxMachineValue * 1.25 * 1.15 * 2.0));
        client.SetBudget(budget);
        client.OverrideBudget(budget);
        client.clientCash = budget;
        client.clientBudget = budget;
        client.useClientBudget = true;
        client.budgetConversionRate = 1;
    }

    private static void ConfigureDialogue(StoreClient client)
    {
        var greeting = new Dialogue()
            .SetText(client.displayName, LanguageText.Get("text.da4324746524") );
        var offer = new Dialogue()
            .SetText(client.displayName, LanguageText.Get("text.8d59d9e16dff") );
        greeting.isMainDialog = true;
        greeting.SetNextDialogue(offer);
        client.mainDialogue = greeting;
        client.isMainDialogueStarted = false;
        client.placedWrongItemWhenSellingToDialogue = new Dialogue().SetText(client.displayName,
            LanguageText.Get("text.c9eaa60d077e") );
        client.placeRightItemWhenSellingToDialogue = new Dialogue().SetText(client.displayName,
            LanguageText.Get("text.bb9a3627859b") );
    }

    private static Dialogue RefusalDialogue(StoreClient client, GameItem? item)
    {
        try
        {
            var spec = item == null ? null : Components.Find(Core.Clean(item.identifier));
            if (spec != null && !spec.Owner.IsCase && WorkroomComponentParts.ConfigurationTier(item!) <= 2)
                return new Dialogue().SetText(client.displayName,
                    LanguageText.Get("text.2c6bd05e0f43") );
        }
        catch (Exception ex) { Core.Log?.Warning("读取电脑拒收原因失败，使用通用对白：" + ex.Message); }

        return new Dialogue().SetText(client.displayName,
            LanguageText.Get("text.c9eaa60d077e") );
    }

    private static bool IsAllowedPurchase(GameItem? item)
    {
        if (item == null) return false;
        try
        {
            if (Components.IsComponent(item))
            {
                var component = Components.Find(Core.Clean(item.identifier));
                if (component?.Owner.Tag == Components.MotherboardTag)
                    return WorkroomMachineAssembly.AllowsBoardPurchase(WorkroomMachineAssembly.Read(item), component.Tier,
                        component.Broken || item.IsTag(Components.BrokenTag), 3, 5);
                return component != null && !component.Owner.IsCase && !component.Broken &&
                       WorkroomComponentParts.ConfigurationTier(item) >= 3 && !item.IsTag(Components.BrokenTag);
            }

            if (!ComputerCase.IsCase(item) || item.IsTag(Components.BrokenTag)) return false;
            CaseEconomy.EvaluateCase(item);
            var label = CaseEconomy.MachineLabel(item);
            if (label != "整机" && label != "刀把机" && label != "性价比机器") return false;
            return !HasDisallowedContainedPart(item);
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("上城区电脑收购资格判断失败：" + ex.Message);
            return false;
        }
    }

    internal static bool RefreshPurchaseEligibility(GameItem? item)
    {
        if (item == null || (!Components.IsComponent(item) && !ComputerCase.IsCase(item))) return false;
        try
        {
            var allowed = IsAllowedPurchase(item);
            if (allowed && !item.IsTag(PurchaseTag)) item.EnableTag(PurchaseTag, false);
            else if (!allowed && item.IsTag(PurchaseTag)) item.DisableTag(PurchaseTag, false);
            return allowed;
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("同步上城区收购资格标签失败：" + ex.Message);
            return false;
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
                Core.Log?.Warning("加载上城区电脑收购商立绘失败：" + ex.Message);
                return true;
            }
        }
    }

    private static bool HasDisallowedContainedPart(GameItem caseItem)
    {
        foreach (var part in CaseEconomy.Parts(caseItem)) if (part.Broken || part.Tier < 3) return true;
        return false;
    }

    private static void EnsureRegistered()
    {
        var registry = StoreClientListDict.storeClientDict;
        if (registry == null)
        {
            registry = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Func<StoreClient>>();
            StoreClientListDict.storeClientDict = registry;
        }
        registry[Id] = Factory();
    }

    internal static bool IsEligibleShowcaseItem(GameItem? item)
    {
        if (item == null) return false;
        try
        {
            var spec = Components.Find(Core.Clean(item.identifier));
            if (spec?.Owner.Tag == Components.MotherboardTag &&
                WorkroomMachineAssembly.Read(item) is { } machine && machine.Kind == WorkroomMachineAssembly.Motherboard)
            {
                var assessment = WorkroomMachineAssembly.Assess(machine);
                return !item.IsTag(Components.BrokenTag) && spec?.Broken == false && assessment.UpperShowcaseEligible;
            }
            if (spec != null && !spec.Owner.IsCase)
                return WorkroomComponentParts.ConfigurationTier(item) >= 4 && IsAllowedPurchase(item);
            // 招客与实际收购使用同一资格，不能用含低阶/破损件的机箱吸引收购商。
            return ComputerCase.IsCase(item) && IsAllowedPurchase(item) &&
                   CaseEconomy.HighestStoredPartTier(item) >= 4;
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("电脑橱窗资格判断失败：" + ex.Message);
            return false;
        }
    }

    private static bool HasEligibleT4OrHigherPartInShowcase()
    {
        try
        {
            var items = EmporiumEntry.Instance?.showcaseElement?.childItems;
            if (items == null) return false;
            foreach (var item in items)
            {
                if (IsEligibleShowcaseItem(item)) return true;
            }
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("检查展示窗高阶电脑配件失败：" + ex.Message);
        }
        return false;
    }

    private static void AddStock()
    {
        try
        {
            var store = PlayerStore.Instance;
            var client = store?.currentClientInstance?.storeClient;
            if (store == null || !IsBuyer(client)) return;

            ConfigurePurchaseRules(client!);
            ApplyBuyerBudget(client!);
            NpcStockOffers.Stock(store, client!, StockTag, "UPPER", () => new List<string>
            {
                "pcrepair.computer_case_t" + random.Next(4, 6) + "_broken"
            }, 1, 1, id => Components.Find(id) is { } spec && spec.Owner.IsCase && spec.Broken && spec.Tier >= 4);
        }
        catch (Exception ex)
        {
            Core.Log?.Error("上城区电脑收购商上柜失败：" + ex);
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.LoadGame))]
    internal static class StockLoadPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PlayerStore __instance, bool __runOriginal)
        {
            if (__runOriginal && __instance.isClientArrived && IsBuyer(__instance.currentClientInstance?.storeClient)) AddStock();
        }
    }

    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetDisplayCaseType))]
    internal static class ShowcaseFactoryRegistrationPatch
    {
        private static void Prefix()
        {
            try { EnsureRegistered(); }
            catch (Exception ex) { Core.Log?.Error("注册上城区电脑收购商失败：" + ex); }
        }

        private static void Postfix(ref Il2CppSystem.Collections.Generic.List<string> __result)
        {
            try
            {
                if (__result == null || !HasEligibleT4OrHigherPartInShowcase()) return;
                for (var i = 0; i < __result.Count; i++)
                    if (Core.Clean(__result[i]) == ShowcaseType) return;
                __result.Add(ShowcaseType);
                Core.Debug("展示窗含完好 T4/T5 配件或含高阶配件的合格整机，加入电脑收购商候选。");
            }
            catch (Exception ex) { Core.Log?.Warning("加入上城区电脑收购商展示窗候选失败：" + ex); }
        }
    }

    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.CreateClientByStockType))]
    internal static class ShowcaseClientFactoryPatch
    {
        private static bool Prefix(string type, ref string __result)
        {
            if (Core.Clean(type) != ShowcaseType) return true;
            __result = Id;
            return false;
        }
    }

    [HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.CreateStoreClient))]
    internal static class ClientRestorePatch
    {
        private static bool Prefix(string identifier, ref StoreClient __result)
        {
            if (Core.Clean(identifier) != Id) return true;
            try
            {
                __result = Create();
                return false;
            }
            catch (Exception ex)
            {
                Core.Log?.Error("恢复上城区电脑收购商失败：" + ex);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.OnNextClientArrived))]
    internal static class ArrivalStockPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => AddStock();
    }

    [HarmonyPatch(typeof(StoreClient), nameof(StoreClient.IsClientBuyingThisItem))]
    internal static class PurchaseFilterPatch
    {
        private static void Postfix(StoreClient __instance, GameItem gameItem, ref bool __result)
        {
            if (IsBuyer(__instance)) __result = IsAllowedPurchase(gameItem);
        }
    }

    [HarmonyPatch(typeof(StoreClient), nameof(StoreClient.IsClientRefusingItem))]
    internal static class RefusalFilterPatch
    {
        private static void Postfix(StoreClient __instance, GameItem gameItem, ref bool __result)
        {
            if (!IsBuyer(__instance)) return;
            __result = !IsAllowedPurchase(gameItem);
            if (__result) __instance.placedWrongItemWhenSellingToDialogue = RefusalDialogue(__instance, gameItem);
        }
    }

    [HarmonyPatch(typeof(StoreClient), nameof(StoreClient.HasBudgetLeftToBuy))]
    internal static class BudgetFilterPatch
    {
        private static void Postfix(StoreClient __instance, GameItem gameItem, ref bool __result)
        {
            if (IsBuyer(__instance) && !IsAllowedPurchase(gameItem)) __result = false;
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.PlacedItemForSelling))]
    internal static class SaleEligibilityPatch
    {
        private static void Prefix(PlayerStore __instance, GameItem targetItem)
        {
            if (!IsBuyer(__instance.currentClientInstance?.storeClient) || targetItem == null) return;
            if (ComputerCase.IsCase(targetItem)) CaseEconomy.EvaluateCase(targetItem);
            RefreshPurchaseEligibility(targetItem);
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.CanSellThisItem))]
    internal static class CanSellFilterPatch
    {
        private static void Prefix(PlayerStore __instance, GameItem gameItem)
        {
            if (!IsBuyer(__instance.currentClientInstance?.storeClient) || gameItem == null) return;
            if (ComputerCase.IsCase(gameItem)) CaseEconomy.EvaluateCase(gameItem);
            RefreshPurchaseEligibility(gameItem);
        }

        private static void Postfix(PlayerStore __instance, GameItem gameItem, ref bool __result)
        {
            if (IsBuyer(__instance.currentClientInstance?.storeClient))
                __result = IsAllowedPurchase(gameItem) && __result;
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetNegociatedValue))]
    internal static class TransactionPricePatch
    {
        private static void Prefix(GameItem __instance)
        {
            try
            {
                var client = PlayerStore.Instance?.currentClientInstance?.storeClient;
                if (!IsBuyer(client)) return;

                if (GeneralHelper.IsItemOwned(__instance))
                {
                    // 原版 UL_TIPS 已解锁时同样使用 tips ID；只添加一次，避免双重小费。
                    // 在估价前添加，柜台报价、预算检查和最终成交价保持一致。
                    if (IsAllowedPurchase(__instance) && !__instance.IsAlreadyContainFeatureWithId("tips"))
                        __instance.AddItemFeature(ItemFeatureList.Pourboire());
                }
                else if (__instance.IsTag(StockTag) && !__instance.IsAlreadyContainFeatureWithId("discount"))
                {
                    __instance.AddItemFeature(ItemFeatureList.Discount(-10));
                }
                else if (__instance.IsTag(StockTag))
                {
                    // Existing stock can retain the old 30% feature across saves.
                    __instance.FindItemFeatureByID("discount")?.SetValueModifier(-10);
                }
            }
            catch (Exception ex) { Core.Log?.Warning("同步上城区交易词条失败：" + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.BuyItem))]
    internal static class RemoveStockMarkerPatch
    {
        private static void Postfix(PlayerStore __instance, GameItem item)
        {
            if (item != null && IsBuyer(__instance.currentClientInstance?.storeClient) && item.IsTag(StockTag))
                item.DisableTag(StockTag, false);
        }
    }
}
