#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>只处理本 Mod 入口提示；工作间悬停期间暂借原生 widget，自有 Canvas 承载。</summary>
internal static class WorkroomNativeTooltip
{
    private sealed class Source
    {
        internal TooltipContainer Container;
        internal bool InRoom;
    }
    private static readonly Dictionary<IntPtr, Source> sources = new();
    private static Canvas bridge, previousCanvas;
    private static TooltipManager manager;
    private static RectTransform widget;
    private static Transform previousParent;
    private static int previousSibling;
    private static Vector2 anchorMin, anchorMax, pivot, size;
    private static Vector3 position, scale;
    private static Quaternion rotation;
    private static bool leased;
    private static long warnAt;

    internal static void Register(TooltipContainer source, bool inRoom) => sources.Add(source.Pointer,
        new Source { Container = source, InRoom = inRoom });
    private static bool Owns(Component source) => source != null &&
        sources.TryGetValue(source.Pointer, out var own) && WorkroomUiCleanup.IsAlive(own.Container);
    private static bool InRoom(Component source) => source != null &&
        sources.TryGetValue(source.Pointer, out var own) && own.InRoom && WorkroomUiCleanup.IsAlive(own.Container) &&
        WorkroomUiCleanup.IsAlive(source) && source.gameObject.activeInHierarchy;

    internal static GameObject PointerTarget()
    {
        var events = EventSystem.current;
        if (events == null || !events.isActiveAndEnabled) return null;
        var data = new PointerEventData(events) { position = Input.mousePosition };
        var hits = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
        events.RaycastAll(data, hits);
        for (var i = 0; i < hits.Count; i++)
            if (WorkroomUiCleanup.IsAlive(hits[i].gameObject)) return hits[i].gameObject;
        return null;
    }

    internal static bool AllowUpdate(TooltipContainer source)
    {
        if (!Owns(source)) return true;
        try
        {
            var current = TooltipManager._instance;
            var hit = PointerTarget();
            if (!WorkroomUiCleanup.IsAlive(source) || !source.gameObject.activeInHierarchy ||
                hit == null || hit.Pointer != source.gameObject.Pointer)
            { Hide(source); return false; }
            if (!WorkroomUiCleanup.IsAlive(current)) return true;
            // Native UpdateTooltip prefixes English "Unavailable" when isAvailable is temporarily false.
            // Keep the actual interaction guard; this exact-owner path only supplies the Chinese description.
            current.SetAndShowTooltip(source.content, source);
            return false;
        }
        catch (Exception ex) { Warn(ex); return true; }
    }

    internal static void BeforeLayer(TooltipManager current)
    {
        if (leased && (!ReferenceEquals(manager, current) && manager.Pointer != current.Pointer ||
            !InRoom(current.tooltipOwner))) Release();
    }

    internal static void BeforeShow(TooltipManager current, Component nextOwner)
    {
        // Release before vanilla measures the incoming description; restoring later would overwrite its size.
        if (leased && (!ReferenceEquals(manager, current) && manager.Pointer != current.Pointer || !InRoom(nextOwner)))
            Release();
    }

    internal static void AfterLayer(TooltipManager current, ref bool pixelPosition)
    {
        if (!InRoom(current.tooltipOwner)) return;
        if (!WorkroomUiCleanup.IsAlive(bridge))
        {
            bridge = AssemblyDebugUi.NewCanvas("PCExpansion.EntryNativeTooltip", 32690);
            bridge.GetComponent<GraphicRaycaster>().enabled = false;
        }
        if (!leased)
        {
            widget = current.rectTransform;
            if (!WorkroomUiCleanup.IsAlive(widget)) return;
            manager = current;
            previousParent = widget.parent;
            previousSibling = widget.GetSiblingIndex();
            previousCanvas = current.tooltipCanvas;
            anchorMin = widget.anchorMin; anchorMax = widget.anchorMax; pivot = widget.pivot;
            size = widget.sizeDelta; position = widget.anchoredPosition3D;
            scale = widget.localScale; rotation = widget.localRotation;
            leased = true;
        }
        // Native EnsureTooltipLayer re-docks the widget each call; reapply only for our current owner.
        widget.SetParent(bridge.transform, true);
        widget.SetAsLastSibling();
        current.tooltipCanvas = bridge;
        pixelPosition = true; // Native true branch positions by screen pixels and actual world scale.
    }

