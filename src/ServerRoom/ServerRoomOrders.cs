using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;

namespace PCExpansion;

/// <summary>独立单键预订发布。调用方负责原生入队和成交证据，本类只提交意图及凭据。</summary>
internal static class ServerRoomOrders
{
    private sealed class Publication
    {
        internal PlayerStore Owner = null!;
        internal string Run = "", After = "";
        internal int Slot;
        internal long Epoch;
        internal bool BeforePresent;
        internal string? Before;
    }
    private static PlayerStore? owner;
    private static string? run, raw, error;
    private static int slot;
    private static long epoch;
    private static bool present, publishing;
    private static Publication? publication;
    private static ServerRoomOrderState.Document? document;

    // Detached views: an interface cannot mutate the live document or remove receipts.
    internal static ServerRoomOrderState.Document? Current => Ready && document != null ? ServerRoomOrderState.Clone(document) : null;
    internal static string? Error => error;
    internal static bool Ready
    {
        get
        {
            try { return document != null && error == null && !publishing && publication == null && SameOwner() && ServerRoomRuntime.Ready && MatchesRaw(); }
            catch { return false; }
        }
    }

    internal static IReadOnlyList<ServerRoomOrderState.Order> List() => Ready && document != null
        ? document.Orders.Select(order => new ServerRoomOrderState.Order
            { Id = order.Id, Kind = order.Kind, Tier = order.Tier, VisitId = order.VisitId, DueDay = order.DueDay, Placed = order.Placed }).ToArray()
        : Array.Empty<ServerRoomOrderState.Order>();

    internal static void Update(PlayerStore store)
    {
        try
        {
            if (publication != null)
            {
                ResolvePublication();
                if (publication != null) return;
            }
            if (!SaveGeneration.IsSupported(store) || !PlayerStore.IsInstanceExist() || PlayerStore.instance == null ||
                PlayerStore.instance.Pointer != store.Pointer) return;
            if (owner == null || owner.Pointer != store.Pointer || run != store.runID || slot != store.saveSlotId)
            {
                owner = store; run = store.runID; slot = store.saveSlotId;
                epoch = checked(epoch + 1); document = null; raw = null; present = false; error = null;
            }
            if (error != null || publishing || !ServerRoomRuntime.Ready) return;
            if (document == null)
            {
                // Existing valid M2 saves have no orders key. Generation and M2
                // readiness must pass before initializing this separate module.
                present = HasKey(store); raw = Read(store);
                document = ServerRoomOrderState.Load(present, raw, run!, slot) ?? ServerRoomOrderState.New(run!, slot);
                document.RuntimeBinding = Binding();
                if (!present) Publish(ServerRoomOrderState.Serialize(document));
            }
            else if (!MatchesRaw()) throw new InvalidOperationException("预订原文在会话绑定后被外部改变。");
        }
        catch (Exception ex) { Fail(ex); }
    }

    internal static string Add(string kind, int tier)
    {
        RequireUiCommand(tier);
        var id = Guid.NewGuid().ToString("N");
        Commit(ServerRoomOrderState.PrepareAdd(Require(), id, kind, tier, StoreStation.GetDayCounter()));
        return id;
    }
    internal static void Replace(string orderId, string kind, int tier)
    {
        RequireUiCommand(tier);
        Commit(ServerRoomOrderState.PrepareReplace(Require(), orderId, kind, tier));
    }
    internal static void Cancel(string orderId)
    {
        RequireUiCommand();
        Commit(ServerRoomOrderState.PrepareCancel(Require(), orderId));
    }
    internal static void Dispatch(string visitToken, int visitDay, IReadOnlyCollection<string> orderIds) =>
        Commit(ServerRoomOrderState.PrepareDispatch(Require(), visitToken, visitDay, orderIds));
    internal static void MarkArrived(string visitToken, IReadOnlyCollection<string> orderIds) =>
        Commit(ServerRoomOrderState.PrepareMarkArrived(Require(), visitToken, orderIds));
    internal static void MarkPlaced(string orderId, string visitToken) =>
        Commit(ServerRoomOrderState.PrepareMarkPlaced(Require(), orderId, visitToken));
    internal static void FinishVisit(string visitToken, int currentDay) =>
        Commit(ServerRoomOrderState.PrepareFinishVisit(Require(), visitToken, currentDay));
    internal static bool CompletePurchased(string orderId, string visitToken)
    {
        var change = ServerRoomOrderState.PrepareCompletePurchased(Require(), orderId, visitToken);
        Commit(change);
        return change.Changed;
    }

