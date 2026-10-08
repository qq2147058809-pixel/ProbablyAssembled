using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace PCExpansion;

/// <summary>交易NPC的每访客货单与原生候选保管；未知库存不是空库存。</summary>
internal static class NpcStockOffers
{
    internal enum StockState { Unknown, Empty, Present }
    private sealed class Candidate
    {
        internal IntPtr Owner, Pointer;
        internal string? Run;
        internal int SaveSlot;
        internal StoreClient Visitor = null!;
        internal string Posted = "";
        internal GameItem Item = null!;
        internal GameItemElement? Element;
        internal UnityEngine.Object? Visual;
        internal bool Submitted, Destroyed, Uncertain, CleanupAttempted;
        internal int DestroyDepth;
        internal bool SameOwner => PlayerStore.IsInstanceExist() && PlayerStore.instance != null &&
            PlayerStore.instance.Pointer == Owner && PlayerStore.instance.runID == Run && PlayerStore.instance.saveSlotId == SaveSlot;
    }
    private static readonly List<Candidate> candidates = new();
    private static bool stocking;
    private static long retryAt, warnAt;
    internal static bool Pending => stocking || candidates.Count != 0;

    internal static StockState Probe(Func<GameItem, bool> match)
    {
        try
        {
            var entry = EmporiumEntry.Instance;
            if (entry?.invElement == null || StoreUIManager.Instance == null) return StockState.Unknown;
            var items = entry.GetAllNonOwnedItem();
            if (items == null) return StockState.Unknown;
            var found = false;
            foreach (var item in items)
            {
                if (item == null) return StockState.Unknown;
                if (match(item)) found = true;
            }
            return found ? StockState.Present : StockState.Empty;
        }
        catch (Exception ex) { Warn(ex); return StockState.Unknown; }
    }

    internal static void Update()
    {
        if (stocking || !Pending || Environment.TickCount64 < retryAt) return;
        retryAt = Environment.TickCount64 + 500;
        try { Cleanup(); } catch (Exception ex) { Warn(ex); }
    }

    internal static bool AllowSessionChange()
    {
        if (stocking) return false;
        try { Cleanup(); return !Pending; }
        catch (Exception ex) { Warn(ex); return false; }
    }

    private static bool Placed(PlayerStore store, IntPtr pointer)
    {
        foreach (var inventory in WorkroomStorage.Sources())
            foreach (var actual in inventory.childItems)
                if (actual != null && actual.Pointer == pointer && WorkroomStorage.Contains(inventory, actual)) return true;
        var items = store.FindAllItem(true) ?? throw new InvalidOperationException("NPC库存归属暂不可读。");
        foreach (var actual in items)
            if (actual != null && actual.Pointer == pointer && WorkroomStorage.Contains(actual.parentInventory, actual)) return true;
        return false;
    }

    private static void Cleanup()
    {
        var failures = new List<Exception>();
        foreach (var candidate in candidates.ToArray())
        {
            try
            {
                if (!candidate.SameOwner) throw new InvalidOperationException("NPC候选会话已变化，禁止对另一存档销毁物品。");
                if (candidate.Destroyed) { candidates.Remove(candidate); continue; }
                if (Placed(PlayerStore.instance, candidate.Pointer))
                {
                    if (candidate.Submitted) candidate.Visitor.AddTag(candidate.Posted);
                    candidates.Remove(candidate); continue;
                }
                if (candidate.Uncertain && candidate.CleanupAttempted && candidate.Element != null)
                {
                    if (candidate.Element.IsDestroyed()) { candidates.Remove(candidate); continue; }
                    candidate.Uncertain = false;
                }
                if (candidate.DestroyDepth != 0 || candidate.Uncertain || candidate.Submitted && ReferenceEquals(candidate.Visual, null))
                    throw new InvalidOperationException("NPC候选销毁/交付结果不明，保留保管引用。");
                if (candidate.Item.parentInventory != null)
                    throw new InvalidOperationException("NPC候选仍有库存归属，禁止清理。");
                candidate.CleanupAttempted = true;
                candidate.Item.Destroy();
                candidates.Remove(candidate);
            }
            catch (Exception ex) { failures.Add(ex); }
        }
        if (failures.Count != 0) throw new AggregateException("NPC库存候选清理未完成。", failures);
    }

