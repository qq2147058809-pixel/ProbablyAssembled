using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Il2Cpp;

namespace PCExpansion;

/// <summary>机房会话与现金/双键发布。任何未决操作由本类保管；UI 与视图不是库存。</summary>
internal static class ServerRoomRuntime
{
    private sealed class Operation
    {
        internal PlayerStore Owner = null!;
        internal ServerRoomTransaction.Receipt Receipt = null!;
        internal ServerRoomState.Document Before = null!, After = null!;
        internal List<WorkroomStorageState.Record> StorageBefore = null!, StorageAfter = null!;
        internal long StorageEpoch;
        internal bool Committed, Executing;
    }

    private static PlayerStore? owner;
    private static string? run;
    private static int slot;
    private static long epoch, checkedStorageRevision = -1, retryAt;
    private static ServerRoomState.Document? document;
    private static string? moduleRaw, error;
    private static readonly List<Operation> operations = new();

    // Callers read this view and prepare detached candidates with State.Clone/Advance.
    internal static ServerRoomState.Document? Current => document;
    internal static ServerRoomState.Document? Document => document;
    internal static string? Error => error;
    internal static bool Ready
    {
        get
        {
            try { return document != null && error == null && SameOwner() && WorkroomStorage.State.Ready && !Pending; }
            catch { return false; }
        }
    }

    internal static bool Pending
    {
        get
        {
            try { return operations.Any(Owns); }
            catch { return true; }
        }
    }

    private static bool Owns(Operation operation) => PlayerStore.IsInstanceExist() && PlayerStore.instance != null &&
        PlayerStore.instance.Pointer == operation.Receipt.Binding.Owner && PlayerStore.instance.runID == operation.Receipt.Binding.Run &&
        PlayerStore.instance.saveSlotId == operation.Receipt.Binding.Slot && operation.Owner.Pointer == operation.Receipt.Binding.Owner &&
        operation.Owner.runID == operation.Receipt.Binding.Run && operation.Owner.saveSlotId == operation.Receipt.Binding.Slot;

    private static bool SameOwner() => owner != null && PlayerStore.IsInstanceExist() && PlayerStore.instance != null &&
        PlayerStore.instance.Pointer == owner.Pointer && owner.runID == run && owner.saveSlotId == slot && SaveGeneration.IsSupported(owner);

    internal static void Update(PlayerStore store)
    {
        try
        {
            if (!SaveGeneration.IsSupported(store) || !PlayerStore.IsInstanceExist() || PlayerStore.instance == null ||
                PlayerStore.instance.Pointer != store.Pointer) return;
            if (owner == null || owner.Pointer != store.Pointer || run != store.runID || slot != store.saveSlotId)
            {
                owner = store; run = store.runID; slot = store.saveSlotId;
                epoch = checked(epoch + 1); document = null; moduleRaw = null; error = null; checkedStorageRevision = -1;
                moduleRaw = Raw(store, ServerRoomState.SaveKey);
                var present = store.modData != null && store.modData.ContainsKey(ServerRoomState.SaveKey);
                document = ServerRoomState.Load(present, moduleRaw, run!, slot) ?? ServerRoomState.New(run!, slot);
                document.RuntimeBinding = Binding();
            }
            if (document == null || error != null || Pending || !WorkroomStorage.State.Ready) return;
            if (!Equal(Raw(store, ServerRoomState.SaveKey), moduleRaw))
                throw new InvalidOperationException("机房原文在会话绑定后被外部改变。");
            if (checkedStorageRevision != WorkroomStorage.State.Revision)
            {
                ValidateRacks(document, WorkroomStorage.State.Items);
                checkedStorageRevision = WorkroomStorage.State.Revision;
            }
        }
        catch (Exception ex)
        {
            error = "data_error";
            Core.Log?.Error("机房模块未加载或引用无效；保留原文并禁止资产操作：" + ex.Message);
        }
    }

    internal static bool AllowSessionChange()
    {
        RecoverActive();
        if (!ServerRoomDaily.AllowSessionChange) return false;
        if (!Pending) return true;
        Core.Log?.Warning("机房资产事务仍待恢复，暂不切档；原凭据保留。");
        return false;
    }

