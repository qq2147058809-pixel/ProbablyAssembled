using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>以原版成功跨日为唯一经营时钟；同一会话、同一日只发布一笔日结。</summary>
internal static class ServerRoomDaily
{
    private sealed class NightTicket
    {
        internal IntPtr Owner;
        internal string Run = "";
        internal int Slot, FromDay;
        internal long Revision, StorageEpoch;
        internal IReadOnlyList<ServerRoomEconomy.MachineFact> Machines = Array.Empty<ServerRoomEconomy.MachineFact>();
        internal bool EndNightReturned, BeginDayEntered;
        internal ServerRoomEconomy.DayResult? Plan;
        internal int CashBeforeCommit;
    }

    private static NightTicket? ticket;
    private static string? fault;
    private static bool sleepRequested;
    private static long nextNotice, nextInitializationWarning;
    internal static bool AllowSave => !sleepRequested && ticket == null && fault == null;
    internal static bool AllowCommands => !sleepRequested && ticket == null && fault == null;
    internal static bool AllowSessionChange => fault != null || (!sleepRequested && ticket == null);

    internal static void ResetSession()
    {
        ticket = null;
        fault = null;
        sleepRequested = false;
    }

    internal static void Tick()
    {
        if (ticket?.Plan != null) TryCommitPlan();
        if (fault != null || ticket != null || sleepRequested || !ServerRoomRuntime.Ready ||
            ServerRoomRuntime.Current is not { } current || StoreStation.Instance == null ||
            current.Market.Initialized) return;
        try
        {
            var day = StoreStation.GetDayCounter();
            var next = ServerRoomState.Advance(current);
            var boom = Boom(out var identity);
            next.Market = ServerRoomMarket.Initialize(current.Market, current.Run, current.Slot, day, boom, identity);
            ServerRoomRuntime.Commit(new ServerRoomState.Change(next, 0), null);
        }
        catch (Exception ex)
        {
            if (Environment.TickCount64 >= nextInitializationWarning)
            {
                nextInitializationWarning = Environment.TickCount64 + 5000;
                Core.Log?.Warning("首次币市报价尚未发布，将在事务清理后重试：" + ex.Message);
            }
        }
    }

    internal static void RequireToday(ServerRoomState.Document current)
    {
        if (fault != null || ticket != null || sleepRequested || !current.Enabled || !current.Market.Initialized ||
            StoreStation.Instance == null || current.Market.QuoteDay != StoreStation.GetDayCounter())
            throw new InvalidOperationException("今日币市尚未就绪，暂停交易。");
    }

    private static bool Boom(out string identity)
    {
        identity = "";
        var manager = StoreStation.Instance?.storeEventManager;
        if (manager == null) throw new InvalidOperationException("原版事件管理器尚未就绪。");
        if (!manager.IsEventActive(MarketEvents.CryptoId)) return false;
        foreach (var current in manager.GetActiveEvents())
        {
            if (current == null || Core.Clean(current.identifier) != MarketEvents.CryptoId) continue;
            identity = MarketEvents.CryptoId + ":" + current.startDay;
            return true;
        }
        throw new InvalidOperationException("暴涨事件已激活，但缺少可验证的事件记录。");
    }

    private static bool Same(NightTicket value, PlayerStore store) =>
        store != null && store.Pointer == value.Owner && store.runID == value.Run && store.saveSlotId == value.Slot;

    private static bool AssetsPending() => WorkroomStorage.Busy || WorkroomItemCodec.CleanupPending ||
        WorkroomStorage.State.PendingRecords.Count != 0 || WorkroomCashRepair.Pending ||
        ComputerSupplierNpcs.StockPending || NpcStockOffers.Pending || !ServerRoomOrderStock.BeforeSave();

