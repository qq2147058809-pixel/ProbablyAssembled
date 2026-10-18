#nullable disable
using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PCExpansion;

/// <summary>只处理本 Mod 入口提示；室内说明使用自有容器，店铺入口保留原生路径。</summary>
internal static class WorkroomNativeTooltip
{
    private sealed class Source
    {
        internal TooltipContainer Container;
        internal bool InRoom;
    }
    private static readonly Dictionary<IntPtr, Source> sources = new();
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
            if (InRoom(source)) { SceneEntryTooltip.Show(source, current); return false; }
            if (!WorkroomUiCleanup.IsAlive(current)) return true;
            // Native UpdateTooltip prefixes English "Unavailable" when isAvailable is temporarily false.
            // Keep the actual interaction guard; this exact-owner path only supplies the Chinese description.
            current.SetAndShowTooltip(source.content, source);
            return false;
        }
        catch (Exception ex) { Warn(ex); return true; }
    }

    internal static bool BeforeShow(TooltipManager current, Component nextOwner)
    {
        if (InRoom(nextOwner))
        {
            var source = sources[nextOwner.Pointer].Container;
            var hit = PointerTarget();
            if (hit != null && hit.Pointer == source.gameObject.Pointer) SceneEntryTooltip.Show(source, current);
            else Hide(source);
            return false;
        }
        SceneEntryTooltip.Hide();
        return true;
    }

    internal static void Hide(Component source)
    {
        var current = TooltipManager._instance;
        if (WorkroomUiCleanup.IsAlive(current) && current.tooltipOwner != null &&
            current.tooltipOwner.Pointer == source.Pointer) current.HideTooltip();
        SceneEntryTooltip.Hide(source);
    }

    internal static void Unregister(TooltipContainer source)
    {
        if (ReferenceEquals(source, null)) return;
        Hide(source);
        sources.Remove(source.Pointer);
    }

    internal static void Update()
    {
        var sceneOwner = SceneEntryTooltip.Owner;
        if (!ReferenceEquals(sceneOwner, null))
        {
            var hit = PointerTarget();
            if (!InRoom(sceneOwner) || hit == null || hit.Pointer != sceneOwner.gameObject.Pointer) SceneEntryTooltip.Hide();
            else SceneEntryTooltip.Move();
        }
        var current = TooltipManager._instance;
        if (WorkroomUiCleanup.IsAlive(current) && Owns(current.tooltipOwner))
        {
            var owner = current.tooltipOwner;
            var hit = PointerTarget();
            if (!WorkroomUiCleanup.IsAlive(owner) || !owner.gameObject.activeInHierarchy ||
                hit == null || hit.Pointer != owner.gameObject.Pointer) Hide(owner);
        }
    }

    internal static void Reset()
    {
        var current = TooltipManager._instance;
        if (WorkroomUiCleanup.IsAlive(current) && Owns(current.tooltipOwner)) current.HideTooltip();
        SceneEntryTooltip.Reset();
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
    private static bool Prefix(TooltipManager __instance, object[] __args)
    {
        try { return WorkroomNativeTooltip.BeforeShow(__instance, __args.Length > 1 ? __args[1] as Component : null); }
        catch (Exception ex) { WorkroomNativeTooltip.Warn(ex); return true; }
    }
}
