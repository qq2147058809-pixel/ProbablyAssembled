using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>独立电脑材料商贩和配件小偷；只替换自身库存，复用原版外观与交易模板。</summary>
internal static class ComputerSupplierNpcs
{
    internal const string SellerId = "pcrepair.computer_material_seller";
    internal const string ThiefId = "pcrepair.computer_parts_thief";
    private const string ClientTag = "PCREPAIR_COMPUTER_MATERIAL_SELLER";
    private const string StockTag = "PCREPAIR_COMPUTER_MATERIAL_STOCK";
    private const string PlanPrefix = "PCREPAIR_COMPUTER_MATERIAL_PLAN_";
    private const string PostedPrefix = "PCREPAIR_COMPUTER_MATERIAL_POSTED_";
    private const string StockedTag = "PCREPAIR_COMPUTER_MATERIAL_STOCKED";
    private const string PlanReadyTag = "PCREPAIR_COMPUTER_SUPPLIER_PLAN_READY";
    private const string BatchPrefix = "PCREPAIR_COMPUTER_SUPPLIER_BATCH_";
    private const string ThiefTag = "PCREPAIR_COMPUTER_PARTS_THIEF";
    private static readonly Random random = new();
    private static Il2CppSystem.Func<StoreClient>? sellerFactory, thiefFactory;
    private static Il2CppSystem.Action? introduceAction;
    private static bool stocking;
    private static long cleanupAt;
    private sealed class StockCandidate
    {
        internal IntPtr Owner, Pointer;
        internal StoreClient Visitor = null!;
        internal string? Run;
        internal int SaveSlot;
        internal string SlotTag = "";
        internal GameItem Item = null!;
        internal UnityEngine.Object? Visual;
        internal GameItemElement? Element;
        internal bool Submitted, Destroyed, DestroyUncertain, CleanupAttempted;
        internal int DestroyDepth;
        internal bool SameOwner => PlayerStore.IsInstanceExist() && PlayerStore.instance != null &&
            PlayerStore.instance.Pointer == Owner && PlayerStore.instance.runID == Run && PlayerStore.instance.saveSlotId == SaveSlot;
    }
    // Keep a custodian until delivery or cleanup is proved. Failed cleanup is
    // retried before creating more stock, never against a later save session.
    private static readonly List<StockCandidate> stockCandidates = new();

    internal static bool StockPending
    {
        get
        {
            try { foreach (var candidate in stockCandidates) if (candidate.SameOwner) return true; return false; }
            catch { return true; }
        }
    }

    internal static void UpdateStockCleanup()
    {
        if (stocking || stockCandidates.Count == 0 || Environment.TickCount64 < cleanupAt) return;
        cleanupAt = Environment.TickCount64 + 500;
        try { CleanupCandidates(); }
        catch (Exception ex) { Core.Debug("电脑供应者库存候选仍待核对：" + ex.Message); }
    }

    private static void ObserveDestroy(GameItem item, bool starting, Exception? error = null)
    {
        foreach (var candidate in stockCandidates)
        {
            if (candidate.Pointer != item.Pointer) continue;
            try
            {
                if (!candidate.SameOwner) continue;
                if (starting) candidate.DestroyDepth++;
                else if (candidate.DestroyDepth > 0 && --candidate.DestroyDepth == 0)
                {
                    candidate.Destroyed = error == null;
                    candidate.DestroyUncertain = error != null;
                }
            }
            // An observer must never replace the original Destroy result.
            // An unreadable context keeps stock recovery conservative.
            catch { candidate.DestroyUncertain = true; }
        }
    }

