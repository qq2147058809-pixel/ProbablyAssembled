#nullable disable
using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>装机调试专用 UGUI 构造及悬停记录，清理不触及其他界面。</summary>
internal static class AssemblyDebugUi
{
    private sealed class Hover
    {
        internal Canvas Owner;
        internal RectTransform Rect;
        internal Image Image;
        internal Color Color;
    }
    private static readonly List<Hover> hovers = new();

    internal static bool Alive(UnityEngine.Object value)
    {
        try { return !ReferenceEquals(value, null) && UnityEngine.Object.IsNativeObjectAlive(value); }
        catch { return false; }
    }

    private static T Add<T>(GameObject go) where T : Il2CppObjectBase
    {
        var type = Il2CppSystem.Type.internal_from_handle(
            IL2CPP.il2cpp_class_get_type(Il2CppClassPointerStore<T>.NativeClassPtr));
        var component = go.AddComponent(type);
        var result = component == null ? null : component.TryCast<T>();
        if (result == null) throw new InvalidOperationException("界面组件未就绪：" + typeof(T).Name);
        return result;
    }

    internal static GameObject New(string name, Transform parent)
    {
        var go = new GameObject(name);
        try { Add<RectTransform>(go).SetParent(parent, false); return go; }
        catch { UnityEngine.Object.Destroy(go); throw; }
    }

    internal static RectTransform Rect(GameObject go) => go.transform.TryCast<RectTransform>();

    internal static Canvas NewCanvas(string name, int order)
    {
        var go = New(name, null);
        try
        {
            var canvas = Add<Canvas>(go);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            canvas.overrideSorting = true;
            var scaler = Add<CanvasScaler>(go);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;
            Add<GraphicRaycaster>(go);
            return canvas;
        }
        catch { UnityEngine.Object.Destroy(go); throw; }
    }

    internal static void Stretch(GameObject go)
    {
        var rect = Rect(go);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    internal static void Place(GameObject go, float x, float y, float w, float h)
    {
        var rect = Rect(go);
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
    }

    internal static Image Image(Transform parent, string name, Color color, bool raycast)
    {
        var image = Add<Image>(New(name, parent));
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    internal static TextMeshProUGUI Text(Transform parent, string name, string text,
        TMP_FontAsset font, float size, bool left = false)
    {
        var label = Add<TextMeshProUGUI>(New(name, parent));
        label.font = font;
        label.fontSharedMaterial = font.material;
        label.text = text;
        // Workroom-owned text only: preserve every panel and button rectangle.
        label.fontSize = size * 1.25f;
        label.fontStyle = FontStyles.Bold;
        label.enableAutoSizing = false;
        label.enableWordWrapping = false;
        label.richText = false;
        label.raycastTarget = false;
        label.color = new Color(.96f, .98f, 1, 1);
        label.alignment = (TextAlignmentOptions)(left ? 513 : 514);
        return label;
    }

    // A drawn key can use the same event/hover ownership without a text label.
    internal static GameObject Button(Canvas owner, Transform parent, string name, Color color, Action click)
    {
        var image = Image(parent, name, color, true);
        var button = Add<Button>(image.gameObject);
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(click));
        hovers.Add(new Hover { Owner = owner, Rect = Rect(image.gameObject), Image = image, Color = color });
        return image.gameObject;
    }

    internal static void Forget(Canvas owner)
    {
        for (var i = hovers.Count - 1; i >= 0; i--)
            if (!Alive(hovers[i].Owner) || ReferenceEquals(hovers[i].Owner, owner)) hovers.RemoveAt(i);
    }

    internal static void UpdateHover()
    {
        for (var i = hovers.Count - 1; i >= 0; i--)
        {
            var hover = hovers[i];
            if (!Alive(hover.Owner) || !Alive(hover.Rect) || !Alive(hover.Image))
            { hovers.RemoveAt(i); continue; }
            var on = hover.Rect.gameObject.activeInHierarchy && Input.mousePresent &&
                RectTransformUtility.RectangleContainsScreenPoint(hover.Rect, Input.mousePosition, null);
            var c = hover.Color;
            hover.Image.color = on ? new Color(c.r + (1-c.r)*.22f, c.g + (1-c.g)*.22f,
                c.b + (1-c.b)*.22f, c.a) : c;
        }
    }

    internal static void Destroy(Canvas canvas)
    {
        if (ReferenceEquals(canvas, null)) return;
        Forget(canvas);
        if (!WorkroomUiCleanup.IsAlive(canvas)) return;
        var root = canvas.gameObject;
        if (!WorkroomUiCleanup.IsAlive(root)) return;
        root.SetActive(false);
        UnityEngine.Object.Destroy(root);
    }
}
