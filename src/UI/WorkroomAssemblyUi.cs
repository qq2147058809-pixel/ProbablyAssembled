using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>独立装配窗口共用拖动和槽位表现；窗口外不接管输入。</summary>
internal sealed class WorkroomAssemblyWindow
{
    internal readonly GameObject Root;
    private readonly GameObject header, close;
    private Vector2 pressed, start;
    internal bool Dragging { get; private set; }
    internal bool Open => WorkroomUiCleanup.IsAlive(Root) && Root.activeSelf;
    internal static string? PreviewSlot;

    internal WorkroomAssemblyWindow(Canvas canvas, Transform parent, TMP_FontAsset font, string name,
        string title, float x, float y, float width, float height, Action onClose)
    {
        Root = AssemblyDebugUi.Image(parent, name, new Color(.08f,.12f,.13f,1), true).gameObject;
        AssemblyDebugUi.Place(Root, x, y, width, height);
        var theme = name.Contains("motherboard") ? "motherboard" : name.Contains("case") ? "case" : "component";
        WorkroomPanelSkin.Scene(Root, theme);
        header = AssemblyDebugUi.Image(Root.transform, "Header", new Color(.23f,.28f,.24f,1), true).gameObject;
        AssemblyDebugUi.Place(header, 0, 0, width, 36);
        var label = AssemblyDebugUi.Text(header.transform, "Title", title, font, 18, true);
        AssemblyDebugUi.Place(label.gameObject, 12, 0, width-64, 36);
        close = WorkroomPanelSkin.Button(canvas, header.transform, "Close", "Esc", font, 14, onClose);
        AssemblyDebugUi.Place(close, width-46, 4, 38, 28);
        var footer = AssemblyDebugUi.Text(Root.transform, "WindowHint", WorkroomComponentTemplates.Text("window_hint"), font, 10);
        AssemblyDebugUi.Place(footer.gameObject, 12, height-30, 150, 26);
        footer.enableWordWrapping = true;
        footer.fontSize = 11.25f;
        Root.SetActive(false);
    }

    internal bool Hit(Vector2 point) => Open && RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(Root), point, null);
    internal void Show()
    {
        if (!WorkroomUiCleanup.IsAlive(Root)) return;
        Root.SetActive(true); Root.transform.SetAsLastSibling();
    }
    internal void Hide()
    {
        Dragging = false; PreviewSlot = null;
        WorkroomUiCleanup.Deactivate(Root);
    }
    internal bool HitBody(Vector2 point)
    {
        if (!Hit(point) || RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(header), point, null)) return false;
        for (var i = 0; i < Root.transform.childCount; i++)
        {
            var child = Root.transform.GetChild(i).gameObject;
            if (child.activeInHierarchy && child.GetComponent<Button>() != null &&
                RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(child), point, null)) return false;
        }
        return true;
    }
    internal void Update(bool allowPress)
    {
        if (!Open) return;
        var parent = Root.transform.parent.TryCast<RectTransform>();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Input.mousePosition, null, out var point)) return;
        var rect = AssemblyDebugUi.Rect(Root);
        if (allowPress && Input.GetMouseButtonDown(0) && Hit(Input.mousePosition))
        {
            Root.transform.SetAsLastSibling();
            if (RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(header), Input.mousePosition, null) &&
                !RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(close), Input.mousePosition, null))
            { Dragging = true; pressed = point; start = rect.anchoredPosition; }
        }
        if (Dragging && Input.GetMouseButton(0)) rect.anchoredPosition = start + point-pressed;
        if (!Input.GetMouseButton(0)) Dragging = false;
        var position = rect.anchoredPosition;
        position.x = Mathf.Clamp(position.x, 0, 1280-rect.rect.width);
        position.y = Mathf.Clamp(position.y, -720+rect.rect.height, 0);
        rect.anchoredPosition = position;
    }
}

