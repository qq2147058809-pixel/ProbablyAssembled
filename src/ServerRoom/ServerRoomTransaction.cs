using System;
using System.Security.Cryptography;
using System.Text;

namespace PCExpansion;

/// <summary>纯事务凭据与恢复决策；不调用现金、Unity 或任何存档写入。</summary>
internal static class ServerRoomTransaction
{
    internal enum Decision
    {
        // Both documents and cash are still before. Native candidate cleanup remains caller-owned.
        NoChange,
        // Cash is after; publish the exact prepared candidate, including either missing half.
        PublishCandidate,
        // Cash is before; restore document metadata only when no external delivery was confirmed.
        RestoreCandidate,
        // Both documents and cash are after. Caller's native custody proof is still required.
        Complete,
        // Unknown cash, context, document contents or irreversible delivery: retain the receipt.
        Blocked
    }

    internal sealed class Receipt
    {
        internal string Id { get; }
        internal ServerRoomState.Binding Binding { get; }
        internal int CashBefore { get; }
        internal int CashDelta { get; }
        internal int CashAfter { get; }
        internal string? ModuleBefore { get; }
        internal string ModuleAfter { get; }
        internal string? StorageBefore { get; }
        internal string? StorageAfter { get; }
        internal string ModuleBeforeFingerprint { get; }
        internal string ModuleAfterFingerprint { get; }
        internal string StorageBeforeFingerprint { get; }
        internal string StorageAfterFingerprint { get; }
        internal long AfterRevision { get; }

        internal Receipt(ServerRoomState.Binding binding, string id, int before, int delta,
            string? moduleBefore, string moduleAfter, string? storageBefore, string? storageAfter, long afterRevision)
        {
            Binding = binding; Id = id; CashBefore = before; CashDelta = delta; CashAfter = checked(before + delta);
            ModuleBefore = moduleBefore; ModuleAfter = moduleAfter; StorageBefore = storageBefore; StorageAfter = storageAfter;
            ModuleBeforeFingerprint = Fingerprint(moduleBefore); ModuleAfterFingerprint = Fingerprint(moduleAfter);
            StorageBeforeFingerprint = Fingerprint(storageBefore); StorageAfterFingerprint = Fingerprint(storageAfter);
            AfterRevision = afterRevision;
        }
    }

    internal static Receipt Prepare(ServerRoomState.Binding binding, string id, int cashBefore, int delta,
        string? moduleBefore, string moduleAfter, string? storageBefore, string? storageAfter)
    {
        if (binding == null || string.IsNullOrWhiteSpace(id) || id.Length > 256 || cashBefore < 0)
            throw new InvalidOperationException("机房事务凭据无效。");
        var expected = checked(cashBefore + delta);
        if (expected < 0) throw new InvalidOperationException("现金不足或目标余额无效。");
        var before = ServerRoomState.Load(moduleBefore != null, moduleBefore, binding.Run, binding.Slot);
        if ((before?.Revision ?? 0) != binding.Revision)
            throw new InvalidOperationException("机房原记录版本已变化。");
        var after = ServerRoomState.Parse(moduleAfter, binding.Run, binding.Slot);
        if (after.Revision != checked(binding.Revision + 1))
            throw new InvalidOperationException("机房候选必须只前进一个版本。");
        // Opaque storage documents are validated by the sole WorkroomStorage owner before this call.
        // This layer keeps their exact raw strings; two assignments are never assumed atomic.
        return new Receipt(binding, id, cashBefore, delta, moduleBefore, moduleAfter, storageBefore, storageAfter, after.Revision);
    }

    internal static void RequireBefore(Receipt receipt, ServerRoomState.Binding current, int cash,
        string? moduleRaw, string? storageRaw)
    {
        receipt.Binding.Require(current);
        if (cash != receipt.CashBefore || !Same(moduleRaw, receipt.ModuleBefore) || !Same(storageRaw, receipt.StorageBefore))
            throw new InvalidOperationException("机房事务的现金或原文已变化。");
    }

    internal static Decision Decide(Receipt receipt, ServerRoomState.Binding current, int cash,
        string? moduleRaw, string? storageRaw, bool externalDeliveryConfirmed = false)
    {
        if (receipt == null || current == null || !receipt.Binding.SameSession(current) ||
            current.Revision != receipt.Binding.Revision && current.Revision != receipt.AfterRevision)
            return Decision.Blocked;
        var moduleBefore = Same(moduleRaw, receipt.ModuleBefore); var moduleAfter = Same(moduleRaw, receipt.ModuleAfter);
        var storageBefore = Same(storageRaw, receipt.StorageBefore); var storageAfter = Same(storageRaw, receipt.StorageAfter);
        if ((!moduleBefore && !moduleAfter) || (!storageBefore && !storageAfter) ||
            cash != receipt.CashBefore && cash != receipt.CashAfter)
            return Decision.Blocked;
        if (cash == receipt.CashAfter && moduleAfter && storageAfter) return Decision.Complete;
        if (externalDeliveryConfirmed)
            return cash == receipt.CashAfter ? Decision.PublishCandidate : Decision.Blocked;
        if (cash == receipt.CashBefore && moduleBefore && storageBefore) return Decision.NoChange;
        if (cash == receipt.CashAfter) return Decision.PublishCandidate;
        return Decision.RestoreCandidate;
    }

    // A missing key is different from a present empty value. Hash never replaces exact comparison.
    internal static string Fingerprint(string? raw) => raw == null ? "missing" :
        "present:" + raw.Length + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.Ordinal);
}