    // Outstanding/frozen orders are normal data and never block saving. Only an
    // ambiguous publication, changed raw envelope or corrupted key refuses it.
    internal static bool BeforeSave()
    {
        if (owner == null) return true;
        try
        {
            ResolvePublication();
            return !publishing && publication == null && (!SameOwner() || error == null && (document == null || MatchesRaw()));
        }
        catch { return false; }
    }

    internal static bool AllowSessionChange()
    {
        if (publishing) return false;
        ResolvePublication();
        // A corrupt key read from disk carries no in-flight command and can be
        // left unchanged when switching saves. An unknown write retains proof.
        return publication == null;
    }

    internal static void ResetSession()
    {
        if (publishing || publication != null) throw new InvalidOperationException("预订正在发布或结果未确认，不能切换会话。");
        owner = null; run = raw = error = null; slot = 0; present = false; document = null;
        epoch = checked(epoch + 1);
    }

    private static ServerRoomOrderState.Document Require()
    {
        if (!Ready || document == null) throw new InvalidOperationException("预订会话不可用或记录已变化。");
        return ServerRoomOrderState.Clone(document);
    }

    private static void RequireUiCommand(int? tier = null)
    {
        if (!WorkroomTrial.ServerCommandsAllowed || !ServerRoomDaily.AllowCommands || !ServerRoomRuntime.Ready ||
            ServerRoomRuntime.Current is not { } current || !WorkroomStorage.State.Ready ||
            WorkroomStorage.State.PendingRecords.Count != 0)
            throw new InvalidOperationException("机房预订操作尚未就绪，请稍后重试。");
        if (!current.Enabled || tier.HasValue && (tier.Value < 1 || tier.Value > ServerRoomState.HighestTier(current)))
            throw new InvalidOperationException("机房尚未启用或所选专用机箱档位尚未开放。");
        // No native item is created by a preorder, so the physical codec probe
        // remains in the later asset command/native-stock admission path.
    }

    private static void Commit(ServerRoomOrderState.Change change)
    {
        if (!Ready || document == null || change?.Next.RuntimeBinding == null)
            throw new InvalidOperationException("预订候选没有当前会话来源。");
        change.Next.RuntimeBinding.Require(Binding());
        var after = ServerRoomOrderState.Serialize(change.Next);
        if (!change.Changed)
        {
            if (after != ServerRoomOrderState.Serialize(document)) throw new InvalidOperationException("无变化预订候选与当前状态不同。");
            return;
        }
        if (change.Next.Revision != checked(document.Revision + 1)) throw new InvalidOperationException("预订候选版本无效。");
        // Parse/allocate detached state before the single native dictionary write.
        var next = ServerRoomOrderState.Parse(after, run!, slot);
        try
        {
            Publish(after);
            document = next; document.RuntimeBinding = Binding();
        }
        catch (Exception ex)
        {
            // If the write definitely left the before-image, the command simply
            // failed. An unknown image is locked by Publish and kept untouched.
            if (error != null) Core.Log?.Error("预订发布未确认，保留原文并停止后续操作：" + ex.Message);
            throw;
        }
    }