internal sealed class WorkroomAssemblySlotUi
{
    internal readonly string Slot;
    internal bool RouteBody, Enabled = true;
    internal string Target => RouteBody ? WorkroomComponentAssembly.EntryBoard : Slot;
    internal readonly GameObject Root;
    private readonly Image icon;
    private readonly TextMeshProUGUI label, condition;
    private readonly TextMeshProUGUI emptyMark;
    private readonly GameObject picture;
    private readonly bool bodyPreview;
    private readonly string empty;
    private WorkroomItemCodec.Snapshot? displayed;
    private bool displayedChinese;
    internal long Epoch;
    internal WorkroomAssemblySlotUi(Canvas canvas, Transform parent, TMP_FontAsset font, string slot,
        string empty, float x, float y, float width, float height, bool bodyPreview = false)
    {
        Slot = slot; this.empty = empty; this.bodyPreview = bodyPreview;
        Root = WorkroomPanelSkin.Button(canvas, parent, slot, "", font, 10,
            () =>
            {
                if (WorkroomComponentAssembly.At(Slot) != null) WorkroomComponentAssembly.ReturnSlots(new[] { Slot }, Epoch);
            }, true);
        AssemblyDebugUi.Place(Root, x, y, width, height);
        picture = AssemblyDebugUi.New("Picture", Root.transform);
        icon = AssemblyDebugUi.Image(picture.transform, "Icon", Color.white, false); icon.preserveAspect = true;
        icon.gameObject.SetActive(false);
        label = AssemblyDebugUi.Text(Root.transform, "Part", empty, font, 10);
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.enableWordWrapping = bodyPreview;
        condition = AssemblyDebugUi.Text(Root.transform, "Condition", "", font, 10);
        emptyMark = AssemblyDebugUi.Text(Root.transform, "EmptyMark", "+", font, 24);
        Layout(x, y, width, height);
    }
    internal void Layout(float x, float y, float width, float height)
    {
        AssemblyDebugUi.Place(Root, x, y, width, height);
        AssemblyDebugUi.Place(picture, 6, bodyPreview ? 6 : 21, width-12, Math.Max(14, height-(bodyPreview ? 62 : 40)));
        AssemblyDebugUi.Place(label.gameObject, 3, bodyPreview ? height-53 : 1, width-6, bodyPreview ? 36 : 18);
        AssemblyDebugUi.Place(condition.gameObject, 3, height-16, width-6, 15);
        AssemblyDebugUi.Place(emptyMark.gameObject, 6, 20, width-12, Math.Max(20,height-40));
        if (displayed != null) WorkroomItemSprite.Pose(icon, displayed.Orientation, displayed.Flipped, AssemblyDebugUi.Rect(picture).rect.size);
    }
    internal bool Hit(Vector2 point) => Root.activeInHierarchy &&
        RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(Root), point, null);
    internal void Refresh()
    {
        Epoch = WorkroomStorage.State.Epoch;
        var record = WorkroomComponentAssembly.At(Slot);
        if (!ReferenceEquals(displayed, record?.Item) || displayedChinese != LanguageText.IsChinese || record != null && !AssemblyDebugUi.Alive(icon.sprite))
        {
            displayed = record?.Item; displayedChinese = LanguageText.IsChinese;
            icon.sprite = record == null ? null : WorkroomInventoryUi.Icon(record.Item);
            icon.gameObject.SetActive(record != null && AssemblyDebugUi.Alive(icon.sprite));
            if (record != null)
            {
                var size = AssemblyDebugUi.Rect(icon.transform.parent.gameObject).rect.size;
                WorkroomItemSprite.Pose(icon, record.Item.Orientation, record.Item.Flipped, size);
            }
            label.text = bodyPreview ? record?.Item.Name ?? empty : empty;
            var tier = record == null ? 0 : Components.Find(record.Item.Identifier)?.Tier ?? 0;
            condition.text = record == null ? "" : (bodyPreview && tier > 0 ? "T"+tier+" · " : "") +
                WorkroomComponentTemplates.Text(record.Item.Broken ? "broken_short" : "intact_short");
            condition.color = record?.Item.Broken == true ? new Color(1,.55f,.45f,1) : new Color(.65f,1,.75f,1);
        }
        emptyMark.gameObject.SetActive(record == null);
        Root.GetComponent<Button>().interactable = Enabled && WorkroomComponentAssembly.Ready(Epoch);
        WorkroomPanelSkin.Selected(Root, WorkroomAssemblyWindow.PreviewSlot == Target);
    }
}

/// <summary>一个可见工作台复用处理和六类装配布局；物品切换先由保管模型提交。</summary>
internal sealed class WorkroomComponentUi
{
    private sealed class FamilyView
    {
        internal WorkroomComponentTemplates.Template Template = null!;
        internal WorkroomAssemblyWindow Window = null!;
        internal TextMeshProUGUI Hint = null!, Summary = null!, Status = null!;
        internal GameObject Assemble = null!, Return = null!, AutoFill = null!;
        internal long SummaryEpoch = -1, SummaryRevision = -1;
        internal bool SummaryChinese;
        internal readonly List<(string Kind, int Index, WorkroomAssemblySlotUi Ui)> Content = new();
    }
    private readonly WorkroomAssemblyWindow entry, disassemblyWindow;
    private readonly List<FamilyView> families = new();
    private readonly List<WorkroomAssemblySlotUi> slots = new();
    private readonly TextMeshProUGUI entryStatus, disassemblyStatus, processingPreview;
    private readonly GameObject execute, take, previewViewport;
    private readonly Dictionary<WorkroomAssemblyWindow, (GameObject Processing, GameObject Assembly)> workbenchModes = new();
    private WorkroomAssemblyWindow? activeWindow;
    private FamilyView? selectedFamily;
    private bool assemblyMode;
    private long workbenchEpoch = -1;
    private const float ProcessingWidth = 478, ProcessingHeight = 218;
    private readonly GameObject quantityMinus, quantityNumber, quantityPlus;
    private readonly TextMeshProUGUI quantityLabel;
    private int recycleQuantity = 1;
    private string quantityText = "1";
    private bool editingQuantity, replaceQuantity;
    private string? quantityTarget;
    private long quantityEpoch = -1;
    private readonly Dictionary<WorkroomPartProcessing.Mode, GameObject> modeButtons = new();
    private WorkroomPartProcessing.Mode mode;
    private WorkroomPartProcessing.DiscardConfirmation? discardConfirmation;
    private string previewText = "", lastStatus = "";
    private float previewOffset, previewHeight;
    private readonly GameObject ghostRoot;
    private readonly Image ghost;
    private long epoch, revision = -1, generation = -1;
    private IEnumerable<WorkroomAssemblyWindow> Windows => new[] { entry, disassemblyWindow }.Concat(families.Select(family => family.Window));
    internal bool Open => Windows.Any(window => window.Open);
    internal bool Dragging => Windows.Any(window => window.Dragging);
    private WorkroomAssemblyWindow? TopAt(Vector2 point) => Windows.Where(window => window.Hit(point))
        .OrderByDescending(window => window.Root.transform.GetSiblingIndex()).FirstOrDefault();
    internal int SiblingIndex(Vector2 point) => TopAt(point)?.Root.transform.GetSiblingIndex() ?? -1;

