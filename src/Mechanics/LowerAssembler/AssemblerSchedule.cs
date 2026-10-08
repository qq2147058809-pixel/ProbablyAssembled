using System;
using System.Globalization;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

internal static class AssemblerSchedule
{
    internal const string LastScheduledDayKey = "pcrepair.lower_assembler.last_scheduled_day";

    // 与原版日历及博士周五来访一致：dayCounter % 7 == 5。
    internal static bool IsScheduledDay(int day) => day >= 5 && day % 7 == 5;

    internal static void Arrange(StoreClientManager manager)
    {
        try
        {
            var store = PlayerStore.Instance;
            var day = StoreStation.GetDayCounter();
            if (store == null || !IsScheduledDay(day) || manager.clientStack == null) return;
            var dayValue = day.ToString(CultureInfo.InvariantCulture);
            var data = store.modData;
            var handled = data != null && data.ContainsKey(LastScheduledDayKey) && data[LastScheduledDayKey] == dayValue;

            var current = store.currentClientInstance?.storeClient;
            var alreadyHere = store.isClientArrived && LowerAssemblerNpc.IsLowerAssembler(current);
            StoreClient? existing = null;
            // 随机生成在此时已结束；保留一个实例，避免固定与随机来访叠加。
            var stack = manager.clientStack;
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (!LowerAssemblerNpc.IsLowerAssembler(stack[i])) continue;
                if (!alreadyHere && existing == null) existing = stack[i];
                stack.RemoveAt(i);
            }
            if (existing != null) stack.Insert(0, existing);
            if (handled) return;
            if (!alreadyHere && existing == null)
            {
                var client = LowerAssemblerNpc.CreateLowerLevelAssembler();
                manager.AddNextClient(client);
                // 原版可能拒绝加入，不能将失败安排写成已完成。
                var queued = false;
                foreach (var candidate in stack)
                    if (candidate != null && candidate.Pointer == client.Pointer) { queued = true; break; }
                if (!queued) return;
            }
            if (store.modData == null)
                store.modData = new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
            store.modData[LastScheduledDayKey] = dayValue;
            Core.Log?.Msg("[深空装机] 已安排周五装机佬，第 " + dayValue + " 天；同日随机来访已合并。");
        }
        catch (Exception ex) { Core.Log?.Error("安排周五装机佬失败：" + ex); }
    }

    [HarmonyPatch(typeof(StoreClientManager), nameof(StoreClientManager.GenerateClient))]
    internal static class FridaySchedulePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(StoreClientManager __instance) => Arrange(__instance);
    }
}
