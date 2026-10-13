using System;
using System.Collections.Generic;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Fail-closed native item roundtrip gate, executed once per valid live store before M2 charges money.</summary>
internal static class ServerRoomNativeProbe
{
    private static IntPtr owner;
    private static string? run;
    private static int slot;
    private static bool passed;
    private static string? failure;
    internal static string? Failure => failure;

    internal static void Ensure()
    {
        var store = PlayerStore.Instance ?? throw new InvalidOperationException("原版商店未就绪。");
        if (!SaveGeneration.IsSupported(store)) throw new InvalidOperationException("当前存档版本不支持机房物品验证。");
        if (owner != store.Pointer || run != store.runID || slot != store.saveSlotId)
        { owner = store.Pointer; run = store.runID; slot = store.saveSlotId; passed = false; failure = null; }
        if (failure != null) throw new InvalidOperationException("机房物品原生往返验证失败，交易已锁定：" + failure);
        if (passed) return;
        try
        {
            foreach (var definition in ServerRoomMachine.Definitions)
            {
                var sprite = SpriteAssets.Get(definition.Identifier);
                if (sprite == null || sprite.texture == null || !sprite.texture.isReadable || sprite.pixelsPerUnit <= 0)
                    throw new InvalidOperationException("专用机箱原生库存贴图不可读：" + definition.Identifier);
                Roundtrip(NewSnapshot(() => ServerRoomHardware.Create(definition.Identifier)));
            }
            foreach (var kind in new[] { ServerRoomMachine.Mining, ServerRoomMachine.Server })
            {
                foreach (var tier in new[] { 1, 5 })
                {
                    var definition = ServerRoomMachine.DefinitionFor(kind, tier);
                    var body = NewSnapshot(() => ServerRoomHardware.Create(definition.Identifier));
                    var info = new WorkroomMachineAssembly.MachineInfo { Version = 3, Kind = kind, Body = body };
                    if (kind == ServerRoomMachine.Mining)
                        info.Parts.Add(new WorkroomMachineAssembly.Installed
                        { Kind = "gpu", Index = 0, Item = NewSnapshot(() => Components.Create("pcrepair.component_gpu_t" + tier)) });
                    else
                    {
                        info.Parts.Add(new WorkroomMachineAssembly.Installed
                        { Kind = "cpu", Index = 0, Item = NewSnapshot(() => Components.Create("pcrepair.component_cpu_t" + tier)) });
                        info.Parts.Add(new WorkroomMachineAssembly.Installed
                        { Kind = "ram", Index = 0, Item = NewSnapshot(() => Components.Create("pcrepair.component_ram_t" + tier)) });
                    }
                    ServerRoomMachine.ValidateInfo(info, body.Identifier, body.Nodes[0].UniqueId);
                    var assembled = ServerRoomOperations.AssembleSnapshot(body, info);
                    if (!ServerRoomMachine.Assess(info).CanRun)
                        throw new InvalidOperationException("专用机器原生组装后的运行资格不符：" + kind);
                    Roundtrip(assembled);
                    // The first assembly starts from a bare body. Exercise the
                    // installed-item edit path too: the reported failure appeared
                    // only when adding or removing parts from an assembled case.
                    foreach (var part in info.Parts)
                    {
                        var reduced = new WorkroomMachineAssembly.MachineInfo
                        {
                            Version = 3, Kind = kind, Body = body,
                            Parts = new List<WorkroomMachineAssembly.Installed>(info.Parts)
                        };
                        reduced.Parts.Remove(part);
                        ServerRoomMachine.ValidateInfo(reduced, body.Identifier, body.Nodes[0].UniqueId);
                        var afterRemoval = ServerRoomOperations.AssembleSnapshot(assembled, reduced);
                        Roundtrip(afterRemoval);
                        var afterInstall = ServerRoomOperations.AssembleSnapshot(afterRemoval, info);
                        Roundtrip(afterInstall);
                        if (!WorkroomItemCodec.Equivalent(assembled, afterInstall))
                            throw new InvalidOperationException("机房配件拆装后本体未恢复：" + kind + "/" + part.Kind);
                    }
                }
            }
            if (WorkroomItemCodec.CleanupPending) throw new InvalidOperationException("临时机房物品清理未完成。");
            passed = true;
            Core.Log?.Msg("[机房原生门禁] 十档空箱、两类T1/T5配置及逐件拆装 GameItem→快照→恢复→再捕获验证通过。");
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            Core.Log?.Error("[机房原生门禁] 禁止 M2 资产操作：" + ex);
            throw;
        }
    }

    private static WorkroomItemCodec.Snapshot NewSnapshot(Func<GameItem> create)
    {
        var native = create();
        var candidate = new WorkroomItemCodec.Candidate { Root = native, Nodes = new List<GameItem> { native } };
        try
        {
            var snapshot = WorkroomItemCodec.Capture(native, out var nodes);
            candidate.Nodes = nodes;
            return snapshot;
        }
        finally { WorkroomItemCodec.Destroy(candidate); }
    }

    private static void Roundtrip(WorkroomItemCodec.Snapshot source)
    {
        var candidate = WorkroomItemCodec.Restore(source);
        try
        {
            var restored = WorkroomItemCodec.Capture(candidate.Root);
            if (!WorkroomItemCodec.Equivalent(source, restored))
                throw new InvalidOperationException("机房物品恢复后身份、组成、价值或外观不一致：" + source.Identifier);
        }
        finally { WorkroomItemCodec.Destroy(candidate); }
    }
}