    private static void CompletePlan()
    {
        if (ticket?.Plan is { } result)
        {
            Core.Log?.Msg($"[机房日结] 第{ticket.FromDay}→{ticket.FromDay + 1}日；费用{result.Fees}；" +
                $"运行矿机{result.PaidRecords.Count}；欠费跳过{result.SkippedRecords.Count}；" +
                $"配置不可运行{result.UnavailableRecords.Count}；到期租约{result.ExpiredContracts.Count}。");
            RoomAudio.DailyNotice(result.SkippedRecords.Count > 0, result.ExpiredContracts.Count > 0);
        }
        ticket = null;
        sleepRequested = false;
    }

    private static void TryCommitPlan()
    {
        var value = ticket;
        if (value?.Plan == null || fault != null) return;
        try
        {
            var store = PlayerStore.Instance;
            if (store == null || !Same(value, store) || StoreStation.GetDayCounter() != value.FromDay + 1 ||
                WorkroomStorage.State.Epoch != value.StorageEpoch)
                throw new InvalidOperationException("延后日结的会话或日期已变化。");
            if (ServerRoomRuntime.Pending || AssetsPending() || !ServerRoomRuntime.Ready) return;
            var current = ServerRoomRuntime.Current ?? throw new InvalidOperationException("机房日结来源丢失。");
            if (current.Revision == value.Plan.Change.Next.Revision)
            {
                if (ServerRoomState.Serialize(current) != ServerRoomState.Serialize(value.Plan.Change.Next) ||
                    store.GetCash() != value.Plan.CashAfter)
                    throw new InvalidOperationException("事务恢复结果与冻结日结计划不符。");
                CompletePlan();
                return;
            }
            if (current.Revision != value.Revision || store.GetCash() != value.CashBeforeCommit)
                throw new InvalidOperationException("延后日结前现金或机房版本发生变化。");
            ServerRoomRuntime.Commit(value.Plan.Change, null);
            if (ServerRoomRuntime.Current is not { } after ||
                ServerRoomState.Serialize(after) != ServerRoomState.Serialize(value.Plan.Change.Next) ||
                store.GetCash() != value.Plan.CashAfter)
                throw new InvalidOperationException("冻结日结提交后未达到目标状态。");
            CompletePlan();
        }
        catch (Exception ex)
        {
            if (!ServerRoomRuntime.Pending) Fault("冻结日结提交失败：" + ex);
        }
    }

    private static void Fault(string message)
    {
        fault = message;
        Core.Log?.Error("机房跨日结算暂停保存与交易：" + message);
        if (Environment.TickCount64 < nextNotice) return;
        nextNotice = Environment.TickCount64 + 4000;
        try { StoreUIManager.Instance?.Notify("机房结算异常，已暂停保存和交易；请查看日志。", "#FFFFFF"); }
        catch { /* Log is the durable diagnostic if native UI is unavailable. */ }
    }

    private static bool CanBeginSleep()
    {
        var store = PlayerStore.Instance;
        // Preserve short-circuit order: AssetsPending also prepares order stock.
        string? reason;
        if (store == null) reason = "no-player";
        else if (!SaveGeneration.IsSupported(store)) reason = "unsupported-save";
        else if (StoreStation.Instance?.storeEventManager == null) reason = "no-event-manager";
        else if (ticket != null) reason = "ticket-pending";
        else if (fault != null) reason = "settlement-fault";
        else if (!ServerRoomRuntime.Ready) reason = "runtime-not-ready";
        else if (ServerRoomRuntime.Current is not { } current) reason = "no-current-document";
        else if (!current.Market.Initialized) reason = "market-not-initialized";
        else if (current.Market.QuoteDay != StoreStation.GetDayCounter()) reason = "quote-day-mismatch";
        else if (AssetsPending()) reason = "assets-pending";
        else reason = null;
        if (reason != null)
        {
            PlayerHealthDiagnostics.SleepRejected(reason);
            try { StoreUIManager.Instance?.Notify("机房资料或资产事务未就绪，请稍后再休息。", "#FFFFFF"); }
            catch { }
            return false;
        }
        return true;
    }

