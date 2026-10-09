using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Loads marked saves from the 1.0 generation onward. No migration.</summary>
internal static class SaveGeneration
{
    private const string Key = "pcexpansion.save-generation";
    private const string Contract = "1.0";
    private static readonly Version MinimumContract = new(1, 0);
    private static IntPtr owner;
    private static string? run;
    private static string? expectedRun;
    private static int expectedSlot;
    private static bool pendingNew, initialSave, loading;
    [ThreadStatic] private static int decoding;
    internal static bool Decoding => decoding > 0;
    private static long noticeAt;

    private static bool HasContract(PlayerStore? store)
    {
        if (store?.modData == null || !store.modData.ContainsKey(Key)) return false;
        var value = store.modData[Key];
        return Version.TryParse(value, out var version) && version != null &&
            version.CompareTo(MinimumContract) >= 0;
    }

    internal static bool IsSupported(PlayerStore? store)
    {
        try
        {
            return store != null && store.Pointer == owner && !string.IsNullOrEmpty(run) &&
                store.runID == run && store.saveSlotId == expectedSlot && HasContract(store);
        }
        catch { return false; }
    }

    internal static bool PrepareLoad(PlayerStore store)
    {
        try
        {
            // The game's preload reads only the playerStore envelope. Its full
            // LoadGame restores inventories later, after FireOnGameLoadedEarly.
            var saved = PlayerStore.PreLoadStore();
            if (!HasContract(saved) || string.IsNullOrEmpty(saved.runID) ||
                NewGameData.Instance == null || saved.saveSlotId != NewGameData.Instance.saveSlot)
                return Reject();
            owner = store.Pointer; run = null; pendingNew = initialSave = false;
            expectedRun = saved.runID; expectedSlot = saved.saveSlotId; loading = true;
            return true;
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("读取存档标记失败，已停止加载：" + ex.Message);
            return Reject();
        }
    }

    internal static void BeginNew(PlayerStore store)
    {
        owner = store.Pointer; run = expectedRun = null;
        pendingNew = true; initialSave = loading = false;
        // The native new-run path can reuse its store object. Reset only this
        // mod's state, never stamp a previous run as a supported new save.
        if (store.modData == null) return;
        var remove = new List<string>();
        foreach (var pair in store.modData)
            if (pair.Key.StartsWith("pcrepair.", StringComparison.Ordinal) ||
                pair.Key.StartsWith("pcexpansion.", StringComparison.Ordinal)) remove.Add(pair.Key);
        foreach (var key in remove) store.modData.Remove(key);
    }

    internal static void Failed()
    {
        owner = IntPtr.Zero; run = expectedRun = null;
        pendingNew = initialSave = loading = false;
    }

    private static bool Reject()
    {
        // A refused preflight must not revoke the still-running valid session.
        // Only an attempted native session change may invalidate that session.
        if (Environment.TickCount64 >= noticeAt)
        {
            noticeAt = Environment.TickCount64 + 3000;
            var message = LanguageText.Get("save.unsupported");
            Core.Log?.Warning(message);
            try { StoreUIManager.Instance?.Notify(message, "#FFFFFF"); }
            catch (Exception ex) { Core.Debug("存档校验提示暂不可显示：" + ex.Message); }
        }
        return false;
    }

    [HarmonyPatch(typeof(ModHook), nameof(ModHook.FireOnGameLoadedEarly))]
    internal static class EarlyLoadPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            var store = PlayerStore.Instance;
            if (!loading || store == null || store.Pointer != owner || !HasContract(store) ||
                store.runID != expectedRun || store.saveSlotId != expectedSlot)
            {
                Failed();
                Reject();
                throw new InvalidOperationException("存档封套在预检后变化，停止物品恢复。");
            }
            run = expectedRun;
            loading = false;
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.InitialSave))]
    internal static class InitialSavePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PlayerStore __instance, out bool __state)
        {
            __state = pendingNew && __instance.Pointer == owner && WorkroomGameAdapter.SaveHooksReady;
            if (!__state) return Reject();
            // InitialSave creates the native run GUID before calling SaveGame.
            initialSave = true;
            return true;
        }
        private static Exception? Finalizer(Exception? __exception, bool __state)
        {
            if (__state)
            {
                initialSave = pendingNew = false;
                if (__exception != null) Failed();
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PlayerStore))]
    internal static class WritePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.SaveGame));
            yield return AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.WriteSlotFile));
        }
        [HarmonyPriority(Priority.First + 100)]
        private static bool Prefix(PlayerStore __instance, MethodBase __originalMethod)
        {
            if (!WorkroomGameAdapter.SaveHooksReady) return Reject();
            if (initialSave && pendingNew && run == null && __instance.Pointer == owner && !string.IsNullOrEmpty(__instance.runID))
            {
                __instance.modData ??= new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
                __instance.modData[Key] = Contract;
                run = __instance.runID;
                expectedSlot = __instance.saveSlotId;
            }
            if (!IsSupported(__instance)) return Reject();
            // Also cover a direct native file write and re-check any transaction
            // that started after SaveGame's own entry guard.
            return __originalMethod.Name != nameof(PlayerStore.WriteSlotFile) || WorkroomStorage.BeforeSave();
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.DecodeSaveItem))]
    internal static class DecodePatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(PlayerStore __instance, out bool __state)
        {
            __state = IsSupported(__instance);
            if (!__state) return Reject();
            decoding++;
            return true;
        }
        private static Exception? Finalizer(Exception? __exception, bool __state)
        {
            if (__state) decoding--;
            if (__exception != null) Failed();
            return __exception;
        }
    }
}
