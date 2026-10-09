#nullable disable
using System;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>储存箱目录与独立装配窗口；非模态窗口只接管自身区域。</summary>
internal sealed class WorkroomPanels : IDisposable
{
    internal enum Kind { Storage, Components, Motherboard, Case }
    private Canvas canvas;
    private GameObject reference, face;
    private TextMeshProUGUI title, context;
    private GameObject header, closeButton, footer;
    private readonly WorkroomPanelState state = new();
    private WorkroomStorageUi storage;
    private WorkroomComponentUi components;
    private WorkroomMachineUi machines;
    private Kind kind;
    private bool titleDragging, pointerOwned, placed;
    private int pointerFrame = -1, neutralFrames, escapeFrame = -1;
    private Vector2 pressed, panelStart, storagePosition;

    internal bool BlocksInput => state.BlocksInput;
    internal bool Alive => ReferenceEquals(canvas, null) || (AssemblyDebugUi.Alive(canvas) && (machines?.Alive ?? true));
    internal bool StorageOpen => state.IsOpen && kind == Kind.Storage;
    internal bool ComponentsOpen => components?.Open ?? false;
    internal bool MachinesOpen => machines?.Open ?? false;
    internal bool PreviewOpen => StorageOpen || ComponentsOpen || MachinesOpen;
    internal bool IsDragging => titleDragging || (components?.Dragging ?? false) || (machines?.Dragging ?? false);
    internal bool OwnsTextInput => storage?.OwnsTextInput ?? false;
    private bool HitFace(Vector2 point) => state.IsOpen && AssemblyDebugUi.Alive(face) &&
        RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(face), point, null);
    internal bool HitWindow(Vector2 point) => HitFace(point) || (components?.Hit(point) ?? false) || (machines?.Hit(point) ?? false);
    private int FrontGroup(Vector2 point)
    {
        var storageIndex = HitFace(point) ? face.transform.GetSiblingIndex() : -1;
        var componentIndex = components?.SiblingIndex(point) ?? -1;
        var machineIndex = machines?.SiblingIndex(point) ?? -1;
        if (machineIndex > storageIndex && machineIndex > componentIndex) return 2;
        if (componentIndex > storageIndex) return 1;
        return storageIndex >= 0 ? 0 : -1;
    }
    private bool ComponentInFront(Vector2 point) => FrontGroup(point) == 1;
    internal string HitAssembly(Vector2 point) => FrontGroup(point) switch
    {
        1 => components.HitSlot(point),
        2 => machines.HitSlot(point),
        _ => null
    };
    internal string ResolveAssembly(Vector2 point, WorkroomItemCodec.Snapshot item)
    {
        var direct = HitAssembly(point);
        var target = direct ?? (FrontGroup(point) switch
        {
            1 => components.ResolveBody(point, item),
            2 => machines.ResolveBody(point, item),
            _ => null
        });
        WorkroomAssemblyWindow.PreviewSlot = target != null && WorkroomComponentAssembly.Accepts(target, item) ? target : null;
        return target;
    }
    internal WorkroomStorageState.Record HoveredAssembly(Vector2 point) => FrontGroup(point) switch
    {
        1 => components.HoveredItem(point),
        2 => machines.HoveredItem(point),
        _ => null
    };
    internal bool QuickStage(string id, long epoch)
    {
        if (MachinesOpen && machines.QuickStage(id, epoch)) return true;
        if (ComponentsOpen) return components.QuickStage(id, epoch);
        if (MachinesOpen) WorkroomStorage.NotifyText(WorkroomMachineAssembly.Text("quick_unavailable"));
        return false;
    }
    internal bool OwnsMousePress
    {
        get { SamplePointer(); return pointerOwned || OwnsTextInput || HitWindow(Input.mousePosition); }
    }
    internal bool OwnsScroll => OwnsTextInput || HitWindow(Input.mousePosition) || IsDragging;
    internal bool OwnsEscape => escapeFrame == Time.frameCount ||
        (PreviewOpen && !(Il2Cpp.ItemMouseDragHandler.current?.isDragging ?? false));
    internal bool HitStorage(Vector2 point) => StorageOpen && FrontGroup(point) == 0 && storage.Hit(point);
    internal void RoomPreview(Sprite sprite, Vector2 position, Vector2 size, Color color,
        int orientation, bool flipped, bool visible)
    {
        if (!visible)
        {
            WorkroomAssemblyWindow.PreviewSlot = null;
            storage?.Cancel(); components?.CancelPreview(); machines?.CancelPreview(); return;
        }
        var front = FrontGroup(Input.mousePosition);
        if (MachinesOpen && (front == 2 || (!ComponentsOpen && front != 0)))
        {
            storage.Cancel(); components.CancelPreview();
            machines.RoomPreview(sprite, position, size, color, orientation, flipped, visible);
        }
        else if (ComponentsOpen && front != 0)
        {
            storage.Cancel(); machines.CancelPreview();
            components.RoomPreview(sprite, position, size, color, orientation, flipped, visible);
        }
        else if (StorageOpen)
        {
            components.CancelPreview(); machines.CancelPreview();
            storage.RoomPreview(sprite, position, size, color, orientation, flipped, visible);
        }
    }

    internal static string Name(Kind kind) => LanguageText.Get("workroom.layout." + kind.ToString().ToLowerInvariant());

    internal void Open(Kind kind, bool inRoom)
    {
        if (BlocksInput) return;
        if (!AssemblyDebugUi.Alive(canvas)) Build();
        if (kind == Kind.Components)
        {
            if (!inRoom) return;
            storage.Blur();
            storage.CloseFilters();
            components.Show(); canvas.gameObject.SetActive(true); return;
        }
        if (kind == Kind.Motherboard || kind == Kind.Case)
        {
            if (!inRoom) return;
            storage.Blur();
            storage.CloseFilters();
            machines.Show(kind == Kind.Motherboard ? WorkroomMachineAssembly.Motherboard : WorkroomMachineAssembly.Case);
            canvas.gameObject.SetActive(true); return;
        }
        this.kind = kind;
        const int width = WorkroomStorageUi.Width, height = WorkroomStorageUi.Height;
        AssemblyDebugUi.Place(face, 686, 74, width, height);
        if (placed) AssemblyDebugUi.Rect(face).anchoredPosition = storagePosition;
        AssemblyDebugUi.Place(header, 0, 0, width, 40);
        AssemblyDebugUi.Place(title.gameObject, 16, 0, width-90, 40);
        AssemblyDebugUi.Place(closeButton, width-46, 6, 38, 28);
        AssemblyDebugUi.Place(context.gameObject, 16, 42, width-32, 18);
        AssemblyDebugUi.Place(footer, 16, height-17, width-32, 17);
        footer.GetComponent<TextMeshProUGUI>().text = WorkroomStorage.Text("window_hint");
        title.text = Name(kind);
        context.text = LanguageText.Get(inRoom ? "workroom.layout.context.room" : "workroom.layout.context.shop");
        storage.Open(inRoom);
        state.Open(inRoom, false);
        face.SetActive(true);
        face.transform.SetAsLastSibling();
        canvas.gameObject.SetActive(true);
    }

    private void Build()
    {
        var text = string.Join("", Name(Kind.Storage), Name(Kind.Components), Name(Kind.Motherboard), Name(Kind.Case),
            LanguageText.Get("workroom.layout.context.room"), LanguageText.Get("workroom.layout.context.shop"),
            LanguageText.Get("workroom.layout.not_open"), LanguageText.Get("workroom.layout.component_scope"),
            LanguageText.Get("workroom.layout.close_hint"), "Esc");
        foreach (var key in new[] { "take", "empty", "hint", "collect", "collect_result", "room_hint", "summary", "row", "broken", "pending",
            "stored", "taken", "full", "refused", "not_owned", "source_unsupported", "source_changed", "capture_failed",
            "failed", "recovery", "data_error", "unavailable", "invalid_position", "window_hint" })
            text += key == "collect_result" ? WorkroomStorage.Text(key, 0, 0, 0, 0) :
                key == "summary" || key == "row" ? WorkroomStorage.Text(key, "", 0, 0) : WorkroomStorage.Text(key);
        text += WorkroomStorage.Text("selection_hint", 0) +
            WorkroomStorage.Text("selection_empty") + WorkroomStorage.Text("selected_result", 0, 0, 0, 0);
        text += "↑↓0123456789" + WorkroomComponentUi.FontText + WorkroomMachineUi.FontText + WorkroomStorageUi.FontText;
        var font = AssemblyDebugFonts.Prepare(text, text);
        canvas = AssemblyDebugUi.NewCanvas("PCExpansion.WorkroomSubpanel", 32650);
        reference = AssemblyDebugUi.New("PanelReference", canvas.transform);
        var referenceRect = AssemblyDebugUi.Rect(reference);
        referenceRect.anchorMin = referenceRect.anchorMax = referenceRect.pivot = new Vector2(.5f, .5f);
        referenceRect.sizeDelta = new Vector2(1280, 720);
        referenceRect.anchoredPosition = Vector2.zero;
        face = AssemblyDebugUi.Image(reference.transform, "PanelFace", new Color(.08f, .12f, .13f, 1), true).gameObject;
        AssemblyDebugUi.Place(face, 686, 74, WorkroomStorageUi.Width, WorkroomStorageUi.Height);
        WorkroomPanelSkin.Scene(face, "storage", false);
        AssemblyDebugUi.Place(face.transform.Find("OperationSurface").gameObject, 6, 38, 528, WorkroomStorageUi.Height-58);
        var bar = AssemblyDebugUi.Image(face.transform, "Header", new Color(.17f, .24f, .24f, 1), true);
        header = bar.gameObject;
        AssemblyDebugUi.Place(bar.gameObject, 0, 0, 540, 40);
        title = AssemblyDebugUi.Text(face.transform, "Title", "", font, 18, true);
        AssemblyDebugUi.Place(title.gameObject, 16, 0, 450, 40);
        var close = WorkroomPanelSkin.Button(canvas, face.transform, "Close", "Esc", font, 14, Close);
        closeButton = close;
        AssemblyDebugUi.Place(close, 494, 6, 38, 28);
        context = AssemblyDebugUi.Text(face.transform, "Context", "", font, 11, true);
        AssemblyDebugUi.Place(context.gameObject, 16, 42, 508, 18);
        var footerText = AssemblyDebugUi.Text(face.transform, "CloseHint", LanguageText.Get("workroom.layout.close_hint"), font, 9);
        footer = footerText.gameObject;
        AssemblyDebugUi.Place(footer, 16, WorkroomStorageUi.Height-17, 508, 17);
        storage = new WorkroomStorageUi(canvas, face, font);
        components = new WorkroomComponentUi(canvas, reference.transform, font);
        machines = new WorkroomMachineUi(canvas, reference.transform, font);
        face.SetActive(false);
        canvas.gameObject.SetActive(false);
    }

    private void Close()
    {
        if (storage?.CloseFilters() == true) return;
        if (!state.IsOpen) return;
        state.Close();
        HideFace();
    }

    private void HideFace()
    {
        titleDragging = false;
        if (WorkroomUiCleanup.IsAlive(face)) storage?.Close();
        else storage?.Cancel();
        WorkroomUiCleanup.Deactivate(face);
    }

    private void CloseTop()
    {
        for (var index = reference.transform.childCount-1; index >= 0; index--)
        {
            var root = reference.transform.GetChild(index).gameObject;
            if (!root.activeInHierarchy || root.GetComponent<Image>()?.raycastTarget != true) continue;
            if (root.Pointer == face.Pointer) Close();
            else if (machines.OwnsRoot(root)) machines.CloseTop();
            else components.CloseTop();
            return;
        }
    }

    internal void Render(bool inRoom, bool stable, Rect viewport, bool roomDragging)
    {
        if (!AssemblyDebugUi.Alive(canvas)) return;
        SamplePointer();
        storage.UpdateInput(!roomDragging && (!Input.GetMouseButtonDown(0) || FrontGroup(Input.mousePosition) == 0));
        var wasOpen = state.IsOpen;
        var escape = OwnsEscape && !roomDragging && !storage.Composing && Input.GetKeyDown(KeyCode.Escape);
        if (escape) escapeFrame = Time.frameCount;
        if (escape && storage.CloseFilters()) escape = false;
        if (!inRoom || !stable) { components.Close(); machines.Close(); }
        else if (escape) CloseTop();
        state.Tick(inRoom, stable, escape && !inRoom, WorkroomGameAdapter.InputNeutral());
        if (wasOpen && !state.IsOpen) HideFace();
        WorkroomView.Fit(reference, viewport);
        canvas.gameObject.SetActive(state.IsOpen || ComponentsOpen || MachinesOpen || BlocksInput);
        var front = FrontGroup(Input.mousePosition);
        var scrolling = Input.mouseScrollDelta.y != 0;
        var allowPress = !roomDragging && !(Il2Cpp.ItemMouseDragHandler.current?.isDragging ?? false);
        if (StorageOpen)
        {
            MoveWindow(roomDragging);
            if (!scrolling || front == 0 || front == -1) storage.Update();
        }
        if (ComponentsOpen && (!scrolling || front == 1 || front == -1))
            components.Update(allowPress && !OwnsTextInput && ComponentInFront(Input.mousePosition));
        if (MachinesOpen) machines.Update(allowPress && !OwnsTextInput && FrontGroup(Input.mousePosition) == 2);
        WorkroomPanelSkin.Update(canvas, allowPress && !IsDragging);
        storage.UpdateInformation(StorageOpen && FrontGroup(Input.mousePosition) == 0 && allowPress && !IsDragging);
    }

    private void SamplePointer()
    {
        if (pointerFrame == Time.frameCount) return;
        pointerFrame = Time.frameCount;
        var down = false;
        var neutral = true;
        for (var i = 0; i < 7; i++)
        {
            down |= Input.GetMouseButtonDown(i);
            neutral &= !Input.GetMouseButton(i) && !Input.GetMouseButtonDown(i) && !Input.GetMouseButtonUp(i);
        }
        if (down && HitWindow(Input.mousePosition)) { pointerOwned = true; neutralFrames = 0; }
        if (!pointerOwned) return;
        neutralFrames = neutral ? neutralFrames+1 : 0;
        if (neutralFrames >= 2) pointerOwned = false;
    }

    private void MoveWindow(bool roomDragging)
    {
        var rect = AssemblyDebugUi.Rect(face);
        var parent = AssemblyDebugUi.Rect(reference);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Input.mousePosition, null, out var point)) return;
        if (Input.GetMouseButtonDown(0) && !roomDragging && !storage.FiltersOpen && FrontGroup(Input.mousePosition) == 0 && !(Il2Cpp.ItemMouseDragHandler.current?.isDragging ?? false) &&
            RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(header), Input.mousePosition, null) &&
            !RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(closeButton), Input.mousePosition, null))
        { titleDragging = true; pressed = point; panelStart = rect.anchoredPosition; face.transform.SetAsLastSibling(); }
        if (titleDragging && Input.GetMouseButton(0)) rect.anchoredPosition = panelStart + point-pressed;
        if (!Input.GetMouseButton(0)) titleDragging = false;
        var position = rect.anchoredPosition;
        position.x = Mathf.Clamp(position.x, 0, 1280-rect.rect.width);
        position.y = Mathf.Clamp(position.y, -720+rect.rect.height, 0);
        rect.anchoredPosition = storagePosition = position;
        placed = true;
    }

    internal void EnsureOverlay() => WorkroomView.Overlay(canvas);
    internal Canvas Canvas => canvas;

    public void Dispose()
    {
        WorkroomAssemblyWindow.PreviewSlot = null;
        titleDragging = pointerOwned = false;
        pointerFrame = escapeFrame = -1;
        neutralFrames = 0;
        var cleanup = new WorkroomUiCleanup();
        cleanup.Run("panels.stop", () => WorkroomUiCleanup.DeactivateCanvas(canvas));
        cleanup.Run("panels.storage", () => { storage?.Release(); storage = null; });
        cleanup.Run("panels.components", () => { components?.Release(); components = null; });
        cleanup.Run("panels.machines", () => { machines?.Close(); machines = null; });
        // A failed child release may still need its parent objects on the retry.
        cleanup.ThrowIfFailed();
        cleanup.Run("panels.canvas", () =>
        {
            if (!ReferenceEquals(canvas, null))
            {
                WorkroomGameAdapter.ForgetLetterboxCanvases(canvas, null, null);
                WorkroomPanelSkin.Forget(canvas);
                AssemblyDebugUi.Destroy(canvas);
            }
            canvas = null;
            reference = face = header = closeButton = footer = null;
            title = context = null;
            state.Reset();
        });
        cleanup.ThrowIfFailed();
    }
}