    private static bool IsActuallyPlaced(PlayerStore store, IntPtr pointer)
    {
        // Inspect current inventory wrappers, not the submitted wrapper that
        // the native table operation may already have destroyed.
        foreach (var inventory in WorkroomStorage.Sources())
            foreach (var actual in inventory.childItems)
                if (actual != null && actual.Pointer == pointer && WorkroomStorage.Contains(inventory, actual)) return true;
        var items = store.FindAllItem(true);
        if (items != null)
            foreach (var actual in items)
                if (actual != null && actual.Pointer == pointer && WorkroomStorage.Contains(actual.parentInventory, actual)) return true;
        return false;
    }

    private static void CleanupCandidates()
    {
        var failures = new List<Exception>();
        foreach (var candidate in stockCandidates.ToArray())
            try
            {
                if (!candidate.SameOwner)
                {
                    stockCandidates.Remove(candidate);
                    Core.Log?.Warning("电脑材料卖家旧档候选已解除引用，未在新档调用销毁。");
                    continue;
                }
                var store = PlayerStore.instance;
                if (candidate.Destroyed)
                { stockCandidates.Remove(candidate); continue; }
                if (IsActuallyPlaced(store, candidate.Pointer))
                {
                    // An unexpected real inventory is still a successful
                    // transfer of custody; do not destroy or duplicate it.
                    if (candidate.Submitted) candidate.Visitor.AddTag(candidate.SlotTag);
                    stockCandidates.Remove(candidate); continue;
                }
                if (candidate.DestroyUncertain && candidate.CleanupAttempted && candidate.Element != null)
                {
                    // The table operation had returned without destroying this
                    // candidate; only our later cleanup threw. Use the same
                    // IsDestroyed retry guard as WorkroomItemCodec.Destroy.
                    if (candidate.Element.IsDestroyed()) { stockCandidates.Remove(candidate); continue; }
                    candidate.DestroyUncertain = false;
                }
                if (candidate.DestroyDepth != 0 || candidate.DestroyUncertain)
                    throw new InvalidOperationException("电脑材料卖家候选销毁结果不明，保留候选等待重试。");
                if (candidate.Submitted && candidate.Visual == null)
                    throw new InvalidOperationException("电脑材料卖家交付候选缺少活性证据，保留候选。");
                // Only a live, unattached candidate may be destroyed. A
                // non-null parent is protected even if its inventory is not
                // covered by the native inventory enumeration above.
                if (candidate.Item.parentInventory != null)
                    throw new InvalidOperationException("电脑材料卖家候选仍有库存归属，保留候选等待核对。");
                candidate.CleanupAttempted = true;
                candidate.Item.Destroy();
                stockCandidates.Remove(candidate);
            }
            catch (Exception ex) { failures.Add(ex); }
        if (failures.Count != 0) throw new AggregateException("电脑材料卖家候选清理未完成。", failures);
    }

    internal static bool IsSupplier(StoreClient? client) =>
        client != null && IsSupplierId(Core.Clean(client.identifier));

    internal static bool IsSupplierId(string id) => id == SellerId || id == ThiefId;

    private static bool IsThief(StoreClient client) => Core.Clean(client.identifier) == ThiefId;

    internal static StoreClient Create(string id)
    {
        if (!IsSupplierId(id)) throw new ArgumentOutOfRangeException(nameof(id));
        var thief = id == ThiefId;
        // 保留各自模板的随机原版立绘、阵营、议价及批发字段。
        var client = thief ? StoreClientList.CreateThief() : StoreClientList.CreateScavGeneral();
        if (client == null) throw new InvalidOperationException("原版电脑供应者模板创建失败。");
        client.identifier = id;
        client.displayName = LanguageText.Get(thief ? "supplier.thief.name" : "text.49330d42c7bc");
        client.realName = client.displayName;
        client.clientIntent = StoreClient.ClientIntent.SELL;
        client.AddTag(thief ? ThiefTag : ClientTag);
        client.AddBasicDialog();
        ConfigureDialogue(client);
        client.isIntroduced = false;
        return client;
    }