    [HarmonyPatch(typeof(EndOfDayUIManager), nameof(EndOfDayUIManager.OnEndDayButtonClick))]
    internal static class SleepAdmissionPatch
    {
        [HarmonyPriority(Priority.First + 200)]
        private static bool Prefix()
        {
            if (sleepRequested)
            {
                PlayerHealthDiagnostics.SleepRejected("already-requested");
                return false;
            }
            if (!CanBeginSleep()) return false;
            PlayerHealthDiagnostics.SleepAccepted();
            sleepRequested = true;
            return true;
        }

        private static Exception? Finalizer(Exception? __exception)
        {
            if (__exception != null && sleepRequested) Fault("原版休息入口异常：" + __exception.Message);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.EndNight))]
    internal static class EndNightPatch
    {
        [HarmonyPriority(Priority.First + 200)]
        private static void Prefix(PlayerStore __instance)
        {
            if (!SaveGeneration.IsSupported(__instance)) return;
            try
            {
                if (!CanBeginSleep()) throw new InvalidOperationException("睡前经营状态不可用。");
                var current = ServerRoomRuntime.Current!;
                ticket = new NightTicket
                {
                    Owner = __instance.Pointer, Run = __instance.runID, Slot = __instance.saveSlotId,
                    FromDay = StoreStation.GetDayCounter(), Revision = current.Revision,
                    StorageEpoch = WorkroomStorage.State.Epoch,
                    Machines = ServerRoomOperations.MachineFacts(current)
                };
            }
            catch (Exception ex) { Fault("睡前快照失败：" + ex.Message); }
        }

        private static void Postfix(PlayerStore __instance)
        {
            if (ticket != null && Same(ticket, __instance)) ticket.EndNightReturned = true;
        }

        private static Exception? Finalizer(Exception? __exception)
        {
            if (__exception != null && ticket != null) Fault("原版夜晚处理异常：" + __exception.Message);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.BeginDay))]
    internal static class BeginDayPatch
    {
        [HarmonyPriority(Priority.First + 200)]
        private static void Prefix(PlayerStore __instance)
        {
            if (sleepRequested && ticket == null) Fault("休息已开始，但缺少完成的夜晚凭据。");
            if (ticket == null) return;
            if (!Same(ticket, __instance) || !ticket.EndNightReturned ||
                StoreStation.GetDayCounter() != ticket.FromDay)
            { Fault("原版跨日顺序或会话与睡前凭据不一致。"); return; }
            ticket.BeginDayEntered = true;
        }

        private static void Postfix(PlayerStore __instance)
        {
            if (ticket == null || fault != null) return;
            try
            {
                var value = ticket;
                if (!Same(value, __instance) || !value.BeginDayEntered ||
                    StoreStation.GetDayCounter() != checked(value.FromDay + 1) ||
                    ServerRoomRuntime.Current is not { } current || current.Revision != value.Revision ||
                    current.Market.QuoteDay != value.FromDay || WorkroomStorage.State.Epoch != value.StorageEpoch)
                    throw new InvalidOperationException("新日、机房版本或睡前配置发生变化。");
                var boom = Boom(out var identity);
                var market = ServerRoomMarket.Advance(current.Market, current.Run, current.Slot,
                    StoreStation.GetDayCounter(), boom, identity);
                var result = ServerRoomEconomy.PrepareDay(current, value.Machines, value.FromDay,
                    StoreStation.GetDayCounter(), __instance.GetCash(), market);
                value.Plan = result;
                value.CashBeforeCommit = __instance.GetCash();
                TryCommitPlan();
            }
            catch (Exception ex) { Fault("新日结算失败：" + ex); }
        }

        private static Exception? Finalizer(Exception? __exception)
        {
            if (__exception != null && ticket != null) Fault("原版新日处理异常：" + __exception.Message);
            return __exception;
        }
    }
}
