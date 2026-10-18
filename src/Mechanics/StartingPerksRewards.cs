using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Only the explicitly opened native new-run window can award gifts.</summary>
internal static class StartingPerksRewards
{
    private const string AwardKey = "pcexpansion.starting-perks.awarded";
    private sealed class Gift
    {
        internal Func<GameItem> Factory = null!;
        internal GameItem? Item;
        internal WorkroomItemCodec.Candidate? Cleanup;
        internal bool Delivered;
    }
    private static PlayerStore? owner;
    private static int slot;
    private static bool handled, complete, executing, apprentice, crypto, initialSavePending;
    private static readonly List<Gift> gifts = new();

    internal static void BeginNew(PlayerStore store)
    {
        Reset();
        owner = store;
        // StartNewGame has not yet copied the selected slot into a reused store.
        slot = NewGameData.Instance?.saveSlot ?? throw new InvalidOperationException("新档选择尚未就绪。");
    }

    internal static void Reset()
    {
        if (executing) throw new InvalidOperationException("开局赠品发放中不能切换会话。");
        // Never destroy an object after a native callback may have attached it.
        foreach (var gift in gifts)
            if (!gift.Delivered && gift.Item != null && gift.Item.parentInventory == null &&
                (gift.Item.parents == null || gift.Item.parents.Count == 0))
            {
                var candidate = gift.Cleanup ?? throw new InvalidOperationException("开局赠品清理凭据缺失。");
                // Keep the creation-time owner binding; never destroy old wrappers
                // against a newly loaded store. Include all native nested items.
                if (candidate.SameOwner) candidate.Nodes = CollectNodes(gift.Item);
                WorkroomItemCodec.Destroy(candidate);
            }
        gifts.Clear(); owner = null; handled = complete = apprentice = crypto = initialSavePending = false;
    }

    private static void RequireOwner(PlayerStore store, bool checkSlot = true)
    {
        if (owner == null || owner.Pointer != store.Pointer || (checkSlot && store.saveSlotId != slot) ||
            !PlayerStore.IsInstanceExist() || PlayerStore.instance == null || PlayerStore.instance.Pointer != store.Pointer)
            throw new InvalidOperationException("开局赠品会话已改变。");
    }

    private static void Add(Func<GameItem> factory) => gifts.Add(new Gift { Factory = factory });

    private static List<GameItem> CollectNodes(GameItem root)
    {
        var result = new List<GameItem>();
        var visited = new HashSet<IntPtr>();
        var pending = new Stack<GraphNodeStorage>(); pending.Push(root.Cast<GraphNodeStorage>());
        while (pending.Count != 0)
        {
            var node = pending.Pop();
            if (!visited.Add(node.Pointer)) continue;
            if (visited.Count > 4096) throw new InvalidOperationException("开局赠品子图异常，停止清理并保留实体。");
            if (node.TryCast<GameItem>() is { } item) result.Add(item);
            if (node.children != null) foreach (var child in node.children) if (child != null) pending.Push(child);
        }
        return result;
    }

