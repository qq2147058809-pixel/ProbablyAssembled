using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Run/slot-bound observations. A quote or an ownership change alone is never a payment.</summary>
internal static class NpcStoryProgress
{
    private static PlayerStore? owner;
    private static string? run, raw, error, beforeWrite, afterWrite;
    private static int slot;
    private static bool present, beforePresent, publishing;
    private static bool observed25, observed50;
    private static NpcStoryProgressState.Document? document;
    private static readonly Queue<Payment> payments = new();
    private static long nextObservation, warningAt;
    [ThreadStatic] private static Trade? trade;
    private sealed class Payment
    {
        internal double Amount;
        internal string? Receipt;
        internal NpcStoryProgressState.Document? Candidate;
    }
    private sealed class Trade
    {
        internal PlayerStore Store = null!;
        internal StoreClient Client = null!;
        internal string Run = "";
        internal int Slot, Cash, Amount;
        internal long ChildrenDelta;
        internal bool Buy, Callback, Conflict;
        internal IntPtr Item;
        internal Trade? Parent;
    }
    internal static bool Ready
    { get { try { return document != null && error == null && !publishing && afterWrite == null && SameOwner(); } catch { return false; } } }
    internal static bool WorkPending => afterWrite != null || publishing || payments.Count != 0;
    internal static double LuoxiTurnover => Ready ? document!.LuoxiTurnover : 0;
    internal static bool SecurityReached25 => Ready && document!.SecurityReached25;
    internal static bool SecurityReached50 => Ready && document!.SecurityReached50;
    internal static bool LuoxiEligible(PlayerStore store) { Update(store); return Ready && LuoxiTurnover >= StoryContent.Number("luoxi.core.turnover", 5000); }
    internal static bool JiangbaiEligible(PlayerStore store) { Update(store); return Ready && SecurityReached50; }

    internal static void OnWorldChanged(PlayerStore store) { nextObservation = 0; Update(store); }

    internal static void Update(PlayerStore store)
    {
        try
        {
            ResolveWrite();
            if (!Bind(store)) return;
            DrainPayments();
            if (!Ready || Environment.TickCount64 < nextObservation) return;
            nextObservation = Environment.TickCount64 + 650;
            ObserveSecurity();
        }
        catch (Exception ex) { Warn(ex); }
    }

    // Call only after the quest's native payment has been read back. The stable
    // receipt makes dialogue retries harmless, including after a save/reload.
    internal static bool RecordLuoxiPayment(PlayerStore store, double amount, string receipt)
    {
        try
        {
            if (!Bind(store) || !ValidateRaw()) return false;
            if (document!.PaymentReceipts.Contains(receipt)) return true;
            // Validate before enqueuing; invalid requests cannot poison the queue.
            var candidate = NpcStoryProgressState.Clone(document);
            NpcStoryProgressState.AddPayment(candidate, amount, receipt);
            foreach (var pending in payments) if (pending.Receipt == receipt) { DrainPayments(); return Ready && document.PaymentReceipts.Contains(receipt); }
            payments.Enqueue(new Payment { Amount = amount, Receipt = receipt });
            DrainPayments();
            return Ready && document.PaymentReceipts.Contains(receipt);
        }
        catch (Exception ex) { Warn(ex); return false; }
        finally { NpcBusiness.Resume(); }
    }

    private static bool Bind(PlayerStore store)
    {
        if (!SaveGeneration.IsSupported(store) || !PlayerStore.IsInstanceExist() || PlayerStore.instance == null ||
            PlayerStore.instance.Pointer != store.Pointer) return false;
        if (SameOwner()) return Ready;
        if (publishing || afterWrite != null || payments.Count != 0 || trade != null) return false;
        owner = store; run = store.runID; slot = store.saveSlotId; document = null; error = null;
        present = HasKey(store); raw = Read(store);
        try { document = present ? NpcStoryProgressState.Parse(raw!, run, slot) : NpcStoryProgressState.New(run, slot); }
        catch { error = "invalid_data"; throw; }
        if (!present) Publish(document);
        nextObservation = 0;
        observed25 = document!.SecurityReached25; observed50 = document.SecurityReached50;
        return Ready;
    }

    private static void ObserveSecurity()
    {
        if (!ValidateRaw()) return;
        var faction = StoreReputation.GetSecFaction();
        if (faction == null) return;
        var reputation = faction.GetReputationExact();
        if (!double.IsFinite(reputation)) throw new InvalidOperationException("治安部声望无效。");
        observed25 |= reputation >= 25; observed50 |= reputation >= 50;
        if (observed25 != document!.SecurityReached25 || observed50 != document.SecurityReached50)
        {
            var next = NpcStoryProgressState.Clone(document);
            NpcStoryProgressState.ObserveSecurity(next, reputation);
            next.SecurityReached25 |= observed25; next.SecurityReached50 |= observed50;
            next.Revision = checked(next.Revision + 1); Publish(next);
        }
    }