    internal static string FontText => string.Join("", new[] { "entry_title", "processing_title", "workbench_processing", "workbench_assembly", "entry_hint", "assembly_hint", "assembly_title",
        "assembly_summary", "assemble", "mode_disassemble", "mode_repair", "mode_recycle", "mode_discard", "discard_confirm", "open", "resume", "return_all", "take",
        "window_hint", "need_body", "processing_target", "intact_short", "broken_short", "quick_unavailable",
        "capacity_summary", "capacity_vram", "capacity_ram", "capacity_storage", "auto_fill",
        "assembly_body_hint", "assembly_actual_summary", "recycle_quantity", "recycle_quantity_edit", "recycle_quantity_hint",
        "processing_cash_recovery", "processing_invalid_quantity", "processing_missing_quantity", "processing_cash_limit",
        "processing_missing_cash", "processing_cash_header", "processing_cash_ready", "processing_recycle_batch_header",
        "capacity_health_summary", "rotor_integrated", "power_integrated" }
        .Select(key => WorkroomComponentTemplates.Text(key, "", 0, 0, 0, 0, 0))) +
        string.Join("", WorkroomComponentTemplates.All.Select(template => template.Name + string.Join("", template.Kinds.Select(template.PartName)))) + "T0123456789×↑↓—−+_【】GBTB";