    internal static void HandleNewGame()
    {
        // LoadGame never opens this window. Native callbacks after a load do not award.
        if (owner == null || complete || executing) return;
        var store = owner;
        RequireOwner(store);
        executing = true;
        try
        {
            if (!handled)
            {
                apprentice = StartingPerks.Has(store, StartingPerks.ComputerApprentice);
                if (apprentice)
                {
                    Add(ContactCard.Create); Add(ComputerManual.Create);
                    foreach (var material in new[] { "common_electronic", "metal_ingot", "nuts_metal", "printer_plastic", "scrap_metal", "wire" })
                    {
                        var id = material;
                        Add(() => DirectoryMaster.Item(id, true) ?? throw new InvalidOperationException("找不到开局材料：" + id));
                    }
                }
                crypto = StartingPerks.Has(store, StartingPerks.CryptoExpert);
                if (crypto)
                {
                    Add(() => ServerRoomHardware.Create("pcrepair.mining_case_t1"));
                    Add(() => Components.Create("pcrepair.component_gpu_t1"));
                }
                handled = true;
            }
            if (gifts.Count == 0) { complete = true; return; }
            var entry = EmporiumEntry.Instance ?? throw new InvalidOperationException("原版库存尚未就绪。");
            var main = entry._invElement_k__BackingField ?? throw new InvalidOperationException("原版主库存尚未就绪。");
            var back = entry._backInvinvElement_k__BackingField;
            var mainNode = main.Cast<GraphNodeStorage>();
            var backNode = back?.Cast<GraphNodeStorage>();
            foreach (var gift in gifts)
            {
                if (gift.Delivered) continue;
                if (gift.Item == null)
                {
                    gift.Item = gift.Factory();
                    gift.Cleanup = new WorkroomItemCodec.Candidate { Root = gift.Item, Nodes = new List<GameItem> { gift.Item } };
                }
                var item = gift.Item;
                var itemNode = item.Cast<GraphNodeStorage>();
                RequireOwner(store);
                bool Present() => GraphUtils.FindChildEquals(mainNode, itemNode) || backNode != null && GraphUtils.FindChildEquals(backNode, itemNode);
                // Reconcile actual attachment before retrying an interrupted native callback.
                if (!Present())
                {
                    if (item.parentInventory != null || item.parents != null && item.parents.Count != 0)
                        throw new InvalidOperationException("开局赠品已进入未知位置，保留物品并停止发放。");
                    if (item.unitCount != 1) throw new InvalidOperationException("开局赠品必须是一个完整物件。");
                    GraphUtils.TryAcceptAll(mainNode, item, 1);
                    if (!Present() && backNode != null) GraphUtils.TryAcceptAll(backNode, item, 1);
                }
                if (!Present()) throw new InvalidOperationException("原版库存空间不足，开局赠品尚未全部入库；初始保存已停止。");
                GeneralHelper.SetItemOwned(item, true);
                RequireOwner(store);
                gift.Delivered = true;
            }
            store.modData ??= new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
            if (apprentice)
                store.modData[AssemblerContactAccess.UnlockKey] = "1";
            store.modData[AwardKey] = "1";
            complete = true;
        }
        catch (Exception ex)
        {
            Core.Log?.Error("开局特性赠品未完成，禁止初始保存：" + ex.Message);
            throw;
        }
        finally { executing = false; }
    }

    internal static void RequireInitialSave(PlayerStore store)
    {
        RequireOwner(store);
        if (!handled || !complete || executing)
            throw new InvalidOperationException("开局特性赠品未全部完成，不能建立初始存档。");
    }

    internal static void BindInitialSave(PlayerStore store)
    {
        // Only InitialSave's validated entry opens this one-shot transition.
        // Native InitialSave replaces the selected slot with NextFreeSlotId
        // and creates runID before it calls SaveGame. The owner must not change.
        RequireOwner(store, checkSlot: false);
        if (!initialSavePending || !handled || !complete || executing || string.IsNullOrEmpty(store.runID))
            throw new InvalidOperationException("开局首次保存尚未就绪，不能绑定新档。");
        if (crypto)
        {
            var document = ServerRoomState.New(store.runID, store.saveSlotId);
            document.Enabled = true;
            var raw = ServerRoomState.Serialize(document);
            store.modData ??= new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
            if (store.modData.ContainsKey(ServerRoomState.SaveKey) && store.modData[ServerRoomState.SaveKey] != raw)
                throw new InvalidOperationException("新档机房记录已存在且不符合开局赠品，停止初始保存。");
            store.modData[ServerRoomState.SaveKey] = raw;
        }
        slot = store.saveSlotId;
        initialSavePending = false;
    }

    internal static void PrepareInitialSave(PlayerStore store)
    {
        RequireInitialSave(store);
        if (initialSavePending) throw new InvalidOperationException("开局首次保存正在进行。");
        initialSavePending = true;
    }

    [HarmonyPatch(typeof(StartingPerkContent), nameof(StartingPerkContent.HandleNewGamePerk))]
    internal static class NewGamePatch
    {
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix() => HandleNewGame();
    }
}