    private static void DrainPayments()
    {
        ResolveWrite();
        while (payments.Count != 0 && ValidateRaw())
        {
            var payment = payments.Peek();
            if (payment.Candidate == null)
            {
                payment.Candidate = NpcStoryProgressState.Clone(document!);
                if (!NpcStoryProgressState.AddPayment(payment.Candidate, payment.Amount, payment.Receipt)) { payments.Dequeue(); continue; }
                payment.Candidate.Revision = checked(payment.Candidate.Revision + 1);
            }
            var candidateRaw = NpcStoryProgressState.Serialize(payment.Candidate);
            // Unknown publication may resolve to the after image on a later
            // frame. Do not add the same paid amount again in that case.
            if (raw == candidateRaw) { payments.Dequeue(); continue; }
            Publish(payment.Candidate);
            payments.Dequeue();
        }
    }

    internal static bool BeforeSave()
    {
        NpcBusiness.Resume();
        try
        {
            if (PlayerStore.IsInstanceExist() && PlayerStore.instance != null) Update(PlayerStore.instance);
            ResolveWrite(); DrainPayments();
            if (Ready) ObserveSecurity();
            return trade == null && payments.Count == 0 && !publishing && afterWrite == null &&
                (owner == null || !SameOwner() || error == null && MatchesRaw() && document != null &&
                    (!observed25 || document.SecurityReached25) && (!observed50 || document.SecurityReached50));
        }
        catch (Exception ex) { Warn(ex); return false; }
    }
    internal static bool AllowSessionChange() => BeforeSave();
    internal static void ResetSession()
    {
        if (publishing || afterWrite != null || payments.Count != 0 || trade != null) throw new InvalidOperationException("NPC进度成交发布未完成，不能切档。");
        owner = null; document = null; run = raw = error = beforeWrite = afterWrite = null;
        slot = 0; present = beforePresent = observed25 = observed50 = false; nextObservation = warningAt = 0;
    }

    private static void Publish(NpcStoryProgressState.Document next)
    {
        if (!SameOwner() || publishing || afterWrite != null || !MatchesRaw()) throw new InvalidOperationException("NPC进度发布前会话或原文变化。");
        var after = NpcStoryProgressState.Serialize(next);
        var parsed = NpcStoryProgressState.Parse(after, run!, slot);
        var previousRaw = raw;
        beforeWrite = raw; beforePresent = present; afterWrite = after; publishing = true;
        try
        {
            Exception? failure = null;
            try { owner!.modData ??= new Il2CppSystem.Collections.Generic.Dictionary<string, string>(); owner.modData[NpcStoryProgressState.SaveKey] = after; }
            catch (Exception ex) { failure = ex; }
            if (!SameOwner()) throw new InvalidOperationException("NPC进度发布时会话变化。", failure);
            var now = Read(owner!); var exists = HasKey(owner!);
            if (exists && now == after)
            { raw = after; present = true; document = parsed; afterWrite = beforeWrite = error = null; return; }
            if (exists == beforePresent && now == beforeWrite)
            { afterWrite = beforeWrite = null; throw new InvalidOperationException("NPC进度发布未生效。", failure); }
            error = "unknown_publication";
            throw new InvalidOperationException("NPC进度发布结果未知，保留凭据阻止保存或切档。", failure);
        }
        finally { publishing = false; if (raw != previousRaw) BusinessRuntime.Wake(); }
    }
    private static void ResolveWrite()
    {
        if (publishing || afterWrite == null || !SameOwner()) return;
        try
        {
            var now = Read(owner!); var exists = HasKey(owner!);
            if (exists && now == afterWrite || exists == beforePresent && now == beforeWrite)
            {
                document = exists ? NpcStoryProgressState.Parse(now!, run!, slot) : NpcStoryProgressState.New(run!, slot);
                raw = now; present = exists; afterWrite = beforeWrite = error = null;
                BusinessRuntime.Wake();
            }
        }
        catch { /* Keep before/after proof until readback is unambiguous. */ }
    }
    private static bool SameOwner() => owner != null && PlayerStore.IsInstanceExist() && PlayerStore.instance != null &&
        owner.Pointer == PlayerStore.instance.Pointer && owner.runID == run && owner.saveSlotId == slot && SaveGeneration.IsSupported(owner);
    private static bool HasKey(PlayerStore store) => store.modData != null && store.modData.ContainsKey(NpcStoryProgressState.SaveKey);
    private static string? Read(PlayerStore store) => HasKey(store) ? store.modData![NpcStoryProgressState.SaveKey] : null;
    internal static bool ValidateRaw() { try { return Ready && MatchesRaw(); } catch { return false; } }
    private static bool MatchesRaw() => owner != null && HasKey(owner) == present && Read(owner) == raw;
    private static bool IsBusiness(StoreClient? client) => PhoneAssemblerNpc.IsPhoneAssembler(client) && !LuoxiStory.IsStory(client);
    private static void Warn(Exception ex)
    { if (Environment.TickCount64 < warningAt) return; warningAt = Environment.TickCount64 + 5000; Core.Log?.Warning("NPC剧情进度暂未确认：" + ex); }