    private static string ReadSingle(StoreClient client, string prefix)
    {
        string? value = null;
        if (client.tag != null)
            foreach (var tag in client.tag)
            {
                var clean = Core.Clean(tag);
                if (!clean.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (value != null) throw new InvalidOperationException("NPC货单字段重复。");
                value = clean.Substring(prefix.Length);
            }
        return value ?? "";
    }

    private static List<string> Plan(StoreClient client, string prefix, Func<List<string>> create,
        int minimum, int maximum, Func<string, bool> accepts, Action<List<string>>? validate)
    {
        var countText = ReadSingle(client, prefix + "COUNT_");
        var slotCount = 0;
        if (client.tag != null)
            foreach (var tag in client.tag)
                if (Core.Clean(tag).StartsWith(prefix + "ITEM_", StringComparison.Ordinal)) slotCount++;
        List<string> plan;
        if (countText.Length == 0)
        {
            if (slotCount != 0 || client.IsTag(prefix + "READY"))
                throw new InvalidOperationException("NPC货单保存不完整，禁止重新随机。");
            plan = create();
            ValidatePlan(plan, minimum, maximum, accepts);
            validate?.Invoke(plan);
            client.AddTag(prefix + "COUNT_" + plan.Count);
            for (var i = 0; i < plan.Count; i++) client.AddTag(prefix + "ITEM_" + i + "_" + plan[i]);
            client.AddTag(prefix + "READY");
        }
        else
        {
            if (!int.TryParse(countText, out var count) || count < minimum || count > maximum || slotCount != count || !client.IsTag(prefix + "READY"))
                throw new InvalidOperationException("NPC货单未完整发布，禁止重新随机。");
            plan = new List<string>();
            for (var i = 0; i < count; i++) plan.Add(ReadSingle(client, prefix + "ITEM_" + i + "_"));
            ValidatePlan(plan, minimum, maximum, accepts);
            validate?.Invoke(plan);
        }
        return plan;
    }

    private static void ValidatePlan(List<string> plan, int minimum, int maximum, Func<string, bool> accepts)
    {
        if (plan.Count < minimum || plan.Count > maximum) throw new InvalidOperationException("NPC货单数量无效。");
        foreach (var id in plan)
            if (string.IsNullOrEmpty(id) || !accepts(id)) throw new InvalidOperationException("NPC货单商品无效：" + id);
    }

    internal static void Stock(PlayerStore store, StoreClient client, string stockTag, string group,
        Func<List<string>> create, int minimum, int maximum, Func<string, bool> accepts,
        Action<GameItem>? configure = null, Func<GameItem, bool>? existingFilter = null, Action<List<string>>? validate = null)
    {
        if (stocking) return;
        stocking = true;
        try
        {
            Cleanup();
            if (candidates.Count != 0) throw new InvalidOperationException("NPC库存候选尚未清理，禁止重复生成。");
            var owner = store.Pointer; var run = store.runID; var slot = store.saveSlotId;
            void RequireCurrent()
            {
                if (!PlayerStore.IsInstanceExist() || PlayerStore.instance == null || PlayerStore.instance.Pointer != owner ||
                    PlayerStore.instance.runID != run || PlayerStore.instance.saveSlotId != slot ||
                    store.currentClientInstance?.storeClient?.Pointer != client.Pointer)
                    throw new InvalidOperationException("NPC供货期间当前会话或访客变化。");
            }
            RequireCurrent();
            var prefix = "PCREPAIR_NPC_OFFER_" + group + "_";
            if (client.IsTag(prefix + "DONE")) return;
            if (!client.IsTag(prefix + "READY"))
            {
                var existing = Probe(item => item.IsTag(stockTag) && (existingFilter == null || existingFilter(item)));
                if (existing == StockState.Unknown) throw new InvalidOperationException("NPC现有库存未知，停止供货。");
                if (existing == StockState.Present) { client.AddTag(prefix + "DONE"); return; }
            }
            var batch = ReadSingle(client, prefix + "BATCH_");
            if (batch.Length == 0) { batch = Guid.NewGuid().ToString("N"); client.AddTag(prefix + "BATCH_" + batch); }
            else if (!Guid.TryParseExact(batch, "N", out _)) throw new InvalidOperationException("NPC供货批次无效。");
            var plan = Plan(client, prefix, create, minimum, maximum, accepts, validate);
            var pending = new List<Candidate>();
            Exception? failure = null;
            try
            {
                for (var i = 0; i < plan.Count; i++)
                {
                    RequireCurrent();
                    var posted = prefix + "POSTED_" + batch + "_" + i;
                    if (client.IsTag(posted)) continue;
                    var offered = Probe(item => item.IsTag(stockTag) && item.IsTag(posted));
                    if (offered == StockState.Unknown) throw new InvalidOperationException("NPC货单落位暂不可确认。");
                    if (offered == StockState.Present) { client.AddTag(posted); continue; }
                    var item = DirectoryMaster.Item(plan[i], plan[i] != ContactCard.Id && plan[i] != ComputerManual.Id)
                        ?? throw new InvalidOperationException("NPC库存商品未注册：" + plan[i]);
                    var candidate = new Candidate { Owner = owner, Run = run, SaveSlot = slot, Visitor = client,
                        Pointer = item.Pointer, Item = item, Posted = posted };
                    candidates.Add(candidate); pending.Add(candidate);
                    RequireCurrent();
                    candidate.Element = item.TryCast<GameItemElement>();
                    if (candidate.Element != null)
                    {
                        if (candidate.Element.IsDestroyed()) { candidate.Destroyed = true; throw new InvalidOperationException("NPC工厂返回已销毁商品。"); }
                        candidate.Visual = candidate.Element.rectTransform;
                    }
                    if (!ReferenceEquals(candidate.Visual, null) && !UnityEngine.Object.IsNativeObjectAlive(candidate.Visual))
                        throw new InvalidOperationException("NPC商品实体失活。");
                    item.SetUnitCount(1); item.EnableTag(stockTag, false); item.EnableTag(posted, false);
                    configure?.Invoke(item);
                }
                foreach (var candidate in pending)
                {
                    RequireCurrent();
                    candidate.Submitted = true;
                    store.AddDirectSellingItemToTable(candidate.Item, false, false, false, 0);
                    RequireCurrent();
                    if (candidate.Destroyed || Probe(item => item.IsTag(stockTag) && item.IsTag(candidate.Posted)) != StockState.Present ||
                        !Placed(store, candidate.Pointer)) throw new InvalidOperationException("NPC库存尚未实际入柜。");
                    client.AddTag(candidate.Posted);
                }
            }
            catch (Exception ex) { failure = ex; }
            try { Cleanup(); } catch (Exception ex) { failure = failure == null ? ex : new AggregateException(failure, ex); }
            if (failure != null) throw new InvalidOperationException("NPC货单未完成；保留计划、交付标记和恢复候选。", failure);
            RequireCurrent();
            for (var i = 0; i < plan.Count; i++)
                if (!client.IsTag(prefix + "POSTED_" + batch + "_" + i)) throw new InvalidOperationException("NPC货单仍有未交付项。");
            client.AddTag(prefix + "DONE");
            Core.Log?.Msg("[NPC供货] 本次货单已确认入柜：" + group + "；数量=" + plan.Count);
        }
        finally { stocking = false; }
    }

    private static void ObserveDestroy(GameItem item, bool starting, Exception? error = null)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.Pointer != item.Pointer) continue;
            try
            {
                if (!candidate.SameOwner) continue;
                if (starting) candidate.DestroyDepth++;
                else if (candidate.DestroyDepth > 0 && --candidate.DestroyDepth == 0)
                { candidate.Destroyed = error == null; candidate.Uncertain = error != null; }
            }
            catch { candidate.Uncertain = true; }
        }
    }

    private static void Warn(Exception ex)
    {
        if (Environment.TickCount64 < warnAt) return;
        warnAt = Environment.TickCount64 + 5000;
        Core.Log?.Warning("NPC供货暂不可确认，停止生成并保留恢复：" + ex.Message);
    }

    [HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.Destroy))]
    internal static class CandidateDestroyPatch
    {
        private static void Prefix(GameItemElement __instance) => ObserveDestroy(__instance, true);
        private static Exception? Finalizer(GameItemElement __instance, Exception? __exception)
        { ObserveDestroy(__instance, false, __exception); return __exception; }
    }
}
