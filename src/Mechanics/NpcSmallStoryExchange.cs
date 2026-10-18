using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;

namespace PCExpansion;

/// <summary>One-item purchases and one-time gifts. Native ownership and cash are read back before publication.</summary>
internal static class NpcSmallStoryExchange
{
    private static bool executing;
    private static long retryAt, warningAt;

    internal static bool Pending
    {
        get
        {
            if (executing) return true;
            try { return NpcSmallStories.Ready && NpcSmallStories.Data.Nodes.Values.Any(n => n.Status == "paying"); }
            catch { return true; }
        }
    }

    // A selected original can still be absent before any removal happened. Keep
    // its old save semantics, while ensuring consuming also receives recovery.
    internal static bool RecoveryPending
    {
        get
        {
            if (executing) return true;
            try { return NpcSmallStories.Ready && NpcSmallStories.Data.Nodes.Values.Any(n => n.Status is "consuming" or "paying"); }
            catch { return true; }
        }
    }

    private sealed class Session
    {
        internal readonly PlayerStore Store;
        private readonly IntPtr pointer;
        private readonly string run;
        private readonly int slot;
        internal Session()
        {
            Store = NpcSmallStories.Store ?? throw new InvalidOperationException("小剧情店铺不可用。");
            pointer = Store.Pointer; run = Store.runID; slot = Store.saveSlotId;
            Require();
        }
        internal void Require()
        {
            var current = NpcSmallStories.Store;
            if (!NpcSmallStories.ValidateRaw() || current == null || current.Pointer != pointer ||
                current.runID != run || current.saveSlotId != slot || !PlayerStore.IsInstanceExist() ||
                PlayerStore.instance == null || PlayerStore.instance.Pointer != pointer ||
                !WorkroomStorage.State.Ready || !WorkroomStorage.State.SameOwner())
                throw new InvalidOperationException("小剧情交易会话变化，保留原凭据。");
        }
    }

    internal static string HandIn(string node)
    {
        if (node is not ("L2" or "J2")) throw new ArgumentException("Unknown small-story purchase.", nameof(node));
        if (executing || !NpcSmallStories.Ready || WorkroomStoryTransfer.Pending) return node + "-321";
        executing = true;
        try
        {
            var session = new Session();
            var state = NpcSmallStories.Data.Nodes[node];
            if (state.CompletedDay >= 0) return Repeat(node);
            if (state.Status == "accepted")
            {
                var item = state.Item ?? Select(session, node);
                if (item == null) return node + "-320";
                NpcSmallStories.Change(d => { d.Nodes[node].Item = item; d.Nodes[node].Status = "consuming"; });
                session.Require();
            }
            if (NpcSmallStories.Data.Nodes[node].Status == "consuming") Consume(session, node);
            if (!WorkroomStoryTransfer.Pending && NpcSmallStories.Data.Nodes[node].Status == "paying") Pay(session, node);
            state = NpcSmallStories.Data.Nodes[node];
            if (node == "L2" && state.Status is "paid" or "gift_pending") Grant(session, node);
            session.Require();
            state = NpcSmallStories.Data.Nodes[node];
            if (node == "L2" && state.Status == "gift_pending") return "L2-paid-pending";
            if (state.Status is "paid" or "done") return node + "-310";
            return node + "-321";
        }
        catch (Exception ex)
        {
            Warn(ex);
            if (node == "L2" && NpcSmallStories.Ready && NpcSmallStories.Data.Nodes[node].Status is "paid" or "gift_pending")
                return "L2-paid-pending";
            return node + "-321";
        }
        finally { executing = false; BusinessRuntime.Wake(); }
    }

    internal static string Claim(string node, bool first)
    {
        if (node is not ("L2" or "L4")) throw new ArgumentException("Unknown small-story gift.", nameof(node));
        string success = node == "L2" ? "L2-332" : first ? "L4-102" : "L4-303";
        string waiting = node == "L2" ? "L2-333" : first ? "L4-301" : "L4-304";
        if (executing || !NpcSmallStories.Ready || WorkroomStoryTransfer.Pending) return waiting;
        executing = true;
        try
        {
            var session = new Session();
            var state = NpcSmallStories.Data.Nodes[node];
            if (state.CompletedDay >= 0) return Repeat(node);
            if (state.Status == "done") return success;
            if (node == "L4" && state.Status == "new" || node == "L2" && state.Status is "paid" or "gift_pending" ||
                node == "L4" && state.Status == "gift_pending") Grant(session, node);
            session.Require();
            return NpcSmallStories.Data.Nodes[node].Status == "done" ? success : waiting;
        }
        catch (Exception ex) { Warn(ex); return waiting; }
        finally { executing = false; BusinessRuntime.Wake(); }
    }