    private static void Publish(string after)
    {
        if (owner == null || !SameOwner() || publishing || !MatchesRaw())
            throw new InvalidOperationException("预订发布前会话或原文已变化。");
        var boundOwner = owner; var boundRun = run; var boundSlot = slot; var boundEpoch = epoch;
        var before = raw; var beforePresent = present;
        publication = new Publication { Owner = boundOwner, Run = boundRun!, Slot = boundSlot, Epoch = boundEpoch,
            BeforePresent = beforePresent, Before = before, After = after };
        publishing = true;
        Exception? writeError = null;
        try
        {
            try
            {
                boundOwner.modData ??= new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
                boundOwner.modData[ServerRoomOrderState.SaveKey] = after;
            }
            catch (Exception ex) { writeError = ex; }
            if (!SameOwner() || boundOwner.Pointer != owner?.Pointer || boundRun != run || boundSlot != slot || boundEpoch != epoch)
                throw new InvalidOperationException("预订发布时会话发生变化。");
            var nowPresent = HasKey(boundOwner); var now = Read(boundOwner);
            if (nowPresent && string.Equals(now, after, StringComparison.Ordinal))
            { raw = after; present = true; publication = null; return; }
            if (nowPresent == beforePresent && string.Equals(now, before, StringComparison.Ordinal))
            {
                publication = null;
                throw new KnownBeforeException("预订写入未生效。", writeError);
            }
            throw new InvalidOperationException("预订写入回读不属于本次 before/after 凭据。");
        }
        catch (KnownBeforeException) { throw; }
        catch (Exception ex) { Fail(ex); throw; }
        finally { publishing = false; }
    }

    private static void ResolvePublication()
    {
        var proof = publication;
        if (proof == null || publishing) return;
        try
        {
            if (!SameOwner() || owner == null || proof.Owner.Pointer != owner.Pointer || proof.Run != run ||
                proof.Slot != slot || proof.Epoch != epoch || proof.Owner.runID != proof.Run || proof.Owner.saveSlotId != proof.Slot)
                return;
            var nowPresent = HasKey(proof.Owner); var now = Read(proof.Owner);
            ServerRoomOrderState.Document? resolved;
            if (nowPresent && string.Equals(now, proof.After, StringComparison.Ordinal))
                resolved = ServerRoomOrderState.Parse(proof.After, proof.Run, proof.Slot);
            else if (nowPresent == proof.BeforePresent && string.Equals(now, proof.Before, StringComparison.Ordinal))
                resolved = proof.BeforePresent ? ServerRoomOrderState.Parse(proof.Before!, proof.Run, proof.Slot) : null;
            else return;
            document = resolved; raw = now; present = nowPresent; error = null;
            if (document != null) document.RuntimeBinding = Binding();
            publication = null;
        }
        catch { /* A transient read or unfamiliar image keeps the exact proof. */ }
    }

    private sealed class KnownBeforeException : InvalidOperationException
    { internal KnownBeforeException(string message, Exception? inner) : base(message, inner) { } }

    private static void Fail(Exception ex)
    {
        error = "data_error";
        Core.Log?.Error("机房预订未加载或发布身份不明；保留原文：" + ex.Message);
    }
    private static ServerRoomState.Binding Binding() => new(owner?.Pointer ?? IntPtr.Zero, run ?? "", slot, epoch,
        document?.Revision ?? throw new InvalidOperationException("预订状态未绑定。"));
    private static bool SameOwner() => owner != null && PlayerStore.IsInstanceExist() && PlayerStore.instance != null &&
        PlayerStore.instance.Pointer == owner.Pointer && owner.runID == run && owner.saveSlotId == slot && SaveGeneration.IsSupported(owner);
    private static bool HasKey(PlayerStore store) => store.modData != null && store.modData.ContainsKey(ServerRoomOrderState.SaveKey);
    private static string? Read(PlayerStore store) => HasKey(store) ? store.modData[ServerRoomOrderState.SaveKey] : null;
    private static bool MatchesRaw() => owner != null && HasKey(owner) == present && string.Equals(Read(owner), raw, StringComparison.Ordinal);
}
