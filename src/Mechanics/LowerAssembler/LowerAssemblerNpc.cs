using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>下层装机佬：按下城区化学家客户权重出现，同时出售商品并收购符合条件的整机和完好配件。</summary>
internal static class LowerAssemblerNpc
{
    internal const string Id = "pcrepair.lower_assembler";
    internal const string SpriteKey = "pcrepair.lower_assembler_sprite";
    internal const string ClientTag = "PCREPAIR_LOWER_ASSEMBLER";
    internal const string PurchaseTag = "PCREPAIR_ASSEMBLER_PURCHASABLE";
    private const string DayFiveAssemblerQueuedKey = "pcrepair.lower_assembler.day_five_queued";
    private const string StockTag = "PCREPAIR_LOWER_ASSEMBLER_STOCK";
    private static int AssemblerBudget => CalculateAssemblerBudget();

    private static bool spawnWeightRegistered;
    private static readonly Random random = new();

    internal static StoreClient CreateLowerLevelAssembler()
    {
        var client = StoreClientList.CreateUpperLowerVisitor();
        if (client == null) throw new InvalidOperationException("原版下城区访客模板创建失败。");

        // 采用固定的原版下城区立绘参照，高清源图也保持原版显示尺寸和锚点。
        var portraitReference = SpriteDict.Instance?.GetSprite("wanted3");
        if (portraitReference != null)
            SpriteAssets.SetPortraitReference(SpriteKey, portraitReference);

        client.identifier = Id;
        client.displayName = LanguageText.Get("下层装机佬", "Lower-District PC Builder");
        client.realName = client.displayName;
        client.spriteName = SpriteKey;
        client.clientFaction = StoreClient.FACTION_LOWER;
        client.clientIntent = StoreClient.ClientIntent.SELLNBUY;
        ConfigurePurchaseRules(client);
        client.isMainDialogueStarted = false;
        client.isNoContributeToEvidence = true;
        ApplyAssemblerBudget(client);
        client.AddTag(ClientTag);
        ConfigureAppearanceDialogue(client);
        return client;
    }

    internal static void ApplyAssemblerBudget(StoreClient client, int budgetOverride = 0)
    {
        var budget = budgetOverride > 0 ? budgetOverride : AssemblerBudget;
        client.SetBudget(budget);
        client.OverrideBudget(budget);
        client.clientCash = budget;
        client.clientBudget = budget;
        client.useClientBudget = true;
        client.budgetConversionRate = 1;
    }

    private static int CalculateAssemblerBudget()
    {
        var caseType = Array.Find(Components.All, type => type.IsCase);
        long fullT3Value = caseType == null ? 0 : Components.ValueFor(caseType, 3, false);

        // 按实际插槽数量计算满配 T3 机，而不是只按每类配件各一件估算。
        foreach (var slot in CaseInteriorUI.SlotTable)
        {
            var type = Array.Find(Components.All, candidate => !candidate.IsCase && candidate.Tag == slot.Tag);
            if (type != null) fullT3Value += Components.ValueFor(type, 3, false);
        }

        // 满 T3 机器按原版估价规则获得“整机”20%加价；预算留出另一台同价机器的额度。
        var completeMachinePrice = (long)Math.Round(fullT3Value * 1.20);
        return checked((int)(completeMachinePrice * 2));
    }

    internal static void ConfigurePurchaseRules(StoreClient client)
    {
        // 原版先查 tag / ID 清单，命中就直接接受，最后才查 willAcceptItemPreference。
        // 必须替换模板的 SUBSTANCE 清单，否则烟酒仍可绕过电脑收购委托。
        client.clientBuyingTagList = new Il2CppSystem.Collections.Generic.List<string>();
        // 原版客户直接用 GameItem.IsTag 匹配此清单，资格必须同步到物品标签上。
        client.clientBuyingTagList.Add(PurchaseTag);
        client.clientBuyingIdList = new Il2CppSystem.Collections.Generic.List<string>();
        client.clientBlackTagList = new Il2CppSystem.Collections.Generic.List<string>();
        client.clientBlackIdList = new Il2CppSystem.Collections.Generic.List<string>();
        client.willAcceptItemPreference = (Func<GameItem, bool>)IsAllowedAssemblerPurchase;
        client.noAcceptingContraband = false;
        client.AcceptingContrabandOverride = true;
        client.isRequireVariety = false;
        client.isMultiBuyDisabled = false;
    }

