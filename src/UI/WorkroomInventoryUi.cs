using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>仿原版网格；只移动保管记录，不创建隐藏原版库存或副本。</summary>
internal sealed class WorkroomInventoryUi
{
    private const float Cell = 17;
    private const float GridLeft = 373, GridTop = 489;
    private const float GridRimWidth = 3;
    private static readonly Color GridBackground = new(142f/255f, 155f/255f, 139f/255f, 1f);
    private static readonly Color GridLine = new(.33f, .39f, .35f, .62f);
    private static readonly Color GridRim = new(.15f, .17f, .21f, 1f);
    private static readonly WorkroomPanels.Kind[] Workbenches =
        { WorkroomPanels.Kind.Components, WorkroomPanels.Kind.Motherboard, WorkroomPanels.Kind.Case };
    private sealed class ItemView
    {
        internal GameObject Root = null!;
        internal Image Icon = null!;
        internal TextMeshProUGUI Count = null!;
        internal GameObject Selection = null!;
        internal GameObject Compatible = null!;
    }
    private sealed class GroupView
    {
        internal GameObject Root = null!;
        internal Image Icon = null!;
    }
    private GameObject grid, ghostRoot, bandRoot, groupGhostRoot;
    private Canvas canvas;
    private TMP_FontAsset font;
    private Image ghost;
    private readonly Dictionary<string, ItemView> icons = new();
    private readonly WorkroomItemInfo information = new();
    private string? held;
    private string? lastClicked;
    private long clickEpoch, clickRevision;
    private long clickInteraction;
    private float clickedAt;
    private Vector2 clickedPosition;
    private WorkroomItemCodec.Shape? heldShape;
    private int heldOrientation;
    private long epoch, revision = -1;
    private Vector2 pressed, offset;
    private bool dragging;
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private WorkroomStorage.RoomDrop[]? groupHeld;
    private readonly HashSet<string> groupIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GroupView> groupIcons = new(StringComparer.Ordinal);
    private Vector2 groupPoint;
    private bool groupDragging;
    private long groupPreviewRevision = -1, matPreviewRevision = -1;
    private int groupPreviewX, groupPreviewY, matPreviewOrientation;
    private string? matPreviewId;
    private WorkroomPanels.Kind matPreviewTarget;
    private bool groupPreviewFits, matPreviewFits;
    private long selectionEpoch = -1;
    private bool bandPending, banding;
    private Vector2 bandStart, bandPressed;
    private int escapeFrame = -1;
    private long selectionVersion, paintedVersion = -1, paintedRevision = -1, paintedEpoch = -1;
    private long highlightedRevision = -1;
    internal bool IsDragging => held != null || groupHeld != null || bandPending;
    internal bool HasSelection => selectionEpoch == WorkroomStorage.State.Epoch && selected.Count > 0;
    internal bool OwnsEscape => IsDragging || HasSelection || escapeFrame == Time.frameCount;
    internal bool OwnsMousePress => grid.activeInHierarchy && (IsDragging || GridPoint(Input.mousePosition).HasValue);
    private Camera? CanvasCamera => canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

    internal WorkroomInventoryUi(Canvas canvas, Transform parent, TMP_FontAsset font)
    {
        this.canvas = canvas; this.font = font;
        grid = AssemblyDebugUi.Image(parent, "WorkingInventory", GridBackground, false).gameObject;
        AssemblyDebugUi.Place(grid, GridLeft, GridTop, WorkroomStorageState.Columns*Cell, WorkroomStorageState.Rows*Cell);
        for (var x = 1; x < WorkroomStorageState.Columns; x++)
            AssemblyDebugUi.Place(AssemblyDebugUi.Image(grid.transform, "Column"+x, GridLine, false).gameObject,
                x*Cell, 0, 1, WorkroomStorageState.Rows*Cell);
        for (var y = 1; y < WorkroomStorageState.Rows; y++)
            AssemblyDebugUi.Place(AssemblyDebugUi.Image(grid.transform, "Row"+y, GridLine, false).gameObject,
                0, y*Cell, WorkroomStorageState.Columns*Cell, 1);
        ghostRoot = AssemblyDebugUi.New("InventoryDragPreview", canvas.transform);
        ghostRoot.SetActive(false);
        ghost = AssemblyDebugUi.Image(ghostRoot.transform, "ItemIcon", new Color(1,1,1,.7f), false);
        ghost.preserveAspect = true;
        groupGhostRoot = AssemblyDebugUi.New("InventoryGroupDragPreview", canvas.transform);
        groupGhostRoot.SetActive(false);
        bandRoot = AssemblyDebugUi.Image(grid.transform, "SelectionBand", new Color(.28f,.65f,.91f,.30f), false).gameObject;
        bandRoot.SetActive(false);
        var gridWidth = WorkroomStorageState.Columns*Cell;
        var gridHeight = WorkroomStorageState.Rows*Cell;
        PlaceRim(parent, "InventoryRimTop", GridLeft, GridTop, gridWidth, GridRimWidth);
        PlaceRim(parent, "InventoryRimBottom", GridLeft, GridTop+gridHeight-GridRimWidth, gridWidth, GridRimWidth);
        PlaceRim(parent, "InventoryRimLeft", GridLeft, GridTop, GridRimWidth, gridHeight);
        PlaceRim(parent, "InventoryRimRight", GridLeft+gridWidth-GridRimWidth, GridTop, GridRimWidth, gridHeight);
    }

