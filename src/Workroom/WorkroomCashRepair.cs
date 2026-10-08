using System;
using System.Collections.Generic;
using Il2Cpp;

namespace PCExpansion;

/// <summary>现金维修的单次扣款凭据；异常后按实测余额补偿，恢复前禁止再次处理和保存。</summary>
internal static class WorkroomCashRepair
{
    private sealed class Receipt
    {
        internal PlayerStore Owner = null!;
        internal IntPtr Pointer;
        internal string? Run;
        internal int Slot, Before, Fee;
        internal bool Recovery;
    }
    // Retain unmatched receipts without applying their balance to another save.
    // Only a receipt proven to belong to the active session blocks that session.
    private static readonly List<Receipt> receipts = new();
    private static long retryAt;
    internal static bool Pending
    {
        get
        {
            if (receipts.Count == 0) return false;
            try
            {
                foreach (var receipt in receipts) if (SameOwner(receipt)) return true;
                return false;
            }
            catch { return true; } // Unknown ownership must not permit another debit/save.
        }
    }

    internal static bool AllowSessionChange()
    {
        foreach (var receipt in receipts.ToArray()) if (receipt.Recovery) Recover(receipt);
        if (!Pending) return true;
        Core.Log?.Warning("当前存档现金维修仍待恢复，暂不切档；原凭据保留，稍后重试。");
        return false;
    }

    internal static void Commit(PlayerStore owner, int fee, Action requireCurrent, Action publish)
    {
        if (Pending) throw new InvalidOperationException("此前现金维修仍待恢复。");
        requireCurrent();
        var before = owner.GetCash();
        if (fee < 0 || before < fee) throw new InvalidOperationException("现金不足或费用无效。");
        if (fee == 0) { requireCurrent(); publish(); return; }
        var receipt = new Receipt { Owner = owner, Pointer = owner.Pointer, Run = owner.runID,
            Slot = owner.saveSlotId, Before = before, Fee = fee };
        receipts.Add(receipt);
        try
        {
            // Native calls may throw after changing cash. The receipt is installed
            // before calling, and recovery reads the balance rather than assuming.
            owner.PayMoney(fee, false);
            if (!SameOwner(receipt) || owner.GetCash() != before - fee)
                throw new InvalidOperationException("扣款后余额或存档上下文不符合预期。");
            requireCurrent();
            publish();
            receipts.Remove(receipt);
        }
        catch
        {
            receipt.Recovery = true;
            Recover(receipt);
            throw;
        }
    }

    internal static void RetryRecovery()
    {
        if (receipts.Count == 0 || Environment.TickCount64 < retryAt) return;
        retryAt = Environment.TickCount64 + 500;
        foreach (var receipt in receipts.ToArray()) if (receipt.Recovery) Recover(receipt);
    }

    private static bool SameOwner(Receipt receipt) => PlayerStore.IsInstanceExist() && PlayerStore.instance != null &&
        PlayerStore.instance.Pointer == receipt.Pointer && PlayerStore.instance.runID == receipt.Run &&
        PlayerStore.instance.saveSlotId == receipt.Slot && receipt.Owner.Pointer == receipt.Pointer &&
        receipt.Owner.runID == receipt.Run && receipt.Owner.saveSlotId == receipt.Slot;

    private static void Recover(Receipt receipt)
    {
        try
        {
            if (!SameOwner(receipt)) return;
            var current = receipt.Owner.GetCash();
            if (current != receipt.Before)
            {
                if (current < receipt.Before - receipt.Fee || current > receipt.Before)
                    throw new InvalidOperationException("余额超出本次扣款范围，无法自动补偿其他现金变化。");
                var adjustment = checked(receipt.Before - current);
                try { receipt.Owner.ModCash(adjustment, false); }
                catch (Exception ex)
                {
                    // A refund can also throw after applying. Confirm the balance
                    // before treating that exception as an outstanding refund.
                    if (!SameOwner(receipt) || receipt.Owner.GetCash() != receipt.Before) throw;
                    Core.Log?.Warning("现金退款已到账，但原版接口报告异常：" + ex.Message);
                }
            }
            if (!SameOwner(receipt) || receipt.Owner.GetCash() != receipt.Before)
                throw new InvalidOperationException("现金退款尚未到账。");
            receipts.Remove(receipt);
        }
        catch (Exception ex)
        {
            retryAt = Environment.TickCount64 + 500;
            Core.Log?.Error("现金维修恢复未完成，保留凭据并保护处理及保存：" + ex.Message);
        }
    }
}
