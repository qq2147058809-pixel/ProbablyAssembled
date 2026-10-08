#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppEPOOutline;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>原按钮保留点击；原生 Interactable/Outliner 将物件轮廓绘入自有透明层。</summary>
internal sealed class WorkroomEntryHover : IDisposable
{
    private sealed class Entry
    {
        internal GameObject Button, Proxy;
        internal Interactable Interaction;
        internal TooltipContainer Tooltip;
        internal Outlinable Outline;
        internal bool InRoom, Ready;
    }

    private static readonly List<WorkroomEntryHover> live = new();
    private static readonly Dictionary<IntPtr, Interactable> interactions = new();
    private readonly List<Entry> entries = new();
    private readonly List<Mesh> meshes = new();
    private GameObject scene;
    private Camera camera;
    private RenderTexture buffer;
    private RawImage roomImage, shopImage;
    private Material backgroundMaterial;
    private bool disposed;

    internal WorkroomEntryHover() => live.Add(this);
    internal bool Alive => !disposed && WorkroomUiCleanup.IsAlive(scene) &&
        WorkroomUiCleanup.IsAlive(camera) && WorkroomUiCleanup.IsAlive(buffer);

    private void Build(Canvas roomCanvas, Transform roomSpace, Canvas shopCanvas, Transform shopSpace)
    {
        if (WorkroomUiCleanup.IsAlive(scene)) return;
        scene = new GameObject("PCExpansion.EntryOutlineScene");
        // Vanilla cameras include every layer. Keep these render-only proxies far outside their view.
        scene.transform.position = new Vector3(100000, 100000, 0);
        var cameraObject = new GameObject("EntryOutlineCamera");
        cameraObject.transform.SetParent(scene.transform, false);
        cameraObject.transform.localPosition = new Vector3(6.4f, -3.6f, -10);
        camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 3.6f;
        camera.aspect = 1280f / 720;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 50;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.cullingMask = 1 << 31;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        buffer = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        buffer.name = "PCExpansion.EntryNativeOutline";
        buffer.filterMode = FilterMode.Point;
        buffer.wrapMode = TextureWrapMode.Clamp;
        if (!buffer.Create()) throw new InvalidOperationException("入口原生描边绘制层未创建。");
        camera.targetTexture = buffer;
        var outliner = cameraObject.AddComponent<Outliner>();
        outliner.OutlineLayerMask = long.MinValue;
        outliner.RenderStage = (RenderStage)1;
        outliner.PrimaryBufferSizeMode = (BufferSizeMode)0;
        outliner.PrimaryRendererScale = .75f;
        outliner.PrimarySizeReference = 960;
        outliner.BlurShift = 0;
        outliner.DilateShift = 1;
        outliner.BlurIterations = 1;
        outliner.DilateIterations = 1;
        outliner.DilateQuality = (DilateQuality)0;
        outliner.BlurType = (BlurType)1;

        roomImage = NewImage(roomSpace, "RoomNativeEntryOutline");
        // Above the background, below the existing fixed labels and interactive UI.
        roomImage.transform.SetSiblingIndex(1);
        shopImage = NewImage(shopSpace, "ShopNativeEntryOutline");
        shopImage.transform.SetAsFirstSibling();
    }

    private RawImage NewImage(Transform parent, string name)
    {
        var root = AssemblyDebugUi.New(name, parent);
        var image = root.AddComponent<RawImage>();
        image.texture = buffer;
        image.raycastTarget = false;
        image.color = Color.white;
        AssemblyDebugUi.Place(root, 0, 0, 1280, 720);
        root.SetActive(false);
        return image;
    }

    internal void AddRoom(GameObject button, Vector2[] contour, Canvas roomCanvas,
        Transform roomSpace, Canvas shopCanvas, Transform shopSpace)
    {
        Build(roomCanvas, roomSpace, shopCanvas, shopSpace);
        if (!WorkroomUiCleanup.IsAlive(backgroundMaterial))
        {
            var shader = Shader.Find("Sprites/Default");
            if (!WorkroomUiCleanup.IsAlive(shader)) throw new InvalidOperationException("入口绘制材质缺失。");
            backgroundMaterial = new Material(shader);
            backgroundMaterial.mainTexture = WorkroomArt.Get("workroom_background").texture;
        }
        var proxy = NewProxy(button.name);
        // The boundary clips the original background's UVs, including concave stand recesses.
        var vertices = new Vector3[contour.Length];
        var uv = new Vector2[contour.Length];
        var colors = new Color32[contour.Length];
        for (var i = 0; i < contour.Length; i++)
        {
            vertices[i] = new Vector3(contour[i].x / 100, -contour[i].y / 100, 0);
            uv[i] = new Vector2(contour[i].x / 1280, 1 - contour[i].y / 720);
            colors[i] = new Color32(255, 255, 255, 255);
        }
        var triangles = Triangulate(contour);
        var mesh = new Mesh();
        meshes.Add(mesh);
        mesh.name = "PCExpansion.EntryContour." + button.name;
        mesh.vertices = new Il2CppStructArray<Vector3>(vertices);
        mesh.uv = new Il2CppStructArray<Vector2>(uv);
        mesh.colors32 = new Il2CppStructArray<Color32>(colors);
        mesh.triangles = new Il2CppStructArray<int>(triangles);
        mesh.RecalculateBounds();
        proxy.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = proxy.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = backgroundMaterial;
        Register(button, proxy, renderer, true);
    }

