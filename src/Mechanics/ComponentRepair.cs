using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>维修只在工作间提交；阻止原版拖料行为绕过本 Mod 配方。</summary>
internal static class ComponentRepair
{
    private static readonly string[] RepairMaterials =
    {
        "common_electronic", "metal_ingot", "nuts_metal",
        "printer_plastic", "scrap_metal", "wire"
    };
    private static readonly HashSet<string> TargetMaterials = new(RepairMaterials, StringComparer.Ordinal)
    { "energy_credit" };

    // Callers may enumerate or keep their own set; do not expose our mutable array.
    internal static string[] GetRepairMaterialIds() => (string[])RepairMaterials.Clone();

    private static bool ShouldBlock(GameItem? source, GameItem? target) =>
        source != null && target != null && TargetMaterials.Contains(Core.Clean(source.identifier)) &&
        Components.Find(Core.Clean(target.identifier)) is { Broken: true };

    [HarmonyPatch(typeof(ItemBehaviourManager), nameof(ItemBehaviourManager.Target))]
    private static class RepairBehaviourRoutingPatch
    {
        private static bool Prefix(GameItem __0, GameItem __1) => !ShouldBlock(__0, __1);
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.MayTarget))]
    private static class RepairMayTargetPatch
    {
        private static bool Prefix(GameItem __instance, GameItem __0, ref bool __result)
        {
            if (!ShouldBlock(__instance, __0)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.CanTarget))]
    private static class RepairCanTargetPatch
    {
        private static bool Prefix(GameItem __instance, GameItem __0, ref bool __result)
        {
            if (!ShouldBlock(__instance, __0)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.Target))]
    private static class RepairTargetPatch
    {
        private static bool Prefix(GameItem __instance, GameItem __0) => !ShouldBlock(__instance, __0);
    }
}
