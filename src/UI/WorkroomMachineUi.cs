using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>主板与机箱共用保管槽，各自持有独立非模态装配及材料维修视图。</summary>
internal sealed class WorkroomMachineUi
{
    private sealed class FamilyView
    {
        internal string Family = "";
        internal WorkroomAssemblyWindow Window = null!, RepairWindow = null!;
        internal TextMeshProUGUI Hint = null!, Summary = null!, Status = null!, RepairStatus = null!, Preview = null!;
        internal GameObject Assemble = null!, Disassemble = null!, Return = null!, ExecuteRepair = null!, Viewport = null!, AutoFill = null!;
        internal long SummaryEpoch = -1, SummaryRevision = -1;
        internal bool SummaryChinese;
        internal readonly List<(string Kind, int Index, WorkroomAssemblySlotUi Ui)> Content = new();
        internal string PreviewText = "";
        internal float PreviewOffset, PreviewHeight;
    }

    private readonly List<FamilyView> families = new();
    private readonly List<WorkroomAssemblySlotUi> slots = new();
    private readonly GameObject ghostRoot;
    private readonly Image ghost;
    private long epoch, revision = -1, generation = -1;
    private string lastStatus = "";
    private IEnumerable<WorkroomAssemblyWindow> Windows => families.SelectMany(family => new[] { family.Window, family.RepairWindow });
    internal bool Open => Windows.Any(window => window.Open);
    internal bool Dragging => Windows.Any(window => window.Dragging);
    internal bool Alive => AssemblyDebugUi.Alive(ghostRoot) && Windows.All(window => AssemblyDebugUi.Alive(window.Root));
    private WorkroomAssemblyWindow? TopAt(Vector2 point) => Windows.Where(window => window.Hit(point))
        .OrderByDescending(window => window.Root.transform.GetSiblingIndex()).FirstOrDefault();
    internal int SiblingIndex(Vector2 point) => TopAt(point)?.Root.transform.GetSiblingIndex() ?? -1;
    internal bool OwnsRoot(GameObject root) => Windows.Any(window => window.Root.Pointer == root.Pointer);

    internal static string FontText => string.Join("", new[] { "motherboard_title", "case_title", "repair_title", "need_body",
        "assembly_hint", "assemble", "disassemble", "repair", "return_all", "repair_execute", "cpu", "cooler", "gpu", "ram", "ssd",
        "motherboard", "case", "psu", "hdd", "fan", "motherboard_body", "case_body", "quick_unavailable" }
        .Select(key => WorkroomMachineAssembly.Text(key, "", 0))) + WorkroomComponentTemplates.Text("auto_fill") + "T12345×↑↓—";

    internal WorkroomMachineUi(Canvas canvas, Transform reference, TMP_FontAsset font)
    {
        families.Add(BuildFamily(canvas, reference, font, WorkroomMachineAssembly.Motherboard, 0));
        families.Add(BuildFamily(canvas, reference, font, WorkroomMachineAssembly.Case, 1));
        ghostRoot = AssemblyDebugUi.New("MachineDragPreview", canvas.transform);
        ghostRoot.SetActive(false);
        ghost = AssemblyDebugUi.Image(ghostRoot.transform, "Icon", Color.white, false);
        ghost.preserveAspect = true;
    }