    private static void ConfigureDialogue(StoreClient client)
    {
        // 替换模板库存回调，避免原版杂货混入专属供应者库存。
        var thief = IsThief(client);
        var greeting = new Dialogue().SetText(client.displayName,
            LanguageText.Get(thief ? "supplier.thief.greeting" : "text.47b2c0f573a3"));
        var offer = new Dialogue().SetText(client.displayName,
            LanguageText.Get(thief ? "supplier.thief.offer" : "text.055f6bdf0038"));
        greeting.isMainDialog = true;
        greeting.SetNextDialogue(offer);
        introduceAction ??= (Il2CppSystem.Action)(Action)IntroduceCurrentSupplier;
        offer.SetEndAction(introduceAction);
        client.mainDialogue = greeting;
        client.isMainDialogueStarted = false;
    }

    internal static void EnsureRegistered()
    {
        var registry = StoreClientListDict.storeClientDict;
        if (registry == null)
        {
            registry = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Func<StoreClient>>();
            StoreClientListDict.storeClientDict = registry;
        }
        sellerFactory ??= (Il2CppSystem.Func<StoreClient>)(Func<StoreClient>)(() => Create(SellerId));
        thiefFactory ??= (Il2CppSystem.Func<StoreClient>)(Func<StoreClient>)(() => Create(ThiefId));
        registry[SellerId] = sellerFactory;
        registry[ThiefId] = thiefFactory;
    }

    private static void IntroduceCurrentSupplier()
    {
        try
        {
            var client = PlayerStore.Instance?.currentClientInstance?.storeClient;
            if (!IsSupplier(client)) return;
            AddStock(client!);
            client!.OnIntroduced();
        }
        catch (Exception ex) { Core.Log?.Error("电脑材料卖家开始交易失败：" + ex); }
    }

