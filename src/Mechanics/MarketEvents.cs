using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>原版普通事件池中的两场电脑配件行情；事件本身由原版保存和推进日期。</summary>
internal static class MarketEvents
{
    internal const string AiId = "pcrepair.ai_demand_surge";
    internal const string CryptoId = "pcrepair.crypto_boom";
    private const int PoolWeight = 15;
    private static Il2CppSystem.Func<StoreEvent>? aiFactory, cryptoFactory;
    private static long nextWarningAt;

    private static StoreEventManager? Manager
    {
        get
        {
            var station = StoreStation.Instance;
            return station == null ? null : station.storeEventManager;
        }
    }

    internal static void Register(StoreEventManager manager)
    {
        var pool = StoreEventManager.normalEventBlueprints;
        if (pool == null) return;
        aiFactory ??= (Il2CppSystem.Func<StoreEvent>)(Func<StoreEvent>)CreateAi;
        cryptoFactory ??= (Il2CppSystem.Func<StoreEvent>)(Func<StoreEvent>)CreateCrypto;
        if (!Contains(pool, AiId)) pool.Add(new StoreEventBlueprint(aiFactory, PoolWeight, AiId));
        if (!Contains(pool, CryptoId)) pool.Add(new StoreEventBlueprint(cryptoFactory, PoolWeight, CryptoId));
    }

    private static bool Contains(Il2CppSystem.Collections.Generic.List<StoreEventBlueprint> pool, string id)
    {
        foreach (var blueprint in pool)
            if (blueprint != null && Core.Clean(blueprint.identifier) == id) return true;
        return false;
    }

    private static StoreEvent CreateAi() => Create(AiId, CryptoId,
        "event.ai.name", "event.ai.news", "event.ai.description");

    private static StoreEvent CreateCrypto() => Create(CryptoId, AiId,
        "event.crypto.name", "event.crypto.news", "event.crypto.description");

    private static StoreEvent Create(string id, string incompatibleId,
        string nameKey, string newsKey, string descriptionKey)
    {
        var result = new StoreEvent
        {
            identifier = id,
            newsName = LanguageText.Get(newsKey),
            displayName = LanguageText.Get(nameKey),
            newsDescription = LanguageText.Get(descriptionKey),
            duration = 5,
            importance = 4,
            isDurationVisible = true,
        };
        result.InitNormalEvent();
        result.imcompatibleEvents.Add(incompatibleId);
        return result;
    }

    /// <summary>返回事件对整件电脑配件的倍率，不应用到拆解叶子件或盲盒本体。</summary>
    internal static int Multiplier(string typeTag)
    {
        if (typeTag != Components.GpuTag && typeTag != Components.RamTag) return 1;
        var manager = Manager;
        if (manager == null) return 1;
        if (typeTag == Components.GpuTag && manager.IsEventActive(CryptoId)) return 3;
        return manager.IsEventActive(AiId) ? 2 : 1;
    }

    /// <summary>机箱基础价已合并内部物件；只补足被行情影响的配件份额。</summary>
    internal static long AddedCaseValue(IEnumerable<CaseEconomy.PartRecord> parts)
    {
        var manager = Manager;
        if (manager == null) return 0;
        var ai = manager.IsEventActive(AiId);
        var crypto = manager.IsEventActive(CryptoId);
        if (!ai && !crypto) return 0;
        var materialFactor = MaterialFactor(manager);
        long extra = 0;
        foreach (var part in parts)
        {
            var multiplier = part.TypeTag == Components.GpuTag ? (crypto ? 3 : ai ? 2 : 1) :
                part.TypeTag == Components.RamTag && ai ? 2 : 1;
            if (multiplier == 1) continue;
            var type = Array.Find(Components.All, candidate => candidate.Tag == part.TypeTag);
            if (type == null) continue;
            var baseValue = part.Value ?? Components.ValueFor(type, part.Tier, part.Broken);
            // 散件先由原版材料行情调整，再乘本事件倍率；整机内采用相同份额。
            extra = checked(extra + (long)Math.Round(baseValue * (materialFactor * multiplier - 1.0)));
        }
        return extra;
    }

    private static double MaterialFactor(StoreEventManager manager)
    {
        var percent = 0;
        foreach (var storeEvent in manager.GetActiveEvents())
        {
            if (storeEvent == null || Core.Clean(storeEvent.identifier) == AiId ||
                Core.Clean(storeEvent.identifier) == CryptoId) continue;
            foreach (var data in storeEvent.negociationDatas)
                if (data != null && Core.Clean(data.itemType) == "MATERIAL")
                    percent = checked(percent + data.value);
        }
        return Math.Max(0, 1.0 + percent / 100.0);
    }

    internal static void ApplyLoose(GameItem item, ref long value)
    {
        if (item == null || ComputerCase.IsCase(item)) return;
        var spec = Components.Find(Core.Clean(item.identifier));
        if (spec == null) return;
        var multiplier = Multiplier(spec.Owner.Tag);
        if (multiplier > 1) value = checked(value * multiplier);
    }

    internal static void Warn(Exception ex)
    {
        if (Environment.TickCount64 < nextWarningAt) return;
        nextWarningAt = Environment.TickCount64 + 5000;
        Core.Log?.Warning("电脑行情事件处理失败：" + ex.Message);
    }

    [HarmonyPatch(typeof(StoreEventManager), nameof(StoreEventManager.QueueEvent))]
    internal static class RegistrationPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(StoreEventManager __instance)
        {
            try { Register(__instance); }
            catch (Exception ex) { Warn(ex); }
        }
    }

    [HarmonyPatch(typeof(StoreEventManager), MethodType.Constructor)]
    internal static class ConstructorPatch
    {
        private static void Postfix(StoreEventManager __instance)
        {
            try { Register(__instance); }
            catch (Exception ex) { Warn(ex); }
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetCurrentValue))]
    internal static class CurrentValuePatch
    {
        private static void Postfix(GameItem __instance, bool includeEvents, ref long __result)
        {
            if (!includeEvents) return;
            try { ApplyLoose(__instance, ref __result); }
            catch (Exception ex) { Warn(ex); }
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetNegociatedValue))]
    internal static class NegotiatedValuePatch
    {
        private static void Postfix(GameItem __instance, ref long __result)
        {
            try { ApplyLoose(__instance, ref __result); }
            catch (Exception ex) { Warn(ex); }
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetRefreshedValue))]
    internal static class RefreshedValuePatch
    {
        private static void Postfix(GameItem __instance, ref long __result)
        {
            try { ApplyLoose(__instance, ref __result); }
            catch (Exception ex) { Warn(ex); }
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetValue))]
    internal static class BaseValuePatch
    {
        private static void Postfix(GameItem __instance, ref long __result)
        {
            try { ApplyLoose(__instance, ref __result); }
            catch (Exception ex) { Warn(ex); }
        }
    }
}
