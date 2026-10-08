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

/// <summary>仅工作间13个子窗使用的场景和金属按钮；不登记到全局调试悬停表。</summary>
internal static class WorkroomPanelSkin
{
    internal const float Width = 720, Height = 424, SceneX = 176, SceneY = 38;
    internal const float PreviewWidth = 306, PreviewHeight = 250;
    private sealed class Binding
    {
        internal Canvas Owner;
        internal GameObject Root, Window;
        internal Button Button;
        internal Image Face;
        internal Image[] Edges;
        internal TextMeshProUGUI Label;
        internal bool Slot, Selected;
        internal int State = -1;
    }
    private static readonly Dictionary<IntPtr, Binding> bindings = new();
    private static Binding pressed;

    private static T Add<T>(GameObject go) where T : Il2CppObjectBase
    {
        var type = Il2CppSystem.Type.internal_from_handle(IL2CPP.il2cpp_class_get_type(Il2CppClassPointerStore<T>.NativeClassPtr));
        return go.AddComponent(type)?.TryCast<T>() ?? throw new InvalidOperationException("工作间界面组件未就绪：" + typeof(T).Name);
    }
    private static GameObject Window(Transform parent)
    {
        while (parent.parent != null && parent.parent.name != "PanelReference") parent = parent.parent;
        return parent.gameObject;
    }
    internal static GameObject Button(Canvas owner, Transform parent, string name, string text,
        TMP_FontAsset font, float size, Action click, bool slot = false)
    {
        var face = AssemblyDebugUi.Image(parent, name, Color.white, true);
        var button = Add<Button>(face.gameObject);
        button.targetGraphic = face;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(click));
        var binding = new Binding { Owner = owner, Root = face.gameObject, Window = Window(parent),
            Button = button, Face = face, Slot = slot, Edges = Frame(face.gameObject, new Color(.65f,.67f,.58f,1)) };
        if (!slot)
        {
            binding.Label = AssemblyDebugUi.Text(face.transform, "Label", text, font, size);
            AssemblyDebugUi.Stretch(binding.Label.gameObject);
            binding.Label.overflowMode = TextOverflowModes.Ellipsis;
        }
        bindings[face.gameObject.Pointer] = binding;
        Paint(binding, 0);
        return face.gameObject;
    }
    internal static void Selected(GameObject root, bool selected)
    {
        if (bindings.TryGetValue(root.Pointer, out var binding)) binding.Selected = selected;
    }
    private static Image[] Frame(GameObject root, Color color)
    {
        var result = new Image[4];
        for (var i = 0; i < 4; i++)
        {
            result[i] = AssemblyDebugUi.Image(root.transform, "Edge"+i, color, false);
            var rect = AssemblyDebugUi.Rect(result[i].gameObject);
            rect.anchorMin = i == 1 ? new Vector2(1,0) : i == 3 ? new Vector2(0,1) : Vector2.zero;
            rect.anchorMax = i == 0 ? new Vector2(0,1) : i == 2 ? new Vector2(1,0) : Vector2.one;
            rect.offsetMin = i == 1 ? new Vector2(-1,0) : i == 3 ? new Vector2(0,-1) : Vector2.zero;
            rect.offsetMax = i == 0 ? new Vector2(1,0) : i == 2 ? new Vector2(0,1) : Vector2.zero;
        }
        return result;
    }
    internal static GameObject Panel(GameObject parent, string name, float x, float y, float width, float height, Color color)
    {
        var face = AssemblyDebugUi.Image(parent.transform, name, color, false).gameObject;
        AssemblyDebugUi.Place(face, x, y, width, height);
        Frame(face, new Color(.40f,.47f,.42f,1));
        return face;
    }
    internal static void Scene(GameObject parent, string theme, bool sidebar = true)
    {
        parent.GetComponent<Image>().color = new Color(.12f,.16f,.15f,1);
        Frame(parent, new Color(.55f,.58f,.49f,1));
        var scene = AssemblyDebugUi.Image(parent.transform, "OperationSurface", Color.white, false);
        scene.sprite = WorkroomArt.Get("workroom_panel_"+theme);
        AssemblyDebugUi.Place(scene.gameObject, sidebar ? SceneX : 6, SceneY, 528, 352);
        if (sidebar) Panel(parent, "BodyInformation", 10, 40, 150, 350, new Color(.14f,.19f,.17f,.97f));
    }
    internal static void Update(Canvas owner, bool allowPress)
    {
        // A single foreground root controls hover/press, including overlapping windows from other families.
        GameObject front = null;
        foreach (var binding in bindings.Values)
        {
            if (binding.Owner.Pointer != owner.Pointer || !AssemblyDebugUi.Alive(binding.Window) || !binding.Window.activeInHierarchy) continue;
            if (!RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(binding.Window), Input.mousePosition, null)) continue;
            if (front == null || binding.Window.transform.GetSiblingIndex() > front.transform.GetSiblingIndex()) front = binding.Window;
        }
        if (!Input.GetMouseButton(0)) pressed = null;
        foreach (var binding in bindings.Values)
        {
            if (binding.Owner.Pointer != owner.Pointer || !binding.Root.activeInHierarchy) continue;
            var hover = allowPress && front != null && binding.Window.Pointer == front.Pointer && Input.mousePresent &&
                RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(binding.Root), Input.mousePosition, null);
            if (hover && binding.Button.interactable && Input.GetMouseButtonDown(0)) pressed = binding;
            Paint(binding, !binding.Button.interactable ? 4 : hover && ReferenceEquals(pressed, binding) ? 2 : hover ? 1 : binding.Selected ? 3 : 0);
        }
    }
    private static void Paint(Binding binding, int state)
    {
        if (binding.State == state) return;
        binding.State = state;
        var c = state switch { 1 => new Color(.38f,.43f,.33f,1), 2 => new Color(.14f,.20f,.16f,1),
            3 => new Color(.28f,.37f,.24f,1), 4 => new Color(.18f,.21f,.19f,1), _ => new Color(.29f,.34f,.29f,1) };
        if (binding.Slot) c.a = state == 0 ? .27f : state == 4 ? .20f : .65f;
        binding.Face.color = c;
        var edge = state == 1 || state == 2 || state == 3 ? new Color(.88f,.78f,.47f,1) :
            state == 4 ? new Color(.32f,.36f,.31f,1) : new Color(.64f,.66f,.56f,1);
        foreach (var line in binding.Edges) line.color = edge;
        if (AssemblyDebugUi.Alive(binding.Label)) binding.Label.color = state == 4 ? new Color(.45f,.49f,.43f,1) : new Color(.93f,.92f,.81f,1);
    }
    internal static void Forget(Canvas owner)
    {
        var dead = new List<IntPtr>();
        foreach (var pair in bindings)
            if (!AssemblyDebugUi.Alive(pair.Value.Owner) || pair.Value.Owner.Pointer == owner.Pointer) dead.Add(pair.Key);
        foreach (var key in dead) { if (ReferenceEquals(pressed, bindings[key])) pressed = null; bindings.Remove(key); }
    }
}
