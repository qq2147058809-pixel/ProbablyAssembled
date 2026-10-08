#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace PCExpansion;

// Only initiation is gated. Native release/reset/abort paths retain ownership
// of their pointers and ghosts and always finish their normal cleanup.
[HarmonyPatch]
internal static class WorkroomShopItemPressPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var arguments = new[] { typeof(Il2CppSystem.Collections.Generic.ISet<KeyCode>) };
        foreach (var type in new[] { typeof(ItemMouseDragHandler), typeof(ItemMouseDoubleClickHandler),
            typeof(ItemSelectHandler), typeof(ItemQuickTransferHandler),
            typeof(ItemQuickTransferAltHandler), typeof(ItemQuickTransferAltHandler1) })
            yield return AccessTools.DeclaredMethod(type, "OnEventPress", arguments) ??
                throw new MissingMethodException(type.FullName, "OnEventPress");
    }
    private static bool Prefix(Il2CppSystem.Collections.Generic.ISet<KeyCode> __0) => WorkroomGameAdapter.AllowShopItemPress(__0);
}

[HarmonyPatch(typeof(ItemMouseDoubleClickHandler))]
internal static class WorkroomShopItemActionPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var name in new[] { "DoubleClickAction", "OpenContentAction", "OpenExamineAction" })
            yield return AccessTools.DeclaredMethod(typeof(ItemMouseDoubleClickHandler), name,
                new[] { typeof(GameItem), typeof(Vector2) }) ??
                throw new MissingMethodException(typeof(ItemMouseDoubleClickHandler).FullName, name);
    }
    private static bool Prefix(GameItem __0) => WorkroomGameAdapter.AllowShopItem(__0);
}

[HarmonyPatch(typeof(ItemSelectHandler))]
internal static class WorkroomShopItemSelectPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var name in new[] { "TrySelectItem", "TryEquipItem", "SelectItem" })
            yield return AccessTools.DeclaredMethod(typeof(ItemSelectHandler), name, new[] { typeof(GameItem) }) ??
                throw new MissingMethodException(typeof(ItemSelectHandler).FullName, name);
    }
    private static bool Prefix(GameItem __0) => WorkroomGameAdapter.AllowShopItem(__0);
}

[HarmonyPatch(typeof(ItemMouseDragHandler), nameof(ItemMouseDragHandler.StartDrag), new Type[] { })]
internal static class WorkroomShopItemDragStartPatch
{
    private static bool Prefix(ItemMouseDragHandler __instance) => WorkroomGameAdapter.AllowShopItem(__instance.currentItem);
}

[HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.ToFront), new[] { typeof(bool) })]
internal static class WorkroomShopWindowFrontPatch
{
    private static bool Prefix(PixelWindow __instance) => WorkroomGameAdapter.AllowShopWindow(__instance);
}