    private static string Repeat(string node) => NpcSmallStories.Data.Nodes[node].Status == "refused" ? node + "-202" :
        node == "J2" ? "J2-903" : node + "-901";
    private static string Identifier(string node) => node == "L2" ? "pcrepair.component_psu_t1" : "pcrepair.component_fan_t1";
    private static int Price(string node) => node == "L2" ? 75 : 30;

    private static bool Eligible(WorkroomItemCodec.Snapshot item, string node) =>
        item.Identifier == Identifier(node) && item.Count == 1 && !item.Broken && item.Nodes.Count != 0 &&
        item.Nodes.All(n => n.Machine == null) && item.Footprint.Cells.All(cell => cell != 0);

    private static WorkroomItemCodec.Snapshot? Select(Session session, string node)
    {
        // Filter top-level identifier/ownership before the sole full Capture. Never traverse a machine for a quest part.
        var seen = new HashSet<IntPtr>();
        foreach (var grid in WorkroomStorage.Sources())
        foreach (var item in grid.childItems)
        {
            if (item == null || Core.Clean(item.identifier) != Identifier(node) || item.unitCount != 1 ||
                !seen.Add(item.Pointer) || !WorkroomStorage.Contains(grid, item) || !GeneralHelper.IsItemOwned(item) ||
                item.IsTag(Components.BrokenTag) || item.IsTag("BROKEN_MACHINE")) continue;
            session.Require();
            var snapshot = WorkroomItemCodec.Capture(item);
            session.Require();
            WorkroomItemCodec.Validate(snapshot);
            if (Eligible(snapshot, node) && WorkroomStorage.Contains(grid, item) && GeneralHelper.IsItemOwned(item) &&
                WorkroomStoryIdentity.Matches(snapshot, item)) return snapshot;
        }
        foreach (var record in WorkroomStorage.State.Items)
            if (record.Place is "box" or "room" && Eligible(record.Item, node))
            { WorkroomItemCodec.Validate(record.Item); session.Require(); return record.Item; }
        return null;
    }

    private static void Consume(Session session, string node)
    {
        session.Require();
        var state = NpcSmallStories.Data.Nodes[node];
        var item = state.Item ?? throw new InvalidOperationException("已锁定的委托货物凭据缺失。");
        if (!Eligible(item, node)) throw new InvalidOperationException("委托货物合同不符。");
        WorkroomStoryTransfer.TryConsumeStoryBatch(new[] { item }, () =>
        {
            session.Require();
            var current = NpcSmallStories.Data.Nodes[node];
            if (current.Item == null ||
                current.Item.Nodes[0].UniqueId != item.Nodes[0].UniqueId)
                throw new InvalidOperationException("委托扣物回调与锁定货物不一致。");
            if (current.Status == "paying") return; // Callback publication may have applied before its setter threw.
            if (current.Status != "consuming") throw new InvalidOperationException("委托扣物阶段变化。");
            // This callback runs only after removal, while the transfer's own save barrier remains installed.
            int before = session.Store.GetCash();
            int target = checked(before + Price(node));
            NpcSmallStories.Change(d =>
            {
                var next = d.Nodes[node]; next.CashBefore = before; next.CashTarget = target; next.Status = "paying";
            });
            session.Require();
            if (NpcSmallStories.Data.Nodes[node].Status != "paying")
                throw new InvalidOperationException("委托扣物完成凭据尚未发布。");
        }, out _);
    }

    private static void Pay(Session session, string node)
    {
        session.Require();
        var state = NpcSmallStories.Data.Nodes[node];
        if (state.Status != "paying") return;
        int before = state.CashBefore, target = state.CashTarget;
        if ((long)target - before != Price(node)) throw new InvalidOperationException("委托付款凭据金额无效。");
        int cash = session.Store.GetCash();
        if (cash == before)
        {
            try { session.Store.ModCash(Price(node), false); }
            catch (Exception ex)
            {
                session.Require();
                if (session.Store.GetCash() != target) throw;
                Core.Log?.Warning("委托货款已到账，原版回调报告异常：" + ex.Message);
            }
        }
        else if (cash != target) throw new InvalidOperationException("委托付款期间余额变化，禁止覆盖其他现金。");
        session.Require();
        if (session.Store.GetCash() != target) throw new InvalidOperationException("委托货款未确认，保留付款凭据。");
        if (node == "L2" && !NpcStoryProgress.RecordLuoxiPayment(session.Store, 75, "smallstory.L2.payment"))
            throw new InvalidOperationException("委托实际成交收据尚未确认。");
        session.Require();
        NpcSmallStories.Change(d => d.Nodes[node].Status = "paid");
        session.Require();
    }

