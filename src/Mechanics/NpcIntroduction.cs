using System;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Delayed merchant introductions belong to the visitor and session that displayed them.</summary>
internal static class NpcIntroduction
{
    private static long generation;
    internal static void Invalidate() => generation = unchecked(generation + 1);

    internal static void Bind(StoreClient client, Dialogue root, Dialogue terminal, Action<StoreClient> introduce)
    {
        var armed = false;
        var owner = IntPtr.Zero;
        var displayedMain = IntPtr.Zero;
        string? run = null;
        var slot = 0;
        long expectedGeneration = 0;
        // Factories also run during save restoration. Bind session values when
        // the terminal is actually displayed, after native restoration finishes.
        terminal.SetStartAction((Il2CppSystem.Action)(Action)(() =>
        {
            armed = false;
            var store = PlayerStore.Instance;
            if (store == null || !SaveGeneration.IsSupported(store) || !store.isClientArrived ||
                store.currentClientInstance?.storeClient?.Pointer != client.Pointer) return;
            owner = store.Pointer; run = store.runID; slot = store.saveSlotId;
            displayedMain = client.mainDialogue?.Pointer ?? IntPtr.Zero;
            expectedGeneration = generation; armed = true;
        }));
        terminal.SetEndAction((Il2CppSystem.Action)(Action)(() =>
        {
            if (!armed) return;
            armed = false;
            var store = PlayerStore.Instance;
            if (generation != expectedGeneration || store == null || !SaveGeneration.IsSupported(store) ||
                store.Pointer != owner || store.runID != run || store.saveSlotId != slot || !store.isClientArrived ||
                store.currentClientInstance?.storeClient?.Pointer != client.Pointer ||
                (client.mainDialogue?.Pointer != root.Pointer && client.mainDialogue?.Pointer != displayedMain) ||
                client.isIntroduced) return;
            introduce(client);
        }));
    }

    [HarmonyPatch(typeof(DialogUIManager), "InterruptDialog")]
    internal static class InterruptPatch
    {
        private static void Prefix() => Invalidate();
    }
}
