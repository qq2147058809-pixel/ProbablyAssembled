using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>27571 only: completed dialogue, current suspected crime, and absolute game-day cooldown.</summary>
internal static class MysteriousContact
{
    internal const long PhoneNumber = 27571;
    internal const string Id = "pcrepair.mysterious_contact_27571";
    internal const string SaveKey = "pcrepair.jiangbai.contact.v1";
    internal sealed class Document
    {
        public int Version { get; set; } = 1;
        public string Run { get; set; } = "";
        public int Slot { get; set; }
        public int UsedDay { get; set; } = -1;
        public int ReadyDay { get; set; } = -1;
    }
    private sealed class Service
    {
        internal SecData Data = null!;
        internal Il2CppSystem.Collections.Generic.List<int> Crimes = null!;
        internal int[] Before = Array.Empty<int>();
        internal int Evidence;
        internal Document Next = null!;
        internal string Encoded = "";
    }
    private static PlayerStore? owner;
    private static string? run, raw, error;
    private static int slot;
    private static bool present, resolving;
    private static Document? document;
    private static Service? pending;
    private static long warningAt, refreshAt;
    private static Dialogue? first, last;
    private static int callEpoch;
    private static IntPtr callUi;
    private static bool firstComplete, lastComplete, callEnded;

    internal static bool WorkPending => pending != null || resolving;
    internal static int Remaining(Document value, int day) => value.ReadyDay < 0 ? 0 : Math.Max(0, value.ReadyDay - day);
    internal static Document Parse(string value, string expectedRun, int expectedSlot)
    {
        using var json = JsonDocument.Parse(value);
        var root = json.RootElement;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var required = new[] { "Version", "Run", "Slot", "UsedDay", "ReadyDay" };
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(p => !names.Add(p.Name)) ||
            names.Count != required.Length || required.Any(p => !names.Contains(p)))
            throw new InvalidOperationException("神秘人冷却记录字段无效。");
        var result = JsonSerializer.Deserialize<Document>(value, WorkroomStorageState.JsonOptions)
            ?? throw new InvalidOperationException("神秘人冷却记录为空。");
        if (result.Version != 1 || result.Run != expectedRun || result.Slot != expectedSlot ||
            !(result.UsedDay == -1 && result.ReadyDay == -1 || result.UsedDay >= 1 && (long)result.ReadyDay == (long)result.UsedDay + 10))
            throw new InvalidOperationException("神秘人冷却版本、会话或日期无效。");
        return result;
    }
    private static bool SameOwner() => owner != null && PlayerStore.IsInstanceExist() && PlayerStore.instance != null &&
        PlayerStore.instance.Pointer == owner.Pointer && owner.runID == run && owner.saveSlotId == slot && SaveGeneration.IsSupported(owner);
    private static bool HasKey(PlayerStore store) => store.modData != null && store.modData.ContainsKey(SaveKey);
    private static string? Read(PlayerStore store) => HasKey(store) ? store.modData[SaveKey] : null;
    internal static bool ValidateRaw() { try { return Ready && MatchesRaw(); } catch { return false; } }
    private static bool MatchesRaw() => owner != null && HasKey(owner) == present && Read(owner) == raw;
    private static bool Ready => document != null && error == null && pending == null && !resolving && SameOwner();
    private static bool Bind(PlayerStore store)
    {
        if (!SaveGeneration.IsSupported(store) || !PlayerStore.IsInstanceExist() || PlayerStore.instance?.Pointer != store.Pointer) return false;
        if (SameOwner()) return true;
        if (pending != null || resolving) return false;
        CancelCall(); document = null; owner = store; run = store.runID; slot = store.saveSlotId; error = null; refreshAt = 0;
        present = HasKey(store); raw = Read(store);
        try { document = present ? Parse(raw!, run, slot) : new Document { Run = run, Slot = slot }; }
        catch { error = "invalid_record"; throw; }
        return true;
    }
    internal static void OnWorldChanged(PlayerStore store) { refreshAt = 0; Update(store); }

    internal static void Update(PlayerStore store)
    {
        try
        {
            ResolveService();
            if (!Bind(store) || Environment.TickCount64 < refreshAt || !ValidateRaw()) return;
            refreshAt = Environment.TickCount64 + 650;
            Sync(Ensure(store.PhoneClientDict));
        }
        catch (Exception ex) { Warn(ex); }
    }
    internal static void AfterLoad(PlayerStore store) { CancelCall(); refreshAt = 0; Update(store); }
    internal static bool BeforeSave()
    {
        NpcBusiness.Resume();
        ResolveService();
        return !resolving && pending == null && (owner == null || !SameOwner() || error == null && MatchesRaw());
    }
    internal static bool AllowSessionChange() => BeforeSave();
    internal static void ResetSession()
    {
        if (!AllowSessionChange()) throw new InvalidOperationException("神秘人服务结算未确认，不能切换会话。");
        CancelCall(); owner = null; run = raw = error = null; document = null; slot = 0; present = false; warningAt = refreshAt = 0;
    }
    private static bool IsOurs(StorePhoneClient? contact) => contact != null && Core.Clean(contact.locID) == Id;
    private static StorePhoneClient Ensure(Il2CppSystem.Collections.Generic.Dictionary<long, StorePhoneClient>? contacts)
    {
        if (contacts == null) throw new InvalidOperationException("电话簿尚未初始化。");
        if (contacts.ContainsKey(PhoneNumber))
        {
            var existing = contacts[PhoneNumber];
            if (!IsOurs(existing)) throw new InvalidOperationException("27571已被其他联系人占用，保留原联系人。");
            existing.displayName = LanguageText.Get("jiangbai.contact.name");
            return existing;
        }
        var created = new StorePhoneClient
        {
            locID = Id, dialogFuncId = Id, displayName = LanguageText.Get("jiangbai.contact.name"),
            phoneClientType = StorePhoneClient.PhoneClientType.Supplier,
            phoneState = StorePhoneClient.PhoneState.Regular, cooldownDuration = 10, currentCooldown = 0, dialedBefore = false,
        };
        contacts.Add(PhoneNumber, created);
        return created;
    }
    private static bool OwnNumber()
    {
        var contacts = PlayerStore.Instance?.PhoneClientDict;
        return contacts != null && contacts.ContainsKey(PhoneNumber) && IsOurs(contacts[PhoneNumber]);
    }
    private static bool Accessible()
    {
        var store = PlayerStore.Instance;
        return store != null && Bind(store) && ValidateRaw() && JiangbaiStory.ValidateRaw() && JiangbaiStory.ContactUnlocked && OwnNumber();
    }
    private static void Sync(StorePhoneClient contact)
    {
        if (!Ready) return;
        var remaining = Remaining(document!, StoreStation.GetDayCounter());
        contact.cooldownDuration = 10; contact.currentCooldown = remaining;
        contact.phoneState = remaining == 0 ? StorePhoneClient.PhoneState.Regular : StorePhoneClient.PhoneState.Cooldown;
    }
    private static Dialogue Dialog(StorePhoneClient contact)
    {
        CancelCall();
        if (!Accessible()) return PhoneDialogList.GenericCooldown(LanguageText.Get("jiangbai.contact.name"));
        Sync(contact);
        if (Remaining(document!, StoreStation.GetDayCounter()) != 0)
            return PhoneDialogList.GenericCooldown(LanguageText.Get("jiangbai.contact.name"));
        first = new Dialogue().SetText(LanguageText.Get("jiangbai.contact.name"), LanguageText.Get("jiangbai.contact.line1"));
        last = new Dialogue().SetText(LanguageText.Get("jiangbai.contact.name"), LanguageText.Get("jiangbai.contact.line2"));
        var expectedFirst = first; var expectedLast = last;
        first.SetNextDialogue(last);
        // Native ContinueConversation may ExecuteEndAction before text finishes.
        // A completed-text receipt plus the exact live call is therefore required.
        last.SetEndAction((Il2CppSystem.Action)(Action)(() => Complete(expectedFirst, expectedLast)));
        return first;
    }
    private static void TextCompleted(DialoguePhoneUIManager manager)
    {
        if (first == null || last == null || manager.currentDialog == null) return;
        if (manager.currentDialog.Pointer == first.Pointer)
        { firstComplete = true; callEpoch = manager.conversationEpoch; callUi = manager.Pointer; callEnded = false; }
        if (manager.currentDialog.Pointer == last.Pointer && firstComplete && manager.Pointer == callUi &&
            manager.conversationEpoch == callEpoch) lastComplete = true;
    }
    private static void Complete(Dialogue expectedFirst, Dialogue expectedLast)
    {
        try
        {
            var phone = PhoneUIManager.Instance; var manager = DialoguePhoneUIManager.Instance;
            if (first?.Pointer != expectedFirst.Pointer || last?.Pointer != expectedLast.Pointer || !firstComplete || !lastComplete ||
                manager == null || manager.Pointer != callUi || manager.currentDialog?.Pointer != expectedLast.Pointer ||
                !(manager.conversationEpoch == callEpoch || callEnded && manager.conversationEpoch == unchecked(callEpoch + 1)) ||
                phone == null || !phone.isTalking || phone.currentNumber != PhoneNumber || !IsOurs(phone.currentPhoneClient) || !SameOwner() || !Accessible() ||
                Remaining(document!, StoreStation.GetDayCounter()) != 0) return;
            CancelCall();
            var data = owner!.secData ?? throw new InvalidOperationException("治安数据未准备好。");
            var crimes = data.crimeAmount ?? throw new InvalidOperationException("犯罪记录未准备好。");
            var before = new int[crimes.Count]; for (var i = 0; i < before.Length; i++) before[i] = crimes[i];
            var evidence = data.evidenceLevel;
            if (evidence == 0 && before.All(v => v == 0)) return;
            var day = StoreStation.GetDayCounter();
            if (day < 1) throw new InvalidOperationException("当前游戏日无效。");
            var next = new Document { Run = run!, Slot = slot, UsedDay = day, ReadyDay = checked(day + 10) };
            var encoded = JsonSerializer.Serialize(next, WorkroomStorageState.JsonOptions);
            Parse(encoded, run!, slot);
            pending = new Service { Data = data, Crimes = crimes, Before = before, Evidence = evidence, Next = next, Encoded = encoded };
            ResolveService();
        }
        catch (Exception ex) { Warn(ex); }
        finally { NpcBusiness.Resume(); }
    }
    private static void ResolveService()
    {
        if (pending == null || resolving) return;
        resolving = true;
        try
        {
            var operation = pending;
            if (!SameOwner() || owner!.secData?.Pointer != operation.Data.Pointer || operation.Data.crimeAmount?.Pointer != operation.Crimes.Pointer ||
                operation.Crimes.Count != operation.Before.Length) throw new InvalidOperationException("神秘人结算时会话或治安记录变化。");
            // Read back field setters after failures. A setter may have succeeded
            // before throwing; only the original value or its intended zero is safe.
            for (var i = 0; i < operation.Before.Length; i++)
                if (operation.Crimes[i] != 0 && operation.Crimes[i] != operation.Before[i]) throw new InvalidOperationException("结算期间出现新犯罪记录。");
            if (operation.Data.evidenceLevel != 0 && operation.Data.evidenceLevel != operation.Evidence)
                throw new InvalidOperationException("结算期间调查进度变化。");
            if (!MatchesRaw() && !(HasKey(owner) && Read(owner) == operation.Encoded))
                throw new InvalidOperationException("神秘人冷却原文变化，保留未决结算。");
            for (var i = 0; i < operation.Before.Length; i++) if (operation.Crimes[i] != 0) operation.Crimes[i] = 0;
            if (operation.Data.evidenceLevel != 0) operation.Data.evidenceLevel = 0;
            if (operation.Data.evidenceLevel != 0 || Enumerable.Range(0, operation.Before.Length).Any(i => operation.Crimes[i] != 0))
                throw new InvalidOperationException("治安数据尚未确认清零。");
            // ResetCrime also resets state (dispatch/enforcement), so do not call
            // it. Historical totals, trackers, assigned officer and queues remain.
            Exception? failure = null;
            try { owner.modData ??= new Il2CppSystem.Collections.Generic.Dictionary<string, string>(); owner.modData[SaveKey] = operation.Encoded; }
            catch (Exception ex) { failure = ex; }
            if (!SameOwner() || !HasKey(owner) || Read(owner) != operation.Encoded)
                throw new InvalidOperationException("神秘人冷却发布尚未确认。", failure);
            document = operation.Next; raw = operation.Encoded; present = true; error = null; pending = null; BusinessRuntime.Wake();
        }
        catch (Exception ex) { Warn(ex); }
        finally { resolving = false; }
    }
    private static void CancelCall()
    { first = last = null; firstComplete = lastComplete = callEnded = false; callEpoch = 0; callUi = IntPtr.Zero; }
    private static void Warn(Exception ex)
    { if (Environment.TickCount64 < warningAt) return; warningAt = Environment.TickCount64 + 5000; Core.Log?.Warning("神秘人电话暂未完成：" + ex); }

    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.InitPhoneClientDict))]
    internal static class RegistrationPatch
    {
        private static void Postfix(ref Il2CppSystem.Collections.Generic.Dictionary<long, StorePhoneClient> __result)
        { try { Ensure(__result); } catch (Exception ex) { Warn(ex); } }
    }
    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.GetLocalizedDisplayName))]
    internal static class NamePatch
    { private static bool Prefix(StorePhoneClient __instance, ref string __result) { if (!IsOurs(__instance)) return true; __result = LanguageText.Get("jiangbai.contact.name"); return false; } }
    // Cooldown-ready night reports resolve locID directly through LocHelper.
    [HarmonyPatch(typeof(LocHelper), nameof(LocHelper.GetLocalizedClientName))]
    internal static class ReportNamePatch
    {
        private static bool Prefix(string key, ref string __result)
        {
            if (Core.Clean(key) != Id) return true;
            __result = LanguageText.Get("jiangbai.contact.name");
            return false;
        }
    }
    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.GetCallDialog))]
    internal static class DialogPatch
    { private static bool Prefix(StorePhoneClient __instance, ref Dialogue __result) { if (!IsOurs(__instance)) return true; __result = Dialog(__instance); return false; } }
    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.ShownInPhoneBook))]
    internal static class BookPatch
    { private static void Postfix(StorePhoneClient __instance, ref bool __result) { if (IsOurs(__instance)) { __result = Accessible(); if (__result) Sync(__instance); } } }
    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.IsPhoneClientAtStore))]
    internal static class AtStorePatch
    { private static bool Prefix(long number, ref bool __result) { if (number != PhoneNumber || !OwnNumber()) return true; __result = false; return false; } }
    [HarmonyPatch(typeof(PhoneUIManager), nameof(PhoneUIManager.WillAnswerCall))]
    internal static class AnswerPatch
    {
        private static bool Prefix(long number, ref bool __result)
        { if (number != PhoneNumber || !OwnNumber()) return true; if (Accessible()) { Sync(Ensure(owner!.PhoneClientDict)); return true; } __result = false; return false; }
    }
    [HarmonyPatch(typeof(PhoneUIManager), nameof(PhoneUIManager.StartPhoneDialog))]
    internal static class StartPatch
    { private static bool Prefix(long currentNumber) => currentNumber != PhoneNumber || !OwnNumber() || Accessible(); }
    [HarmonyPatch(typeof(DialoguePhoneUIManager), nameof(DialoguePhoneUIManager.OnCurrentTextDisplayed))]
    internal static class TextPatch
    { private static void Prefix(DialoguePhoneUIManager __instance) => TextCompleted(__instance); }
    [HarmonyPatch(typeof(DialoguePhoneUIManager), nameof(DialoguePhoneUIManager.EndConversation))]
    internal static class EndPatch
    {
        // Native phone end retains currentDialog and advances epoch. Its delayed
        // end action may run afterwards; capture only this fully displayed call.
        private static void Prefix(DialoguePhoneUIManager __instance, out Dialogue? __state)
        {
            __state = null; callEnded = false;
            if (firstComplete && lastComplete && last != null && __instance.Pointer == callUi &&
                __instance.currentDialog?.Pointer == last.Pointer && __instance.conversationEpoch == callEpoch)
                __state = last;
        }
        private static void Postfix(DialoguePhoneUIManager __instance, bool __runOriginal, Dialogue? __state)
        {
            if (__runOriginal && __state != null && firstComplete && lastComplete && last?.Pointer == __state.Pointer &&
                __instance.Pointer == callUi && __instance.currentDialog?.Pointer == __state.Pointer &&
                __instance.conversationEpoch == unchecked(callEpoch + 1)) callEnded = true;
        }
    }
    [HarmonyPatch(typeof(DialoguePhoneUIManager), nameof(DialoguePhoneUIManager.InterruptDialog))]
    internal static class InterruptPatch
    { private static void Prefix() => CancelCall(); }
    [HarmonyPatch(typeof(PhoneUIManager), nameof(PhoneUIManager.StopCall))]
    internal static class HangupPatch
    { private static void Prefix() => CancelCall(); }
}
