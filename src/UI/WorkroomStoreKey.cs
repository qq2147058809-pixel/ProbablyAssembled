#nullable disable
using System;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using Il2CppEPOOutline;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PCExpansion;

/// <summary>店内桌面钥匙仅为场景交互对象，不创建库存物品。</summary>
internal static class WorkroomStoreKey
{
    // User's red circle, normalized to the shop's original 960x540 artwork.
    private static readonly Vector2 TablePoint = new(756.5f, 310f);
    private const float TableWidth = 36f;
    private static StorePC anchor;
    private static SpriteRenderer native, renderer;
    private static GameObject root;
    private static Interactable interaction;
    private static TooltipContainer tooltip;
    private static Sprite sprite;
    private static Texture2D texture;
    private static Vector2 visibleSize;
    private static float nextCheck;
    private static long warnAt;
    private static bool pointerOwned, cleanupPending;
    private static int pointerFrame = -1, neutralFrames;

    internal static bool OwnsMousePress
    {
        get { SamplePointer(); return pointerOwned; }
    }
    internal static bool CleanupPending => cleanupPending;

    internal static void Update()
    {
        try
        {
            SamplePointer(); // Release ownership even when no native press handler runs.
            if (cleanupPending)
            {
                if (Time.realtimeSinceStartup >= nextCheck) Reset();
                return;
            }
            if (!WorkroomTrial.ShopEntryVisible)
            {
                if (WorkroomUiCleanup.IsAlive(root)) root.SetActive(false);
                return;
            }
            if (Time.realtimeSinceStartup < nextCheck) return;
            if (!WorkroomUiCleanup.IsAlive(anchor))
            {
                nextCheck = Time.realtimeSinceStartup + .25f;
                DestroyObject();
                // The native PC starts inactive; its fixture state must not gate this entry.
                anchor = UnityEngine.Object.FindObjectOfType<StorePC>(true);
                native = WorkroomUiCleanup.IsAlive(anchor) ? anchor.GetComponent<SpriteRenderer>() : null;
            }
            if (!WorkroomUiCleanup.IsAlive(native) || !WorkroomUiCleanup.IsAlive(native.sprite) ||
                native.sprite.pixelsPerUnit <= 0 || native.sprite.rect.width <= 0) return;
            if (!WorkroomUiCleanup.IsAlive(root)) Create();
            FollowAnchor();
            interaction.isAvailable = WorkroomTrial.CanRequestEntry;
            tooltip.content = LanguageText.Get("workroom.entry.tooltip");
            // The sibling follows the shop parent, independently of the optional PC fixture.
            root.SetActive(true);
        }
        catch (Exception ex)
        {
            Warn(ex);
            // A failed cleanup retains every remaining reference; retry it before creating again.
            if (!cleanupPending)
                try { Reset(); } catch (Exception cleanup) { Warn(cleanup); }
            nextCheck = Time.realtimeSinceStartup + .5f;
        }
    }

