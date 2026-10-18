using System;
using Il2Cpp;

namespace PCExpansion;

/// <summary>World events wake NPC work; only unresolved work retains a bounded main-thread retry.</summary>
internal static class NpcBusiness
{
    private static bool stepping;
    internal static void Resume()
    {
        // A recovery attempt must never restart its own exhausted budget.
        if (!stepping && PlayerStore.IsInstanceExist() && PlayerStore.instance != null) Wake(PlayerStore.instance);
    }
    internal static void Wake(PlayerStore store)
    {
        if (!PlayerStore.IsInstanceExist() || PlayerStore.instance?.Pointer != store.Pointer) return;
        var pointer = store.Pointer; var run = store.runID; var slot = store.saveSlotId;
        void Schedule(string key, Action update, Func<bool> pending, int metric = -1)
        {
            bool Step()
            {
                if (!PlayerStore.IsInstanceExist() || PlayerStore.instance?.Pointer != pointer ||
                    store.runID != run || store.saveSlotId != slot) return false;
                stepping = true;
                try
                {
                    if (metric >= 0) NpcVisitPerformance.Begin(metric);
                    update(); return pending();
                }
                finally
                {
                    try { if (metric >= 0) NpcVisitPerformance.End(metric); }
                    finally { stepping = false; }
                }
            }
            BusinessWork.Schedule("npc." + key, Step, 50, 750, 120);
        }
        // Never run synchronously inside an ownership/dialogue callback. A new event also
        // restarts work that exhausted its retry budget; before-save barriers stay conservative.
        Schedule("supplier", ComputerSupplierNpcs.UpdateStockCleanup, () => ComputerSupplierNpcs.WorkPending);
        Schedule("offers", NpcStockOffers.Update, () => NpcStockOffers.Pending);
        Schedule("progress", () => NpcStoryProgress.OnWorldChanged(store), () => NpcStoryProgress.WorkPending);
        Schedule("small", () => NpcSmallStories.OnWorldChanged(store), () => NpcSmallStories.WorkPending);
        Schedule("luoxi", () => LuoxiStory.OnWorldChanged(store), () => LuoxiStory.WorkPending, NpcVisitPerformance.StoryLuoxi);
        Schedule("jiangbai", () => JiangbaiStory.OnWorldChanged(store), () => JiangbaiStory.WorkPending, NpcVisitPerformance.StoryJiangbai);
        Schedule("contact", () => MysteriousContact.OnWorldChanged(store), () => MysteriousContact.WorkPending, NpcVisitPerformance.Phone);
    }
}
