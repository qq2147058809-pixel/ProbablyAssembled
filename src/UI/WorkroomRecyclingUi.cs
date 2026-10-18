using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>A selection preview: input records stay at their original locations until the batch commits.</summary>
internal sealed class WorkroomRecyclingUi
{
    private sealed class Group
    {
        internal readonly List<WorkroomStorageState.Record> Records = new();
        internal WorkroomStorageState.Record First => Records[0];
    }
    private sealed class Row
    {
        internal GameObject Root = null!, Minus = null!, Number = null!, Plus = null!, Remove = null!;
        internal Image Icon = null!;
        internal TextMeshProUGUI Label = null!;
        internal Group? Group;
    }
    private readonly WorkroomAssemblyWindow owner;
    private readonly GameObject root;
    private readonly Canvas canvas;
    private readonly TMP_FontAsset font;
    private readonly List<Group> groups = new();
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private readonly Row[] rows = new Row[3];
    private readonly GameObject[] materialButtons = new GameObject[5], tabs = new GameObject[2];
    private readonly TextMeshProUGUI pageLabel, summary;
    private readonly GameObject sourceButton, clear, ghostRoot;
    private readonly Image ghost;
    private WorkroomBatchRecycling.Plan? plan;
    private long epoch = -1, revision = -1;
    private int page, chosenPage, sourceFilter, materialIndex, lastCommitFrame = -1;
    private string locale = "";
    private bool busy, keyboardOwned, replaceNumber, selectedTab;
    private string? stagedId;
    private int neutralFrames, inputFrame = -1, readFrame = -1;
    private Group? editing;
    private string numberText = "";
    private static readonly string[] Keys = { "title", "available", "selected", "source_all", "source_room", "source_box", "source_slot",
        "source_line", "selected_line", "material", "summary", "empty", "page", "clear", "execute", "full", "changed", "unavailable",
        "hint", "pick_failed", "invalid_material", "invalid_source", "zero", "ready", "done" };
    internal static string FontText => string.Join("", Keys.Select(k => Text(k, 0, 0, 0, 0, 0, 0, 0, 0))) +
        string.Join("", WorkroomBatchRecycling.MaterialIds.Select(MaterialName)) + "0123456789+-×↑↓";
    private static string Text(string key, params object[] values) => WorkroomBatchRecycling.Text(key, values);
    private static string MaterialName(string id) => LanguageText.Get("material." + id);
    internal bool Open => owner.Open && WorkroomUiCleanup.IsAlive(root) && root.activeSelf;
    internal bool Dragging => Open && owner.Dragging;
    internal bool Alive => WorkroomUiCleanup.IsAlive(owner.Root) && WorkroomUiCleanup.IsAlive(root);
    internal bool CanCommit => Current && editing == null && plan?.Allowed == true && !busy;
    internal bool OwnsTextInput { get { SampleInput(); return keyboardOwned; } }
    internal int SiblingIndex(Vector2 point) => Hit(point) ? owner.Root.transform.GetSiblingIndex() : -1;
    internal bool Hit(Vector2 point) => Open && owner.Hit(point) &&
        RectTransformUtility.ScreenPointToLocalPointInRectangle(AssemblyDebugUi.Rect(owner.Root), point, null, out var p) &&
        p.y <= -114 && p.y >= -386 && p.x >= 176 && p.x <= 704;
    internal bool HitDrop(Vector2 point) => Hit(point) &&
        RectTransformUtility.ScreenPointToLocalPointInRectangle(AssemblyDebugUi.Rect(owner.Root), point, null, out var p) &&
        p.y <= -148 && p.y >= -270 && p.x >= 188 && p.x <= 692;

