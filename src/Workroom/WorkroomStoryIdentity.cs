using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes;

namespace PCExpansion;

// Queries only: never manufacture a saveable snapshot from these projections.
// Native graph edges and embedded snapshots both participate in identity checks.
internal static class WorkroomStoryIdentity
{
    private static IEnumerable<GameItem> Items(IEnumerable<GameItem> roots)
    {
        var seen = new HashSet<IntPtr>();
        var pending = new Stack<Il2CppObjectBase>();
        foreach (var root in roots)
        {
            if (root == null) continue;
            pending.Push(root);
            while (pending.Count != 0)
            {
                var node = pending.Pop();
                if (!seen.Add(node.Pointer)) continue;
                var item = node.TryCast<GameItem>();
                if (item != null) yield return item;
                var children = node.TryCast<GraphNodeStorage>()?.children;
                if (children != null)
                    for (var i = children.Count - 1; i >= 0; i--)
                        if (children[i] != null) pending.Push(children[i]);
            }
        }
    }

    internal static HashSet<int> Owned()
    {
        using var timing = new WorkroomFeedbackDiagnostics.Operation("LuoxiOwnedIdentities");
        var roots = new List<GameItem>();
        var owned = EmporiumEntry.Instance?.GetAllOwnedItems();
        if (owned != null) foreach (var item in owned) if (item != null) roots.Add(item);
        var ids = new HashSet<int>();
        foreach (var item in Items(roots))
        {
            ids.Add(item.uniqueId);
            if (WorkroomMachineFacts.Read(item) is { } machine) ids.UnionWith(machine.StoredIdentities);
            foreach (var content in WorkroomCrateContents.Read(item)) ids.UnionWith(WorkroomItemCodec.Identities(content));
            var parts = WorkroomComponentParts.ReadComposition(item);
            if (parts != null) foreach (var part in parts) ids.UnionWith(WorkroomItemCodec.Identities(part));
        }
        return ids;
    }

    internal static bool Matches(WorkroomItemCodec.Snapshot expected, GameItem root)
    {
        // SameOriginal deliberately ignores prices, placement and transient tags.
        // Read exactly its contract; Capture/Validate remain mandatory at custody
        // entry and Restore still verifies its full native round trip.
        var actual = new WorkroomItemCodec.Snapshot
        {
            Identifier = Core.Clean(root.identifier), Count = root.unitCount,
            Broken = root.IsTag(Components.BrokenTag) || root.IsTag("BROKEN_MACHINE")
        };
        foreach (var item in Items(new[] { root }))
        {
            if (item.IsTag(WorkroomMachineAssembly.Tag)) return false;
            actual.Nodes.Add(new WorkroomItemCodec.Node
            {
                UniqueId = item.uniqueId, Identifier = Core.Clean(item.identifier),
                Part = WorkroomComponentParts.ReadPart(item),
                SlotItems = WorkroomCrateContents.Read(item),
                Composition = WorkroomComponentParts.ReadComposition(item)
            });
        }
        return LuoxiStoryGoods.SameOriginal(expected, actual);
    }
}