    internal static void ResetSession()
    {
        if (Pending) throw new InvalidOperationException("机房事务未完成，不能清除当前会话。");
        ServerRoomDaily.ResetSession();
        owner = null; run = null; slot = 0; document = null; moduleRaw = null; error = null;
        checkedStorageRevision = -1; epoch = checked(epoch + 1);
        // Unmatched receipts are retained, never applied to a different owner/run/slot.
    }

    internal static void Commit(ServerRoomState.Change change, IReadOnlyList<WorkroomStorageState.Record>? storageNext)
    {
        if (change == null || !Ready || document == null || owner == null || WorkroomStorage.Busy ||
            WorkroomItemCodec.CleanupPending || WorkroomStorage.State.PendingRecords.Count != 0 ||
            WorkroomCashRepair.Pending || ComputerSupplierNpcs.StockPending || NpcStockOffers.Pending)
            throw new InvalidOperationException("机房会话不可用或此前资产交付尚未完成。");
        if (change.Next.RuntimeBinding == null)
            throw new InvalidOperationException("候选没有当前运行会话的来源绑定。");
        change.Next.RuntimeBinding.Require(Binding());
        if (!change.Changed)
        {
            if (change.CashDelta != 0 || storageNext != null || ServerRoomState.Serialize(change.Next) != ServerRoomState.Serialize(document))
                throw new InvalidOperationException("无变化操作不能携带资产变化。");
            return;
        }
        var binding = Binding();
        var beforeModule = Raw(owner, ServerRoomState.SaveKey);
        if (!Equal(beforeModule, moduleRaw)) throw new InvalidOperationException("机房原文已变化。");
        var beforeDocument = ServerRoomState.Clone(document);
        var afterRaw = ServerRoomState.Serialize(change.Next);
        var afterDocument = ServerRoomState.Parse(afterRaw, binding.Run, binding.Slot);
        var state = WorkroomStorage.State;
        var storageEpoch = state.Epoch; var storageRevision = state.Revision;
        var beforeStorage = Raw(owner, WorkroomStorageState.SaveKey);
        var beforeLive = StorageJson(state.Items);
        var beforeRecords = StorageClone(beforeLive);
        if (beforeStorage == null ? beforeRecords.Count != 0 : StorageJson(StorageClone(beforeStorage)) != beforeLive)
            throw new InvalidOperationException("储物原文与实时保管模型不一致。");
        var expectedBefore = beforeModule == null ? ServerRoomState.New(binding.Run, binding.Slot) :
            ServerRoomState.Parse(beforeModule, binding.Run, binding.Slot);
        if (ServerRoomState.Serialize(expectedBefore) != ServerRoomState.Serialize(beforeDocument))
            throw new InvalidOperationException("机房原文与实时状态不一致。");
        List<WorkroomStorageState.Record> afterRecords;
        string? afterStorage;
        if (storageNext == null)
        { afterRecords = beforeRecords; afterStorage = beforeStorage; }
        else
        {
            state.ValidateReplacement(storageNext.ToList());
            afterStorage = StorageJson(storageNext);
            afterRecords = StorageClone(afterStorage);
            state.ValidateReplacement(afterRecords);
        }
        // This is a detached joint candidate check. Live cross-key checks wait for both publications.
        ValidateRacks(beforeDocument, beforeRecords);
        ValidateRacks(afterDocument, afterRecords);
        var beforeCash = owner.GetCash();
        var receipt = ServerRoomTransaction.Prepare(binding, Guid.NewGuid().ToString("N"), beforeCash,
            change.CashDelta, beforeModule, afterRaw, beforeStorage, afterStorage);
        if (!Ready || state.Epoch != storageEpoch || state.Revision != storageRevision || StorageJson(state.Items) != beforeLive)
            throw new InvalidOperationException("预备期间工作间保管模型已变化。");
        ServerRoomTransaction.RequireBefore(receipt, Binding(), owner.GetCash(), Raw(owner, ServerRoomState.SaveKey),
            Raw(owner, WorkroomStorageState.SaveKey));
        var operation = new Operation { Owner = owner, Receipt = receipt, Before = beforeDocument, After = afterDocument,
            StorageBefore = beforeRecords, StorageAfter = afterRecords, StorageEpoch = storageEpoch, Executing = true };
        operations.Add(operation);
        try
        {
            RequireOperation(operation);
            if (receipt.CashDelta < 0) owner.PayMoney(checked(-receipt.CashDelta), false);
            else if (receipt.CashDelta > 0) owner.ModCash(receipt.CashDelta, false);
            RequireOperation(operation);
            if (owner.GetCash() != receipt.CashAfter) throw new InvalidOperationException("现金变化未达到本笔事务的精确目标。");
            Publish(operation, true);
            Finish(operation, true);
        }
        catch (Exception ex)
        {
            operation.Executing = false;
            Core.Log?.Warning("机房提交异常，按实际现金和两个原文恢复：" + ex.Message);
            Recover(operation);
            if (!operation.Committed) throw;
        }
        finally { operation.Executing = false; }
    }

