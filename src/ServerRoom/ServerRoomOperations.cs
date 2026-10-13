using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Il2Cpp;

namespace PCExpansion;

/// <summary>M2 commands. A command prepares actual snapshots before the cash/asset coordinator publishes them.</summary>
internal static partial class ServerRoomOperations
{
    internal static IReadOnlyList<WorkroomStorageState.Record> AvailableBodies() => Sources()
        .Where(record => ServerRoomMachine.TryDefinition(record.Item.Identifier, out _)).ToArray();

    internal static IReadOnlyList<WorkroomStorageState.Record> AvailableParts(string kind) => Sources()
        .Where(record => Matches(kind, record.Item)).ToArray();

    private static IEnumerable<WorkroomStorageState.Record> Sources() =>
        WorkroomStorage.State.Ready ? WorkroomStorage.State.Items.Where(record => record.Place is "box" or "room") :
        Enumerable.Empty<WorkroomStorageState.Record>();

    private static ServerRoomState.Document Require()
    {
        if (!WorkroomTrial.ServerCommandsAllowed || !ServerRoomDaily.AllowCommands || !ServerRoomRuntime.Ready ||
            ServerRoomRuntime.Current is not { } current || !WorkroomStorage.State.Ready ||
            WorkroomStorage.State.PendingRecords.Count != 0)
            throw new InvalidOperationException("机房操作尚未就绪，请稍后重试。");
        ServerRoomNativeProbe.Ensure();
        return current;
    }

    internal static void Enable()
    {
        var current = Require();
        ServerRoomRuntime.Commit(ServerRoomState.PrepareEnable(current), null);
    }

    internal static void Upgrade(bool power)
    {
        var current = Require();
        ServerRoomRuntime.Commit(ServerRoomState.PrepareUpgrade(current, power), null);
    }

    internal static string? ActiveVisitId()
    {
        var store = PlayerStore.Instance;
        if (store == null || !store.isClientArrived ||
            !PhoneAssemblerNpc.IsPhoneAssembler(store.currentClientInstance?.storeClient)) return null;
        // This caller can be summoned only once per day under the native three-day
        // contact cooldown. The day token survives a save/load of the same visit.
        return store.runID + ":vesper:" + StoreStation.GetDayCounter().ToString(CultureInfo.InvariantCulture);
    }

    internal static void RackBody(int rack, string recordId)
    {
        var current = Require();
        if (rack < 0 || rack >= ServerRoomState.UnlockedSlots(current) ||
            current.Racks.Any(entry => entry.RackIndex == rack) || WorkroomStorage.State.FindRack(rack) != null)
            throw new InvalidOperationException("机位尚未开放或已被占用。");
        var source = WorkroomStorage.State.Find(recordId);
        if (source == null || source.Place is not ("box" or "room") ||
            !ServerRoomMachine.TryDefinition(source.Item.Identifier, out var definition) || source.Item.Count != 1)
            throw new InvalidOperationException("请选择储物箱或工作间内的专用机箱。");
        var nextState = ServerRoomState.Advance(current);
        nextState.Racks.Add(new ServerRoomState.RackReference
        {
            RackIndex = rack, RecordId = recordId, UniqueId = source.Item.Nodes[0].UniqueId,
            Kind = definition.Kind, CoinId = ServerRoomState.CoinIds[0], Priority = rack
        });
        ServerRoomState.Validate(nextState);
        var nextStorage = WorkroomStorage.State.Items.Select(record => record.Id == recordId
            ? new WorkroomStorageState.Record { Id = record.Id, Place = "rack", Slot = WorkroomStorageState.RackSlot(rack), Item = record.Item }
            : record).ToList();
        ServerRoomRuntime.Commit(new ServerRoomState.Change(nextState, 0), nextStorage);
    }

    internal static void TakeBody(int rack)
    {
        var current = Require();
        var source = Rack(current, rack);
        var nextState = ServerRoomState.Advance(current);
        nextState.Racks.RemoveAll(entry => entry.RackIndex == rack);
        ServerRoomState.Validate(nextState);
        var nextStorage = WorkroomStorage.State.Items.Select(record => record.Id == source.Id
            ? new WorkroomStorageState.Record { Id = record.Id, Place = "box", Item = record.Item }
            : record).ToList();
        ServerRoomRuntime.Commit(new ServerRoomState.Change(nextState, 0), nextStorage);
    }

    internal static void Install(int rack, string sourceId)
    {
        var current = Require();
        var body = Rack(current, rack);
        var source = WorkroomStorage.State.Find(sourceId);
        if (source == null || source.Place is not ("box" or "room") || source.Item.Count != 1)
            throw new InvalidOperationException("安装件来源已变化。");
        var definition = ServerRoomMachine.TryDefinition(body.Item.Identifier, out var found) ? found :
            throw new InvalidOperationException("机架本体型号无效。");
        var kind = PartKind(source.Item) ?? throw new InvalidOperationException("只接受完整显卡、CPU 或内存条。");
        if (!ServerRoomMachine.IntactComponent(source.Item))
            throw new InvalidOperationException("只能安装完好的完整配件。");
        var info = Draft(body.Item, definition);
        var index = Enumerable.Range(0, definition.Slots(kind))
            .FirstOrDefault(slot => !info.Parts.Any(part => part.Kind == kind && part.Index == slot), -1);
        if (index < 0) throw new InvalidOperationException("该类型插槽已满或机箱不支持此配件。");
        info.Parts.Add(new WorkroomMachineAssembly.Installed { Kind = kind, Index = index, Item = source.Item });
        ServerRoomMachine.ValidateInfo(info, body.Item.Identifier, body.Item.Nodes[0].UniqueId);
        var assembled = AssembleSnapshot(body.Item, info);
        var nextStorage = WorkroomStorage.State.Items.Where(record => record.Id != sourceId)
            .Select(record => record.Id == body.Id
                ? new WorkroomStorageState.Record { Id = record.Id, Place = "rack", Slot = record.Slot, Item = assembled }
                : record).ToList();
        ServerRoomRuntime.Commit(new ServerRoomState.Change(ServerRoomState.Advance(current), 0), nextStorage);
    }