    private static void Shuffle<T>(List<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static List<string> ReadPlan(StoreClient client)
    {
        var plan = new List<string>();
        var ended = false;
        // 商贩最多七件，含独立追加的CPU。先保存整个计划，再上柜；重入不重新随机。
        for (var slot = 0; slot < 7; slot++)
        {
            var prefix = PlanPrefix + slot + "_";
            string? id = null;
            if (client.tag != null)
                foreach (var tag in client.tag)
                {
                    var value = Core.Clean(tag);
                    if (!value.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    if (id != null) throw new InvalidOperationException("电脑供应者货单槽位重复，保留原记录。");
                    id = value.Substring(prefix.Length);
                }
            if (id == null) { ended = true; continue; }
            if (ended || string.IsNullOrEmpty(id)) throw new InvalidOperationException("电脑供应者货单有缺失槽位。");
            var spec = Components.Find(id);
            if (spec == null && Array.IndexOf(ComponentRepair.GetRepairMaterialIds(), id) < 0 ||
                spec != null && (spec.Owner.IsCase || (IsThief(client) ? spec.Tier < 4 : spec.Broken || spec.Tier > 3)))
                throw new InvalidOperationException("电脑供应者货单含无效商品，保留原记录。");
            plan.Add(id);
        }
        return plan;
    }

    private static void ValidatePlan(StoreClient client, List<string> plan)
    {
        var thief = IsThief(client);
        if (thief ? plan.Count < 2 || plan.Count > 3 : plan.Count < 6 || plan.Count > 7)
            throw new InvalidOperationException("电脑供应者货单数量无效。");
        var partCount = thief ? plan.Count : plan.Count - 4;
        for (var i = 0; i < partCount; i++)
        {
            var spec = Components.Find(plan[i]);
            if (spec == null || spec.Owner.IsCase ||
                (thief ? spec.Tier < 4 : spec.Tier > 3 || spec.Broken) ||
                thief && (i == 0 && spec.Broken || spec.Broken &&
                    (spec.Owner.Tag == Components.CpuTag || spec.Owner.Tag == Components.MotherboardTag)))
                throw new InvalidOperationException("电脑供应者货单配件不符合供货范围。");
        }
        if (thief) return;
        // The final part slot is the additional CPU; random slots may also contain CPUs.
        var bonusCpu = Components.Find(plan[partCount - 1]);
        if (bonusCpu == null || bonusCpu.Owner.Tag != Components.CpuTag || bonusCpu.Broken || bonusCpu.Tier is < 1 or > 3)
            throw new InvalidOperationException("电脑材料卖家货单缺少额外完好CPU。");
        var materials = new HashSet<string>(StringComparer.Ordinal);
        var allowed = new[] { "nuts_metal", "common_electronic", "wire", "printer_plastic", "scrap_metal", "metal_ingot" };
        for (var i = partCount; i < plan.Count; i++)
            if (Array.IndexOf(allowed, plan[i]) < 0 || !materials.Add(plan[i]))
                throw new InvalidOperationException("电脑材料卖家货单材料无效或重复。");
        if (!materials.Contains("nuts_metal") || !materials.Contains("common_electronic"))
            throw new InvalidOperationException("电脑材料卖家货单缺少螺丝或电子零件。");
    }

    private static int RollSellerTier()
    {
        var roll = random.Next(100);
        return roll < 50 ? 1 : roll < 80 ? 2 : 3;
    }

    private static List<string> GetOrCreatePlan(StoreClient client)
    {
        var plan = ReadPlan(client);
        if (plan.Count > 0)
        {
            if (!client.IsTag(PlanReadyTag))
                throw new InvalidOperationException("电脑供应者货单未完整保存，禁止重新随机。");
            ValidatePlan(client, plan);
            return plan;
        }
        if (client.IsTag(PlanReadyTag)) throw new InvalidOperationException("电脑供应者货单缺失。");
        var thief = IsThief(client);
        var partCount = thief ? random.Next(2, 4) : random.Next(1, 3);
        for (var i = 0; i < partCount; i++)
        {
            var tier = thief ? (random.Next(100) < 90 ? 4 : 5) : RollSellerTier();
            var broken = thief && i > 0 && random.Next(2) == 1;
            var parts = new List<string>();
            foreach (var spec in Components.AllItems())
                if (!spec.Owner.IsCase && spec.Tier == tier && spec.Broken == broken &&
                    (!broken || spec.Owner.Tag != Components.CpuTag && spec.Owner.Tag != Components.MotherboardTag))
                    parts.Add(spec.Id);
            if (parts.Count == 0) throw new InvalidOperationException("电脑供应者可选配件不足。");
            plan.Add(parts[random.Next(parts.Count)]);
        }
        if (!thief)
        {
            var cpuTier = RollSellerTier();
            var cpus = new List<string>();
            foreach (var spec in Components.AllItems())
                if (spec.Owner.Tag == Components.CpuTag && spec.Tier == cpuTier && !spec.Broken)
                    cpus.Add(spec.Id);
            if (cpus.Count == 0) throw new InvalidOperationException("电脑材料卖家可选CPU不足。");
            plan.Add(cpus[random.Next(cpus.Count)]);
            var materials = new List<string> { "wire", "printer_plastic", "scrap_metal", "metal_ingot" };
            Shuffle(materials);
            plan.Add("nuts_metal");
            plan.Add("common_electronic");
            plan.Add(materials[0]);
            plan.Add(materials[1]);
        }
        ValidatePlan(client, plan);
        for (var i = 0; i < plan.Count; i++) client.AddTag(PlanPrefix + i + "_" + plan[i]);
        client.AddTag(PlanReadyTag);
        return plan;
    }

    private static string BatchId(StoreClient client)
    {
        string? id = null;
        if (client.tag != null)
            foreach (var tag in client.tag)
            {
                var value = Core.Clean(tag);
                if (!value.StartsWith(BatchPrefix, StringComparison.Ordinal)) continue;
                if (id != null) throw new InvalidOperationException("电脑供应者批次重复。");
                id = value.Substring(BatchPrefix.Length);
                if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidOperationException("电脑供应者批次无效。");
            }
        if (id != null) return id;
        id = Guid.NewGuid().ToString("N");
        client.AddTag(BatchPrefix + id);
        return id;
    }

    private static bool HasPostedOffer(string slotTag)
    {
        var items = EmporiumEntry.Instance?.GetAllNonOwnedItem();
        if (items == null) throw new InvalidOperationException("电脑供应者柜台尚未就绪。");
        foreach (var item in items)
            if (item != null && item.IsTag(StockTag) && item.IsTag(slotTag)) return true;
        return false;
    }

    private static void AddStock(StoreClient client)
    {
        if (stocking || !IsSupplier(client)) return;
        var store = PlayerStore.IsInstanceExist() ? PlayerStore.instance : null;
        if (store == null) return;
        stocking = true;
        try { AddStockCore(store, client); }
        finally { stocking = false; }
    }

    private static void AddStockCore(PlayerStore store, StoreClient client)
    {
        var owner = store.Pointer; var run = store.runID; var saveSlot = store.saveSlotId;
        void RequireSession()
        {
            if (!PlayerStore.IsInstanceExist() || PlayerStore.instance == null || PlayerStore.instance.Pointer != owner ||
                PlayerStore.instance.runID != run || PlayerStore.instance.saveSlotId != saveSlot)
                throw new InvalidOperationException("电脑材料卖家上柜期间存档已变化。");
        }
        CleanupCandidates();
        if (StockPending) throw new InvalidOperationException("电脑供应者库存候选仍待核对，暂不重复创建。");
        if (client.IsTag(StockedTag)) return;
        var batchId = BatchId(client);
        var plan = GetOrCreatePlan(client);
        var pending = new List<StockCandidate>();
        Exception? failure = null;
        try
        {
            // Register each returned item before any further native setter can
            // throw. A later factory failure still cleans all earlier items.
            for (var i = 0; i < plan.Count; i++)
            {
                RequireSession();
                var slotTag = PostedPrefix + batchId + "_" + i;
                if (client.IsTag(slotTag)) continue;
                if (HasPostedOffer(slotTag)) { client.AddTag(slotTag); continue; }
                var item = DirectoryMaster.Item(plan[i], true);
                if (item == null) throw new InvalidOperationException("库存物品未注册：" + plan[i]);
                var candidate = new StockCandidate { Owner = owner, Run = run,
                    SaveSlot = saveSlot, Visitor = client, Pointer = item.Pointer, Item = item, SlotTag = slotTag };
                stockCandidates.Add(candidate); pending.Add(candidate);
                RequireSession();
                if (item.TryCast<GameItemElement>() is { } element)
                {
                    candidate.Element = element;
                    if (element.IsDestroyed()) { candidate.Destroyed = true; throw new InvalidOperationException("库存工厂返回已销毁物品。"); }
                    candidate.Visual = element.rectTransform;
                }
                if (candidate.Visual != null && !UnityEngine.Object.IsNativeObjectAlive(candidate.Visual))
                    throw new InvalidOperationException("库存物品实体已失活，未交给柜台。");
                item.SetUnitCount(1);
                item.EnableTag(StockTag, false);
                item.EnableTag(slotTag, false);
            }
            foreach (var candidate in pending)
            {
                if (!candidate.SameOwner) throw new InvalidOperationException("电脑材料卖家上柜期间存档已变化。");
                candidate.Submitted = true;
                store.AddDirectSellingItemToTable(candidate.Item, false, IsThief(client), false, 0);
                if (!candidate.SameOwner || candidate.Destroyed || !HasPostedOffer(candidate.SlotTag) || !IsActuallyPlaced(store, candidate.Pointer))
                    throw new InvalidOperationException("柜台未能放下电脑材料卖家库存，槽位=" + candidate.SlotTag);
                client.AddTag(candidate.SlotTag);
            }
        }
        catch (Exception ex) { failure = ex; }
        try { CleanupCandidates(); }
        catch (Exception ex) { failure = failure == null ? ex : new AggregateException(failure, ex); }
        if (failure != null) throw new InvalidOperationException("电脑材料卖家上柜未完成；已交付标记和未清理候选保留。", failure);
        RequireSession();
        for (var i = 0; i < plan.Count; i++)
            if (!client.IsTag(PostedPrefix + batchId + "_" + i))
                throw new InvalidOperationException("电脑供应者货单尚有未交付商品。");
        client.AddTag(StockedTag);
        Core.Log?.Msg(IsThief(client) ? "电脑配件小偷库存已上柜：" + plan.Count + " 件 T4/T5 配件，按原版标为赃物。" :
            "电脑材料卖家库存已上柜：" + (plan.Count - 5) + " 件 T1～T3 完好随机配件、额外 1 颗完好 CPU 和 4 件维修材料（含螺丝、电子零件）。");
    }

    // GameItem.Destroy is abstract in the native game metadata. Observe its
    // concrete implementation; the generated base wrapper has no hook target.
    [HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.Destroy))]
    internal static class CandidateElementDestroyPatch
    {
        private static void Prefix(GameItemElement __instance) => ObserveDestroy(__instance, true);
        private static Exception? Finalizer(GameItemElement __instance, Exception? __exception)
        { ObserveDestroy(__instance, false, __exception); return __exception; }
    }