    private static void ConfigureAppearanceDialogue(StoreClient client)
    {
        var greeting = new Dialogue()
            .SetText(client.displayName, LanguageText.Get("柜上这些都是我淘来的货，看看有没有喜欢的？",
                "Found these on my rounds. See anything you like?") );
        var buyLine = new Dialogue()
            .SetText(client.displayName, LanguageText.Get("你有好的配件，装好的性价比机器，我也全都要了。",
                "Got decent parts or a tidy value build? I'll take a look.") );

        greeting.isMainDialog = true;
        greeting.SetNextDialogue(buyLine);
        client.mainDialogue = greeting;
        client.isMainDialogueStarted = false;
        ConfigureTradeDialogue(client);
    }

    internal static void ConfigureTradeDialogue(StoreClient client)
    {
        // AddBasicDialog 已按旧模板生成交易对白，修改委托后还需要替换这些缓存对白。
        client.placedWrongItemWhenSellingToDialogue = new Dialogue().SetText(client.displayName,
            LanguageText.Get("我收完好的电脑配件，还有 T3 及以下的整机、性价比机器。刀把机和没装齐的机箱不收。",
                "I buy working PC parts and complete builds up to T3. No bottleneck builds or half-empty cases.") );
        client.placeRightItemWhenSellingToDialogue = new Dialogue().SetText(client.displayName,
            LanguageText.Get("这东西我收，咱们谈个价吧。", "Looks good. Let's talk price.") );
    }

    private static Il2CppSystem.Func<StoreClient> Factory() =>
        (Func<StoreClient>)CreateLowerLevelAssemblerSafely;

    private static StoreClient CreateLowerLevelAssemblerSafely()
    {
        try { return CreateLowerLevelAssembler(); }
        catch (Exception ex)
        {
            Core.Log?.Error("[何小鲁模板] 创建下层装机佬失败，回退普通下城区访客：" + ex);
            return StoreClientList.CreateUpperLowerVisitor();
        }
    }

    internal static bool IsLowerAssembler(StoreClient? client) =>
        client != null && (client.IsTag(ClientTag) || Core.Clean(client.identifier) == Id);

    internal static bool IsAssembler(StoreClient? client) =>
        IsLowerAssembler(client) || PhoneAssemblerNpc.IsPhoneAssembler(client);

