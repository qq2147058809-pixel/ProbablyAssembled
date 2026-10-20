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
    private const string SaleLabelFeatureId = "pcrepair.market_event_sale_label";
    private const int PoolWeight = 15;
    private static Il2CppSystem.Func<StoreEvent>? aiFactory, cryptoFactory;
    private static long nextWarningAt;
    [ThreadStatic] private static HashSet<IntPtr>? quotingMachines;

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

    /// <summary>装配物品基础价已合并内部物件；只补足被行情影响的配件份额。</summary>
    private static long AddedAssemblyValue(IEnumerable<CaseEconomy.PartRecord> parts, bool isCase)
    {
        var manager = Manager;
        if (manager == null) return 0;
        var ai = manager.IsEventActive(AiId);
        var crypto = manager.IsEventActive(CryptoId);
        if (!ai && !crypto) return 0;
        // Motherboard quotes already include the native MATERIAL event factor.
        var materialFactor = isCase ? MaterialFactor(manager) : 1.0;
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

    // The price patches run after the native quote is calculated. The native
    // sale sheet lists visible Market-stage item features separately, so give
    // it a zero-value feature only for the duration of that sheet refresh.
    private static string? SaleLabel(GameItem item)
    {
        var manager = Manager;
        if (item == null || manager == null) return null;
        var ai = manager.IsEventActive(AiId);
        var crypto = manager.IsEventActive(CryptoId);
        if (!ai && !crypto) return null;

        var gpu = false;
        var ram = false;
        if (ComputerCase.IsCase(item) || item.IsTag(WorkroomMachineAssembly.Tag))
        {
            var machine = WorkroomMachineFacts.Read(item);
            if (machine == null || machine.Value <= 0 || item.unitValue != machine.Value ||
                AddedAssemblyValue(machine.Parts, ComputerCase.IsCase(item) || ServerRoomHardware.IsHardware(item)) == 0) return null;
            foreach (var part in machine.Parts)
            {
                gpu |= part.TypeTag == Components.GpuTag;
                ram |= part.TypeTag == Components.RamTag;
            }
        }
        else
        {
            var spec = Components.Find(Core.Clean(item.identifier));
            if (spec == null) return null;
            gpu = spec.Owner.Tag == Components.GpuTag;
            ram = spec.Owner.Tag == Components.RamTag;
        }

        var labels = new List<string>(2);
        if (crypto && gpu) labels.Add(LanguageText.Get("event.crypto.name"));
        if (ai && (ram || gpu && !crypto)) labels.Add(LanguageText.Get("event.ai.name"));
        return labels.Count == 0 ? null : string.Join(" / ", labels);
    }

    private static void RemoveSaleLabel(GameItem item)
    {
        if (item?.FindItemFeatureByID(SaleLabelFeatureId) != null)
            item.RemoveItemFeatureByID(SaleLabelFeatureId);
    }

    private static void AddSaleLabel(GameItem item)
    {
        RemoveSaleLabel(item);
        var label = SaleLabel(item);
        if (label == null) return;
        var feature = ItemFeatureList.Discount(0);
        feature.identifier = SaleLabelFeatureId;
        feature.category = SaleLabelFeatureId;
        feature.featureType = ItemFeature.FeatureType.Normale;
        feature.valueStage = ItemFeature.ValueStage.Market;
        feature.initiallyShown = true;
        feature.isPublicHidden = false;
        feature.isExposable = false;
        // The sale sheet formats exposed features as a numeric match result.
        // Use the public display path so this zero-value marker is name only.
        feature.isFeatureExposed = false;
        feature.SetValueModifier(0);
        feature.SetPublicDisplay(label);
        feature.SetActualDisplay(label);
        item.AddItemFeature(feature);
    }

    // The native quote getters can call one another. Only their outermost call
    // for an assembled item adds the market share, leaving stored unitValue intact.
    private static bool EnterAssemblyQuote(GameItem item) =>
        item != null && (ComputerCase.IsCase(item) || item.IsTag(WorkroomMachineAssembly.Tag)) &&
        (quotingMachines ??= new HashSet<IntPtr>()).Add(item.Pointer);

    private static void LeaveAssemblyQuote(GameItem item, bool entered)
    {
        if (entered) quotingMachines?.Remove(item.Pointer);
    }

    private static void ApplyAssemblyQuote(GameItem item, bool includeEvents, bool entered, ref long value)
    {
        if (!entered || !includeEvents || value <= 0) return;
        var machine = WorkroomMachineFacts.Read(item);
        if (machine == null || machine.Value <= 0 || item.unitValue != machine.Value) return;
        var extra = AddedAssemblyValue(machine.Parts, ComputerCase.IsCase(item) || ServerRoomHardware.IsHardware(item));
        if (extra == 0) return;
        // Native discounts and the machine's final-stage bonus have already
        // changed this quote. Apply the same proportion to the affected share.
        value = checked(value + (long)Math.Round((decimal)value * extra / machine.Value));
    }

    private static void CompleteQuote(GameItem item, bool includeEvents, bool entered, ref long value)
    {
        try
        {
            if (includeEvents) ApplyLoose(item, ref value);
            ApplyAssemblyQuote(item, includeEvents, entered, ref value);
        }
        catch (Exception ex) { Warn(ex); }
        finally { LeaveAssemblyQuote(item, entered); }
    }

    private static Exception? FinishQuote(GameItem item, bool entered, Exception? failure)
    {
        LeaveAssemblyQuote(item, entered);
        return failure;
    }

    internal static void Warn(Exception ex)
    {
        if (Environment.TickCount64 < nextWarningAt) return;
        nextWarningAt = Environment.TickCount64 + 5000;
        Core.Log?.Warning("电脑行情事件处理失败：" + ex.Message);
    }

    // The calendar's detail panel uses this native method, not the event's
    // news description. These two events price items in the quote patches
    // below, so adding native negociation data would apply the rise twice.
    [HarmonyPatch(typeof(StoreEvent), nameof(StoreEvent.GetEventPriceEffect))]
    internal static class CalendarEffectPatch
    {
        private static void Postfix(StoreEvent __instance, ref string __result)
        {
            var key = Core.Clean(__instance.identifier) == AiId ? "event.ai.effect" :
                Core.Clean(__instance.identifier) == CryptoId ? "event.crypto.effect" : null;
            if (key != null) __result = LanguageText.Get(key);
        }
    }

    [HarmonyPatch(typeof(NegociationUIManager), nameof(NegociationUIManager.UpdateItemFeatureGroup))]
    internal static class SaleLabelPatch
    {
        private static void Prefix(GameItem item)
        {
            try { AddSaleLabel(item); }
            catch (Exception ex)
            {
                try { RemoveSaleLabel(item); } catch (Exception cleanup) { Warn(cleanup); }
                Warn(ex);
            }
        }

        private static void Postfix(GameItem item)
        {
            try { RemoveSaleLabel(item); }
            catch (Exception ex) { Warn(ex); }
        }

        private static Exception? Finalizer(GameItem item, Exception? __exception)
        {
            try { RemoveSaleLabel(item); }
            catch (Exception ex) { Warn(ex); }
            return __exception;
        }
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
        private static void Prefix(GameItem __instance, ref bool __state) => __state = EnterAssemblyQuote(__instance);

        private static void Postfix(GameItem __instance, bool includeEvents, bool __state, ref long __result)
            => CompleteQuote(__instance, includeEvents, __state, ref __result);

        private static Exception? Finalizer(GameItem __instance, bool __state, Exception? __exception)
            => FinishQuote(__instance, __state, __exception);
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetNegociatedValue))]
    internal static class NegotiatedValuePatch
    {
        private static void Prefix(GameItem __instance, ref bool __state) => __state = EnterAssemblyQuote(__instance);

        private static void Postfix(GameItem __instance, bool __state, ref long __result)
            => CompleteQuote(__instance, true, __state, ref __result);

        private static Exception? Finalizer(GameItem __instance, bool __state, Exception? __exception)
            => FinishQuote(__instance, __state, __exception);
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetRefreshedValue))]
    internal static class RefreshedValuePatch
    {
        private static void Prefix(GameItem __instance, ref bool __state) => __state = EnterAssemblyQuote(__instance);

        private static void Postfix(GameItem __instance, bool __state, ref long __result)
            => CompleteQuote(__instance, true, __state, ref __result);

        private static Exception? Finalizer(GameItem __instance, bool __state, Exception? __exception)
            => FinishQuote(__instance, __state, __exception);
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetValue))]
    internal static class BaseValuePatch
    {
        private static void Prefix(GameItem __instance, ref bool __state) => __state = EnterAssemblyQuote(__instance);

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(GameItem __instance, bool __state, ref long __result)
            => CompleteQuote(__instance, true, __state, ref __result);

        private static Exception? Finalizer(GameItem __instance, bool __state, Exception? __exception)
            => FinishQuote(__instance, __state, __exception);
    }
}
