using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>开店完成原版全部来客安排后，独立追加当日随机电脑供应者。</summary>
internal static class ComputerSupplierSchedule
{
    private const string SellerDecisionKey = "pcrepair.computer_supplier.seller_day";
    private const string ThiefDecisionKey = "pcrepair.computer_supplier.thief_day";
    private static bool arranging;

    private static bool Present(PlayerStore store, StoreClientManager manager, string id)
    {
        var current = store.currentClientInstance?.storeClient;
        if (store.isClientArrived && current != null && Core.Clean(current.identifier) == id) return true;
        foreach (var client in manager.clientStack)
            if (client != null && Core.Clean(client.identifier) == id) return true;
        return false;
    }

    private static bool Roll(string run, int day, string id, int chance)
    {
        // 同档、同日、同角色的独立随机值固定，避免尚未保存就重载时重抽。
        // 不使用星期、间隔或未到天数，不引入周期或保底。
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes("PCREPAIR_SUPPLIER_V1\n" + run + "\n" +
            day.ToString(CultureInfo.InvariantCulture) + "\n" + id));
        var value = (uint)bytes[0] | (uint)bytes[1] << 8 | (uint)bytes[2] << 16 | (uint)bytes[3] << 24;
        return value < (1UL << 32) * (ulong)chance / 100;
    }

    private static void ArrangeOne(PlayerStore store, StoreClientManager manager, int day,
        string run, string id, string key, int firstDay, int chance)
    {
        if (day < firstDay) return;
        var owner = store.Pointer; var slot = store.saveSlotId;
        void RequireSession()
        {
            if (!PlayerStore.IsInstanceExist() || PlayerStore.instance == null || PlayerStore.instance.Pointer != owner ||
                PlayerStore.instance.runID != run || PlayerStore.instance.saveSlotId != slot || StoreStation.GetDayCounter() != day)
                throw new InvalidOperationException("电脑供应者安排期间存档或日期已变化。");
        }
        RequireSession();
        store.modData ??= new Il2CppSystem.Collections.Generic.Dictionary<string, string>();
        var data = store.modData;
        var dayText = day.ToString(CultureInfo.InvariantCulture);
        var chosen = false; var queued = false; var recorded = false;
        if (data.ContainsKey(key))
        {
            var parts = Core.Clean(data[key]).Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var priorDay) ||
                (parts[1] != "0" && parts[1] != "1") || (parts[2] != "0" && parts[2] != "1") ||
                parts[1] == "0" && parts[2] == "1")
                throw new InvalidOperationException("电脑供应者每日记录无效，保留原记录。");
            if (priorDay == day)
            { recorded = true; chosen = parts[1] == "1"; queued = parts[2] == "1"; }
        }
        if (Present(store, manager, id))
        {
            RequireSession();
            data[key] = dayText + ":1:1";
            return;
        }
        if (!recorded)
        {
            chosen = Roll(run, day, id, chance);
            // 失败结果也保存；工厂或入队失败保持成功决定，重试不重新抽。
            data[key] = dayText + (chosen ? ":1:0" : ":0:0");
            Core.Debug("电脑供应者第" + dayText + "天判定：" + id + "，概率=" + chance + "%，结果=" + chosen);
        }
        if (!chosen || queued) return;
        var client = ComputerSupplierNpcs.Create(id);
        RequireSession();
        try { manager.AddClient(client); }
        finally
        {
            RequireSession();
            foreach (var actual in manager.clientStack)
                if (actual != null && actual.Pointer == client.Pointer)
                {
                    data[key] = dayText + ":1:1";
                    Core.Log?.Msg("[深空装机] 已在当日来客队尾加入" + client.displayName + "，第" + dayText + "天。");
                    break;
                }
        }
    }

    private static void Arrange(PlayerStore store)
    {
        if (arranging) return;
        arranging = true;
        try
        {
            if (!PlayerStore.IsInstanceExist() || PlayerStore.instance == null || PlayerStore.instance.Pointer != store.Pointer) return;
            var manager = store.storeClientManager;
            var run = store.runID;
            var day = StoreStation.GetDayCounter();
            if (manager == null || manager.clientStack == null || string.IsNullOrEmpty(run) || day < 5) return;
            ComputerSupplierNpcs.EnsureRegistered();
            var sign = ShowcaseHelper.IsShowcaseContainItemById(ComputerSign.Id);
            try { ArrangeOne(store, manager, day, run, ComputerSupplierNpcs.SellerId, SellerDecisionKey, 5,
                sign ? ComputerSign.SellerInviteChance : 30); }
            catch (Exception ex) { Core.Log?.Error("安排电脑材料卖家失败：" + ex); }
            try { ArrangeOne(store, manager, day, run, ComputerSupplierNpcs.ThiefId, ThiefDecisionKey, 12,
                sign ? ComputerSign.ThiefInviteChance : 20); }
            catch (Exception ex) { Core.Log?.Error("安排电脑配件小偷失败：" + ex); }
        }
        catch (Exception ex) { Core.Log?.Error("电脑供应者每日安排失败：" + ex); }
        finally { arranging = false; }
    }

    // GenerateClient之后原版还会处理未来/特殊访客；在整个开店安排完成后追加，保留其前序。
    // 此时原版尚未调用OpenShutter，不能要求门的opened标记已切换。
    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.OnShutterOpened))]
    internal static class DailySupplierPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PlayerStore __instance) => Arrange(__instance);
    }
}