    // Built once with the view. A fan from vertex zero would fill concave notches outside the object.
    private static int[] Triangulate(Vector2[] contour)
    {
        if (contour.Length < 3) throw new ArgumentException("入口轮廓至少需要三个点。", nameof(contour));
        var remaining = new List<int>(contour.Length);
        var triangles = new List<int>((contour.Length - 2) * 3);
        float area = 0;
        for (var i = 0; i < contour.Length; i++)
        {
            remaining.Add(i);
            var next = contour[(i + 1) % contour.Length];
            area += contour[i].x * next.y - next.x * contour[i].y;
        }
        var direction = Math.Sign(area);
        if (direction == 0) throw new ArgumentException("入口轮廓面积为空。", nameof(contour));
        while (remaining.Count > 3)
        {
            var clipped = false;
            for (var i = 0; i < remaining.Count; i++)
            {
                var a = remaining[(i + remaining.Count - 1) % remaining.Count];
                var b = remaining[i];
                var c = remaining[(i + 1) % remaining.Count];
                if (Cross(contour[a], contour[b], contour[c]) * direction <= .0001f) continue;
                var occupied = false;
                foreach (var index in remaining)
                    if (index != a && index != b && index != c &&
                        Cross(contour[a], contour[b], contour[index]) * direction >= -.0001f &&
                        Cross(contour[b], contour[c], contour[index]) * direction >= -.0001f &&
                        Cross(contour[c], contour[a], contour[index]) * direction >= -.0001f)
                    { occupied = true; break; }
                if (occupied) continue;
                triangles.Add(a); triangles.Add(direction > 0 ? c : b); triangles.Add(direction > 0 ? b : c);
                remaining.RemoveAt(i); clipped = true; break;
            }
            if (!clipped) throw new ArgumentException("入口轮廓无法三角剖分。", nameof(contour));
        }
        triangles.Add(remaining[0]);
        triangles.Add(remaining[direction > 0 ? 2 : 1]);
        triangles.Add(remaining[direction > 0 ? 1 : 2]);
        return triangles.ToArray();
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
        (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

    internal void AddShop(GameObject button, Sprite sprite, Rect rect, Canvas roomCanvas,
        Transform roomSpace, Canvas shopCanvas, Transform shopSpace)
    {
        Build(roomCanvas, roomSpace, shopCanvas, shopSpace);
        var proxy = NewProxy(button.name);
        var renderer = proxy.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        var scale = Mathf.Min(rect.width / sprite.bounds.size.x, rect.height / sprite.bounds.size.y) / 100;
        proxy.transform.localScale = new Vector3(scale, scale, 1);
        proxy.transform.localPosition = new Vector3(rect.center.x / 100, -rect.center.y / 100, 0);
        Register(button, proxy, renderer, false);
    }

    private GameObject NewProxy(string name)
    {
        var proxy = new GameObject("EntryContour." + name);
        proxy.transform.SetParent(scene.transform, false);
        proxy.layer = 31;
        proxy.SetActive(false);
        return proxy;
    }

    private void Register(GameObject button, GameObject proxy, Renderer renderer, bool inRoom)
    {
        // Native filtering reads the Outlinable owner's layer as well as its target Renderer.
        // Only this far-away camera changes; EPO layer 63 still limits outlines to our entries.
        camera.cullingMask |= 1 << button.layer;
        // Awake must find a valid Outlinable before the Interactable is added.
        var outline = button.AddComponent<Outlinable>();
        outline.OutlineLayer = 63;
        var target = new OutlineTarget(renderer, 0);
        target.CullMode = UnityEngine.Rendering.CullMode.Off;
        outline.TryAddTarget(target);
        outline.OutlineParameters.Color = Interactable.HOVER_COLOR;
        outline.enabled = false;
        var interaction = button.AddComponent<Interactable>();
        interaction.outlinable = outline;
        interaction.isControlable = true;
        interaction.doNotOutline = false;
        interaction.isAvailable = false;
        var entry = new Entry { Button = button, Proxy = proxy, Interaction = interaction,
            Outline = outline, InRoom = inRoom };
        entries.Add(entry);
        interactions.Add(interaction.Pointer, interaction);
        entry.Tooltip = button.AddComponent<TooltipContainer>();
        entry.Tooltip.interactable = interaction;
        WorkroomNativeTooltip.Register(entry.Tooltip, inRoom);
    }

    internal void Update(bool roomReady, bool shopReady)
    {
        foreach (var entry in entries)
        {
            entry.Ready = entry.InRoom ? roomReady : shopReady;
            entry.Interaction.isAvailable = entry.Ready;
            entry.Tooltip.content = entry.InRoom && entry.Button.name == "Exit"
                ? LanguageText.Get("workroom.trial.exit") : entry.InRoom
                ? WorkroomPanels.Name(Enum.Parse<WorkroomPanels.Kind>(entry.Button.name))
                : WorkroomPanels.Name(WorkroomPanels.Kind.Storage);
        }
    }

    internal static bool AllowNativeClick(Interactable target) => target == null ||
        !interactions.TryGetValue(target.Pointer, out var own) || !WorkroomUiCleanup.IsAlive(own);

    internal static void LateUpdate()
    {
        foreach (var owner in live) owner.Draw();
        WorkroomNativeTooltip.Update();
    }

    private void Draw()
    {
        if (!Alive) return;
        var hit = WorkroomNativeTooltip.PointerTarget();
        Entry selected = null;
        foreach (var entry in entries)
        {
            var on = entry.Ready && WorkroomUiCleanup.IsAlive(entry.Button) && entry.Button.activeInHierarchy &&
                hit != null && hit.Pointer == entry.Button.Pointer && entry.Interaction.isPointerOver;
            entry.Proxy.SetActive(on);
            if (on) selected = entry;
        }
        roomImage.gameObject.SetActive(selected != null && selected.InRoom);
        shopImage.gameObject.SetActive(selected != null && !selected.InRoom);
        if (selected != null) camera.Render(); // Native Outliner.OnPreRender owns the outline pass.
    }

    public void Dispose()
    {
        var cleanup = new WorkroomUiCleanup();
        cleanup.Run("entry-tooltip", WorkroomNativeTooltip.Reset);
        cleanup.Run("entry-render.stop", () => WorkroomUiCleanup.Deactivate(scene));
        foreach (var entry in entries)
        {
            cleanup.Run("entry-outline.stop", () => { if (WorkroomUiCleanup.IsAlive(entry.Outline)) entry.Outline.enabled = false; });
            cleanup.Run("entry-tooltip.unregister", () => WorkroomNativeTooltip.Unregister(entry.Tooltip));
        }
        cleanup.Run("entry-render.scene", () =>
        {
            if (WorkroomUiCleanup.IsAlive(camera)) camera.targetTexture = null;
            if (WorkroomUiCleanup.IsAlive(scene)) UnityEngine.Object.Destroy(scene);
            scene = null; camera = null;
        });
        cleanup.Run("entry-render.texture", () =>
        {
            if (WorkroomUiCleanup.IsAlive(buffer)) { buffer.Release(); UnityEngine.Object.Destroy(buffer); }
            buffer = null;
        });
        cleanup.Run("entry-render.material", () =>
        {
            if (WorkroomUiCleanup.IsAlive(backgroundMaterial)) UnityEngine.Object.Destroy(backgroundMaterial);
            backgroundMaterial = null;
        });
        for (var i = 0; i < meshes.Count; i++)
        {
            var index = i;
            cleanup.Run("entry-render.mesh", () =>
            { if (WorkroomUiCleanup.IsAlive(meshes[index])) UnityEngine.Object.Destroy(meshes[index]); meshes[index] = null; });
        }
        cleanup.ThrowIfFailed();
        foreach (var entry in entries) interactions.Remove(entry.Interaction.Pointer);
        entries.Clear(); meshes.Clear();
        live.Remove(this);
        disposed = true;
    }
}

[HarmonyPatch(typeof(Interactable))]
internal static class WorkroomEntryHoverClickPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        // UI Button owns the full press/click path; the extra native component supplies hover only.
        foreach (var name in new[] { "OnPointerClick", "HandleClick", "OnClick", "OnDoubleClick", "OnPointerDown", "OnPointerUp" })
            yield return AccessTools.DeclaredMethod(typeof(Interactable), name) ??
                throw new MissingMethodException(typeof(Interactable).FullName, name);
    }
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(Interactable __instance) => WorkroomEntryHover.AllowNativeClick(__instance);
}
