using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PCExpansion;

/// <summary>场景工作区只接收明确目标与保管物品；维修、拆解仍由面板明确执行。</summary>
internal static class WorkroomMatTransfer
{
    internal enum Destination { Processing, ComponentAssembly, Motherboard, Case }

    internal sealed class Plan
    {
        internal Destination Target;
        internal string Family = "", Slot = "";
        internal WorkroomPartProcessing.Mode ProcessingMode;
        internal bool RepairMachine;
        internal WorkroomStorageState.Record[] Returning = Array.Empty<WorkroomStorageState.Record>();
        internal long Epoch, Revision;
        internal WorkroomStorageState.Record Source = null!;
        internal byte[] Baseline = Array.Empty<byte>();
    }

    private const string Prefix = "workroom.mat.";

    internal static bool TryPlan(string id, long epoch, int orientation, WorkroomPanels.Kind target, out Plan? plan, out string failure)
        => Build(id, epoch, orientation, target, false, out plan, out failure);

    internal static bool TryStage(string id, long epoch, int orientation, WorkroomPanels.Kind target, out Plan? plan, out string failure)
    {
        plan = null; failure = Prefix + "unavailable";
        if (!Available(epoch, false)) return false;
        Plan? committed = null;
        var reason = failure;
        WorkroomStorage.Run(() =>
        {
            // A hover plan is never trusted as a command. Re-read custody and
            // every applicable slot while holding the storage operation lease.
            if (!Build(id, epoch, orientation, target, true, out var current, out reason)) return;
            if (!MatchesTarget(current!.Target, target)) { reason = Prefix + "unsupported"; return; }
            if (!Current(current!, true)) { reason = Prefix + "changed"; return; }
            reason = Prefix + "invalid";
            if (!WorkroomSelection.StageWithReturns(current.Source, current.Slot, current.Returning, epoch,
                orientation, WorkroomSelection.Remember(current.Source), out var returnFailure, () => Current(current, true)))
            { reason = Prefix + (returnFailure == "return_full" ? "full" : returnFailure == "changed" ? "changed" : "unavailable"); return; }
            committed = current;
            reason = "";
        });
        plan = committed; failure = reason;
        return committed != null;
    }

    private static bool Available(long epoch, bool insideRun)
    {
        var state = WorkroomStorage.State;
        return state.Ready && state.Epoch == epoch && !WorkroomItemCodec.CleanupPending &&
            state.PendingRecords.Count == 0 && (insideRun
                ? !WorkroomCashRepair.Pending && !ServerRoomRuntime.Pending && !WorkroomStoryTransfer.BlocksStorage
                : !WorkroomStorage.Busy);
    }

    // Keep exact serialized-content protection without allocating two complete
    // inventory JSON strings on every uncached hover. Serialization streams to
    // SHA256; the small fingerprint owns no mutable DTO references.
    private sealed class FingerprintStream : Stream
    {
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => hash.AppendData(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => hash.AppendData(buffer);
        internal byte[] Finish() => hash.GetHashAndReset();
        protected override void Dispose(bool disposing) { if (disposing) hash.Dispose(); base.Dispose(disposing); }
    }

    private static byte[] Contents()
    {
        using var output = new FingerprintStream();
        JsonSerializer.Serialize(output, WorkroomStorage.State.Items, WorkroomStorageState.JsonOptions);
        return output.Finish();
    }

    private static bool Current(Plan plan, bool insideRun) => Available(plan.Epoch, insideRun) &&
        WorkroomStorage.State.Revision == plan.Revision &&
        ReferenceEquals(WorkroomStorage.State.Find(plan.Source.Id), plan.Source) && plan.Source.Place == "room" &&
        Contents().AsSpan().SequenceEqual(plan.Baseline.AsSpan());