    internal static void Remove(int rack, string kind, int index)
    {
        var current = Require();
        var body = Rack(current, rack);
        if (!ServerRoomMachine.TryDefinition(body.Item.Identifier, out var definition))
            throw new InvalidOperationException("机架本体型号无效。");
        var info = Draft(body.Item, definition);
        var selected = info.Parts.SingleOrDefault(part => part.Kind == kind && part.Index == index)
            ?? throw new InvalidOperationException("安装件已被取出。");
        info.Parts.Remove(selected);
        ServerRoomMachine.ValidateInfo(info, body.Item.Identifier, body.Item.Nodes[0].UniqueId);
        var assembled = AssembleSnapshot(body.Item, info);
        var nextStorage = WorkroomStorage.State.Items.Select(record => record.Id == body.Id
            ? new WorkroomStorageState.Record { Id = record.Id, Place = "rack", Slot = record.Slot, Item = assembled }
            : record).ToList();
        nextStorage.Add(new WorkroomStorageState.Record
            { Id = Guid.NewGuid().ToString("N"), Place = "box", Item = selected.Item });
        ServerRoomRuntime.Commit(new ServerRoomState.Change(ServerRoomState.Advance(current), 0), nextStorage);
    }

    private static WorkroomStorageState.Record Rack(ServerRoomState.Document current, int rack)
    {
        var reference = current.Racks.SingleOrDefault(entry => entry.RackIndex == rack)
            ?? throw new InvalidOperationException("机位没有机器。");
        if (reference.PoweredOn || current.Leases.Any(lease => lease.RecordId == reference.RecordId))
            throw new InvalidOperationException("设备运行或出租中，不能移动配件。");
        var body = WorkroomStorage.State.FindRack(rack);
        if (body == null || body.Id != reference.RecordId || body.Item.Nodes[0].UniqueId != reference.UniqueId)
            throw new InvalidOperationException("机位与物品保管记录不一致，已停止操作。");
        return body;
    }

    private static bool Matches(string kind, WorkroomItemCodec.Snapshot item) => PartKind(item) == kind;
    private static string? PartKind(WorkroomItemCodec.Snapshot item)
    {
        if (item.Count != 1 || item.Nodes.Count != 1) return null;
        var tag = Components.Find(item.Identifier)?.Owner.Tag;
        return tag == Components.GpuTag ? "gpu" : tag == Components.CpuTag ? "cpu" :
            tag == Components.RamTag ? "ram" : null;
    }

    private static WorkroomMachineAssembly.MachineInfo Draft(WorkroomItemCodec.Snapshot body,
        ServerRoomMachine.Definition definition)
    {
        var existing = body.Nodes[0].Machine;
        if (existing != null)
        {
            ServerRoomMachine.ValidateInfo(existing, body.Identifier, body.Nodes[0].UniqueId);
            return new WorkroomMachineAssembly.MachineInfo
                { Version = 3, Kind = existing.Kind, Body = existing.Body, Parts = new List<WorkroomMachineAssembly.Installed>(existing.Parts) };
        }
        return new WorkroomMachineAssembly.MachineInfo
            { Version = 3, Kind = definition.Kind, Body = body, Parts = new List<WorkroomMachineAssembly.Installed>() };
    }

    internal static WorkroomItemCodec.Snapshot AssembleSnapshot(WorkroomItemCodec.Snapshot source,
        WorkroomMachineAssembly.MachineInfo info)
    {
        if (source.Identifier != info.Body.Identifier || source.Nodes[0].UniqueId != info.Body.Nodes[0].UniqueId)
            throw new InvalidOperationException("机架本体与装配记录身份不一致。");
        // Restoring an already assembled machine leaves its old V3 record in the
        // native saved graph and modified state. Capture then sees two different
        // records after SetString. Rebuild from the same bare body instead.
        var candidate = WorkroomItemCodec.Restore(info.Body);
        try
        {
            var root = candidate.Root;
            root.EnableTag(WorkroomMachineAssembly.Tag, false);
            root.state.GetTag(WorkroomMachineAssembly.Tag).SetString(WorkroomAssemblyData.Encode(info));
            WorkroomMachineAssembly.Refresh(root);
            var snapshot = WorkroomItemCodec.Capture(root);
            if (snapshot.Nodes[0].Machine == null)
                throw new InvalidOperationException("专用整机快照回读失败。");
            return snapshot;
        }
        finally { WorkroomItemCodec.Destroy(candidate); }
    }
}