    private static void Create()
    {
        using var input = typeof(Core).Assembly.GetManifestResourceStream("PCExpansion.Assets.workroom_store_key.png")
            ?? throw new InvalidOperationException("工作间店内钥匙贴图缺失。");
        if (!WorkroomUiCleanup.IsAlive(sprite))
        {
            using var bytes = new MemoryStream();
            input.CopyTo(bytes);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, new Il2CppStructArray<byte>(bytes.ToArray()), false) ||
                texture.width != 128 || texture.height != 128)
                throw new InvalidOperationException("店内钥匙贴图必须为128×128透明PNG。");
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;
            var pixels = texture.GetPixels32();
            var minX = 128; var minY = 128; var maxX = -1; var maxY = -1;
            for (var y = 0; y < 128; y++)
                for (var x = 0; x < 128; x++)
                {
                    if (pixels[y * 128 + x].a == 0) continue;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
            if (maxX < minX || maxY < minY) throw new InvalidOperationException("店内钥匙贴图全透明。");
            var pivot = new Vector2((minX + maxX + 1) / 256f, (minY + maxY + 1) / 256f);
            // Match the visible key to the marked tabletop area, independently of source canvas size.
            var ppu = (maxX - minX + 1) * 960f * native.sprite.pixelsPerUnit /
                (TableWidth * native.sprite.rect.width);
            sprite = Sprite.Create(texture, new Rect(0, 0, 128, 128), pivot, ppu);
            sprite.name = "PCExpansion.WorkroomStoreKey";
            visibleSize = new Vector2(maxX - minX + 1, maxY - minY + 1) / ppu;
        }
        root = new GameObject("PCExpansion.WorkroomStoreKey");
        root.SetActive(false);
        renderer = root.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        var collider = root.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        // Sprite bounds include transparent margins; use only the visible alpha bounds plus two scene pixels.
        var pixelsPerScenePixel = native.sprite.rect.width / (960f * native.sprite.pixelsPerUnit);
        collider.size = visibleSize + Vector2.one * (4 * pixelsPerScenePixel);
        collider.offset = Vector2.zero;
        var outline = root.AddComponent<Outlinable>();
        outline.AddAllChildRenderersToRenderingList(RenderersAddingMode.SpriteRenderer);
        interaction = root.AddComponent<Interactable>();
        var nativeInteraction = anchor.GetComponent<Interactable>();
        interaction.isControlable = nativeInteraction != null && nativeInteraction.isControlable;
        interaction.isAvailable = false;
        interaction.doNotOutline = false;
        tooltip = root.AddComponent<TooltipContainer>();
        tooltip.interactable = interaction;
        WorkroomNativeTooltip.Register(tooltip, false);
        FollowAnchor();
        Core.Log?.Msg("[工作间店内钥匙] 已创建桌面入口；位置=(756.5,310)/960×540，显示宽36像素；原版电脑激活=" + native.gameObject.activeInHierarchy + "。");
    }

    private static void FollowAnchor()
    {
        var reference = native.sprite;
        var offset = new Vector3((TablePoint.x / 960f * reference.rect.width - reference.pivot.x) / reference.pixelsPerUnit,
            ((1f - TablePoint.y / 540f) * reference.rect.height - reference.pivot.y) / reference.pixelsPerUnit, 0);
        if (native.flipX) offset.x = -offset.x;
        if (native.flipY) offset.y = -offset.y;
        var source = native.transform;
        root.transform.SetParent(source.parent, false);
        root.transform.localPosition = source.localPosition + source.localRotation * Vector3.Scale(offset, source.localScale);
        root.transform.localRotation = source.localRotation;
        root.transform.localScale = source.localScale;
        root.layer = native.gameObject.layer;
        renderer.sharedMaterial = native.sharedMaterial;
        renderer.sortingLayerID = native.sortingLayerID;
        renderer.sortingOrder = native.sortingOrder + 1;
        renderer.flipX = native.flipX;
        renderer.flipY = native.flipY;
    }

    private static void SamplePointer()
    {
        if (pointerFrame == Time.frameCount) return;
        pointerFrame = Time.frameCount;
        if (Input.GetMouseButtonDown(0) && WorkroomTrial.CanRequestEntry && HitPointer())
        { pointerOwned = true; neutralFrames = 0; }
        if (!pointerOwned) return;
        var neutral = !Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0) && !Input.GetMouseButtonUp(0);
        neutralFrames = neutral ? neutralFrames + 1 : 0;
        if (neutralFrames >= 2) pointerOwned = false;
    }

    private static bool HitPointer()
    {
        if (!WorkroomUiCleanup.IsAlive(root) || !root.activeInHierarchy || EventSystem.current == null) return false;
        // Use the EventSystem's sorted hits so a UI window covering the key owns the press.
        var data = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var hits = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(data, hits);
        for (var i = 0; i < hits.Count; i++)
        {
            var target = hits[i].gameObject;
            if (!WorkroomUiCleanup.IsAlive(target)) continue;
            return target.Pointer == root.Pointer;
        }
        return false;
    }

    internal static bool HandleClick(Interactable target)
    {
        if (!WorkroomUiCleanup.IsAlive(interaction) || target == null || target.Pointer != interaction.Pointer) return true;
        if (WorkroomUiCleanup.IsAlive(root) && root.activeInHierarchy && WorkroomTrial.CanRequestEntry)
            WorkroomTrial.RequestEntry();
        return false; // This scene object has no native IClickable business action.
    }

    private static void DestroyObject()
    {
        WorkroomNativeTooltip.Unregister(tooltip);
        if (WorkroomUiCleanup.IsAlive(root))
        {
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }
        root = null; renderer = null; interaction = null; tooltip = null;
    }

    internal static void Reset()
    {
        cleanupPending = true;
        DestroyObject();
        if (WorkroomUiCleanup.IsAlive(sprite)) UnityEngine.Object.Destroy(sprite);
        sprite = null;
        if (WorkroomUiCleanup.IsAlive(texture)) UnityEngine.Object.Destroy(texture);
        texture = null;
        anchor = null; native = null;
        nextCheck = 0; pointerFrame = -1; neutralFrames = 0; pointerOwned = false;
        cleanupPending = false;
    }

    private static void Warn(Exception ex)
    {
        if (Environment.TickCount64 < warnAt) return;
        warnAt = Environment.TickCount64 + 5000;
        Core.Log?.Warning("店内工作间钥匙暂不可用，稍后重试：" + ex);
    }
}

[HarmonyPatch(typeof(Interactable), nameof(Interactable.OnClick), new Type[0])]
internal static class WorkroomStoreKeyClickPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(Interactable __instance) => WorkroomStoreKey.HandleClick(__instance);
}