    internal WorkroomComponentUi(Canvas canvas, Transform reference, TMP_FontAsset font)
    {
        disassemblyWindow = new WorkroomAssemblyWindow(canvas, reference, font, "ProcessingEntry",
            WorkroomComponentTemplates.Text("entry_title"), 280, 64, 720, 424, () => Hide(disassemblyWindow));
        entry = new WorkroomAssemblyWindow(canvas, reference, font, "BoardEntry",
            WorkroomComponentTemplates.Text("entry_title"), 280, 64, 720, 424, () => Hide(entry));
        var hint = AssemblyDebugUi.Text(entry.Root.transform, "EntryHint", WorkroomComponentTemplates.Text("entry_hint"), font, 11, true);
        hint.enableWordWrapping = true;
        AssemblyDebugUi.Place(hint.gameObject, 188, 96, 504, 96);
        AddSlot(canvas, entry.Root, font, WorkroomComponentAssembly.EntryBoard, WorkroomComponentTemplates.Text("need_body"), 18, 50, 134, 196, true).RouteBody = true;
        entryStatus = Result(entry.Root, font, 18, 264, 134, 118);
        for (var i = 0; i < WorkroomComponentTemplates.All.Count; i++)
        {
            var template = WorkroomComponentTemplates.All[i];
            var family = BuildFamily(canvas, reference, font, template);
            families.Add(family);
        }
        foreach (var window in Windows) BuildWorkbenchModes(canvas, font, window);
        foreach (var candidate in Enum.GetValues(typeof(WorkroomPartProcessing.Mode)).Cast<WorkroomPartProcessing.Mode>())
        {
            var current = candidate;
            var button = ActionButton(canvas, disassemblyWindow.Root, font, "Mode_"+current, ModeKey(current),
                188+modeButtons.Count*128, 82, 122, () => { FinishQuantity(); CancelDiscard(); mode = current; previewText = ""; previewOffset = 0; Update(false); });
            modeButtons.Add(current, button);
        }
        AddSlot(canvas, disassemblyWindow.Root, font, WorkroomComponentAssembly.Disassembly,
            WorkroomComponentTemplates.Text("processing_target"), 18, 50, 134, 196, true);
        quantityLabel = AssemblyDebugUi.Text(disassemblyWindow.Root.transform, "RecycleQuantityLabel", "", font, 10);
        quantityLabel.enableWordWrapping = true;
        AssemblyDebugUi.Place(quantityLabel.gameObject, 18, 256, 134, 28);
        quantityMinus = ActionButton(canvas, disassemblyWindow.Root, font, "RecycleQuantityMinus", "", 18, 286, 28,
            () => ChangeQuantity(-1));
        quantityMinus.GetComponentInChildren<TextMeshProUGUI>().text = "−";
        quantityNumber = ActionButton(canvas, disassemblyWindow.Root, font, "RecycleQuantityNumber", "recycle_quantity_edit", 50, 286, 70,
            () => { editingQuantity = replaceQuantity = true; });
        quantityPlus = ActionButton(canvas, disassemblyWindow.Root, font, "RecycleQuantityPlus", "", 124, 286, 28,
            () => ChangeQuantity(1));
        quantityPlus.GetComponentInChildren<TextMeshProUGUI>().text = "+";
        // Keep the quantity row above the action row, with no overlap of hit targets.
        foreach (var control in new[] { quantityMinus, quantityNumber, quantityPlus })
        {
            var rect = AssemblyDebugUi.Rect(control);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, 24);
        }
        previewViewport = WorkroomPanelSkin.Panel(disassemblyWindow.Root, "PreviewViewport", 188, 120, 504, 230, new Color(.12f,.17f,.14f,.97f));
        var maskType = Il2CppSystem.Type.internal_from_handle(IL2CPP.il2cpp_class_get_type(Il2CppClassPointerStore<RectMask2D>.NativeClassPtr));
        if (previewViewport.AddComponent(maskType)?.TryCast<RectMask2D>() == null)
            throw new InvalidOperationException("配件处理预览裁剪组件未就绪。");
        processingPreview = AssemblyDebugUi.Text(previewViewport.transform, "Preview", "", font, 11, true);
        processingPreview.enableWordWrapping = true;
        processingPreview.alignment = TextAlignmentOptions.TopLeft;
        AssemblyDebugUi.Place(processingPreview.gameObject, 13, 6, ProcessingWidth, ProcessingHeight);
        var up = ActionButton(canvas, disassemblyWindow.Root, font, "Previous", "", 188, 356, 250, () => ScrollPreview(-90));
        var down = ActionButton(canvas, disassemblyWindow.Root, font, "Next", "", 444, 356, 248, () => ScrollPreview(90));
        up.GetComponentInChildren<TextMeshProUGUI>().text = "↑";
        down.GetComponentInChildren<TextMeshProUGUI>().text = "↓";
        execute = ActionButton(canvas, disassemblyWindow.Root, font, "Execute", ModeKey(mode), 188, 396, 250,
            ExecuteProcessing);
        take = ActionButton(canvas, disassemblyWindow.Root, font, "Take", "take", 444, 396, 248,
            () => { CancelDiscard(); WorkroomComponentAssembly.ReturnSlots(new[] { WorkroomComponentAssembly.Disassembly }, epoch); });
        disassemblyStatus = Result(disassemblyWindow.Root, font, 18, 318, 134, 66);
        ghostRoot = AssemblyDebugUi.New("AssemblyDragPreview", canvas.transform); ghostRoot.SetActive(false);
        ghost = AssemblyDebugUi.Image(ghostRoot.transform, "Icon", Color.white, false); ghost.preserveAspect = true;
    }

    private FamilyView BuildFamily(Canvas canvas, Transform reference, TMP_FontAsset font, WorkroomComponentTemplates.Template template)
    {
        var family = new FamilyView { Template = template };
        family.Window = new WorkroomAssemblyWindow(canvas, reference, font, "Assembly_"+template.Key,
            WorkroomComponentTemplates.Text("entry_title"), 280, 64, 720, 424, () => Hide(family.Window));
        var familyName = AssemblyDebugUi.Text(family.Window.Root.transform, "FamilyName", WorkroomComponentTemplates.Text("assembly_title", template.Name), font, 13);
        AssemblyDebugUi.Place(familyName.gameObject, 414, 46, 280, 26);
        family.Hint = AssemblyDebugUi.Text(family.Window.Root.transform, "Hint", "", font, 11, true);
        family.Hint.enableWordWrapping = true;
        AssemblyDebugUi.Place(family.Hint.gameObject, 188, 354, 504, 32);
        AddSlot(canvas, family.Window.Root, font, WorkroomComponentAssembly.Slot(template, "body"), template.PartName("body"), 18, 50, 134, 196, true).RouteBody = true;
        var cell = 0;
        foreach (var kind in template.Kinds.Where(kind => kind != "body"))
        for (var i = 0; i < template.MaxCount(kind); i++, cell++)
        {
            var label = template.PartName(kind) + (template.MaxCount(kind) > 1 ? (i+1).ToString() : "");
            var ui = AddSlot(canvas, family.Window.Root, font, WorkroomComponentAssembly.Slot(template, kind, i), label,
                398+(cell%3)*94, 93+(cell/3)*130, 82, 90);
            PlacePart(ui, template.Key, kind, i);
            family.Content.Add((kind, i, ui));
        }
        family.Summary = AssemblyDebugUi.Text(family.Window.Root.transform, "Summary", "", font, 11, true);
        family.Summary.enableWordWrapping = true;
        family.Summary.alignment = TextAlignmentOptions.TopLeft;
        AssemblyDebugUi.Place(family.Summary.gameObject, 18, 258, 134, 100);
        family.Status = Result(family.Window.Root, font, 18, 360, 134, 30);
        family.AutoFill = ActionButton(canvas, family.Window.Root, font, "AutoFill", "auto_fill", 176, 396, 172,
            () => WorkroomComponentAssembly.AutoFill(template.Key, epoch));
        family.Assemble = ActionButton(canvas, family.Window.Root, font, "Assemble", "assemble", 354, 396, 172,
            () => WorkroomComponentAssembly.Assemble(template.Key, epoch));
        family.Return = ActionButton(canvas, family.Window.Root, font, "ReturnAll", "return_all", 532, 396, 172,
            () => WorkroomComponentAssembly.ReturnSlots(WorkroomComponentAssembly.Slots(template), epoch));
        return family;
    }

    private void BuildWorkbenchModes(Canvas canvas, TMP_FontAsset font, WorkroomAssemblyWindow window)
    {
        var processing = ActionButton(canvas, window.Root, font, "WorkbenchProcessing", "workbench_processing", 188, 46, 100, () => SwitchMode(false));
        var assembly = ActionButton(canvas, window.Root, font, "WorkbenchAssembly", "workbench_assembly", 300, 46, 100, () => SwitchMode(true));
        workbenchModes.Add(window, (processing, assembly));
    }

    private void Activate(WorkroomAssemblyWindow window)
    {
        if (ReferenceEquals(activeWindow, window) && window.Open) return;
        var position = activeWindow != null && WorkroomUiCleanup.IsAlive(activeWindow.Root)
            ? AssemblyDebugUi.Rect(activeWindow.Root).anchoredPosition : AssemblyDebugUi.Rect(window.Root).anchoredPosition;
        FinishQuantity(); CancelDiscard();
        foreach (var other in Windows) other.Hide();
        activeWindow = window;
        AssemblyDebugUi.Rect(window.Root).anchoredPosition = position;
        window.Show(); CancelPreview();
    }

    private void SwitchMode(bool nextAssembly)
    {
        if (nextAssembly == assemblyMode || !WorkroomComponentAssembly.ReturnModeSlots(assemblyMode, epoch)) return;
        FinishQuantity(); CancelDiscard();
        assemblyMode = nextAssembly;
        previewText = ""; previewOffset = 0;
        Activate(assemblyMode ? selectedFamily?.Window ?? entry : disassemblyWindow);
        Update(false);
    }

    private static void PlacePart(WorkroomAssemblySlotUi ui, string family, string kind, int index)
    {
        if (family == "gpu")
        {
            if (kind == "core") ui.Layout(216, 163, 102, 108);
            else ui.Layout(398+(index%3)*94, 93+(index/3)*130, 82, 90);
        }
        else if (family == "ram") ui.Layout(214+(index%3)*154, 92+(index/3)*134, 132, 104);
        else if (family == "ssd")
        {
            if (kind == "controller") ui.Layout(216, 163, 108, 108);
            else ui.Layout(414+(index%2)*128, 92+(index/2)*132, 110, 102);
        }
        else if (family == "cooler") ui.Layout(kind == "thermal" ? 268 : 470, 122, 144, 164);
        else ui.Layout(348, 116, 184, 184);
    }

    private WorkroomAssemblySlotUi AddSlot(Canvas canvas, GameObject parent, TMP_FontAsset font, string slot, string label,
        float x, float y, float width, float height, bool bodyPreview = false)
    {
        var ui = new WorkroomAssemblySlotUi(canvas, parent.transform, font, slot, label, x, y, width, height, bodyPreview);
        slots.Add(ui); return ui;
    }
    private static TextMeshProUGUI Result(GameObject parent, TMP_FontAsset font, float x, float y, float width, float height)
    {
        var result = AssemblyDebugUi.Text(parent.transform, "Result", "", font, 11, true);
        result.enableWordWrapping = true;
        AssemblyDebugUi.Place(result.gameObject, x, y, width, height); return result;
    }
    private static GameObject ActionButton(Canvas canvas, GameObject parent, TMP_FontAsset font, string name,
        string key, float x, float y, float width, Action action)
    {
        var button = WorkroomPanelSkin.Button(canvas, parent.transform, name, key == "" ? "" : WorkroomComponentTemplates.Text(key), font, 12, action);
        AssemblyDebugUi.Place(button, x, y, width, 26); return button;
    }
    private static string ModeKey(WorkroomPartProcessing.Mode mode) => mode switch
    {
        WorkroomPartProcessing.Mode.Repair => "mode_repair",
        WorkroomPartProcessing.Mode.Recycle => "mode_recycle",
        WorkroomPartProcessing.Mode.Discard => "mode_discard",
        _ => "mode_disassemble"
    };
    private void CancelDiscard() => discardConfirmation = null;
    private void ExecuteProcessing()
    {
        FinishQuantity();
        if (mode != WorkroomPartProcessing.Mode.Discard)
        { CancelDiscard(); WorkroomPartProcessing.Execute(mode, epoch, recycleQuantity); return; }
        if (discardConfirmation == null)
        { discardConfirmation = WorkroomPartProcessing.PrepareDiscard(epoch); Update(false); return; }
        var confirmation = discardConfirmation;
        CancelDiscard();
        WorkroomPartProcessing.ConfirmDiscard(confirmation, epoch);
        Update(false);
    }
    private void ScrollPreview(float delta)
    {
        previewOffset = Mathf.Clamp(previewOffset+delta, 0, Math.Max(0, previewHeight-ProcessingHeight));
        AssemblyDebugUi.Place(processingPreview.gameObject, 13, 6-previewOffset, ProcessingWidth, previewHeight);
    }
    private void FinishQuantity()
    {
        recycleQuantity = int.TryParse(quantityText, out var value) && value > 0 ? value : 1;
        quantityText = recycleQuantity.ToString(); editingQuantity = replaceQuantity = false;
    }
    private void ChangeQuantity(int delta)
    {
        FinishQuantity();
        recycleQuantity = (int)Math.Clamp((long)recycleQuantity + delta, 1, int.MaxValue);
        quantityText = recycleQuantity.ToString(); previewText = ""; previewOffset = 0;
    }
    private void ReadQuantity(bool allowPress, WorkroomAssemblyWindow? top)
    {
        if (!editingQuantity) return;
        if (!allowPress) return;
        if (Input.GetMouseButtonDown(0) && !RectTransformUtility.RectangleContainsScreenPoint(
                AssemblyDebugUi.Rect(quantityNumber), Input.mousePosition, null))
        { FinishQuantity(); return; }
        if (!ReferenceEquals(top, disassemblyWindow) || Dragging) return;
        foreach (var key in Input.inputString)
        {
            if (key == '\n' || key == '\r') { FinishQuantity(); break; }
            if (key == '\b')
            {
                quantityText = replaceQuantity ? "" : quantityText.Length == 0 ? "" : quantityText[..^1];
                replaceQuantity = false;
            }
            else if (key >= '0' && key <= '9')
            {
                var next = (replaceQuantity ? "" : quantityText) + key;
                if (!int.TryParse(next, out _)) continue;
                quantityText = next; replaceQuantity = false;
            }
            else continue;
            recycleQuantity = int.TryParse(quantityText, out var parsed) ? parsed : 0;
            previewText = ""; previewOffset = 0;
        }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) FinishQuantity();
    }
    private void Hide(WorkroomAssemblyWindow? window) { FinishQuantity(); if (ReferenceEquals(window, disassemblyWindow)) CancelDiscard(); window?.Hide(); CancelPreview(); }
    internal void Show()
    {
        epoch = WorkroomStorage.State.Epoch;
        if (workbenchEpoch != epoch)
        {
            workbenchEpoch = epoch; activeWindow = null; selectedFamily = null; assemblyMode = false;
            var staged = families.FirstOrDefault(family => WorkroomComponentAssembly.BoardTier(family.Template.Key) > 0);
            if (staged != null && WorkroomComponentAssembly.At(WorkroomComponentAssembly.Disassembly) == null)
            { selectedFamily = staged; assemblyMode = true; }
        }
        Activate(assemblyMode ? selectedFamily?.Window ?? entry : disassemblyWindow);
        Update(false);
    }
    internal void Close() { FinishQuantity(); CancelDiscard(); foreach (var window in Windows) window.Hide(); CancelPreview(); }
    internal void Release()
    {
        var cleanup = new WorkroomUiCleanup();
        FinishQuantity();
        CancelDiscard();
        var index = 0;
        foreach (var window in Windows) cleanup.Run("components.window." + index++, window.Hide);
        cleanup.Run("components.preview", CancelPreview);
        cleanup.ThrowIfFailed();
    }
    internal void CloseTop()
    {
        var top = Windows.Where(window => window.Open).OrderByDescending(window => window.Root.transform.GetSiblingIndex()).FirstOrDefault();
        if (top != null) Hide(top);
    }
    internal bool Hit(Vector2 point) => TopAt(point) != null;
    internal string? HitSlot(Vector2 point)
        => SlotAt(point)?.Target;
    private WorkroomAssemblySlotUi? SlotAt(Vector2 point)
    {
        var top = TopAt(point);
        return top == null ? null : slots.FirstOrDefault(slot => slot.Root.transform.parent.Pointer == top.Root.transform.Pointer && slot.Hit(point));
    }
    private WorkroomAssemblySlotUi? Compatible(WorkroomAssemblyWindow window, WorkroomItemCodec.Snapshot item) =>
        slots.FirstOrDefault(slot => slot.Root.activeInHierarchy && slot.Root.transform.parent.Pointer == window.Root.transform.Pointer &&
            Exposed(slot, window) && WorkroomComponentAssembly.Accepts(slot.Target, item));
    internal string? ResolveBody(Vector2 point, WorkroomItemCodec.Snapshot item)
    {
        var top = TopAt(point);
        return top != null && (ReferenceEquals(top, entry) || families.Any(f => ReferenceEquals(f.Window, top))) && top.HitBody(point)
            ? Compatible(top, item)?.Target : null;
    }
    internal WorkroomStorageState.Record? HoveredItem(Vector2 point)
    {
        var slot = SlotAt(point);
        return slot == null ? null : WorkroomComponentAssembly.At(slot.Slot);
    }
    private static Rect ScreenBounds(GameObject root)
    {
        var rect = AssemblyDebugUi.Rect(root);
        var min = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.yMin, 0)));
        var max = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(rect.rect.xMax, rect.rect.yMax, 0)));
        return new Rect(min, max-min);
    }
    private bool Exposed(WorkroomAssemblySlotUi slot, WorkroomAssemblyWindow owner)
    {
        var bounds = ScreenBounds(slot.Root);
        var order = owner.Root.transform.GetSiblingIndex();
        // The storage face is another direct child of PanelReference. Include
        // foreground sibling faces so double-clicks obey the same occlusion as drops.
        var parent = owner.Root.transform.parent;
        for (var i = order+1; i < parent.childCount; i++)
        {
            var root = parent.GetChild(i).gameObject;
            if (root.activeInHierarchy && root.GetComponent<Image>()?.raycastTarget == true && ScreenBounds(root).Overlaps(bounds))
                return false;
        }
        return true;
    }
    internal bool QuickStage(string id, long generation)
    {
        var record = WorkroomStorage.State.Find(id);
        if (record?.Place != "room" || generation != WorkroomStorage.State.Epoch) return false;
        // Only visible assembly windows participate; processing never accepts double-clicks.
        var target = families.Select(family => family.Window).Append(entry).Where(window => window.Open)
            .OrderByDescending(window => window.Root.transform.GetSiblingIndex())
            .Select(window => Compatible(window, record.Item)).FirstOrDefault(slot => slot != null);
        if (target == null)
        {
            if (entry.Open || families.Any(family => family.Window.Open)) WorkroomComponentAssembly.Notice("quick_unavailable");
            return false;
        }
        return WorkroomComponentAssembly.Stage(id, target.Target, generation, record.Item.Orientation);
    }
    internal void Update(bool allowPress)
    {
        if (!Open) return;
        epoch = WorkroomStorage.State.Epoch;
        if (assemblyMode)
        {
            var staged = families.FirstOrDefault(family => WorkroomComponentAssembly.BoardTier(family.Template.Key) > 0);
            if (staged != null && !ReferenceEquals(staged, selectedFamily))
            { selectedFamily = staged; Activate(staged.Window); }
        }
        var top = TopAt(Input.mousePosition);
        foreach (var window in Windows) window.Update(allowPress && ReferenceEquals(window, top));
        epoch = WorkroomStorage.State.Epoch;
        if (discardConfirmation != null && (!disassemblyWindow.Open || mode != WorkroomPartProcessing.Mode.Discard ||
            !WorkroomPartProcessing.IsDiscardConfirmationCurrent(discardConfirmation))) CancelDiscard();
        if (revision != WorkroomStorage.State.Revision || generation != epoch)
        {
            var text = string.Join("", WorkroomStorage.State.Items.Where(record => record.Place == "slot").Select(record => record.Item.Name));
            AssemblyDebugFonts.Prepare(text, text); revision = WorkroomStorage.State.Revision; generation = epoch;
        }
        var enabled = WorkroomComponentAssembly.Ready(epoch);
        foreach (var pair in workbenchModes)
        {
            if (!pair.Key.Open) continue;
            pair.Value.Processing.GetComponent<Button>().interactable = enabled;
            pair.Value.Assembly.GetComponent<Button>().interactable = enabled;
            WorkroomPanelSkin.Selected(pair.Value.Processing, !assemblyMode);
            WorkroomPanelSkin.Selected(pair.Value.Assembly, assemblyMode);
        }
        var status = WorkroomStorage.State.Error != null ? WorkroomStorage.Text(WorkroomStorage.State.Error) :
            WorkroomItemCodec.CleanupPending ? WorkroomStorage.Text("recovery") :
            Environment.TickCount64 < WorkroomStorage.MessageUntil ? WorkroomStorage.Message : "";
        if (lastStatus != status) { AssemblyDebugFonts.Prepare(status, status); lastStatus = status; }
        foreach (var family in families)
        {
            var template = family.Template;
            if (!family.Window.Open) continue;
            var tier = WorkroomComponentAssembly.BoardTier(template.Key);
            foreach (var content in family.Content)
            {
                content.Ui.Root.SetActive(true);
                content.Ui.Enabled = tier > 0 && content.Index < template.Count(content.Kind, tier);
                PlacePart(content.Ui, template.Key, content.Kind, content.Index);
            }
            if (family.SummaryEpoch != epoch || family.SummaryRevision != WorkroomStorage.State.Revision || family.SummaryChinese != LanguageText.IsChinese)
            {
                var visibleSlots = WorkroomComponentAssembly.Slots(template, true).ToList();
                var records = visibleSlots.Select(WorkroomComponentAssembly.At)
                    .Where(record => record != null).Select(record => record!).ToList();
                var required = template.Kinds.Sum(template.MinCount);
                var hasCore = records.Any(record => WorkroomComponentTemplates.TryPart(record.Item.Identifier, out _, out var kind, out _) &&
                    (template.Key == "ram" ? kind == "memory" : template.PerformanceKind(kind)));
                var configTier = hasCore ? WorkroomComponentParts.EffectiveTier(records.Select(record => record.Item).ToList()) : 0;
                family.Hint.text = tier == 0 ? WorkroomComponentTemplates.Text("need_body") : WorkroomComponentTemplates.Text("assembly_body_hint");
                var summary = WorkroomComponentTemplates.Text("assembly_actual_summary", records.Count, required,
                    records.Sum(record => record.Item.Value), configTier == 0 ? "—" : configTier.ToString());
                var capacity = WorkroomComponentParts.CapacityText(records.Select(record => record.Item));
                if (capacity.Length > 0) summary += "\n" + capacity;
                if (family.Summary.text != summary)
                {
                    family.Summary.font = AssemblyDebugFonts.Prepare(summary, summary);
                    family.Summary.text = summary;
                }
                family.SummaryEpoch = epoch; family.SummaryRevision = WorkroomStorage.State.Revision; family.SummaryChinese = LanguageText.IsChinese;
            }
            family.Status.text = status;
            family.Assemble.GetComponent<Button>().interactable = enabled && WorkroomComponentAssembly.CanAssemble(template);
            family.AutoFill.GetComponent<Button>().interactable = enabled && tier > 0;
            family.Return.GetComponent<Button>().interactable = enabled && WorkroomComponentAssembly.Slots(template).Any(slot => WorkroomComponentAssembly.At(slot) != null);
        }
        foreach (var slot in slots) if (slot.Root.activeInHierarchy) slot.Refresh();
        entryStatus.text = disassemblyStatus.text = status;
        if (disassemblyWindow.Open)
        {
            var target = WorkroomComponentAssembly.At(WorkroomComponentAssembly.Disassembly)?.Id;
            if (quantityTarget != target || quantityEpoch != epoch)
            {
                quantityTarget = target; quantityEpoch = epoch; recycleQuantity = 1; quantityText = "1";
                CancelDiscard();
                editingQuantity = replaceQuantity = false; previewText = "";
            }
            var recycling = mode == WorkroomPartProcessing.Mode.Recycle;
            quantityLabel.gameObject.SetActive(recycling);
            foreach (var control in new[] { quantityMinus, quantityNumber, quantityPlus })
            { control.SetActive(recycling); control.GetComponent<Button>().interactable = enabled; }
            if (recycling) ReadQuantity(allowPress, top);
            quantityLabel.text = WorkroomComponentTemplates.Text("recycle_quantity", recycleQuantity);
            quantityNumber.GetComponentInChildren<TextMeshProUGUI>().text = quantityText + (editingQuantity ? "_" : "");
            var preview = WorkroomPartProcessing.Preview(mode, recycleQuantity);
            if (discardConfirmation != null) preview += "\n" + WorkroomComponentTemplates.Text("processing_discard_confirm_required");
            if (editingQuantity) preview = WorkroomComponentTemplates.Text("recycle_quantity_hint") + "\n" + preview;
            if (previewText != preview)
            {
                previewText = preview; previewOffset = 0;
                AssemblyDebugFonts.Prepare(preview, preview);
                processingPreview.text = preview;
                previewHeight = Math.Max(ProcessingHeight, processingPreview.GetPreferredValues(preview, ProcessingWidth, float.PositiveInfinity).y);
                ScrollPreview(0);
            }
            if (allowPress && ReferenceEquals(top, disassemblyWindow) && !Dragging &&
                RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(previewViewport), Input.mousePosition, null) && Input.mouseScrollDelta.y != 0)
                ScrollPreview(-Input.mouseScrollDelta.y*30);
            foreach (var pair in modeButtons)
            {
                var label = WorkroomComponentTemplates.Text(ModeKey(pair.Key));
                pair.Value.GetComponentInChildren<TextMeshProUGUI>().text = label;
                WorkroomPanelSkin.Selected(pair.Value, pair.Key == mode);
            }
            execute.GetComponentInChildren<TextMeshProUGUI>().text = WorkroomComponentTemplates.Text(
                discardConfirmation != null ? "discard_confirm" : ModeKey(mode));
            execute.GetComponent<Button>().interactable = enabled && recycleQuantity > 0 && WorkroomPartProcessing.CanExecute(mode, recycleQuantity);
            take.GetComponent<Button>().interactable = enabled && WorkroomComponentAssembly.At(WorkroomComponentAssembly.Disassembly) != null;
        }
    }

    internal void CancelPreview() => WorkroomUiCleanup.Deactivate(ghostRoot);
    internal void RoomPreview(Sprite? sprite, Vector2 position, Vector2 size, Color color,
        int orientation, bool flipped, bool visible)
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