    internal static bool IsAllowedAssemblerPurchase(GameItem? item)
    {
        try { return PurchaseRefusalReason(item) == null; }
        catch (Exception ex)
        {
            Core.Log?.Warning("装机佬收购资格判断失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>把电脑规则计算结果写成原版客户可直接识别的内部标签，不添加可见词条。</summary>
    internal static bool RefreshPurchaseEligibility(GameItem? item)
    {
        if (item == null) return false;
        try
        {
            if (!Components.IsComponent(item) && !ComputerCase.IsCase(item)) return false;
            var allowed = IsAllowedAssemblerPurchase(item);
            if (allowed && !item.IsTag(PurchaseTag)) item.EnableTag(PurchaseTag, false);
            else if (!allowed && item.IsTag(PurchaseTag)) item.DisableTag(PurchaseTag, false);
            return allowed;
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("同步装机佬收购标签失败：" + ex.Message);
            return false;
        }
    }

    private static string? PurchaseRefusalReason(GameItem? item)
    {
        if (item == null) return "没有物品";

        // 单独散件只收完好电脑配件；原版其他客户行为保持不变。
        if (Components.IsComponent(item))
            return item.IsTag(Components.BrokenTag) ? "散件只收完好电脑配件" : null;

        if (!ComputerCase.IsCase(item)) return "不是电脑商品";
        if (item.IsTag(Components.BrokenTag)) return "不收购破损机箱货箱";
        if (Components.TierOf(item) is < 1 or > 3) return "机箱超过收购 T 度范围";

        var label = CaseEconomy.MachineLabel(item);
        if (label == "刀把机") return "不收购刀把机";
        if (label != "整机" && label != "性价比机器") return "缺少配件或 T 度搭配不合规";

        // 整机必须装齐，所有已装配部件均为完好 T1–T3；明确排除刀把机。
        for (var slotIndex = 0; slotIndex < CaseInteriorUI.SlotTable.Length; slotIndex++)
        {
            var slot = CaseInteriorUI.SlotTable[slotIndex];
            if (FindPart(item, slotIndex, slot.Tag, out var tier, out var broken) &&
                (tier > 3 || broken)) return broken ? "整机含有损坏配件" : "整机含有 T4/T5 配件";
        }
        return null;
    }

    private static bool FindPart(GameItem caseItem, int slotIndex, string typeTag,
        out int tier, out bool broken)
    {
        tier = 0;
        broken = false;
        Components.Type? type = null;
        foreach (var candidate in Components.All)
            if (!candidate.IsCase && candidate.Tag == typeTag) { type = candidate; break; }
        if (type == null) return false;

        for (var currentTier = 1; currentTier <= Components.TierCount; currentTier++)
        {
            var id = "pcrepair." + type.Stem + "_t" + currentTier;
            if (caseItem.IsTag(CaseInteriorUI.SlotTagPrefix + slotIndex + "_" + id))
            {
                tier = currentTier;
                return true;
            }
            if (caseItem.IsTag(CaseInteriorUI.SlotTagPrefix + slotIndex + "_" + id + "_broken"))
            {
                tier = currentTier;
                broken = true;
                return true;
            }
        }
        return false;
    }

    private static void AddStock()
    {
        try
        {
            var store = PlayerStore.Instance;
            var client = store?.currentClientInstance?.storeClient;
            if (store == null || !IsLowerAssembler(client)) return;
            // 当前访客读档后也重新配置，避免存档实例保留模板清单或旧交易对白。
            ConfigurePurchaseRules(client!);
            ConfigureTradeDialogue(client!);
            ApplyAssemblerBudget(client!);
            if (HasExistingStock())
            {
                Core.Debug("下层装机佬当前柜台已有库存，跳过重复上柜。");
                return;
            }

            var brokenCaseTier = random.Next(1, 4);
            AddOffer("pcrepair.computer_case_t" + brokenCaseTier + "_broken");

            var emptyCaseTier = random.Next(1, 4);
            AddOffer("pcrepair.computer_case_t" + emptyCaseTier);

            var available = new List<Components.Type>();
            foreach (var type in Components.All)
                if (!type.IsCase && ComponentRepair.IsRepairable(type, 1)) available.Add(type);
            for (var i = available.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (available[i], available[j]) = (available[j], available[i]);
            }
            var count = random.Next(3, 5);
            for (var i = 0; i < count; i++)
            {
                var type = available[i];
                var tier = random.Next(1, 4);
                AddOffer("pcrepair." + type.Stem + "_t" + tier + "_broken");
            }

            if (!store.IsPlayerOwnThisItem(ContactCard.Id))
                AddOffer(ContactCard.Id);

            if (!store.IsPlayerOwnThisItem(UnboxTool.Id))
                AddOffer(UnboxTool.Id);

            Core.Log?.Msg("[深空装机] 下层装机佬库存已上柜：破损机箱、空机箱、" + count +
                          " 件损坏 T1–T3 配件、未持有的 0504 名片" +
                          (store.IsPlayerOwnThisItem(UnboxTool.Id) ? "。" : "和拆机螺丝刀。"));
        }
        catch (Exception ex)
        {
            Core.Log?.Error("[何小鲁模板] 下层装机佬上柜失败：" + ex);
        }
    }

    private static void AddOffer(string id)
    {
        var item = DirectoryMaster.Item(id, true);
        if (item == null)
        {
            Core.Log?.Warning("下层装机佬库存物品尚未注册：" + id);
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
        catch (Exception ex)
        {
            Core.Log?.Warning("[何小鲁模板] 检查装机佬现有库存失败：" + ex.Message);
        }
        return false;
    }

    private static void RegisterSpawnWeight()
    {
        if (spawnWeightRegistered) return;
        try
        {
            var factory = Factory();
            var registry = StoreClientListDict.storeClientDict;
            if (registry == null)
            {
                registry = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Func<StoreClient>>();
                StoreClientListDict.storeClientDict = registry;
            }
            registry[Id] = factory;

            var injected = 0;
            injected += InjectWeightedGroup("常规访客", StoreClientList.clientList,
                StoreClientList.clientListProba, StoreClientList.clientListProbaII,
                StoreClientList.clientListProbaIII);
            injected += InjectWeightedGroup("上城区访客", StoreClientList.clientListUpper,
                StoreClientList.clientListProbaUpper);
            injected += InjectWeightedGroup("黑市访客", StoreClientList.clientListBM,
                StoreClientList.clientListProbaBM, StoreClientList.clientListProbaBMII,
                StoreClientList.clientListProbaBMIII);
            injected += InjectWeightedGroup("买家", StoreClientList.buyClientList,
                StoreClientList.buyClientListProba);
            injected += InjectWeightedGroup("卖家", StoreClientList.sellClientList,
                StoreClientList.sellClientListProba);

            if (injected > 0)
            {
                spawnWeightRegistered = true;
                Core.Log?.Msg("[深空装机] 下层装机佬已加入客户权重表，权重按下城区化学家配置复制；注册组数=" + injected + "。");
            }
            else
            {
                Core.Log?.Warning("下层装机佬暂未找到下城区化学家权重项，将在下一次生成客户时重试。");
            }
        }
        catch (Exception ex)
        {
            Core.Log?.Error("[何小鲁模板] 注册下层装机佬出现概率失败：" + ex);
        }
    }

    private static int InjectWeightedGroup(string name,
        Il2CppSystem.Collections.Generic.List<Il2CppSystem.Func<StoreClient>>? clients,
        params Il2CppSystem.Collections.Generic.List<int>?[] weightsByProgression)
    {
        if (clients == null || clients.Count == 0) return 0;
        var chemistIndexes = new List<int>();
        var alreadyAdded = false;
        for (var i = 0; i < clients.Count; i++)
        {
            var methodName = Core.Clean(clients[i]?.Method?.Name);
            if (methodName.Contains("CreateLowerLevelChemist", StringComparison.Ordinal))
                chemistIndexes.Add(i);
            if (methodName.Contains("CreateLowerLevelAssembler", StringComparison.Ordinal))
                alreadyAdded = true;
        }
        if (chemistIndexes.Count == 0 || alreadyAdded) return 0;

        foreach (var weights in weightsByProgression)
        {
            if (weights == null || weights.Count != clients.Count)
            {
                Core.Log?.Warning("下层装机佬跳过权重组「" + name + "」：客户列表与权重列表长度不一致。");
                return 0;
            }
        }

        clients.Add(Factory());
        foreach (var weights in weightsByProgression)
        {
            long total = 0;
            foreach (var index in chemistIndexes) total += weights![index];
            weights!.Add((int)Math.Clamp(total, 1L, int.MaxValue));
        }
        Core.Debug("下层装机佬加入权重组「" + name + "」，匹配化学家候选数=" + chemistIndexes.Count + "。");
        return 1;
    }

    [HarmonyPatch(typeof(StoreClientManager), nameof(StoreClientManager.GenerateClient))]
    internal static class SpawnWeightPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix() => RegisterSpawnWeight();
    }

    [HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.CreateStoreClient))]
    internal static class ClientRestorePatch
    {
        private static bool Prefix(string identifier, ref StoreClient __result)
        {
            if (Core.Clean(identifier) != Id) return true;
            try
            {
                __result = CreateLowerLevelAssembler();
                return false;
            }
            catch (Exception ex)
            {
                Core.Log?.Error("[何小鲁模板] 恢复下层装机佬失败：" + ex);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(StoreClientManager), nameof(StoreClientManager.HandleClientFromIntro))]
    internal static class DayFiveAssemblerSchedulePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(StoreClientManager __instance)
        {
            try
            {
                var store = PlayerStore.Instance;
                if (store == null || StoreStation.GetDayCounter() != 5 ||
                    WasDayFiveAssemblerQueued(store)) return;

                var stack = __instance.clientStack;
                StoreClient? queuedAssembler = null;
                if (stack != null)
                {
                    for (var i = stack.Count - 1; i >= 0; i--)
                    {
                        var candidate = stack[i];
                        if (!IsLowerAssembler(candidate)) continue;
                        queuedAssembler ??= candidate;
                        stack.RemoveAt(i);
                    }
                    if (queuedAssembler != null) stack.Insert(0, queuedAssembler);
                }

                if (queuedAssembler == null)
                    __instance.AddNextClient(CreateLowerLevelAssembler());

                MarkDayFiveAssemblerQueued(store);
                Core.Log?.Msg("[深空装机] 第五天已按原版客户队列流程安排装机佬。");
            }
            catch (Exception ex)
            {
                Core.Log?.Error("[何小鲁模板] 仿原版故事客户流程安排第五天装机佬失败：" + ex);
            }
        }
    }

    private static bool WasDayFiveAssemblerQueued(PlayerStore store)
    {
        try
        {
            var data = store.modData;
            return data != null && data.ContainsKey(DayFiveAssemblerQueuedKey) &&
                   data[DayFiveAssemblerQueuedKey] == "1";
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("[何小鲁模板] 读取第五天队列标记失败：" + ex.Message);
            return false;
        }
    }

    private static void MarkDayFiveAssemblerQueued(PlayerStore store)
    {
        try
        {
            if (store.modData == null)
                store.modData = new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
            store.modData[DayFiveAssemblerQueuedKey] = "1";
        }
        catch (Exception ex)
        {
            Core.Log?.Error("[何小鲁模板] 保存第五天队列标记失败：" + ex);
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
            try
            {
                if (IsAssembler(__instance)) __result = IsAllowedAssemblerPurchase(gameItem);
            }
            catch (Exception ex) { Core.Log?.Warning("电脑收购资格判断失败：" + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.PlacedItemForSelling))]
    internal static class PurchaseDiagnosticsPatch
    {
        private static void Prefix(PlayerStore __instance, GameItem targetItem)
        {
            try
            {
                var client = __instance.currentClientInstance?.storeClient;
                if (!IsAssembler(client) || targetItem == null) return;
                if (ComputerCase.IsCase(targetItem)) CaseInteriorUI.SyncCaseForTrading(targetItem);
                RefreshPurchaseEligibility(targetItem);
                Core.Log?.Msg("[深空装机] 装机佬收购检查：" + Core.Clean(targetItem.identifier) +
                    "，资格=" + (PurchaseRefusalReason(targetItem) ?? "符合") +
                    "，原版收购标签=" + targetItem.IsTag(PurchaseTag) +
                    "，剩余预算=" + client!.clientBudget +
                    "，机型=" + (ComputerCase.IsCase(targetItem) ? CaseEconomy.MachineLabel(targetItem) ?? "无" : "散件"));
            }
            catch (Exception ex) { Core.Log?.Warning("装机佬收购诊断失败：" + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.CanSellThisItem))]
    internal static class NativeSaleEligibilityPatch
    {
        private static IntPtr lastItem;
        private static bool lastResult;

        private static void Prefix(PlayerStore __instance, GameItem gameItem)
        {
            try
            {
                if (!IsAssembler(__instance.currentClientInstance?.storeClient) || gameItem == null) return;
                // CanSellThisItem is checked before PlacedItemForSelling. If the case
                // interior is still open, first commit its live slots so the shared
                // lower/phone assembler predicate sees the current build, not stale tags.
                if (ComputerCase.IsCase(gameItem)) CaseInteriorUI.SyncCaseForTrading(gameItem);
                RefreshPurchaseEligibility(gameItem);
            }
            catch (Exception ex) { Core.Log?.Warning("交易前同步电脑收购标签失败：" + ex.Message); }
        }

        private static void Postfix(PlayerStore __instance, GameItem gameItem, ref bool __result)
        {
            try
            {
                var client = __instance.currentClientInstance?.storeClient;
                if (!IsAssembler(client) || gameItem == null) return;
                __result = IsAllowedAssemblerPurchase(gameItem) && __result;
                if (lastItem == gameItem.Pointer && lastResult == __result) return;
                lastItem = gameItem.Pointer;
                lastResult = __result;
                Core.Log?.Msg("[深空装机] 装机佬原版成交资格：" + Core.Clean(gameItem.identifier) +
                    "，允许成交=" + __result + "，收购标签=" + gameItem.IsTag(PurchaseTag) +
                    "，预算足够=" + client!.HasBudgetLeftToBuy(gameItem) +
                    "，待鉴定=" + client.CanClientExposeAnyFeature(gameItem));
            }
            catch (Exception ex) { Core.Log?.Warning("装机佬成交资格诊断失败：" + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(StoreClientMono), nameof(StoreClientMono.PlayIdleForCurrentClient))]
    internal static class AssemblerIdleFreezePatch
    {
        private static bool Prefix(StoreClientMono __instance)
        {
            try
            {
                var client = PlayerStore.Instance?.currentClientInstance?.storeClient;
                if (!IsAssembler(client) && !UpperComputerBuyerNpc.IsBuyer(client)) return true;
                __instance.idleDecisionClient = client;
                __instance.idleSuppressed = true;
                __instance.idleShakeHorizontal = false;
                __instance.idleAnimator?.Freeze();
                Core.Debug("电脑商人待机动画已停止（原版 Freeze 节点）：" + Core.Clean(client?.identifier));
                return false;
            }
            catch (Exception ex)
            {
                Core.Log?.Warning("停止电脑商人待机晃动失败：" + ex.Message);
                return true;
            }
        }
    }

    // 原版全局重新启用待机动画时会直接调用 IdleAnimator.Play，也需按人物和对象身份过滤。
    [HarmonyPatch(typeof(IdleAnimator), nameof(IdleAnimator.Play))]
    internal static class AssemblerIdleRestartGuardPatch
    {
        private static bool Prefix(IdleAnimator __instance)
        {
            try
            {
                var animator = StoreClientMono.Instance?.idleAnimator;
                if (animator == null || animator.Pointer != __instance.Pointer) return true;
                var client = PlayerStore.Instance?.currentClientInstance?.storeClient;
                if (!IsAssembler(client) && !UpperComputerBuyerNpc.IsBuyer(client)) return true;
                __instance.Freeze();
                return false;
            }
            catch (Exception ex)
            {
                Core.Log?.Warning("拦截电脑商人待机重启失败：" + ex.Message);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(StoreClient), nameof(StoreClient.IsClientRefusingItem))]
    internal static class RefusalFilterPatch
    {
        private static void Postfix(StoreClient __instance, GameItem gameItem, ref bool __result)
        {
            try
            {
                if (IsAssembler(__instance)) __result = !IsAllowedAssemblerPurchase(gameItem);
            }
            catch (Exception ex) { Core.Log?.Warning("电脑拒收规则判断失败：" + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(StoreReputation), nameof(StoreReputation.OnItemTraded))]
    internal static class TradeReputationPatch
    {
        // 保留原版交易声望结算，只把装机佬这笔交易归入原版下城区声望。
        private static void Prefix(ref string factionId, StoreClient storeClient)
        {
            try
            {
                if (IsAssembler(storeClient)) factionId = StoreClient.FACTION_LOWER;
            }
            catch (Exception ex)
            {
                Core.Log?.Warning("装机佬交易声望路由失败：" + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(StoreClient), nameof(StoreClient.HasBudgetLeftToBuy))]
    internal static class BudgetFilterPatch
    {
        private static void Postfix(StoreClient __instance, GameItem gameItem, ref bool __result)
        {
            try
            {
                if (IsAssembler(__instance) && !IsAllowedAssemblerPurchase(gameItem)) __result = false;
            }
            catch (Exception ex) { Core.Log?.Warning("电脑收购预算判断失败：" + ex.Message); }
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
                Core.Log?.Warning("加载下层装机佬立绘失败：" + ex.Message);
                return true;
            }
        }
    }
}
