#nullable disable
using System;
using System.Collections.Generic;
using Il2CppTMPro;
using Il2Cpp;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>正式工作间底图、固定出口及共用子面板；店内贴图钥匙提供入口。</summary>
internal sealed class WorkroomView : IDisposable
{
    private const float Width = 1280, Height = 720;
    private const int RoomOrder = 32600, BlackOrder = 32700;
    private static readonly Color BackdropColor = new(.10f, .13f, .14f, 1);
    private Canvas noticeCanvas, roomCanvas, blackCanvas, shopCanvas;
    private GameObject noticeSpace, roomSpace, shopSpace, hint;
    private GameObject shopCrate;
    private GameObject exitEntry;
    private readonly Dictionary<WorkroomPanels.Kind, GameObject> roomEntries = new();
    private WorkroomEntryHover entryHover;
    private readonly WorkroomPanels panels = new();
    private Image backdrop, black;
    private TextMeshProUGUI hintText;
    private TMP_FontAsset font;
    private long hintUntil;
    private long lastRoomMessageUntil;
    private WorkroomInventoryUi inventory;
    private bool storagePointerOwned;
    private int storagePointerFrame = -1, storageNeutralFrames;

    internal bool Alive => AssemblyDebugUi.Alive(noticeCanvas) && AssemblyDebugUi.Alive(roomCanvas) &&
        AssemblyDebugUi.Alive(blackCanvas) && AssemblyDebugUi.Alive(shopCanvas) &&
        AssemblyDebugUi.Alive(backdrop) && panels.Alive && (entryHover?.Alive ?? false);
    internal bool BlocksPanelInput => panels.BlocksInput;
    internal bool OwnsTextInput => panels.OwnsTextInput;
    internal bool IsDragging => panels.IsDragging || (inventory?.IsDragging ?? false);
    internal bool HasRoomSelection => inventory?.HasSelection ?? false;
    internal bool OwnsMousePress
    {
        get
        {
            SampleStoragePointer();
            return storagePointerOwned || panels.OwnsMousePress || (inventory?.OwnsMousePress ?? false) || HitShopStorage(Input.mousePosition);
        }
    }