    private FamilyView BuildFamily(Canvas canvas, Transform reference, TMP_FontAsset font, string family, int offset)
    {
        var view = new FamilyView { Family = family };
        view.Window = new WorkroomAssemblyWindow(canvas, reference, font, "MachineAssembly_"+family,
            WorkroomMachineAssembly.Text(family+"_title"), 352+offset*16, 64, 720, 424, () => Hide(view.Window));
        view.Hint = Label(view.Window.Root, font, "Hint", 18, 248, 134, 60);
        AddSlot(canvas, view.Window.Root, font, WorkroomMachineAssembly.MainSlot(family),
            WorkroomMachineAssembly.Text(family+"_body"), 18, 50, 134, 196, true);
        var cell = 0;
        foreach (var kind in WorkroomMachineAssembly.Kinds(family))
        for (var index = 0; index < WorkroomMachineAssembly.MaxCount(family, kind); index++, cell++)
        {
            var name = WorkroomMachineAssembly.Text(kind) + (WorkroomMachineAssembly.MaxCount(family, kind) > 1 ? (index+1).ToString() : "");
            var slot = AddSlot(canvas, view.Window.Root, font, WorkroomMachineAssembly.Slot(family, kind, index), name,
                130+(cell%5)*101, 78+(cell/5)*78, 94, 72);
            PlacePart(slot, family, kind, index);
            view.Content.Add((kind, index, slot));
        }
        view.Summary = Label(view.Window.Root, font, "Summary", 18, 308, 134, 82);
        view.Summary.alignment = TextAlignmentOptions.TopLeft;
        view.Status = Label(view.Window.Root, font, "Status", 188, 40, 280, 28);
        view.AutoFill = ActionButton(canvas, view.Window.Root, font, "AutoFill", "", 176, 396, 102,
            () => WorkroomMachineAssembly.AutoFill(family, epoch));
        view.AutoFill.GetComponentInChildren<TextMeshProUGUI>().text = WorkroomComponentTemplates.Text("auto_fill");
        view.Assemble = ActionButton(canvas, view.Window.Root, font, "Assemble", "assemble", 283, 396, 102,
            () => WorkroomMachineAssembly.Assemble(family, epoch));
        view.Disassemble = ActionButton(canvas, view.Window.Root, font, "Disassemble", "disassemble", 390, 396, 102,
            () => WorkroomMachineAssembly.Disassemble(family, epoch));
        ActionButton(canvas, view.Window.Root, font, "Repair", "repair", 497, 396, 102,
            () => { view.RepairWindow.Show(); CancelPreview(); Update(false); });
        view.Return = ActionButton(canvas, view.Window.Root, font, "ReturnAll", "return_all", 604, 396, 100,
            () => WorkroomMachineAssembly.ReturnSlots(WorkroomMachineAssembly.Slots(family), epoch));

        view.RepairWindow = new WorkroomAssemblyWindow(canvas, reference, font, "MachineRepair_"+family,
            WorkroomMachineAssembly.Text("repair_title", WorkroomMachineAssembly.Text(family)), 26+offset*24, 64, 720, 424,
            () => Hide(view.RepairWindow));
        AddSlot(canvas, view.RepairWindow.Root, font, WorkroomMachineAssembly.MainSlot(family),
            WorkroomMachineAssembly.Text(family+"_body"), 18, 50, 134, 196, true);
        AddSlot(canvas, view.RepairWindow.Root, font, WorkroomMachineAssembly.MainSlot(family),
            WorkroomMachineAssembly.Text(family+"_body"), 204, 110, 140, 220);
        view.Viewport = WorkroomPanelSkin.Panel(view.RepairWindow.Root, "PreviewViewport", 360, 88, 332, 262, new Color(.12f,.17f,.14f,.97f));
        var maskType = Il2CppSystem.Type.internal_from_handle(IL2CPP.il2cpp_class_get_type(Il2CppClassPointerStore<RectMask2D>.NativeClassPtr));
        if (view.Viewport.AddComponent(maskType)?.TryCast<RectMask2D>() == null)
            throw new InvalidOperationException("本体维修预览裁剪组件未就绪。");
        view.Preview = Label(view.Viewport, font, "Preview", 13, 6, WorkroomPanelSkin.PreviewWidth, WorkroomPanelSkin.PreviewHeight);
        view.Preview.alignment = TextAlignmentOptions.TopLeft;
        var up = ActionButton(canvas, view.RepairWindow.Root, font, "Previous", "", 360, 356, 160, () => ScrollPreview(view, -90));
        var down = ActionButton(canvas, view.RepairWindow.Root, font, "Next", "", 532, 356, 160, () => ScrollPreview(view, 90));
        up.GetComponentInChildren<TextMeshProUGUI>().text = "↑";
        down.GetComponentInChildren<TextMeshProUGUI>().text = "↓";
        view.ExecuteRepair = ActionButton(canvas, view.RepairWindow.Root, font, "ExecuteRepair", "repair_execute", 176, 396, 528,
            () => WorkroomMachineRepair.Execute(family, epoch));
        view.RepairStatus = Label(view.RepairWindow.Root, font, "Status", 18, 260, 134, 124);
        return view;
    }

    private static void PlacePart(WorkroomAssemblySlotUi ui, string family, string kind, int index)
    {
        if (family == WorkroomMachineAssembly.Motherboard)
        {
            switch (kind)
            {
                case "cpu": ui.Layout(210, 81, 80, 96); break;
                case "cooler": ui.Layout(306, 81, 82, 96); break;
                case "ram": ui.Layout(478+index*51, 68, 46, 148); break;
                case "gpu": ui.Layout(211+index*105, 224, 87, 150); break;
                case "ssd": ui.Layout(493, 262+index*70, 172, 58); break;
            }
        }
        else
        {
            switch (kind)
            {
                case "motherboard": ui.Layout(261, 105, 168, 167); break;
                case "psu": ui.Layout(240, 298, 192, 78); break;
                case "fan": ui.Layout(581, 48+index*84, 88, 80); break;
            }
        }
    }