    internal static void RetryRecovery()
    {
        if (operations.Count == 0 || Environment.TickCount64 < retryAt) return;
        retryAt = Environment.TickCount64 + 500;
        RecoverActive();
    }

    private static void RecoverActive()
    {
        foreach (var operation in operations.ToArray())
        {
            try { if (Owns(operation)) Recover(operation); }
            catch (Exception ex) { Core.Log?.Error("机房事务身份暂不可读，保留凭据：" + ex.Message); }
        }
    }

    private static void Recover(Operation operation)
    {
        // A callback cannot cancel a receipt while its original native call is on the stack.
        if (operation.Executing) return;
        operation.Executing = true;
        try
        {
            RequireOperation(operation);
            var receipt = operation.Receipt;
            var decision = ServerRoomTransaction.Decide(receipt, Binding(), operation.Owner.GetCash(),
                Raw(operation.Owner, ServerRoomState.SaveKey), Raw(operation.Owner, WorkroomStorageState.SaveKey));
            switch (decision)
            {
                case ServerRoomTransaction.Decision.NoChange:
                    Publish(operation, false); Finish(operation, false); break;
                case ServerRoomTransaction.Decision.Complete:
                    Publish(operation, true); Finish(operation, true); break;
                case ServerRoomTransaction.Decision.PublishCandidate:
                    Publish(operation, true); Finish(operation, true); break;
                case ServerRoomTransaction.Decision.RestoreCandidate:
                    Publish(operation, false); Finish(operation, false); break;
                default:
                    throw new InvalidOperationException("余额或原文超出本笔 before/after 证据，禁止猜测退款或资产重建。");
            }
        }
        catch (Exception ex)
        {
            retryAt = Environment.TickCount64 + 500;
            Core.Log?.Error("机房资产恢复未完成，保留凭据并保护操作/保存/切档：" + ex.Message);
        }
        finally { operation.Executing = false; }
    }

    private static void RequireOperation(Operation operation)
    {
        if (!Owns(operation) || !SameOwner() || !operation.Receipt.Binding.SameSession(Binding()) ||
            !WorkroomStorage.State.Ready || WorkroomStorage.State.Epoch != operation.StorageEpoch)
            throw new InvalidOperationException("事务不属于当前机房/工作间会话。");
    }

    private static void Publish(Operation operation, bool after)
    {
        RequireOperation(operation);
        var receipt = operation.Receipt;
        var targetStorage = after ? receipt.StorageAfter : receipt.StorageBefore;
        var targetModule = after ? receipt.ModuleAfter : receipt.ModuleBefore;
        var currentStorage = Raw(operation.Owner, WorkroomStorageState.SaveKey);
        var currentModule = Raw(operation.Owner, ServerRoomState.SaveKey);
        if (!Known(currentStorage, receipt.StorageBefore, receipt.StorageAfter) ||
            !Known(currentModule, receipt.ModuleBefore, receipt.ModuleAfter))
            throw new InvalidOperationException("发布前原文不属于原凭据。");
        if (operation.Owner.GetCash() != (after ? receipt.CashAfter : receipt.CashBefore))
            throw new InvalidOperationException("发布前余额已改变。");
        var expectedLive = StorageJson(after ? operation.StorageAfter : operation.StorageBefore);
        if (!Equal(currentStorage, targetStorage) || StorageJson(WorkroomStorage.State.Items) != expectedLive)
        {
            // Do not call WorkroomStorage.Run: Pending blocks external admission to that method.
            WorkroomStorage.State.Replace(after ? operation.StorageAfter : operation.StorageBefore);
            // Restoring old pretty/noncanonical bytes must preserve the exact before image.
            if (!after) WriteRaw(operation.Owner, WorkroomStorageState.SaveKey, targetStorage);
            if (!Equal(Raw(operation.Owner, WorkroomStorageState.SaveKey), targetStorage))
                throw new InvalidOperationException("储物候选发布回读不一致。");
        }
        RequireOperation(operation);
        if (!Equal(Raw(operation.Owner, ServerRoomState.SaveKey), targetModule))
            WriteRaw(operation.Owner, ServerRoomState.SaveKey, targetModule);
    }