    private void SampleStoragePointer()
    {
        if (storagePointerFrame == Time.frameCount) return;
        storagePointerFrame = Time.frameCount;
        if (Input.GetMouseButtonDown(0) && HitShopStorage(Input.mousePosition))
        { storagePointerOwned = true; storageNeutralFrames = 0; }
        if (!storagePointerOwned) return;
        var neutral = !Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0) && !Input.GetMouseButtonUp(0);
        storageNeutralFrames = neutral ? storageNeutralFrames + 1 : 0;
        if (storageNeutralFrames < 2) return;
        storagePointerOwned = false;
        Core.Log?.Msg("[工作间箱入口] pointer=released; frame=" + Time.frameCount);
    }
    internal bool OwnsScroll => panels.OwnsScroll;
    internal bool OwnsEscape => (inventory?.OwnsEscape ?? false) || panels.OwnsEscape;
    internal bool HitStorageWindow(Vector2 point) => panels.HitWindow(point);
    internal bool HitShopStorage(Vector2 point)
    {
        if (panels.HitWindow(point)) return panels.HitStorage(point);
        if (!ShopCrateContains(point)) return false;
        var target = WorkroomNativeTooltip.PointerTarget();
        return target != null && (target.Pointer == shopCrate.Pointer || target.transform.IsChildOf(shopCrate.transform));
    }

    // During a native drag its own preview can become the top raycast hit.
    // Drop acceptance follows the visible crate rectangle instead of that
    // changing raycast owner; ordinary clicks retain the owner check above.
    internal bool HitShopStorageDrop(Vector2 point)
    {
        if (panels.HitWindow(point)) return panels.HitStorage(point);
        return ShopCrateContains(point);
    }

    private bool ShopCrateContains(Vector2 point)
    {
        if (!AssemblyDebugUi.Alive(shopCrate) || !shopCrate.activeInHierarchy) return false;
        var camera = shopCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : shopCanvas.worldCamera;
        return RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(shopCrate), point, camera);
    }
    internal void OpenPanel(WorkroomPanels.Kind kind, bool inRoom) => panels.Open(kind, inRoom);
    internal void ReleaseItemInfo() => inventory?.Cancel();
    internal void ClearRoomSelection(long epoch) => inventory?.ClearSelection(epoch);

    internal void Build(Action exit)
    {
        var labels = new[] { "title", "exit", "bench", "transit", "placeholder", "busy", "unavailable" };
        var texts = new List<string>();
        foreach (var label in labels) texts.Add(Text(label));
        foreach (var key in new[] { "title", "storage", "components", "motherboard", "case", "inventory" })
            texts.Add(LanguageText.Get("workroom.layout." + key));
        font = AssemblyDebugFonts.Prepare(texts[0], string.Join("", texts));
        noticeCanvas = AssemblyDebugUi.NewCanvas("PCExpansion.WorkroomNotice", 31010);
        noticeSpace = Space(noticeCanvas.transform, "NoticeReference");
        hint = Box(noticeSpace.transform, "KeyHint", 420, 640,
            440, 44, new Color(.04f, .07f, .09f, .98f));
        hintText = AssemblyDebugUi.Text(hint.transform, "Hint", Text("busy"), font, 15);
        AssemblyDebugUi.Stretch(hintText.gameObject);
        hint.SetActive(false);

        roomCanvas = AssemblyDebugUi.NewCanvas("PCExpansion.WorkroomRegion", RoomOrder);
        // Full-screen cover sits outside the fitted room art, including its margins.
        // The native shop remains rendered underneath and owns its display flags.
        backdrop = AssemblyDebugUi.Image(roomCanvas.transform, "Backdrop", BackdropColor, true);
        AssemblyDebugUi.Stretch(backdrop.gameObject);
        roomSpace = Space(roomCanvas.transform, "RoomReference");
        DrawRoom(exit);
        roomCanvas.gameObject.SetActive(false);

        // Lowest native UI order; follow its actual Overlay/Camera mode below.
        shopCanvas = AssemblyDebugUi.NewCanvas("PCExpansion.ShopStorageEntry", -2);
        shopSpace = Space(shopCanvas.transform, "ShopReference");
        var crate = AssemblyDebugUi.Button(shopCanvas, shopSpace.transform, "StorageEntry", Color.clear,
            () => WorkroomTrial.RequestPanel(WorkroomPanels.Kind.Storage, false));
        shopCrate = crate;
        AssemblyDebugUi.Place(crate, 921, 594, 142, 109);
        var crateArt = AssemblyDebugUi.Image(crate.transform, "CrateArt", Color.white, false);
        crateArt.sprite = WorkroomArt.Get("workroom_storage_chest");
        crateArt.preserveAspect = true;
        AssemblyDebugUi.Stretch(crateArt.gameObject);
        shopCanvas.gameObject.SetActive(false);

        blackCanvas = AssemblyDebugUi.NewCanvas("PCExpansion.WorkroomBlack", BlackOrder);
        black = AssemblyDebugUi.Image(blackCanvas.transform, "Fade", Color.black, true);
        AssemblyDebugUi.Stretch(black.gameObject);
        blackCanvas.gameObject.SetActive(false);
        entryHover = new WorkroomEntryHover();
        // Silhouettes use the existing 1280x720 layout and sample the original art unchanged.
        AttachRoomHover(exitEntry, new[] { new Vector2(60, 105), new Vector2(297, 105),
            new Vector2(297, 573), new Vector2(60, 573) });
        AttachRoomHover(roomEntries[WorkroomPanels.Kind.Components], new[] { new Vector2(515, 342),
            new Vector2(762, 342), new Vector2(786, 404), new Vector2(488, 404) });
        // Trace the metal/PCB edges, including the stand's recesses; exclude the table and cast shadow.
        AttachRoomHover(roomEntries[WorkroomPanels.Kind.Motherboard], new[] {
            new Vector2(914.3f, 299.8f), new Vector2(918.7f, 299.0f), new Vector2(921.0f, 300.3f), new Vector2(923.1f, 294.5f),
            new Vector2(936.0f, 293.8f), new Vector2(939.0f, 296.5f), new Vector2(997.8f, 295.3f), new Vector2(998.7f, 292.6f),
            new Vector2(1002.2f, 291.8f), new Vector2(1002.2f, 291.0f), new Vector2(1017.9f, 290.9f), new Vector2(1019.8f, 295.9f),
            new Vector2(1022.3f, 296.8f), new Vector2(1024.0f, 299.4f), new Vector2(1028.0f, 298.9f), new Vector2(1030.8f, 301.1f),
            new Vector2(1032.5f, 310.0f), new Vector2(1055.8f, 378.5f), new Vector2(1057.4f, 382.0f), new Vector2(1062.0f, 383.0f),
            new Vector2(1061.6f, 391.4f), new Vector2(1063.5f, 394.4f), new Vector2(1063.0f, 395.6f), new Vector2(931.8f, 395.6f),
            new Vector2(905.1f, 363.4f), new Vector2(901.9f, 360.1f), new Vector2(901.5f, 350.7f), new Vector2(907.2f, 347.7f),
            new Vector2(907.6f, 339.0f), new Vector2(909.2f, 337.7f), new Vector2(910.9f, 326.5f), new Vector2(912.1f, 326.5f),
            new Vector2(912.7f, 311.6f), new Vector2(914.1f, 311.0f) });
        AttachRoomHover(roomEntries[WorkroomPanels.Kind.Case], new[] {
            new Vector2(1066.3f, 198.6f), new Vector2(1080.9f, 197.4f), new Vector2(1080.9f, 195.9f), new Vector2(1109.4f, 192.9f),
            new Vector2(1165.6f, 190.1f), new Vector2(1201.1f, 190.9f), new Vector2(1231.6f, 193.2f), new Vector2(1244.7f, 193.6f),
            new Vector2(1244.7f, 388.0f), new Vector2(1242.5f, 391.5f), new Vector2(1183.0f, 400.0f), new Vector2(1178.7f, 400.3f),
            new Vector2(1066.8f, 362.8f) });
        AttachRoomHover(roomEntries[WorkroomPanels.Kind.Storage], new[] { new Vector2(1069, 544),
            new Vector2(1218, 544), new Vector2(1265, 578), new Vector2(1265, 681),
            new Vector2(1088, 680), new Vector2(1069, 639) });
        entryHover.AddShop(shopCrate, WorkroomArt.Get("workroom_storage_chest"), new Rect(921, 594, 142, 109),
            roomCanvas, roomSpace.transform, shopCanvas, shopSpace.transform);
    }

    private void AttachRoomHover(GameObject button, Vector2[] contour) => entryHover.AddRoom(button, contour,
        roomCanvas, roomSpace.transform, shopCanvas, shopSpace.transform);

    private static string Text(string name) => LanguageText.Get("workroom.trial." + name);

    private static GameObject Space(Transform parent, string name)
    {
        var go = AssemblyDebugUi.New(name, parent);
        var rect = AssemblyDebugUi.Rect(go);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(Width, Height);
        rect.anchoredPosition = Vector2.zero;
        return go;
    }

    private static GameObject Box(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        var image = AssemblyDebugUi.Image(parent, name, color, false);
        AssemblyDebugUi.Place(image.gameObject, x, y, w, h);
        return image.gameObject;
    }

    private void DrawRoom(Action exit)
    {
        var parent = roomSpace.transform;
        var art = AssemblyDebugUi.Image(parent, "WorkshopBackground", Color.white, false);
        art.sprite = WorkroomArt.Get("workroom_background");
        AssemblyDebugUi.Place(art.gameObject, 0, 0, Width, Height);
        var door = AssemblyDebugUi.Button(roomCanvas, parent, "Exit", Color.clear, exit);
        exitEntry = door;
        AssemblyDebugUi.Place(door, 65, 145, 218, 425);
        Entry(WorkroomPanels.Kind.Components, 492, 342, 293, 67);
        Entry(WorkroomPanels.Kind.Motherboard, 905, 290, 158, 110);
        // Cover the full case art down to its feet.
        Entry(WorkroomPanels.Kind.Case, 1064, 188, 185, 214);
        Entry(WorkroomPanels.Kind.Storage, 1065, 542, 202, 143);
        var inventoryText = WorkroomStorage.Text("room_hint") + WorkroomStorage.Text("invalid_position") +
            WorkroomStorage.Text("stored") + WorkroomStorage.Text("taken") + "0123456789";
        AssemblyDebugFonts.Prepare(inventoryText, inventoryText);
        inventory = new WorkroomInventoryUi(roomCanvas, parent, font);
    }

    private void Entry(WorkroomPanels.Kind kind, float x, float y, float w, float h)
    {
        var button = AssemblyDebugUi.Button(roomCanvas, roomSpace.transform, kind.ToString(), Color.clear,
            () => WorkroomTrial.RequestPanel(kind, true));
        AssemblyDebugUi.Place(button, x, y, w, h);
        roomEntries.Add(kind, button);
    }

    internal void Render(WorkroomTransition flow, Rect storeViewport)
    {
        var inside = flow.Current == WorkroomTransition.Stage.Inside;
        Fit(noticeSpace, inside ? new Rect(0, 0, Screen.width, Screen.height) : storeViewport);
        Fit(roomSpace, new Rect(0, 0, Screen.width, Screen.height));
        noticeCanvas.gameObject.SetActive(hint.activeSelf && Environment.TickCount64 < hintUntil && !flow.BlocksInput);
        roomCanvas.gameObject.SetActive(flow.RoomVisible);
        blackCanvas.gameObject.SetActive(flow.BlocksInput && flow.Current != WorkroomTransition.Stage.Inside);
        black.color = new Color(0,0,0,flow.Opacity);
        var home = flow.Current == WorkroomTransition.Stage.Home;
        if (inside)
        {
            if (lastRoomMessageUntil != WorkroomStorage.MessageUntil)
            {
                lastRoomMessageUntil = WorkroomStorage.MessageUntil;
                if (Environment.TickCount64 < lastRoomMessageUntil && !string.IsNullOrWhiteSpace(WorkroomStorage.Message))
                    NotifyStorage();
            }
        }
        else lastRoomMessageUntil = WorkroomStorage.MessageUntil;
        panels.Render(inside, inside || home, inside ? new Rect(0, 0, Screen.width, Screen.height) : storeViewport, (inventory?.IsDragging ?? false) || (!panels.OwnsTextInput && Input.GetKeyDown(KeyCode.Escape) && (inventory?.HasSelection ?? false)));
        inventory.Update(inside && (!panels.BlocksInput || panels.StorageOpen), panels);
        if (shopCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            Fit(shopSpace, storeViewport);
        else if (AssemblyDebugUi.Alive(shopCanvas.worldCamera))
        {
            var viewport = shopCanvas.worldCamera.pixelRect;
            var rect = AssemblyDebugUi.Rect(shopSpace);
            var scale = Mathf.Min(viewport.width / Width, viewport.height / Height);
            rect.localScale = new Vector3(scale, scale, 1);
            rect.anchoredPosition = Vector2.zero;
        }
        var shopReady = shopCanvas.renderMode == RenderMode.ScreenSpaceOverlay ||
            (AssemblyDebugUi.Alive(shopCanvas.worldCamera) && shopCanvas.worldCamera.isActiveAndEnabled);
        shopCanvas.gameObject.SetActive(home && shopReady &&
            !panels.BlocksInput &&
            WorkroomGameAdapter.HooksReady && Time.timeScale > 0);
        var entryReady = !panels.BlocksInput && !IsDragging &&
            WorkroomGameAdapter.HooksReady && Time.timeScale > 0 && !Interactable.isPaused;
        entryHover.Update(inside && entryReady, home && shopReady && entryReady);
        // Concrete press callbacks do not run during idle frames. Advance the
        // release tail every frame even when no handler queries mouse ownership.
        SampleStoragePointer();
        if (hint.activeSelf && Environment.TickCount64 >= hintUntil) hint.SetActive(false);
    }

    internal void EnsureOverlay()
    {
        WorkroomGameAdapter.ForgetLetterboxCanvases(noticeCanvas, roomCanvas, blackCanvas);
        WorkroomGameAdapter.ForgetLetterboxCanvases(shopCanvas, panels.Canvas, null);
        foreach (var canvas in new[] { noticeCanvas, roomCanvas, blackCanvas }) Overlay(canvas);
        roomCanvas.sortingOrder = RoomOrder;
        blackCanvas.sortingOrder = BlackOrder;
        // Keep our cover opaque and first in its own Canvas after UI conversion.
        // Missing coverage is a failed view and must release through normal cleanup.
        if (!AssemblyDebugUi.Alive(backdrop)) throw new InvalidOperationException("工作间覆盖底图已失效。");
        if (!backdrop.enabled) backdrop.enabled = true;
        if (!backdrop.gameObject.activeSelf) backdrop.gameObject.SetActive(true);
        if (backdrop.color != BackdropColor) backdrop.color = BackdropColor;
        if (!backdrop.raycastTarget) backdrop.raycastTarget = true;
        if (backdrop.transform.GetSiblingIndex() != 0) backdrop.transform.SetAsFirstSibling();
        ConfigureShopCanvas();
        panels.EnsureOverlay();
        inventory?.EnsureOverlay();
    }

    private void ConfigureShopCanvas()
    {
        if (!AssemblyDebugUi.Alive(shopCanvas)) return;
        var ui = StoreUIManager.Instance;
        var native = AssemblyDebugUi.Alive(ui?.worldInteractCanvas)
            ? ui.worldInteractCanvas.GetComponent<Canvas>() : null;
        if (!AssemblyDebugUi.Alive(native)) native = ui?.primaryCanvas;
        // Letterbox disables its UI camera when no bars are needed and restores
        // native canvases to Overlay. Never bind to that camera unconditionally.
        // Match the native UI mode and keep the crate below its lowest UI order.
        var camera = AssemblyDebugUi.Alive(native) ? native.worldCamera : null;
        var useCamera = AssemblyDebugUi.Alive(native) &&
            native.renderMode == RenderMode.ScreenSpaceCamera &&
            AssemblyDebugUi.Alive(camera) && camera.isActiveAndEnabled;
        shopCanvas.renderMode = useCamera ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
        shopCanvas.worldCamera = useCamera ? camera : null;
        shopCanvas.sortingLayerID = AssemblyDebugUi.Alive(native) ? native.sortingLayerID : 0;
        shopCanvas.sortingOrder = AssemblyDebugUi.Alive(native) ? Math.Min(-2, native.sortingOrder - 1) : -2;
        shopCanvas.overrideSorting = true;
        if (useCamera)
            shopCanvas.planeDistance = Mathf.Clamp(native.planeDistance,
                camera.nearClipPlane + .01f, camera.farClipPlane - .01f);
    }

    internal static void Overlay(Canvas canvas)
    {
        if (!AssemblyDebugUi.Alive(canvas)) return;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        canvas.overrideSorting = true;
    }

    internal static void Fit(GameObject space, Rect viewport)
    {
        var rect = AssemblyDebugUi.Rect(space);
        var scale = Mathf.Min(viewport.width / Width, viewport.height / Height);
        rect.localScale = new Vector3(scale, scale, 1);
        rect.anchoredPosition = new Vector2(viewport.center.x - Screen.width*.5f,
            viewport.center.y - Screen.height*.5f);
    }

    internal void Notify(string keyName)
    {
        PlaceNotice();
        hintText.text = Text(keyName);
        hint.SetActive(true);
        hintUntil = Environment.TickCount64 + 3000;
    }

    internal void NotifyStorage()
    {
        var text = WorkroomStorage.Message;
        AssemblyDebugFonts.Prepare(text, text);
        PlaceNotice();
        hintText.text = text;
        hint.SetActive(true);
        hintUntil = Environment.TickCount64 + 5000;
    }

    private void PlaceNotice()
    {
        var inRoom = roomCanvas.gameObject.activeInHierarchy;
        noticeCanvas.sortingOrder = inRoom ? RoomOrder + 50 : 31010;
        AssemblyDebugUi.Place(hint, inRoom ? 190 : 420, inRoom ? 18 : 640,
            inRoom ? 900 : 440, inRoom ? 50 : 44);
    }

    public void Dispose()
    {
        var cleanup = new WorkroomUiCleanup();
        // Stop live roots first. Each completed resource is forgotten immediately;
        // a partial failure must not send the next retry through released UI.
        cleanup.Run("view.notice.stop", () => WorkroomUiCleanup.DeactivateCanvas(noticeCanvas));
        cleanup.Run("view.room.stop", () => WorkroomUiCleanup.DeactivateCanvas(roomCanvas));
        cleanup.Run("view.black.stop", () => WorkroomUiCleanup.DeactivateCanvas(blackCanvas));
        cleanup.Run("view.shop.stop", () => WorkroomUiCleanup.DeactivateCanvas(shopCanvas));
        cleanup.Run("view.entry-hover", () => { entryHover?.Dispose(); entryHover = null; });
        cleanup.Run("view.inventory", () => { inventory?.Dispose(); inventory = null; });
        cleanup.Run("view.panels", panels.Dispose);
        cleanup.Run("view.notice.canvas", () =>
        {
            WorkroomGameAdapter.ForgetLetterboxCanvases(noticeCanvas, null, null);
            AssemblyDebugUi.Destroy(noticeCanvas); noticeCanvas = null;
        });
        cleanup.Run("view.room.canvas", () =>
        {
            WorkroomGameAdapter.ForgetLetterboxCanvases(roomCanvas, null, null);
            AssemblyDebugUi.Destroy(roomCanvas); roomCanvas = null;
        });
        cleanup.Run("view.black.canvas", () =>
        {
            WorkroomGameAdapter.ForgetLetterboxCanvases(blackCanvas, null, null);
            AssemblyDebugUi.Destroy(blackCanvas); blackCanvas = null;
        });
        cleanup.Run("view.shop.canvas", () =>
        {
            WorkroomGameAdapter.ForgetLetterboxCanvases(shopCanvas, null, null);
            AssemblyDebugUi.Destroy(shopCanvas); shopCanvas = null;
        });
        cleanup.ThrowIfFailed();
        hint = noticeSpace = roomSpace = shopSpace = null;
        shopCrate = exitEntry = null; roomEntries.Clear(); hintText = null; backdrop = black = null; font = null;
        storagePointerOwned = false; storagePointerFrame = -1; storageNeutralFrames = 0;
    }
}
