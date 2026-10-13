using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Read-only injury diagnostics; native recovery and sleep decisions stay unchanged.</summary>
internal static class PlayerHealthDiagnostics
{
    private const string Marker = "[PC-HEALTH-DIAG-20261012]";
    private static readonly HashSet<MethodBase> reportedOwners = new();
    private static long nextRejection;

    internal sealed record Sample(IntPtr Owner, IntPtr Health, string Run, int Slot, int Day,
        int Wound, bool Stable, bool Fresh)
    {
        internal bool Injured => Wound != 0 || Fresh;
        internal string Describe => $"slot={Slot}; day={Day}; wound={Wound}; stable={Stable}; fresh={Fresh}";
        internal bool SameSession(Sample other) => Owner == other.Owner && Health == other.Health &&
            Run == other.Run && Slot == other.Slot;
    }

    private static Sample? Read(HealthData? instance = null)
    {
        try
        {
            if (!PlayerStore.IsInstanceExist() || PlayerStore.instance is not { } store ||
                store.healthData is not { } health || StoreStation.Instance == null ||
                (instance != null && instance.Pointer != health.Pointer)) return null;
            return new Sample(store.Pointer, health.Pointer, store.runID, store.saveSlotId,
                StoreStation.GetDayCounter(), health.woundState, health.isWoundStable, health.isWoundedFresh);
        }
        catch { return null; }
    }

    internal static void SleepRejected(string reason)
    {
        try
        {
            if (Environment.TickCount64 < nextRejection) return;
            nextRejection = Environment.TickCount64 + 2000;
            Core.Log?.Warning($"{Marker} sleep=rejected; reason={reason}; {Read()?.Describe ?? "health=unavailable"}");
        }
        catch { }
    }

    internal static void SleepAccepted()
    {
        try
        {
            if (Read() is { Injured: true } sample)
                Core.Log?.Msg($"{Marker} sleep=admitted-by-PCExpansion; {sample.Describe}");
        }
        catch { }
    }

    private static void Report(HealthData instance, MethodBase method, Sample? before, Exception? error,
        bool? originalRan = null)
    {
        try
        {
            var after = Read(instance);
            if (before == null || after == null || !before.SameSession(after) ||
                (!before.Injured && !after.Injured && error == null)) return;
            if (reportedOwners.Add(method))
            {
                var owners = HarmonyLib.Harmony.GetPatchInfo(method)?.Owners;
                var text = owners == null ? "unavailable" : string.Join(",", owners.OrderBy(x => x).Take(20));
                text = new string(text.Where(c => !char.IsControl(c)).Take(500).ToArray());
                Core.Log?.Msg($"{Marker} method={method.Name}; harmonyOwners={text}");
            }
            Core.Log?.Msg($"{Marker} method={method.Name}; before=({before.Describe}); " +
                $"after=({after.Describe}); originalRan={originalRan?.ToString() ?? "unknown"}; " +
                $"result={(error == null ? "returned" : "exception:" + error.GetType().Name)}");
        }
        catch { /* Logging cannot interrupt health callbacks. */ }
    }

    [HarmonyPatch]
    internal static class HealthCallbackPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { nameof(HealthData.OnSleep), nameof(HealthData.HandleNightlyWound),
                nameof(HealthData.OnHealingItemUsed) })
                yield return AccessTools.DeclaredMethod(typeof(HealthData), name, Type.EmptyTypes) ??
                    throw new MissingMethodException(typeof(HealthData).FullName, name);
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(HealthData __instance, out Sample? __state) => __state = Read(__instance);

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(HealthData __instance, MethodBase __originalMethod, Sample? __state,
            bool __runOriginal) => Report(__instance, __originalMethod, __state, null, __runOriginal);

        private static Exception? Finalizer(HealthData __instance, MethodBase __originalMethod,
            Sample? __state, Exception? __exception)
        {
            if (__exception != null) Report(__instance, __originalMethod, __state, __exception);
            return __exception;
        }
    }
}
