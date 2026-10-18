using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using UnityEngine;

namespace PCExpansion;

// One compatibility projection shared by room, box and loose shop stock.
// Refresh is bounded by the active slot set and custody revision, not item count per frame.
internal static class WorkroomCandidateHighlight
{
    private static string[] slots = Array.Empty<string>();
    private static long epoch = -1, stateRevision = -1;
    private static string locale = "";
    private static readonly Dictionary<WorkroomItemCodec.Snapshot, string> matching = new();
    private static readonly List<GameObject> nativeFrames = new();
    internal static long Revision { get; private set; }

    internal static void UpdateNative()
    {
        var current = WorkroomStorage.State.Ready ? WorkroomPickerUi.HighlightSlots.Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
        if (epoch == WorkroomStorage.State.Epoch && stateRevision == WorkroomStorage.State.Revision &&
            locale == LanguageText.LocaleCode && slots.SequenceEqual(current)) return;
        ClearFrames(); matching.Clear(); slots = current;
        epoch = WorkroomStorage.State.Epoch; stateRevision = WorkroomStorage.State.Revision;
        locale = LanguageText.LocaleCode; Revision++;
        if (slots.Length == 0 || WorkroomStorage.Busy || WorkroomItemCodec.CleanupPending) return;
        try
        {
            foreach (var candidate in WorkroomSelection.Candidates(WorkroomSelection.Source.Shop))
            {
                if (!Matches(candidate.Item)) continue;
                var item = candidate.Native?.TryCast<GameItemElement>();
                if (item == null || item.IsDestroyed() || item.rectTransform == null) continue;
                nativeFrames.Add(Frame(item.rectTransform, "PCExpansionCompatible"));
            }
        }
        catch (Exception ex) { ClearFrames(); Core.Log?.Warning("[装配候选提示] " + ex.Message); }
    }

    internal static bool Matches(WorkroomItemCodec.Snapshot item) => Description(item).Length > 0;
    internal static string Description(WorkroomItemCodec.Snapshot item)
    {
        if (slots.Length == 0) return "";
        if (matching.TryGetValue(item, out var cached)) return cached;
        var names = slots.Where(s => WorkroomComponentAssembly.At(s) == null && WorkroomSelection.Suitable(s, item))
            .Select(SlotName).Distinct().ToArray();
        return matching[item] = names.Length == 0 ? "" : WorkroomSelection.Text("fits_slots", string.Join("、", names));
    }

    private static string SlotName(string slot)
    {
        foreach (var family in WorkroomComponentTemplates.All)
            foreach (var kind in family.Kinds)
                for (var i = 0; i < family.MaxCount(kind); i++)
                    if (WorkroomComponentAssembly.Slot(family, kind, i) == slot) return family.PartName(kind);
        foreach (var family in new[] { WorkroomMachineAssembly.Motherboard, WorkroomMachineAssembly.Case })
            foreach (var kind in WorkroomMachineAssembly.Kinds(family))
                for (var i = 0; i < WorkroomMachineAssembly.MaxCount(family, kind); i++)
                    if (WorkroomMachineAssembly.Slot(family, kind, i) == slot) return WorkroomMachineAssembly.Text(kind);
        return WorkroomSelection.Text("compatible");
    }

    internal static GameObject Frame(Transform parent, string name)
    {
        var root = AssemblyDebugUi.New(name, parent); AssemblyDebugUi.Stretch(root);
        var color = new Color(1f, .9f, .48f, .95f);
        void Edge(string id, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size)
        {
            var edge = AssemblyDebugUi.Image(root.transform, id, color, false);
            var rect = AssemblyDebugUi.Rect(edge.gameObject);
            rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot;
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = size;
        }
        Edge("Top", new Vector2(0,1), Vector2.one, new Vector2(.5f,1), new Vector2(0,1));
        Edge("Bottom", Vector2.zero, new Vector2(1,0), new Vector2(.5f,0), new Vector2(0,1));
        Edge("Left", Vector2.zero, new Vector2(0,1), new Vector2(0,.5f), new Vector2(1,0));
        Edge("Right", new Vector2(1,0), Vector2.one, new Vector2(1,.5f), new Vector2(1,0));
        return root;
    }

    private static void ClearFrames()
    {
        foreach (var frame in nativeFrames) if (WorkroomUiCleanup.IsAlive(frame)) UnityEngine.Object.Destroy(frame);
        nativeFrames.Clear();
    }
    internal static void ClearNative()
    {
        ClearFrames(); slots = Array.Empty<string>(); matching.Clear(); epoch = stateRevision = -1; Revision++;
    }
}