    private static void Callback(GameItem item, int amount, bool buy, StoreClient? client)
    {
        var frame = trade;
        if (frame == null || item == null || frame.Buy != buy || client == null || frame.Client.Pointer != client.Pointer) return;
        if (frame.Callback && (frame.Item != item.Pointer || frame.Amount != amount)) frame.Conflict = true;
        frame.Callback = true; frame.Item = item.Pointer; frame.Amount = amount;
    }

    [HarmonyPatch(typeof(PlayerStore))]
    internal static class NativeTradePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.BuyItem));
            yield return AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.BuyItemFromTable));
            yield return AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.SellItem));
        }
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(PlayerStore __instance, MethodBase __originalMethod, out Trade? __state)
        {
            __state = null;
            try
            {
                var client = __instance.currentClientInstance?.storeClient;
                if (!IsBusiness(client)) return true;
                Update(__instance);
                LuoxiStory.Update(__instance);
                if (!LuoxiStory.ValidateRaw()) return false;
                if (!ValidateRaw() || payments.Count != 0) return false;
                __state = new Trade { Store = __instance, Client = client!, Run = __instance.runID, Slot = __instance.saveSlotId,
                    Cash = __instance.GetCash(), Buy = __originalMethod.Name != nameof(PlayerStore.SellItem), Parent = trade };
                trade = __state;
                return true;
            }
            catch (Exception ex) { Warn(ex); return false; }
        }
        private static Exception? Finalizer(Exception? __exception, Trade? __state)
        {
            if (__state == null) return __exception;
            var frame = __state;
            try
            {
                if (trade != frame || !SameOwner() || frame.Store.Pointer != owner!.Pointer || frame.Run != run || frame.Slot != slot)
                    throw new InvalidOperationException("洛夕成交回读时会话或嵌套调用变化。");
                var delta = (long)frame.Store.GetCash() - frame.Cash;
                if (frame.Parent != null) frame.Parent.ChildrenDelta += delta;
                var ownDelta = delta - frame.ChildrenDelta;
                var paid = frame.Buy ? -ownDelta : ownDelta;
                if (frame.Callback && !frame.Conflict && frame.Amount > 0 && paid == frame.Amount)
                { payments.Enqueue(new Payment { Amount = frame.Amount }); DrainPayments(); }
                else if (ownDelta != 0) throw new InvalidOperationException("洛夕资金变动与原生成交凭据不一致，未推测成交额。");
            }
            catch (Exception ex) { if (payments.Count == 0) error ??= "unconfirmed_trade"; Warn(ex); }
            finally { trade = frame.Parent; BusinessRuntime.Wake(); }
            return __exception;
        }
    }
    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.OnItemBought), new[] { typeof(GameItem), typeof(int) })]
    internal static class BoughtAmountPatch
    {
        private static void Prefix(PlayerStore __instance, GameItem gameItem, int cost)
        { if (trade?.Store.Pointer == __instance.Pointer) Callback(gameItem, cost, true, __instance.currentClientInstance?.storeClient); }
    }
    [HarmonyPatch(typeof(StoreClient), nameof(StoreClient.OnItemBought), new[] { typeof(GameItem), typeof(int) })]
    internal static class SoldAmountPatch
    {
        private static void Prefix(StoreClient __instance, GameItem __0, int __1) => Callback(__0, __1, false, __instance);
    }
    [HarmonyPatch(typeof(StoreReputation))]
    internal static class SecurityThresholdPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // The string Raw overload mutates the faction directly rather than
            // calling the instance-double overload. Cover both native routes.
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(StoreReputation)))
                if (method.Name is nameof(StoreReputation.ModReputation) or nameof(StoreReputation.ModReputationRaw)) yield return method;
        }
        private static void Observe()
        {
            try { var store = PlayerStore.Instance; if (store != null && Bind(store)) ObserveSecurity(); }
            catch (Exception ex) { Warn(ex); }
        }
        private static void Prefix() => Observe();
        private static Exception? Finalizer(Exception? __exception) { Observe(); BusinessRuntime.Wake(); return __exception; }
    }
}