    private static void AddCurrentStock()
    {
        try
        {
            var client = PlayerStore.Instance?.currentClientInstance?.storeClient;
            if (IsSupplier(client)) AddStock(client!);
        }
        catch (Exception ex) { Core.Log?.Error("电脑材料卖家上柜失败：" + ex); }
    }

    [HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.CreateStoreClient))]
    internal static class ClientFactoryPatch
    {
        private static bool Prefix(string identifier, ref StoreClient __result)
        {
            var id = Core.Clean(identifier);
            if (!IsSupplierId(id)) return true;
            try
            {
                __result = Create(id);
                return false;
            }
            catch (Exception ex)
            {
                Core.Log?.Error("创建或恢复电脑供应者失败：" + ex);
                __result = null!;
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.OnNextClientArrived))]
    internal static class ArrivalStockPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => AddCurrentStock();
    }

    [HarmonyPatch(typeof(StoreClient), nameof(StoreClient.OnIntroduced))]
    internal static class IntroductionStockPatch
    {
        private static void Prefix(StoreClient __instance)
        {
            if (!IsSupplier(__instance)) return;
            try { AddStock(__instance); }
            catch (Exception ex) { Core.Log?.Error("电脑材料卖家交易前补全库存失败：" + ex); }
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.LoadGame))]
    internal static class LoadPatch
    {
        private static void Postfix(PlayerStore __instance, bool __runOriginal)
        {
            if (!__runOriginal || !SaveGeneration.IsSupported(__instance)) return;
            try
            {
                EnsureRegistered();
                var stack = __instance.storeClientManager?.clientStack;
                if (stack != null)
                    foreach (var client in stack)
                        if (IsSupplier(client) && !client.isIntroduced) ConfigureDialogue(client);
                var current = __instance.currentClientInstance?.storeClient;
                if (!IsSupplier(current)) return;
                current!.AddBasicDialog();
                // 未完成介绍时重新绑定本商贩的托管回调、对白和动作。
                if (!current.isIntroduced) ConfigureDialogue(current);
                else if (!current.IsTag(StockedTag)) current.AddTag(StockedTag);
                // 读档后柜台已有原版恢复的货物，计划和交付标记只补齐尚未交付的槽位。
                if (__instance.isClientArrived) AddStock(current);
            }
            catch (Exception ex) { Core.Log?.Error("读档后恢复电脑材料卖家失败：" + ex); }
        }
    }
}