    private static void Finish(Operation operation, bool committed)
    {
        RequireOperation(operation);
        var receipt = operation.Receipt;
        if (operation.Owner.GetCash() != (committed ? receipt.CashAfter : receipt.CashBefore) ||
            !Equal(Raw(operation.Owner, ServerRoomState.SaveKey), committed ? receipt.ModuleAfter : receipt.ModuleBefore) ||
            !Equal(Raw(operation.Owner, WorkroomStorageState.SaveKey), committed ? receipt.StorageAfter : receipt.StorageBefore))
            throw new InvalidOperationException("事务终点现金/原文回读失败。");
        var target = committed ? operation.After : operation.Before;
        WorkroomStorage.State.ValidateReplacement(WorkroomStorage.State.Items.ToList());
        ValidateRacks(target, WorkroomStorage.State.Items);
        var expectedLive = StorageJson(committed ? operation.StorageAfter : operation.StorageBefore);
        if (StorageJson(WorkroomStorage.State.Items) != expectedLive)
            throw new InvalidOperationException("事务终点的实时物品图与原候选不符。");
        document = ServerRoomState.Clone(target);
        document.RuntimeBinding = Binding();
        moduleRaw = committed ? receipt.ModuleAfter : receipt.ModuleBefore;
        checkedStorageRevision = WorkroomStorage.State.Revision; error = null;
        operation.Committed = committed;
        operations.Remove(operation);
    }

    private static ServerRoomState.Binding Binding() => new(owner?.Pointer ?? IntPtr.Zero, run ?? "", slot, epoch,
        document?.Revision ?? throw new InvalidOperationException("机房状态未绑定。"));

    private static string? Raw(PlayerStore store, string key) => store.modData != null && store.modData.ContainsKey(key) ? store.modData[key] : null;
    private static void WriteRaw(PlayerStore store, string key, string? raw)
    {
        if (raw == null) { store.modData?.Remove(key); return; }
        store.modData ??= new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
        store.modData[key] = raw;
    }
    private static bool Equal(string? a, string? b) => string.Equals(a, b, StringComparison.Ordinal);
    private static bool Known(string? current, string? before, string? after) => Equal(current, before) || Equal(current, after);
    private static string StorageJson(IEnumerable<WorkroomStorageState.Record> records) => JsonSerializer.Serialize(
        new WorkroomStorageState.Document { Items = records.ToList() }, WorkroomStorageState.JsonOptions);
    private static List<WorkroomStorageState.Record> StorageClone(string raw) =>
        (JsonSerializer.Deserialize<WorkroomStorageState.Document>(raw, WorkroomStorageState.JsonOptions) ??
            throw new InvalidOperationException("储物候选不是完整文档。")).Items;

    private static void ValidateRacks(ServerRoomState.Document module, IEnumerable<WorkroomStorageState.Record> records)
    {
        ServerRoomState.Validate(module);
        var racks = records.Where(record => record.Place == "rack").ToArray();
        if (racks.Length != module.Racks.Count) throw new InvalidOperationException("机房与保管机架数量不一致。");
        foreach (var reference in module.Racks)
        {
            var matching = racks.Where(record => record.Id == reference.RecordId).ToArray();
            if (matching.Length != 1 || !WorkroomStorageState.TryRackIndex(matching[0].Slot, out var index) || index != reference.RackIndex ||
                matching[0].Item.Nodes.Count == 0 || matching[0].Item.Nodes[0].UniqueId != reference.UniqueId ||
                !ServerRoomMachine.TryDefinition(matching[0].Item.Identifier, out var definition) || definition.Kind != reference.Kind)
                throw new InvalidOperationException("机房机器引用、位置、身份或类型不一致。");
        }
    }
}