    private static HashSet<string> ComponentSlots() => WorkroomComponentTemplates.All
        .SelectMany(template => WorkroomComponentAssembly.Slots(template))
        .Append(WorkroomComponentAssembly.EntryBoard).Append(WorkroomComponentAssembly.Disassembly)
        .ToHashSet(StringComparer.Ordinal);

    private static bool MatchesTarget(Destination destination, WorkroomPanels.Kind target) => target switch
    {
        WorkroomPanels.Kind.Components => destination is Destination.Processing or Destination.ComponentAssembly,
        WorkroomPanels.Kind.Motherboard => destination == Destination.Motherboard,
        WorkroomPanels.Kind.Case => destination == Destination.Case,
        _ => false
    };

    private static bool Build(string id, long epoch, int orientation, WorkroomPanels.Kind target, bool insideRun, out Plan? plan, out string failure)
    {
        plan = null; failure = Prefix + "unavailable";
        try
        {
            if (!Available(epoch, insideRun)) return false;
            var state = WorkroomStorage.State;
            var incoming = state.Find(id);
            if (incoming?.Place != "room") { failure = Prefix + "source"; return false; }
            if (incoming.Item.Count != 1) { failure = Prefix + "single"; return false; }
            failure = Prefix + "invalid";
            if (orientation is < 0 or > 3) return false;
            WorkroomItemCodec.Validate(incoming.Item);
            // Validate the original too: a move must not accidentally repair
            // corrupt slot relationships or conceal duplicate native identities.
            state.ValidateReplacement(state.Items.ToList());
            var proposed = new Plan { Epoch = epoch, Revision = state.Revision, Source = incoming, Baseline = Contents() };
            var item = incoming.Item;
            var returning = new HashSet<string>(StringComparer.Ordinal);
            if (ServerRoomMachine.TryDefinition(item.Identifier, out _))
            { failure = Prefix + "server_room"; return false; }
            var spec = Components.Find(item.Identifier);
            if (spec?.Owner.Tag == Components.MotherboardTag || spec?.Owner.Tag == Components.CaseTag)
            {
                proposed.Family = spec.Owner.Tag == Components.MotherboardTag ? WorkroomMachineAssembly.Motherboard : WorkroomMachineAssembly.Case;
                proposed.Target = proposed.Family == WorkroomMachineAssembly.Motherboard ? Destination.Motherboard : Destination.Case;
                proposed.Slot = WorkroomMachineAssembly.MainSlot(proposed.Family);
                returning.UnionWith(WorkroomMachineAssembly.Slots(proposed.Family));
                proposed.RepairMachine = spec.Broken && item.Broken && spec.Tier is >= 1 and <= 3 &&
                    item.Nodes.Count == 1 && item.Nodes[0].Machine == null && item.Nodes[0].Composition == null &&
                    item.Nodes[0].SlotItems.Count == 0 && !WorkroomMachineAssembly.ActiveTag(item.Nodes[0], CaseUnboxing.LockedTagName);
            }
            else if (WorkroomComponentTemplates.TryWhole(item.Identifier, out var whole, out _, out _))
            {
                Processing(proposed, whole.Key, WorkroomPartProcessing.Mode.Disassemble);
                returning.UnionWith(ComponentSlots());
            }
            else if (WorkroomComponentTemplates.TryPart(item.Identifier, out var family, out var kind, out _))
            {
                if (!WorkroomComponentAssembly.Intact(item))
                {
                    Processing(proposed, family.Key, WorkroomPartProcessing.Mode.Repair);
                    returning.UnionWith(ComponentSlots());
                }
                else if (kind == "body")
                {
                    proposed.Target = Destination.ComponentAssembly; proposed.Family = family.Key;
                    proposed.Slot = WorkroomComponentAssembly.Slot(family, "body");
                    returning.UnionWith(ComponentSlots());
                }
                else if (!UniqueSlot(proposed, item, target, out failure)) return false;
            }
            else if (spec?.Owner.Tag == Components.CpuTag)
            {
                if (!WorkroomComponentAssembly.Intact(item))
                {
                    Processing(proposed, "", WorkroomPartProcessing.Mode.Repair);
                    returning.UnionWith(ComponentSlots());
                }
                else if (!UniqueSlot(proposed, item, target, out failure)) return false;
            }
            else { failure = Prefix + "unsupported"; return false; }
            if (!MatchesTarget(proposed.Target, target)) { failure = Prefix + "unsupported"; return false; }

            // Only entering the component assembly mode leaves processing.
            // Independent motherboard/case work remains present in every route.
            if (proposed.Target == Destination.ComponentAssembly) returning.Add(WorkroomComponentAssembly.Disassembly);
            var old = state.Items.Where(record => record.Place == "slot" && returning.Contains(record.Slot)).ToArray();
            var oldIds = old.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
            var next = state.Items.Where(record => !oldIds.Contains(record.Id)).ToList();
            var staged = next.Where(record => record.Place == "slot").ToDictionary(record => record.Slot, StringComparer.Ordinal);
            WorkroomStorageState.Record? Find(string slot) => staged.TryGetValue(slot, out var record) ? record : null;
            if (!WorkroomComponentAssembly.Accepts(proposed.Slot, item, Find)) { failure = Prefix + "no_slot"; return false; }
            // Validate the incoming pose without mutating live records. The
            // shared return planner excludes its footprint from room packing.
            WorkroomComponentAssembly.FillSlot(next, staged, incoming, proposed.Slot, orientation);
            if (!WorkroomSelection.CanReturn(old, incoming.Id))
            { failure = Prefix + "full"; return false; }
            // Original records were fully validated above. The only changed
            // item is validated by Reorient; Accepts and the return planner
            // establish placement. Replace revalidates the full result
            // when publishing, so hover need not parse every payload twice.
            proposed.Returning = old;
            if (!Current(proposed, insideRun)) { failure = Prefix + "changed"; return false; }
            plan = proposed; failure = ""; return true;
        }
        catch (Exception)
        { failure = Prefix + "invalid"; return false; }
    }