    private WorkroomAssemblySlotUi AddSlot(Canvas canvas, GameObject parent, TMP_FontAsset font, string slot, string name,
        float x, float y, float width, float height, bool bodyPreview = false)
    {
        var ui = new WorkroomAssemblySlotUi(canvas, parent.transform, font, slot, name, x, y, width, height, bodyPreview);
        slots.Add(ui); return ui;
    }
    private static TextMeshProUGUI Label(GameObject parent, TMP_FontAsset font, string name, float x, float y, float width, float height)
    {
        var label = AssemblyDebugUi.Text(parent.transform, name, "", font, 11, true);
        label.enableWordWrapping = true;
        AssemblyDebugUi.Place(label.gameObject, x, y, width, height); return label;
    }
    private static GameObject ActionButton(Canvas canvas, GameObject parent, TMP_FontAsset font, string name, string key,
        float x, float y, float width, Action action)
    {
        var button = WorkroomPanelSkin.Button(canvas, parent.transform, name, key == "" ? "" : WorkroomMachineAssembly.Text(key), font, 12, action);
        AssemblyDebugUi.Place(button, x, y, width, 26); return button;
    }
    private static void ScrollPreview(FamilyView view, float delta)
    {
        view.PreviewOffset = Mathf.Clamp(view.PreviewOffset+delta, 0, Math.Max(0, view.PreviewHeight-WorkroomPanelSkin.PreviewHeight));
        AssemblyDebugUi.Place(view.Preview.gameObject, 13, 6-view.PreviewOffset, WorkroomPanelSkin.PreviewWidth, view.PreviewHeight);
    }
    private void Hide(WorkroomAssemblyWindow window) { window.Hide(); CancelPreview(); }
    internal void Show(string family)
    {
        families.First(view => view.Family == family).Window.Show(); CancelPreview(); Update(false);
    }
    internal void Close() { foreach (var window in Windows) window.Hide(); CancelPreview(); }
    internal void CloseTop()
    {
        var top = Windows.Where(window => window.Open).OrderByDescending(window => window.Root.transform.GetSiblingIndex()).FirstOrDefault();
        if (top != null) Hide(top);
    }
    internal bool Hit(Vector2 point) => TopAt(point) != null;
    internal string? HitSlot(Vector2 point)
    {
        var top = TopAt(point);
        return top == null ? null : slots.FirstOrDefault(slot => slot.Root.transform.parent.Pointer == top.Root.transform.Pointer && slot.Hit(point))?.Slot;
    }
    private WorkroomAssemblySlotUi? Compatible(WorkroomAssemblyWindow window, WorkroomItemCodec.Snapshot item) =>
        slots.FirstOrDefault(slot => slot.Root.activeInHierarchy && slot.Root.transform.parent.Pointer == window.Root.transform.Pointer &&
            Exposed(slot, window) && WorkroomMachineAssembly.Accepts(slot.Slot, item));
    internal string? ResolveBody(Vector2 point, WorkroomItemCodec.Snapshot item)
    {
        var top = TopAt(point);
        return top != null && families.Any(f => ReferenceEquals(f.Window, top)) && top.HitBody(point) ? Compatible(top, item)?.Slot : null;
    }
    internal WorkroomStorageState.Record? HoveredItem(Vector2 point)
    {
        var slot = HitSlot(point);
        return slot == null ? null : WorkroomMachineAssembly.At(slot);
    }
    private static Rect ScreenBounds(GameObject root)
    {
        var rect = AssemblyDebugUi.Rect(root);
        var min = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.yMin, 0)));
        var max = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(rect.rect.xMax, rect.rect.yMax, 0)));
        return new Rect(min, max-min);
    }
    private static bool Exposed(WorkroomAssemblySlotUi slot, WorkroomAssemblyWindow owner)
    {
        var bounds = ScreenBounds(slot.Root);
        var parent = owner.Root.transform.parent;
        for (var i = owner.Root.transform.GetSiblingIndex()+1; i < parent.childCount; i++)
        {
            var root = parent.GetChild(i).gameObject;
            if (root.activeInHierarchy && root.GetComponent<Image>()?.raycastTarget == true && ScreenBounds(root).Overlaps(bounds)) return false;
        }
        return true;
    }
    internal bool QuickStage(string id, long generation)
    {
        var record = WorkroomStorage.State.Find(id);
        if (record?.Place != "room") return false;
        var target = families.Select(view => view.Window).Where(window => window.Open)
            .OrderByDescending(window => window.Root.transform.GetSiblingIndex())
            .Select(window => Compatible(window, record.Item)).FirstOrDefault(slot => slot != null);
        return target != null && WorkroomMachineAssembly.Stage(id, target.Slot, generation, record.Item.Orientation);
    }
    internal void Update(bool allowPress)
    {
        if (!Open) return;
        var top = TopAt(Input.mousePosition);
        foreach (var window in Windows) window.Update(allowPress && ReferenceEquals(window, top));
        epoch = WorkroomStorage.State.Epoch;
        if (revision != WorkroomStorage.State.Revision || generation != epoch)
        {
            var names = string.Join("", WorkroomStorage.State.Items.Where(record => record.Place == "slot").Select(record => record.Item.Name));
            AssemblyDebugFonts.Prepare(names, names); revision = WorkroomStorage.State.Revision; generation = epoch;
        }
        var ready = WorkroomMachineAssembly.Ready(epoch);
        var status = WorkroomStorage.State.Error != null ? WorkroomStorage.Text(WorkroomStorage.State.Error) :
            WorkroomItemCodec.CleanupPending ? WorkroomStorage.Text("recovery") :
            Environment.TickCount64 < WorkroomStorage.MessageUntil ? WorkroomStorage.Message : "";
        if (status != lastStatus) { AssemblyDebugFonts.Prepare(status, status); lastStatus = status; }
        foreach (var view in families)
        {
            if (view.Window.Open)
            {
                var tier = WorkroomMachineAssembly.BodyTier(view.Family);
                foreach (var content in view.Content)
                {
                    var active = tier > 0 && content.Index < WorkroomMachineAssembly.Count(view.Family, content.Kind, tier);
                    content.Ui.Root.SetActive(active);
                }
                view.Hint.text = tier == 0 ? WorkroomMachineAssembly.Text("need_body", WorkroomMachineAssembly.Text(view.Family+"_body")) :
                    WorkroomMachineAssembly.Text("assembly_hint", tier);
                if (view.SummaryEpoch != epoch || view.SummaryRevision != WorkroomStorage.State.Revision || view.SummaryChinese != LanguageText.IsChinese)
                {
                    var summary = WorkroomMachineAssembly.Summary(view.Family);
                    if (view.Summary.text != summary) { AssemblyDebugFonts.Prepare(summary, summary); view.Summary.text = summary; }
                    view.SummaryEpoch = epoch; view.SummaryRevision = WorkroomStorage.State.Revision; view.SummaryChinese = LanguageText.IsChinese;
                }
                view.Status.text = view.RepairStatus.text = status;
                view.Assemble.GetComponent<Button>().interactable = ready && WorkroomMachineAssembly.CanAssemble(view.Family);
                view.Disassemble.GetComponent<Button>().interactable = ready && WorkroomMachineAssembly.CanDisassemble(view.Family);
                view.Return.GetComponent<Button>().interactable = ready && WorkroomMachineAssembly.Slots(view.Family).Any(slot => WorkroomMachineAssembly.At(slot) != null);
                view.AutoFill.GetComponent<Button>().interactable = ready;
            }
            if (!view.RepairWindow.Open) continue;
            view.RepairStatus.text = status;
            var preview = WorkroomMachineRepair.Preview(view.Family);
            if (preview != view.PreviewText)
            {
                view.PreviewText = preview; view.PreviewOffset = 0;
                AssemblyDebugFonts.Prepare(preview, preview); view.Preview.text = preview;
                view.PreviewHeight = Math.Max(WorkroomPanelSkin.PreviewHeight, view.Preview.GetPreferredValues(preview, WorkroomPanelSkin.PreviewWidth, float.PositiveInfinity).y);
                ScrollPreview(view, 0);
            }
            view.ExecuteRepair.GetComponent<Button>().interactable = ready && WorkroomMachineRepair.CanExecute(view.Family);
            if (allowPress && ReferenceEquals(top, view.RepairWindow) && !Dragging &&
                RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(view.Viewport), Input.mousePosition, null) && Input.mouseScrollDelta.y != 0)
                ScrollPreview(view, -Input.mouseScrollDelta.y*30);
        }
        foreach (var slot in slots) if (slot.Root.activeInHierarchy) slot.Refresh();
    }

    internal void CancelPreview() { if (AssemblyDebugUi.Alive(ghostRoot)) ghostRoot.SetActive(false); }
    internal void RoomPreview(Sprite? sprite, Vector2 position, Vector2 size, Color color, int orientation, bool flipped, bool visible)
    {
        if (!Open || !visible || !AssemblyDebugUi.Alive(sprite)) { CancelPreview(); return; }
        ghost.sprite = sprite; ghost.color = color;
        var rect = AssemblyDebugUi.Rect(ghostRoot);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f,.5f); rect.pivot = new Vector2(0,1);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        WorkroomItemSprite.Pose(ghost, orientation, flipped, size);
        ghostRoot.transform.SetAsLastSibling(); ghostRoot.SetActive(true);
    }
}