    internal WorkroomRecyclingUi(Canvas canvas, WorkroomAssemblyWindow owner, TMP_FontAsset font)
    {
        this.canvas = canvas; this.font = font; this.owner = owner;
        root = AssemblyDebugUi.New("MixedRecycling", owner.Root.transform);
        AssemblyDebugUi.Stretch(root);
        WorkroomPanelSkin.Panel(root, "RecyclingBody", 176, 114, 528, 272, new Color(.12f,.17f,.14f,.97f));
        for (var i = 0; i < tabs.Length; i++)
        {
            var index = i;
            tabs[i] = Button(root.transform, "ListTab" + i, 188 + i * 136, 118, 130,
                Text(i == 0 ? "available" : "selected"), () => { FinishNumber(); selectedTab = index == 1; RenderRows(); });
        }
        sourceButton = Button(root.transform, "Source", 460, 118, 100, Text("source_all"),
            () => { FinishNumber(); sourceFilter = (sourceFilter + 1) % 3; page = 0; RenderRows(); });
        clear = Button(root.transform, "ClearBatch", 566, 118, 126, Text("clear"), () => { Clear(); Requote(); });
        for (var i = 0; i < rows.Length; i++) rows[i] = MakeRow(i);
        Button(root.transform, "Previous", 188, 274, 48, "↑", () => { FinishNumber(); if (selectedTab) chosenPage--; else page--; RenderRows(); });
        Button(root.transform, "Next", 644, 274, 48, "↓", () => { FinishNumber(); if (selectedTab) chosenPage++; else page++; RenderRows(); });
        pageLabel = Label("ListPage", "", 244, 274, 392, 26, 11);
        for (var i = 0; i < materialButtons.Length; i++)
        {
            var index = i;
            materialButtons[i] = Button(root.transform, "Material" + i, 188 + i * 102, 306, 96,
                MaterialName(WorkroomBatchRecycling.MaterialIds[i]), () => { FinishNumber(); materialIndex = index; Requote(); });
        }
        summary = Label("BatchSummary", "", 188, 336, 504, 48, 10);
        summary.richText = false;
        var hint = Label("Hint", Text("hint"), 18, 314, 134, 70, 10);
        hint.richText = false;
        hint.overflowMode = TextOverflowModes.Truncate;
        ghostRoot = AssemblyDebugUi.New("RecyclingDragPreview", canvas.transform);
        ghost = AssemblyDebugUi.Image(ghostRoot.transform, "Icon", Color.white, false);
        ghost.preserveAspect = true;
        ghostRoot.SetActive(false);
        root.SetActive(false);
    }

    private GameObject Button(Transform parent, string name, float x, float y, float width, string text, Action action)
    {
        var b = WorkroomPanelSkin.Button(canvas, parent, name, text, font, 12, () =>
        {
            if (!Open || busy || epoch != WorkroomStorage.State.Epoch) return;
            if (revision != WorkroomStorage.State.Revision) { Refresh(); return; }
            action();
        });
        AssemblyDebugUi.Place(b, x, y, width, 26);
        return b;
    }
    private TextMeshProUGUI Label(string name, string text, float x, float y, float width, float height, int size)
    {
        var label = AssemblyDebugUi.Text(root.transform, name, text, font, size, true);
        label.enableWordWrapping = true;
        label.alignment = TextAlignmentOptions.TopLeft;
        AssemblyDebugUi.Place(label.gameObject, x, y, width, height);
        return label;
    }
    private Row MakeRow(int index)
    {
        var row = new Row();
        row.Root = AssemblyDebugUi.Image(root.transform, "RecyclingRow" + index,
            new Color(.13f, .18f, .17f, .95f), false).gameObject;
        UiRoundedGeometry.Fill(row.Root.GetComponent<Image>());
        AssemblyDebugUi.Place(row.Root, 188, 148 + index * 42, 504, 38);
        row.Icon = AssemblyDebugUi.Image(row.Root.transform, "Icon", Color.white, false);
        row.Icon.preserveAspect = true;
        AssemblyDebugUi.Place(row.Icon.gameObject, 4, 3, 32, 32);
        row.Label = AssemblyDebugUi.Text(row.Root.transform, "Description", "", font, 10, true);
        row.Label.richText = false; row.Label.enableWordWrapping = true;
        row.Minus = Button(row.Root.transform, "Minus", 300, 6, 28, "−", () => Change(row.Group, -1));
        row.Number = Button(row.Root.transform, "Quantity", 332, 6, 82, "", () => StartNumber(row.Group));
        row.Plus = Button(row.Root.transform, "Plus", 462, 6, 36, "+", () => Change(row.Group, 1));
        row.Remove = Button(row.Root.transform, "Remove", 450, 6, 48, "×", () => SetCount(row.Group, 0));
        return row;
    }
    private static string Source(WorkroomStorageState.Record r) => Text(r.Place == "room" ? "source_room" : r.Place == "box" ? "source_box" : "source_slot");
    private int Count(Group group) => group.Records.Count(r => selected.Contains(r.Id));
    private bool Current => Open && epoch == WorkroomStorage.State.Epoch && revision == WorkroomStorage.State.Revision;