    private static void Grant(Session session, string node)
    {
        session.Require();
        var state = NpcSmallStories.Data.Nodes[node];
        if (state.Status == "done") return;
        if (node == "L2" && state.Status is not ("paid" or "gift_pending") ||
            node == "L4" && state.Status is not ("new" or "gift_pending")) return;
        if (state.Gifts.Count == 0)
        {
            var gifts = new List<WorkroomItemCodec.Snapshot>();
            foreach (var id in new[] { "common_electronic", "wire" })
            {
                var snapshot = WorkroomComponentParts.CaptureFactory(() =>
                {
                    var native = DirectoryMaster.Item(id, true) ?? throw new InvalidOperationException("剧情材料工厂缺失：" + id);
                    try { GeneralHelper.SetItemOwned(native, true); return native; }
                    catch
                    {
                        WorkroomItemCodec.Destroy(new WorkroomItemCodec.Candidate { Root = native, Nodes = new List<GameItem> { native } });
                        throw;
                    }
                });
                WorkroomItemCodec.Validate(snapshot);
                if (snapshot.Identifier != id || snapshot.Count != 1 || snapshot.Broken || snapshot.Nodes.Count != 1 ||
                    snapshot.Nodes[0].Composition != null || snapshot.Nodes[0].SlotItems.Count != 0 ||
                    !snapshot.Footprint.Cells.All(cell => cell != 0))
                    throw new InvalidOperationException("剧情材料工厂结果不符合单件矩形合同。");
                gifts.Add(snapshot);
            }
            session.Require();
            NpcSmallStories.Change(d => { d.Nodes[node].Gifts = gifts; d.Nodes[node].Status = "gift_pending"; });
            session.Require();
        }
        else if (state.Status != "gift_pending")
        {
            NpcSmallStories.Change(d => d.Nodes[node].Status = "gift_pending");
            session.Require();
        }
        var batch = NpcSmallStories.Data.Nodes[node].Gifts;
        if (batch.Count != 2 || batch[0].Identifier != "common_electronic" || batch[1].Identifier != "wire")
            throw new InvalidOperationException("剧情材料奖励凭据不完整，禁止重建。");
        WorkroomStoryTransfer.TryGrantStoryBatch(batch, () =>
        {
            session.Require();
            var current = NpcSmallStories.Data.Nodes[node];
            if (current.Gifts.Count != batch.Count ||
                !current.Gifts.Select(s => s.Nodes[0].UniqueId).SequenceEqual(batch.Select(s => s.Nodes[0].UniqueId)))
                throw new InvalidOperationException("剧情奖励回调与持久货单不一致。");
            if (current.Status == "done") return;
            if (current.Status != "gift_pending") throw new InvalidOperationException("剧情奖励发放阶段变化。");
            NpcSmallStories.Change(d => d.Nodes[node].Status = "done");
            session.Require();
        }, out _);
    }

    internal static void RetryRecovery()
    {
        if (executing || !RecoveryPending || !NpcSmallStories.ValidateRaw() || WorkroomStoryTransfer.Pending || Environment.TickCount64 < retryAt) return;
        retryAt = Environment.TickCount64 + 500;
        executing = true;
        try
        {
            var session = new Session();
            foreach (var node in new[] { "L2", "J2" })
            {
                if (NpcSmallStories.Data.Nodes[node].Status == "consuming") Consume(session, node);
                if (WorkroomStoryTransfer.Pending) return;
                if (NpcSmallStories.Data.Nodes[node].Status == "paying") Pay(session, node);
            }
            // Space-limited gifts are retried only by a player choice, not every half second.
        }
        catch (Exception ex) { Warn(ex); }
        finally { executing = false; }
    }

    internal static void ResetSession()
    {
        if (Pending) throw new InvalidOperationException("小剧情付款尚未确认，不能切换存档。");
        retryAt = warningAt = 0;
    }
    private static void Warn(Exception ex)
    {
        if (Environment.TickCount64 < warningAt) return;
        warningAt = Environment.TickCount64 + 2000;
        Core.Log?.Warning("小剧情交接保留凭据等待恢复：" + ex.Message);
    }
}