    private static void PlaceRim(Transform parent, string name, float x, float y, float width, float height)
    {
        var rim = AssemblyDebugUi.Image(parent, name, GridRim, false);
        AssemblyDebugUi.Place(rim.gameObject, x, y, width, height);
    }

    internal void Update(bool interactive, WorkroomPanels panels)
    {
        panels.RoomPreview(null!, Vector2.zero, Vector2.zero, Color.clear, 0, false, false);
        if (!grid.activeInHierarchy) { Cancel(); return; }
        if (epoch != WorkroomStorage.State.Epoch) Cancel();
        if (revision != WorkroomStorage.State.Revision || epoch != WorkroomStorage.State.Epoch) Refresh();
        PruneSelection();
        PaintSelection();
        PaintCompatibility();
        if (panels.OwnsTextInput) { ResetClicks(); information.Clear(); return; }
        if (!interactive || !WorkroomStorage.State.Ready) { Cancel(); return; }
        var point = panels.HitWindow(Input.mousePosition) ? null : GridPoint(Input.mousePosition);
        var hovered = point.HasValue ? WorkroomStorage.State.RoomItems.LastOrDefault(item => Hit(item, point.Value)) : null;
        if (clickEpoch != WorkroomStorage.State.Epoch || clickRevision != WorkroomStorage.State.Revision || clickInteraction != panels.QuickStageToken ||
            Input.GetKeyDown(KeyCode.Escape) || (Input.GetMouseButtonDown(0) && hovered == null)) ResetClicks();
        if (Input.GetKeyDown(KeyCode.Escape) && OwnsEscape)
        { escapeFrame = Time.frameCount; Cancel(); return; }
        information.Update(!IsDragging && !Input.GetMouseButton(0) && !panels.IsDragging
            ? panels.HoveredAssembly(Input.mousePosition) ?? hovered : null);
        if (!IsDragging && !panels.IsDragging && hovered != null &&
            WorkroomQuickTransfer.TryReturn(hovered.Id, WorkroomStorage.State.Epoch))
        { ResetClicks(); information.Clear(); ClearSelection(WorkroomStorage.State.Epoch); return; }
        if (bandPending) { UpdateBand(); return; }
        if (groupHeld != null) { UpdateGroup(panels, hovered); return; }
        if (held == null && point.HasValue && hovered == null && Input.GetMouseButtonDown(0) && !panels.IsDragging)
        {
            ClearSelection(WorkroomStorage.State.Epoch);
            selectionEpoch = WorkroomStorage.State.Epoch;
            bandStart = point.Value; bandPressed = Input.mousePosition; bandPending = true; banding = false;
            ResetClicks(); information.Clear(); return;
        }
        if (held == null && hovered != null && Input.GetMouseButtonDown(0))
        {
            if (HasSelection && selected.Count > 1 && selected.Contains(hovered.Id) && !panels.IsDragging)
            {
                groupHeld = WorkroomStorage.State.RoomItems.Where(item => selected.Contains(item.Id))
                    .Select(item => new WorkroomStorage.RoomDrop(item)).ToArray();
                groupIds.Clear();
                foreach (var drop in groupHeld) groupIds.Add(drop.Id);
                foreach (var drop in groupHeld)
                    if (groupIcons.TryGetValue(drop.Id, out var preview)) preview.Icon.sprite = Icon(drop.Item);
                epoch = WorkroomStorage.State.Epoch; pressed = Input.mousePosition;
                groupPoint = point!.Value; groupDragging = false; information.Clear(); return;
            }
            ClearSelection(WorkroomStorage.State.Epoch);
            held = hovered.Id; epoch = WorkroomStorage.State.Epoch; pressed = Input.mousePosition;
            offset = point!.Value - new Vector2(hovered.X*Cell, hovered.Y*Cell); dragging = false;
            heldShape = hovered.Item.Footprint; heldOrientation = hovered.Item.Orientation;
            ghost.sprite = Icon(hovered.Item);
        }
        if (held == null) return;
        var itemHeld = WorkroomStorage.State.Find(held);
        if (itemHeld?.Place != "room" || epoch != WorkroomStorage.State.Epoch || Input.GetKeyDown(KeyCode.Escape))
        { Cancel(); return; }
        if (!dragging && ((Vector2)Input.mousePosition-pressed).sqrMagnitude > DragThresholdSquared())
        { dragging = true; RoomAudio.Play("pickup"); }
        var nativeDrag = ItemMouseDragHandler.current;
        var clockwise = Input.GetKeyDown(nativeDrag == null ? KeyCode.E : nativeDrag.rotateCWKey);
        var counterclockwise = Input.GetKeyDown(nativeDrag == null ? KeyCode.Q : nativeDrag.rotateCCWKey);
        if (clockwise != counterclockwise && Input.GetMouseButton(0))
        {
            var rotated = WorkroomItemPose.Rotate(heldShape!, clockwise);
            // Keep the preview centre at the same offset from the mouse.
            offset += new Vector2(rotated.Width-heldShape!.Width, rotated.Height-heldShape.Height) * (Cell*.5f);
            heldShape = rotated; heldOrientation = WorkroomItemPose.Turn(heldOrientation, clockwise);
            RoomAudio.Play("rotate");
            dragging = true;
        }
        if (dragging) ResetClicks();
        ghostRoot.SetActive(dragging && AssemblyDebugUi.Alive(ghost.sprite));
        var size = heldShape!;
        var rect = AssemblyDebugUi.Rect(ghostRoot);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f,.5f); rect.pivot = new Vector2(0,1);
        var parent = AssemblyDebugUi.Rect(ghostRoot.transform.parent.gameObject);
        var scale = Mathf.Abs(grid.transform.lossyScale.x / parent.lossyScale.x);
        rect.sizeDelta = new Vector2(size.Width*Cell*scale, size.Height*Cell*scale);
        WorkroomItemSprite.Pose(ghost, heldOrientation, itemHeld.Item.Flipped, rect.sizeDelta);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Input.mousePosition, CanvasCamera, out var mouseLocal);
        rect.anchoredPosition = mouseLocal - new Vector2(offset.x*scale, -offset.y*scale);
        var x = point.HasValue ? Mathf.RoundToInt((point.Value.x-offset.x)/Cell) : -1;
        var y = point.HasValue ? Mathf.RoundToInt((point.Value.y-offset.y)/Cell) : -1;
        var intoBox = HitBox(panels, Input.mousePosition);
        var onRecycling = panels.HitRecycling(Input.mousePosition);
        var intoRecycling = onRecycling && WorkroomBatchRecycling.Eligible(itemHeld.Item);
        var assemblySlot = panels.ResolveAssembly(Input.mousePosition, itemHeld.Item);
        var intoAssembly = assemblySlot != null && WorkroomComponentAssembly.Accepts(assemblySlot, itemHeld.Item);
        var workbench = HitWorkbench(panels, Input.mousePosition);
        var onMat = workbench.HasValue;
        var intoMat = onMat && MatFits(held, heldOrientation, workbench!.Value);
        var fits = point.HasValue && WorkroomStorage.State.Fits(size, x, y, held);
        ghost.color = intoRecycling || intoBox || intoAssembly || intoMat || fits ? new Color(.65f,1,.65f,.75f) : new Color(1,.5f,.5f,.75f);
        if (panels.PreviewOpen)
        {
            panels.RoomPreview(ghost.sprite, rect.anchoredPosition, rect.sizeDelta, ghost.color,
                heldOrientation, itemHeld.Item.Flipped, dragging);
            ghostRoot.SetActive(false);
        }
        if (Input.GetMouseButtonUp(0))
        {
            var id = held; var generation = epoch; var move = dragging; var orientation = heldOrientation;
            ReleaseHold();
            panels.RoomPreview(null!, Vector2.zero, Vector2.zero, Color.clear, 0, false, false);
            if (!move)
            {
                if (hovered?.Id == id) Click(id, generation, panels); else ResetClicks();
                return;
            }
            ResetClicks();
            if (onRecycling)
            {
                WorkroomStorageDragPatch.MarkConsumed();
                panels.AddRecycling(new[] { id }, generation);
            }
            else if (assemblySlot != null) WorkroomComponentAssembly.Stage(id, assemblySlot, generation, orientation);
            else if (intoBox) WorkroomStorage.StoreRoom(id, generation, orientation);
            else if (onMat)
            {
                WorkroomStorageDragPatch.MarkConsumed();
                panels.StageFromMat(id, generation, orientation, workbench!.Value);
            }
            else if (fits)
            {
                var beforeRevision = WorkroomStorage.State.Revision;
                WorkroomStorage.MoveRoom(id, x, y, generation, orientation);
                if (WorkroomStorage.State.Revision != beforeRevision) RoomAudio.Play("drop");
            }
            else WorkroomStorage.Notify("invalid_position");
        }
        else if (!Input.GetMouseButton(0)) Cancel();
    }

    private bool HitBox(WorkroomPanels panels, Vector2 point) =>
        (panels.StorageOpen && panels.HitStorage(point)) ||
        (!panels.BlocksInput && !panels.HitWindow(point) && InReferenceRect(point, 1065,542,202,143));

    // Click and drag share the scene entrances. A covering window owns its
    // complete area, including incompatible drops and empty body space.
    private WorkroomPanels.Kind? HitWorkbench(WorkroomPanels panels, Vector2 point)
    {
        if (panels.BlocksInput || panels.IsDragging || panels.OwnsTextInput || panels.HitWindow(point)) return null;
        foreach (var kind in Workbenches)
        {
            var area = WorkroomView.WorkbenchRect(kind);
            if (InReferenceRect(point, area.x, area.y, area.width, area.height)) return kind;
        }
        return null;
    }

    private bool MatFits(string id, int orientation, WorkroomPanels.Kind target)
    {
        var state = WorkroomStorage.State;
        if (!WorkroomComponentAssembly.Ready(epoch) || state.PendingRecords.Count != 0 || WorkroomCashRepair.Pending)
        { matPreviewRevision = -1; return false; }
        if (matPreviewId != id || matPreviewRevision != state.Revision || matPreviewOrientation != orientation || matPreviewTarget != target)
        {
            matPreviewFits = WorkroomMatTransfer.TryPlan(id, epoch, orientation, target, out _, out _);
            matPreviewId = id; matPreviewRevision = state.Revision; matPreviewOrientation = orientation; matPreviewTarget = target;
        }
        return matPreviewFits;
    }

    private bool GroupFits(WorkroomStorage.RoomDrop[] command, int dx, int dy)
    {
        var state = WorkroomStorage.State;
        if (!WorkroomComponentAssembly.Ready(epoch) || state.PendingRecords.Count != 0)
        { groupPreviewRevision = -1; return false; }
        if (groupPreviewRevision != state.Revision || groupPreviewX != dx || groupPreviewY != dy)
        {
            groupPreviewFits = WorkroomGroupMove.CanMove(command, dx, dy, epoch);
            groupPreviewRevision = state.Revision; groupPreviewX = dx; groupPreviewY = dy;
        }
        return groupPreviewFits;
    }

    private void UpdateGroup(WorkroomPanels panels, WorkroomStorageState.Record? hovered)
    {
        var command = groupHeld!;
        if (epoch != WorkroomStorage.State.Epoch || command.Length == 0 || command.Any(item => !item.Unchanged))
        { ReleaseGroup(); ResetClicks(); WorkroomStorage.Notify("source_changed"); return; }
        if (!groupDragging && ((Vector2)Input.mousePosition - pressed).sqrMagnitude >= DragThresholdSquared())
        { groupDragging = true; RoomAudio.Play("pickup"); }
        var intoBox = HitBox(panels, Input.mousePosition);
        var onRecycling = panels.HitRecycling(Input.mousePosition);
        var intoRecycling = onRecycling && command.All(item => WorkroomBatchRecycling.Eligible(item.Item));
        var point = panels.HitWindow(Input.mousePosition) ? null : GridPoint(Input.mousePosition);
        var dx = point.HasValue ? Mathf.RoundToInt((point.Value.x-groupPoint.x)/Cell) : 0;
        var dy = point.HasValue ? Mathf.RoundToInt((point.Value.y-groupPoint.y)/Cell) : 0;
        var fits = point.HasValue && GroupFits(command, dx, dy);
        var onMat = HitWorkbench(panels, Input.mousePosition).HasValue;
        var color = intoRecycling || intoBox || fits ? new Color(.65f,1,.65f,.75f) : new Color(1,.5f,.5f,.75f);
        if (groupDragging)
        {
            ResetClicks();
            var parent = AssemblyDebugUi.Rect(groupGhostRoot.transform.parent.gameObject);
            var scale = Mathf.Abs(grid.transform.lossyScale.x / parent.lossyScale.x);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Input.mousePosition, CanvasCamera, out var mouse);
            if (point.HasValue)
                mouse += new Vector2((groupPoint.x+dx*Cell-point.Value.x)*scale,
                    -(groupPoint.y+dy*Cell-point.Value.y)*scale);
            var root = AssemblyDebugUi.Rect(groupGhostRoot);
            root.anchorMin = root.anchorMax = new Vector2(.5f,.5f); root.pivot = new Vector2(0,1);
            root.anchoredPosition = mouse;
            foreach (var pair in groupIcons) pair.Value.Root.SetActive(groupIds.Contains(pair.Key));
            foreach (var item in command)
            {
                if (!groupIcons.TryGetValue(item.Id, out var preview))
                {
                    var outer = AssemblyDebugUi.New(item.Id, groupGhostRoot.transform);
                    var inner = AssemblyDebugUi.Image(outer.transform, "Icon", color, false);
                    inner.preserveAspect = true; inner.sprite = Icon(item.Item);
                    preview = new GroupView { Root = outer, Icon = inner }; groupIcons.Add(item.Id, preview);
                }
                var image = preview.Icon;
                image.color = color; preview.Root.SetActive(true);
                var size = new Vector2(item.Item.Footprint.Width*Cell*scale, item.Item.Footprint.Height*Cell*scale);
                AssemblyDebugUi.Place(preview.Root, (item.X*Cell-groupPoint.x)*scale,
                    (item.Y*Cell-groupPoint.y)*scale, size.x, size.y);
                WorkroomItemSprite.Pose(image, item.Item.Orientation, item.Item.Flipped, size);
            }
            var panelPreview = panels.PreviewOpen && !point.HasValue;
            groupGhostRoot.transform.SetAsLastSibling(); groupGhostRoot.SetActive(!panelPreview);
            // Keep the complete formation visible over the uncovered room grid.
            // Only use the panel's higher preview while outside that grid.
            if (panelPreview)
            {
                var first = command[0]; var image = groupIcons[first.Id].Icon;
                var size = new Vector2(first.Item.Footprint.Width*Cell*scale, first.Item.Footprint.Height*Cell*scale);
                var position = mouse + new Vector2((first.X*Cell-groupPoint.x)*scale, -(first.Y*Cell-groupPoint.y)*scale);
                panels.RoomPreview(image.sprite, position, size, color, first.Item.Orientation, first.Item.Flipped, true);
            }
            else panels.RoomPreview(null!, Vector2.zero, Vector2.zero, Color.clear, 0, false, false);
        }
        if (Input.GetMouseButtonUp(0))
        {
            var generation = epoch; var move = groupDragging;
            ReleaseGroup();
            panels.RoomPreview(null!, Vector2.zero, Vector2.zero, Color.clear, 0, false, false);
            if (!move)
            {
                if (hovered != null && selected.Contains(hovered.Id)) Click(hovered.Id, generation, panels);
                return;
            }
            ResetClicks();
            if (intoBox || onMat || point.HasValue || panels.HitWindow(Input.mousePosition)) WorkroomStorageDragPatch.MarkConsumed();
            if (onRecycling)
            {
                if (panels.AddRecycling(command.Select(item => item.Id).ToArray(), generation)) ClearSelection(generation);
            }
            else if (intoBox)
            {
                WorkroomStorage.StoreRoomGroup(command, generation);
                PruneSelection(); PaintSelection();
            }
            else if (onMat)
            {
                WorkroomStorage.NotifyText(LanguageText.Get("workroom.mat.single"));
                RoomAudio.Play("warning");
            }
            else if (point.HasValue)
            {
                var beforeRevision = WorkroomStorage.State.Revision;
                if (WorkroomGroupMove.Move(command, dx, dy, generation) &&
                    WorkroomStorage.State.Revision != beforeRevision) RoomAudio.Play("drop");
                PruneSelection(); PaintSelection();
            }
            else WorkroomStorage.Notify("invalid_position");
            // No group rotation or automatic repacking. Missed/blocked targets
            // retain every original record; a valid move preserves the selection.
        }
        else if (!Input.GetMouseButton(0)) { ReleaseGroup(); ResetClicks(); }
    }

    private bool InReferenceRect(Vector2 screen, float x, float y, float w, float h)
    {
        var parent = grid.transform.parent.TryCast<RectTransform>();
        if (parent == null) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, CanvasCamera, out var local)) return false;
        var bounds = parent.rect;
        var reference = new Vector2(local.x - bounds.xMin, bounds.yMax - local.y);
        return reference.x >= x && reference.x <= x+w && reference.y >= y && reference.y <= y+h;
    }

    private Vector2? GridPoint(Vector2 screen)
    {
        var rect = AssemblyDebugUi.Rect(grid);
        if (!RectTransformUtility.RectangleContainsScreenPoint(rect, screen, CanvasCamera)) return null;
        return LocalGridPoint(screen);
    }

    private Vector2? LocalGridPoint(Vector2 screen)
    {
        var rect = AssemblyDebugUi.Rect(grid);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, CanvasCamera, out var local)) return null;
        return new Vector2(local.x - rect.rect.xMin, rect.rect.yMax - local.y);
    }

    private static float DragThresholdSquared()
    {
        var threshold = ItemMultiSelectHandler.current?.dragThreshold ?? 4f;
        if (!float.IsFinite(threshold) || threshold <= 0) threshold = 4f;
        return threshold * threshold;
    }

    private void UpdateBand()
    {
        if (selectionEpoch != WorkroomStorage.State.Epoch || Input.GetKeyDown(KeyCode.Escape))
        { Cancel(); return; }
        var point = LocalGridPoint(Input.mousePosition);
        if (!point.HasValue) { Cancel(); return; }
        if (((Vector2)Input.mousePosition-bandPressed).sqrMagnitude >= DragThresholdSquared()) banding = true;
        if (banding)
        {
            var last = new Vector2(Mathf.Clamp(point.Value.x, 0, WorkroomStorageState.Columns*Cell),
                Mathf.Clamp(point.Value.y, 0, WorkroomStorageState.Rows*Cell));
            var min = Vector2.Min(bandStart, last); var max = Vector2.Max(bandStart, last);
            var area = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            var next = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in WorkroomStorage.State.RoomItems)
                if (BandOverlaps(item, area)) next.Add(item.Id);
            if (!selected.SetEquals(next))
            { selected.Clear(); selected.UnionWith(next); selectionVersion++; }
            AssemblyDebugUi.Place(bandRoot, min.x, min.y, max.x-min.x, max.y-min.y);
            bandRoot.transform.SetAsLastSibling(); bandRoot.SetActive(true); PaintSelection();
        }
        if (Input.GetMouseButtonUp(0))
        {
            bandPending = banding = false; bandRoot.SetActive(false);
            Core.Debug($"[工作间框选] selected={selected.Count}; frame={Time.frameCount}");
            PaintSelection(); return;
        }
        if (!Input.GetMouseButton(0)) Cancel();
    }

    private static bool BandOverlaps(WorkroomStorageState.Record item, Rect area)
    {
        var shape = item.Item.Footprint;
        for (var y = 0; y < shape.Height; y++)
            for (var x = 0; x < shape.Width; x++)
                if (shape.Cells[y*shape.Width+x] != 0 &&
                    area.Overlaps(new Rect((item.X+x)*Cell, (item.Y+y)*Cell, Cell, Cell))) return true;
        return false;
    }

    private void PruneSelection()
    {
        if (selectionEpoch != WorkroomStorage.State.Epoch)
        { selected.Clear(); selectionEpoch = WorkroomStorage.State.Epoch; selectionVersion++; }
        if (selected.RemoveWhere(id => WorkroomStorage.State.Find(id)?.Place != "room") > 0) selectionVersion++;
    }

    internal void ClearSelection(long generation)
    {
        if (generation != WorkroomStorage.State.Epoch) return;
        selected.Clear(); selectionEpoch = generation; selectionVersion++; PaintSelection();
    }

    private static bool Hit(WorkroomStorageState.Record item, Vector2 point)
    {
        var x = Mathf.FloorToInt(point.x/Cell)-item.X; var y = Mathf.FloorToInt(point.y/Cell)-item.Y;
        var shape = item.Item.Footprint;
        return x >= 0 && y >= 0 && x < shape.Width && y < shape.Height && shape.Cells[y*shape.Width+x] != 0;
    }

    private void Refresh()
    {
        var items = WorkroomStorage.State.RoomItems;
        var text = string.Join("", items.Select(item => item.Item.Name));
        AssemblyDebugFonts.Prepare(text, text);
        var ids = items.Select(item => item.Id).ToHashSet();
        foreach (var pair in icons.ToArray())
            if (!ids.Contains(pair.Key)) { UnityEngine.Object.Destroy(pair.Value.Root); icons.Remove(pair.Key); }
        foreach (var pair in groupIcons.ToArray())
            if (!ids.Contains(pair.Key)) { UnityEngine.Object.Destroy(pair.Value.Root); groupIcons.Remove(pair.Key); }
        foreach (var item in items)
        {
            if (!icons.TryGetValue(item.Id, out var view))
            {
                var root = AssemblyDebugUi.Image(grid.transform, item.Id, new Color(.2f,.27f,.29f,.5f), false).gameObject;
                var image = AssemblyDebugUi.Image(root.transform, "ItemIcon", Color.white, false); image.preserveAspect = true;
                var count = AssemblyDebugUi.Text(root.transform, "Quantity", "", font, 12, true);
                AssemblyDebugUi.Place(count.gameObject, 2, 0, 80, 16);
                var selection = AssemblyDebugUi.Image(root.transform, "Selected", new Color(.28f,.65f,.91f,.27f), false).gameObject;
                AssemblyDebugUi.Stretch(selection); selection.SetActive(false);
                var compatible = WorkroomCandidateHighlight.Frame(root.transform, "Compatible"); compatible.SetActive(false);
                view = new ItemView { Root = root, Icon = image, Count = count, Selection = selection, Compatible = compatible }; icons[item.Id] = view;
            }
            AssemblyDebugUi.Place(view.Root, item.X*Cell, item.Y*Cell, item.Item.Footprint.Width*Cell, item.Item.Footprint.Height*Cell);
            view.Icon.sprite = Icon(item.Item);
            WorkroomItemSprite.Pose(view.Icon, item.Item.Orientation, item.Item.Flipped,
                new Vector2(item.Item.Footprint.Width*Cell, item.Item.Footprint.Height*Cell));
            view.Count.text = item.Item.Count > 1 ? item.Item.Count.ToString() : "";
        }
        revision = WorkroomStorage.State.Revision; epoch = WorkroomStorage.State.Epoch;
        highlightedRevision = -1;
    }

    private void PaintCompatibility()
    {
        if (highlightedRevision == WorkroomCandidateHighlight.Revision) return;
        foreach (var record in WorkroomStorage.State.RoomItems)
            if (icons.TryGetValue(record.Id, out var view))
                view.Compatible.SetActive(WorkroomCandidateHighlight.Matches(record.Item));
        highlightedRevision = WorkroomCandidateHighlight.Revision;
    }

    internal static Sprite? Icon(WorkroomItemCodec.Snapshot item)
    {
        try
        {
            if (SpriteAssets.IsSpriteKey(item.Sprite)) return SpriteAssets.Get(item.Sprite);
            var render = RenderHandler.current;
            if (render == null) return null;
            return string.IsNullOrEmpty(item.Atlas) ? RenderHandler.LoadFromFile(item.Sprite) : RenderHandler.LoadFromAtlas(item.Atlas, item.Sprite);
        }
        catch (Exception ex) { Core.Debug("工作间图标回退：" + ex.Message); return RenderHandler.unknownSprite; }
    }
    private void PaintSelection()
    {
        var state = WorkroomStorage.State;
        if (paintedVersion == selectionVersion && paintedRevision == state.Revision && paintedEpoch == state.Epoch) return;
        foreach (var pair in icons)
        {
            if (!WorkroomUiCleanup.IsAlive(pair.Value.Root)) continue;
            var background = pair.Value.Root.GetComponent<Image>();
            if (!WorkroomUiCleanup.IsAlive(background)) continue;
            background.color = HasSelection && selected.Contains(pair.Key)
                ? new Color(.22f,.48f,.64f,.85f) : new Color(.2f,.27f,.29f,.5f);
            WorkroomUiCleanup.Deactivate(pair.Value.Selection);
            if (HasSelection && selected.Contains(pair.Key)) pair.Value.Selection.SetActive(true);
        }
        paintedVersion = selectionVersion; paintedRevision = state.Revision; paintedEpoch = state.Epoch;
    }

    internal void Cancel()
    {
        ResetGestures(WorkroomStorage.State.Epoch);
        WorkroomUiCleanup.Deactivate(ghostRoot);
        WorkroomUiCleanup.Deactivate(groupGhostRoot);
        information.Clear();
        WorkroomUiCleanup.Deactivate(bandRoot);
        PaintSelection();
    }

    private void ResetHold()
    {
        held = null; heldShape = null; dragging = false;
        heldOrientation = 0; pressed = offset = Vector2.zero;
        matPreviewId = null; matPreviewRevision = -1;
    }

    private void ResetGroup()
    { groupHeld = null; groupIds.Clear(); groupDragging = false; groupPoint = Vector2.zero; groupPreviewRevision = -1; }

    private void ReleaseGroup()
    { ResetGroup(); WorkroomUiCleanup.Deactivate(groupGhostRoot); }

    private void ResetGestures(long generation)
    {
        ResetHold(); ResetGroup(); ResetClicks();
        bandPending = banding = false;
        bandStart = bandPressed = Vector2.zero;
        if (selected.Count > 0) { selected.Clear(); selectionVersion++; }
        selectionEpoch = generation;
    }

    private void ReleaseHold()
    {
        ResetHold();
        WorkroomUiCleanup.Deactivate(ghostRoot);
    }

    private void ResetClicks() => lastClicked = null;

    private void Click(string id, long generation, WorkroomPanels panels)
    {
        var threshold = ItemMouseDoubleClickHandler.current?.doubleclickTimeThresholdSeconds ?? .3f;
        if (!float.IsFinite(threshold) || threshold <= 0) threshold = .3f;
        var now = Time.unscaledTime;
        if (lastClicked == id && clickEpoch == generation && clickRevision == WorkroomStorage.State.Revision && clickInteraction == panels.QuickStageToken &&
            now >= clickedAt && now-clickedAt <= threshold &&
            ((Vector2)Input.mousePosition-clickedPosition).sqrMagnitude <= 16)
        {
            ResetClicks(); information.Clear(); panels.QuickStage(id, generation); return;
        }
        lastClicked = id; clickEpoch = generation; clickRevision = WorkroomStorage.State.Revision;
        clickInteraction = panels.QuickStageToken;
        clickedAt = now; clickedPosition = Input.mousePosition;
    }

    internal void EnsureOverlay() => information.EnsureOverlay();
    internal void Dispose()
    {
        // The room canvas owns the visuals. Release gestures and cached wrappers
        // without repainting selection or rebuilding an overlay during teardown.
        ResetGestures(-1);
        icons.Clear();
        groupIcons.Clear();
        epoch = revision = paintedVersion = paintedRevision = paintedEpoch = -1;
        escapeFrame = -1;
        clickEpoch = clickRevision = 0; clickedAt = 0; clickedPosition = Vector2.zero;
        var cleanup = new WorkroomUiCleanup();
        cleanup.Run("inventory.grid", () => { WorkroomUiCleanup.Deactivate(grid); grid = null!; });
        cleanup.Run("inventory.ghost", () =>
        {
            WorkroomUiCleanup.Deactivate(ghostRoot); ghostRoot = null!; ghost = null!;
        });
        cleanup.Run("inventory.band", () => { WorkroomUiCleanup.Deactivate(bandRoot); bandRoot = null!; });
        cleanup.Run("inventory.group", () => { WorkroomUiCleanup.Deactivate(groupGhostRoot); groupGhostRoot = null!; });
        cleanup.Run("inventory.information", information.Dispose);
        cleanup.ThrowIfFailed();
        canvas = null!; font = null!;
    }
}