    internal void Show()
    {
        Clear(); epoch = WorkroomStorage.State.Epoch; revision = -1;
        page = chosenPage = 0; selectedTab = false; stagedId = null;
        root.SetActive(true); root.transform.SetAsLastSibling(); Refresh();
    }
    private void Refresh()
    {
        if (!Open) return;
        if (epoch != WorkroomStorage.State.Epoch) { Close(); return; }
        editing = null;
        groups.Clear();
        foreach (var r in WorkroomStorage.State.Items.Where(WorkroomBatchRecycling.Available)
            .OrderBy(r => r.Place == "room" ? 0 : r.Place == "box" ? 1 : 2).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            var group = groups.FirstOrDefault(g => g.First.Place == r.Place && g.First.Item.Value == r.Item.Value &&
                g.First.Item.Name == r.Item.Name && WorkroomRecyclingRules.SamePhysicalSpec(g.First.Item, r.Item));
            if (group == null) { group = new Group(); groups.Add(group); }
            group.Records.Add(r);
        }
        var available = groups.SelectMany(g => g.Records).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var removed = selected.RemoveWhere(id => !available.Contains(id));
        var staged = WorkroomComponentAssembly.At(WorkroomComponentAssembly.Disassembly);
        if (stagedId != staged?.Id)
        {
            stagedId = staged?.Id;
            if (staged != null && WorkroomBatchRecycling.Available(staged)) selected.Add(staged.Id);
        }
        revision = WorkroomStorage.State.Revision; locale = LanguageText.LocaleCode;
        Requote();
        if (removed > 0) WorkroomStorage.NotifyText(Text("changed"));
    }
    private void Change(Group? group, int delta)
    {
        FinishNumber();
        if (group == null || !Current) return;
        SetCount(group, Count(group) + delta);
    }
    private void SetCount(Group? group, int count)
    {
        if (group == null || !Current) return;
        editing = null;
        count = Math.Clamp(count, 0, group.Records.Count);
        var current = Count(group);
        if (count < current)
            foreach (var r in group.Records.Where(r => selected.Contains(r.Id)).Reverse().Take(current - count)) selected.Remove(r.Id);
        else
            foreach (var r in group.Records.Where(r => !selected.Contains(r.Id)).Take(count - current)) selected.Add(r.Id);
        Requote();
    }
    internal bool Add(IReadOnlyList<string> ids, long generation)
    {
        if (!Open || busy || generation != epoch || generation != WorkroomStorage.State.Epoch) return false;
        if (revision != WorkroomStorage.State.Revision) Refresh();
        if (!Current) return false;
        var records = ids.Distinct(StringComparer.Ordinal).Select(WorkroomStorage.State.Find).ToArray();
        if (records.Length == 0 || records.Any(r => r == null || !WorkroomBatchRecycling.Available(r)))
        { WorkroomStorage.NotifyText(Text("pick_failed")); return false; }
        FinishNumber();
        foreach (var r in records) selected.Add(r!.Id);
        Requote(); return true;
    }
    private void Requote()
    {
        plan = selected.Count == 0 ? null : WorkroomBatchRecycling.Preview(selected.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            WorkroomBatchRecycling.MaterialIds[materialIndex], epoch);
        RenderRows(); RenderSummary();
        if (plan != null && !plan.Allowed) WorkroomStorage.NotifyText(Text(plan.Reason));
    }
    private void RenderRows()
    {
        var visible = groups.Where(g => selectedTab ? Count(g) > 0 :
            sourceFilter == 0 || g.First.Place == (sourceFilter == 1 ? "room" : "box")).ToArray();
        var current = Math.Clamp(selectedTab ? chosenPage : page, 0, Math.Max(0, (visible.Length - 1) / rows.Length));
        if (selectedTab) chosenPage = current; else page = current;
        for (var i = 0; i < rows.Length; i++) RenderRow(rows[i], visible.ElementAtOrDefault(current * rows.Length + i), selectedTab);
        pageLabel.text = Text("page", current + 1, Math.Max(1, (visible.Length + rows.Length - 1) / rows.Length));
        for (var i = 0; i < tabs.Length; i++) WorkroomPanelSkin.Selected(tabs[i], (i == 1) == selectedTab);
        sourceButton.GetComponent<Button>().interactable = !selectedTab;
        sourceButton.GetComponentInChildren<TextMeshProUGUI>().text = Text(new[] { "source_all", "source_room", "source_box" }[selectedTab ? 0 : sourceFilter]);
    }
    private void RenderRow(Row row, Group? group, bool picked)
    {
        row.Group = group; row.Root.SetActive(group != null);
        if (group == null) return;
        var record = group.First;
        row.Icon.sprite = WorkroomInventoryUi.Icon(record.Item);
        var count = Count(group);
        row.Label.text = Text(picked ? "selected_line" : "source_line", record.Item.Name,
            WorkroomComponentTemplates.Text(record.Item.Broken ? "broken_short" : "intact_short"), Source(record), record.Item.Value,
            picked ? count : group.Records.Count - count);
        row.Plus.GetComponent<Button>().interactable = count < group.Records.Count;
        row.Minus.SetActive(picked); row.Number.SetActive(picked); row.Remove.SetActive(picked);
        AssemblyDebugUi.Place(row.Label.gameObject, 42, 1, picked ? 250 : 408, 36);
        AssemblyDebugUi.Place(row.Plus, picked ? 418 : 462, 6, picked ? 28 : 36, 26);
        if (picked)
        {
            row.Minus.GetComponent<Button>().interactable = count > 0;
            row.Number.GetComponentInChildren<TextMeshProUGUI>().text = ReferenceEquals(editing, group) ? numberText + "_" : count.ToString();
        }
        AssemblyDebugFonts.Prepare(row.Label.text, row.Label.text);
    }
    private void RenderSummary()
    {
        for (var i = 0; i < 5; i++) WorkroomPanelSkin.Selected(materialButtons[i], i == materialIndex);
        summary.text = plan == null ? Text("empty") : Text("summary", selected.Count, plan.TotalValue, plan.Budget,
            MaterialName(plan.MaterialId), plan.MaterialValue, plan.OutputCount, plan.Remainder, plan.RecoveryPercent);
        clear.GetComponent<Button>().interactable = selected.Count != 0;
        AssemblyDebugFonts.Prepare(summary.text, summary.text);
    }
    internal void Commit()
    {
        FinishNumber();
        if (!Current || busy || plan?.Allowed != true || lastCommitFrame == Time.frameCount) return;
        var command = plan; plan = null; lastCommitFrame = Time.frameCount; busy = true;
        try
        {
            if (WorkroomBatchRecycling.Apply(command)) { selected.Clear(); RoomAudio.Play("recycle"); }
            else RoomAudio.Play("warning");
        }
        finally { busy = false; Refresh(); }
    }
    private void StartNumber(Group? group)
    {
        FinishNumber();
        if (group == null || !Current) return;
        editing = group; numberText = Count(group).ToString(); replaceNumber = true;
        keyboardOwned = true; neutralFrames = 0; RenderRows(); RenderSummary();
    }
    private void FinishNumber()
    {
        var group = editing; editing = null;
        if (group != null && Current) SetCount(group, int.TryParse(numberText, out var count) ? count : 0);
    }
    private void SampleInput()
    {
        if (editing != null) { keyboardOwned = true; neutralFrames = 0; }
        if (inputFrame == Time.frameCount) return;
        inputFrame = Time.frameCount;
        if (!keyboardOwned || editing != null) return;
        var neutral = WorkroomGameAdapter.InputNeutral() && Input.compositionString.Length == 0;
        neutralFrames = neutral ? neutralFrames + 1 : 0;
        if (neutralFrames >= 2) keyboardOwned = false;
    }
    internal void UpdateInput(bool allowed, bool front)
    {
        SampleInput();
        if (editing == null) return;
        var row = rows.FirstOrDefault(r => ReferenceEquals(r.Group, editing));
        if (!allowed || row == null || Input.GetMouseButtonDown(0) && (!front ||
            !RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(row.Number), Input.mousePosition, null)))
        { FinishNumber(); return; }
        if (readFrame == Time.frameCount || Input.compositionString.Length != 0) return;
        readFrame = Time.frameCount;
        foreach (var c in Input.inputString)
        {
            if (c == '\r' || c == '\n') { FinishNumber(); break; }
            if (c == '\b') { numberText = replaceNumber || numberText.Length == 0 ? "" : numberText[..^1]; replaceNumber = false; }
            else if (c is >= '0' and <= '9')
            {
                var next = (replaceNumber ? "" : numberText) + c;
                if (!int.TryParse(next, out _)) continue;
                numberText = next; replaceNumber = false;
            }
        }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) FinishNumber();
        RenderRows(); RenderSummary();
    }
    internal void Update()
    {
        SampleInput();
        if (!Open) return;
        if (epoch != WorkroomStorage.State.Epoch) { Close(); return; }
        if (revision != WorkroomStorage.State.Revision || locale != LanguageText.LocaleCode) Refresh();
    }
    private void Clear() { editing = null; selected.Clear(); plan = null; CancelPreview(); }
    internal void Close()
    {
        if (!Open && selected.Count == 0 && plan == null && editing == null) return;
        Clear(); stagedId = null; WorkroomUiCleanup.Deactivate(root);
    }
    internal void Release() { Close(); keyboardOwned = false; groups.Clear(); WorkroomUiCleanup.Deactivate(ghostRoot); }
    internal void CancelPreview() => WorkroomUiCleanup.Deactivate(ghostRoot);
    internal void RoomPreview(Sprite? sprite, Vector2 position, Vector2 size, Color color, int orientation, bool flipped, bool visible)
    {
        if (!Open || !visible || !AssemblyDebugUi.Alive(sprite)) { CancelPreview(); return; }
        ghost.sprite = sprite; ghost.color = color;
        var rect = AssemblyDebugUi.Rect(ghostRoot);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        WorkroomItemSprite.Pose(ghost, orientation, flipped, size);
        ghostRoot.transform.SetAsLastSibling(); ghostRoot.SetActive(true);
    }
}