    private static void Release()
    {
        if (!leased) return;
        if (WorkroomUiCleanup.IsAlive(widget))
        {
            var parent = WorkroomUiCleanup.IsAlive(previousParent) ? previousParent : null;
            if (parent == null && WorkroomUiCleanup.IsAlive(manager) &&
                WorkroomUiCleanup.IsAlive(manager.tooltipHomeParent)) parent = manager.tooltipHomeParent;
            widget.SetParent(parent, false);
            widget.anchorMin = anchorMin; widget.anchorMax = anchorMax; widget.pivot = pivot;
            widget.sizeDelta = size; widget.anchoredPosition3D = position;
            widget.localScale = scale; widget.localRotation = rotation;
            if (parent != null) widget.SetSiblingIndex(previousSibling);
        }
        if (WorkroomUiCleanup.IsAlive(manager)) manager.tooltipCanvas =
            WorkroomUiCleanup.IsAlive(previousCanvas) ? previousCanvas : null;
        leased = false;
        manager = null; widget = null; previousParent = null; previousCanvas = null;
    }

    private static void Hide(Component source)
    {
        var current = TooltipManager._instance;
        var matched = false;
        if (WorkroomUiCleanup.IsAlive(current) && current.tooltipOwner != null &&
            current.tooltipOwner.Pointer == source.Pointer) { matched = true; current.HideTooltip(); }
        if (leased && (matched || !WorkroomUiCleanup.IsAlive(current) || !InRoom(current.tooltipOwner))) Release();
    }

    internal static void Unregister(TooltipContainer source)
    {
        if (ReferenceEquals(source, null)) return;
        Hide(source);
        sources.Remove(source.Pointer);
    }

    internal static void Update()
    {
        var current = TooltipManager._instance;
        if (WorkroomUiCleanup.IsAlive(current) && Owns(current.tooltipOwner))
        {
            var owner = current.tooltipOwner;
            var hit = PointerTarget();
            if (!WorkroomUiCleanup.IsAlive(owner) || !owner.gameObject.activeInHierarchy ||
                hit == null || hit.Pointer != owner.gameObject.Pointer) Hide(owner);
        }
        if (leased && (!WorkroomUiCleanup.IsAlive(current) || !InRoom(current.tooltipOwner))) Release();
    }

    internal static void Reset()
    {
        var current = TooltipManager._instance;
        if (WorkroomUiCleanup.IsAlive(current) && Owns(current.tooltipOwner)) current.HideTooltip();
        Release(); // A native widget must leave our Canvas before that Canvas can be destroyed.
        if (WorkroomUiCleanup.IsAlive(bridge)) AssemblyDebugUi.Destroy(bridge);
        bridge = null;
    }

    internal static void Warn(Exception ex)
    {
        if (Environment.TickCount64 < warnAt) return;
        warnAt = Environment.TickCount64 + 5000;
        Core.Log?.Warning("[工作间入口提示] " + ex);
    }
}

[HarmonyPatch(typeof(TooltipContainer), nameof(TooltipContainer.UpdateTooltip), new Type[0])]
internal static class WorkroomEntryTooltipPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static bool Prefix(TooltipContainer __instance) => WorkroomNativeTooltip.AllowUpdate(__instance);
}

[HarmonyPatch(typeof(TooltipManager), nameof(TooltipManager.EnsureTooltipLayer), new Type[0])]
internal static class WorkroomEntryTooltipLayerPatch
{
    [HarmonyPrefix]
    private static void Prefix(TooltipManager __instance)
    { try { WorkroomNativeTooltip.BeforeLayer(__instance); } catch (Exception ex) { WorkroomNativeTooltip.Warn(ex); } }
    [HarmonyPostfix]
    private static void Postfix(TooltipManager __instance, ref bool __result)
    { try { WorkroomNativeTooltip.AfterLayer(__instance, ref __result); } catch (Exception ex) { WorkroomNativeTooltip.Warn(ex); } }
}

[HarmonyPatch(typeof(TooltipManager))]
internal static class WorkroomEntryTooltipShowPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (var signature in new[] { new[] { typeof(string) }, new[] { typeof(string), typeof(Component) } })
            yield return AccessTools.DeclaredMethod(typeof(TooltipManager), nameof(TooltipManager.SetAndShowTooltip), signature) ??
                throw new MissingMethodException(typeof(TooltipManager).FullName, nameof(TooltipManager.SetAndShowTooltip));
    }
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(TooltipManager __instance, object[] __args)
    {
        try { WorkroomNativeTooltip.BeforeShow(__instance, __args.Length > 1 ? __args[1] as Component : null); }
        catch (Exception ex) { WorkroomNativeTooltip.Warn(ex); }
    }
}