    private static void Processing(Plan plan, string family, WorkroomPartProcessing.Mode mode)
    {
        plan.Target = Destination.Processing; plan.Family = family;
        plan.ProcessingMode = mode; plan.Slot = WorkroomComponentAssembly.Disassembly;
    }

    private static bool UniqueSlot(Plan plan, WorkroomItemCodec.Snapshot item, WorkroomPanels.Kind target, out string failure)
    {
        // Count actual cells, not kinds or families. Two RAM sockets remain
        // ambiguous even when both belong to the same motherboard.
        var candidates = new List<(Destination Target, string Family, string Slot)>();
        foreach (var family in WorkroomComponentTemplates.All)
        foreach (var slot in WorkroomComponentAssembly.Slots(family))
            if (target == WorkroomPanels.Kind.Components && slot != WorkroomComponentAssembly.Slot(family, "body") && WorkroomComponentAssembly.Accepts(slot, item))
                candidates.Add((Destination.ComponentAssembly, family.Key, slot));
        foreach (var family in new[] { WorkroomMachineAssembly.Motherboard, WorkroomMachineAssembly.Case })
        foreach (var slot in WorkroomMachineAssembly.Slots(family))
            if (MatchesTarget(family == WorkroomMachineAssembly.Motherboard ? Destination.Motherboard : Destination.Case, target) &&
                slot != WorkroomMachineAssembly.MainSlot(family) && WorkroomMachineAssembly.Accepts(slot, item))
                candidates.Add((family == WorkroomMachineAssembly.Motherboard ? Destination.Motherboard : Destination.Case, family, slot));
        failure = Prefix + (candidates.Count > 1 ? "ambiguous" : "no_slot");
        if (candidates.Count != 1) return false;
        (plan.Target, plan.Family, plan.Slot) = candidates[0];
        return true;
    }
}
