using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Run-bound hideout node. Ordinary merchant visits have a separate identity.</summary>
internal static class JiangbaiStory
{
    internal const string Id = "pcrepair.jiangbai_hideout";
    internal const string StoryTag = "PCREPAIR_JIANGBAI_HIDEOUT";
    private const string VisitPrefix = "PCREPAIR_JIANGBAI_HIDEOUT_VISIT_";
    private static PlayerStore? owner;
    private static string? run, raw, error, beforeWrite, afterWrite;
    private static bool present, beforePresent, publishing, transacting, reputationApplied, reputationLossNoticePending;
    private static double reputationResult;
    private static int slot, openedDay = -1;
    private static long retryAt, warningAt;
    private static JiangbaiStoryState.Document? document;
    private static Dialogue? farewellLine;
    private static bool farewellDisplayed, farewellEnded;
    private static IntPtr farewellUi;
    private static int farewellEpoch;
    internal static bool Ready
    { get { try { return document != null && error == null && !publishing && afterWrite == null && SameOwner(); } catch { return false; } } }
    internal static bool WorkPending => publishing || afterWrite != null || transacting ||
        document != null && (document.Visit.Length != 0 || document.Phase is "accepting" or "settling");
    internal static bool ContactUnlocked => Ready && document!.Phase == "success";
    internal static string Phase => Ready ? document!.Phase : "";
    internal static bool Active => Ready && (document!.Visit.Length != 0 || document.Phase is "accepting" or "holding" or "settling");
    internal static bool ResidentVisible => Ready && JiangbaiStoryState.ShowResident(document!);
    internal static bool IsStory(StoreClient? client) => client != null && (client.IsTag(StoryTag) || Core.Clean(client.identifier) == Id);
    internal static string Text(string key) => LanguageText.Get("jiangbai.story." + key);

    internal static bool AllowsOrdinary(PlayerStore store)
    {
        try { return Bind(store) && Ready && !JiangbaiStoryState.SuppressesMerchant(document!); }
        catch (Exception ex) { Warn(ex); return false; }
    }

    internal static void OnWorldChanged(PlayerStore store) { retryAt = 0; Update(store); }

    internal static void Update(PlayerStore store)
    {
        try
        {
            ResolveWrite();
            if (publishing || afterWrite != null || transacting || !Bind(store) || Environment.TickCount64 < retryAt || !ValidateRaw()) return;
            retryAt = Environment.TickCount64 + 650;
            if (document!.Phase is "accepting" or "settling") ResumeReputation();
            if (!Ready) return;
            SuppressOrdinaryQueue(store);
            RemoveDuplicateQueuedVisits(store);
            var current = store.isClientArrived ? store.currentClientInstance?.storeClient : null;
            if (HasExactVisit(current, document!.Visit) && !document.Arrived)
            {
                Change(next => { next.Arrived = true; if (next.VisitKind == "farewell") next.FarewellStarted = true; });
                ConfigureDialogue(current!);
            }
            if (document.Visit.Length != 0 && !VisitExists(store, document.Visit) &&
                (document.Arrived || document.LastVisitDay < StoreStation.GetDayCounter()))
                Change(next => { next.Visit = next.VisitKind = ""; next.Arrived = false; });
            if (openedDay == StoreStation.GetDayCounter()) Arrange(store, openedDay);
        }
        catch (Exception ex) { Warn(ex); }
    }

    internal static void OnShutterOpened(PlayerStore store)
    {
        Update(store); openedDay = StoreStation.GetDayCounter(); retryAt = 0; Update(store);
        TryGiveCard();
    }

    internal static void AfterLoad(PlayerStore store)
    {
        try
        {
            if (!Bind(store) || !Ready) return;
            var stories = new List<StoreClient>(); var seen = new HashSet<IntPtr>();
            void Add(StoreClient? client) { if (IsStory(client) && seen.Add(client!.Pointer)) stories.Add(client); }
            Add(store.currentClientInstance?.storeClient);
            var queue = store.storeClientManager?.clientStack;
            if (queue != null) foreach (var client in queue) Add(client);
            if (stories.Count != 0 && document!.Visit.Length == 0) throw new InvalidOperationException("江白访客缺少访问凭据。");
            if (stories.Count > 1 && stories.Any(client => !HasExactVisit(client, document!.Visit)))
                throw new InvalidOperationException("江白读档访客身份冲突。");
            foreach (var client in stories)
            {
                var tags = new List<string>();
                if (client.tag != null) foreach (var tag in client.tag)
                    if (Core.Clean(tag).StartsWith(VisitPrefix, StringComparison.Ordinal)) tags.Add(Core.Clean(tag));
                if (tags.Count > 1 || tags.Count == 1 && tags[0] != VisitPrefix + document!.Visit)
                    throw new InvalidOperationException("江白访问凭据不一致。");
                if (tags.Count == 0) client.AddTag(VisitPrefix + document!.Visit);
                client.AddTag(StoryTag); ConfigureDialogue(client);
            }
            RemoveDuplicateQueuedVisits(store); SuppressOrdinaryQueue(store);
            TryGiveCard();
        }
        catch (Exception ex) { error = "invalid_loaded_visitor"; Warn(ex); }
    }

    internal static bool BeforeSave()
    {
        NpcBusiness.Resume();
        ResolveWrite();
        return !transacting && !publishing && afterWrite == null && (owner == null || !SameOwner() ||
            error == null && (document == null || MatchesRaw() && document.Phase is not ("accepting" or "settling")));
    }
    internal static bool AllowSessionChange()
    { return BeforeSave(); }
    internal static void ResetSession()
    {
        if (!AllowSessionChange()) throw new InvalidOperationException("江白剧情结算未完成，不能切换会话。");
        owner = null; document = null; run = raw = error = beforeWrite = afterWrite = null;
        present = beforePresent = reputationApplied = transacting = reputationLossNoticePending = false;
        slot = 0; openedDay = -1; retryAt = warningAt = 0; farewellLine = null;
        farewellDisplayed = farewellEnded = false; farewellUi = IntPtr.Zero;
    }

    private static void Arrange(PlayerStore store, int day)
    {
        if (!Ready || document!.Phase is not ("new" or "holding")) return;
        if (document.Phase == "new" && document.LastVisitDay < 1 && document.Visit.Length == 0 && !NpcStoryProgress.JiangbaiEligible(store)) return;
        if (document.Phase == "holding" && day < document.DueDay) return;
        if (store.storeClientManager?.clientStack == null) return;
        if (document.Visit.Length == 0)
        {
            if (document.LastVisitDay == day) return;
            Change(next => { next.Visit = Guid.NewGuid().ToString("N"); next.VisitKind = next.Phase == "new" ? "offer" : "farewell"; next.Arrived = false; next.LastVisitDay = day; });
        }
        if (VisitExists(store, document.Visit)) { Prioritize(store); return; }
        if (document.Arrived) return;
        store.storeClientManager.AddNextClient(CreateClient());
        if (!SameOwner() || !VisitExists(store, document.Visit)) throw new InvalidOperationException("江白入队未确认，保留同一凭据重试。");
        Prioritize(store);
    }

    internal static void Prioritize(PlayerStore store)
    {
        var queue = store.storeClientManager?.clientStack;
        if (queue == null) return;
        LuoxiStory.Prioritize(store);
        var target = queue.Count != 0 && LuoxiStory.IsStory(queue[0]) ? 1 : 0;
        for (var i = 0; i < queue.Count; i++)
            if (IsStory(queue[i])) { var client = queue[i]; if (i != target) { queue.RemoveAt(i); queue.Insert(target, client); } break; }
        // A single final ordering: Luoxi, Jiangbai, then the existing delivery queue.
    }

    private static void SuppressOrdinaryQueue(PlayerStore store)
    {
        if (!Ready || !JiangbaiStoryState.SuppressesMerchant(document!)) return;
        var queue = store.storeClientManager?.clientStack;
        if (queue != null) for (var i = queue.Count - 1; i >= 0; i--)
            if (SecuritySeizureMerchant.IsMerchant(queue[i])) queue.RemoveAt(i);
    }

    internal static StoreClient CreateClient()
    {
        var client = StoreClientList.CreateScavGeneral() ?? throw new InvalidOperationException("江白剧情模板不可用。");
        var portrait = SpriteDict.Instance?.GetSprite("wanted3");
        if (portrait != null) SpriteAssets.SetPortraitReference(SecuritySeizureMerchant.SpriteKey, portrait);
        client.identifier = Id; client.AddTag(StoryTag);
        client.displayName = LanguageText.Get("security.merchant.name"); client.realName = client.displayName;
        client.spriteName = SecuritySeizureMerchant.SpriteKey; client.clientFaction = StoreClient.FACTION_SECURITY;
        client.AddBasicDialog(); client.isIntroduced = false;
        if (Ready && document!.Visit.Length != 0) client.AddTag(VisitPrefix + document.Visit);
        ConfigureDialogue(client); return client;
    }

    private static Dialogue Line(StoreClient client, string key) => new Dialogue().SetText(client.displayName, Text(key));
    private static Il2CppSystem.Action Command(StoreClient client, Action action)
    {
        var boundOwner = owner?.Pointer ?? IntPtr.Zero; var boundRun = run; var boundSlot = slot; var boundVisit = document?.Visit;
        return (Il2CppSystem.Action)(Action)(() =>
        {
            try
            {
                var store = PlayerStore.Instance;
                if (!ValidateRaw() || transacting || store == null || store.Pointer != boundOwner || store.runID != boundRun || store.saveSlotId != boundSlot ||
                    !store.isClientArrived || store.currentClientInstance?.storeClient?.Pointer != client.Pointer ||
                    document!.Visit != boundVisit || !HasExactVisit(client, boundVisit ?? "")) return;
                action();
            }
            catch (Exception ex) { Warn(ex); }
            finally { NpcBusiness.Resume(); }
        });
    }

    private static void Choice(Dialogue line, StoreClient client, string key, Action action)
    {
        var response = Line(client, key == "accept" ? "accepted" : "refused");
        response.SetStartAction(Command(client, action));
        response.SetEndAction(Command(client, Leave));
        var selected = new Dialogue().SetText(DialogUIManager.PLAYER, Text(key)); selected.SetNextDialogue(response);
        line.AddChoice(ItemDirectory.CreateEmptyItem("pcrepair_jiangbai_choice"), Text(key), "", selected, null);
    }
    private static void Leave() => DialogUIManager.Instance?.CurrentClientLeave(.5f);
    private static void ConfigureDialogue(StoreClient client)
    {
        farewellLine = null; farewellDisplayed = farewellEnded = false; farewellUi = IntPtr.Zero;
        Dialogue first;
        if (!Ready) first = Line(client, "recovery");
        else if (document!.Phase == "new")
        {
            first = Line(client, "offer_1"); var second = Line(client, "offer_2"); var third = Line(client, "offer_3");
            first.SetNextDialogue(second); second.SetNextDialogue(third);
            Choice(third, client, "accept", Accept);
            Choice(third, client, "refuse", () => { if (document!.Phase != "new") return; Change(next => next.Phase = "refused"); SuppressOrdinaryQueue(owner!); });
        }
        else if (document.Phase == "holding" && document.VisitKind == "farewell")
        {
            first = Line(client, "farewell_1"); var second = Line(client, "farewell_2"); var last = Line(client, "farewell_3");
            first.SetNextDialogue(second); second.SetNextDialogue(last);
            farewellLine = last; farewellDisplayed = false;
            last.SetEndAction(Command(client, () =>
            {
                var ui = DialogUIManager.Instance;
                if (!farewellDisplayed || farewellLine?.Pointer != last.Pointer || ui == null || ui.Pointer != farewellUi ||
                    document!.Phase != "holding") return;
                // Manual continuation calls the action while the line is current.
                // Native terminal auto-end clears it first; accept only the receipt
                // captured around that exact EndConversation, never an arbitrary null UI.
                var current = ui.currentDialog?.Pointer == last.Pointer && ui.conversationEpoch == farewellEpoch;
                var ended = farewellEnded && ui.currentDialog == null && ui.conversationEpoch == unchecked(farewellEpoch + 1);
                if (!current && !ended) return;
                Complete(); if (ContactUnlocked) Leave();
            }));
        }
        else { first = Line(client, document.Phase == "refused" ? "refused" : document.Phase == "success" ? "farewell_1" : "accepted"); first.SetEndAction(Command(client, Leave)); }
        first.isMainDialog = true; client.mainDialogue = first; client.repeatDialogue = first; client.isMainDialogueStarted = false;
    }

    private static void Accept()
    {
        if (!ValidateRaw() || document!.Phase != "new" || WorkroomStoryTransfer.Pending) return;
        var before = StoreReputation.GetSecFaction().GetReputationExact(); var target = JiangbaiStoryState.AcceptanceTarget(before);
        var day = StoreStation.GetDayCounter();
        reputationApplied = false;
        Change(next => { next.Phase = "accepting"; next.ReputationBefore = before; next.ReputationTarget = target; next.AcceptedDay = day; next.DueDay = checked(day + 7); });
        ResumeReputation(); SuppressOrdinaryQueue(owner!);
    }
    private static void Complete()
    {
        if (!ValidateRaw() || document!.Phase != "holding" || !document.FarewellStarted || WorkroomStoryTransfer.Pending) return;
        var reward = CaptureCard(); var before = StoreReputation.GetSecFaction().GetReputationExact();
        if (!double.IsFinite(before)) throw new InvalidOperationException("治安部声望无效。");
        reputationApplied = false;
        Change(next => { next.Phase = "settling"; next.ReputationBefore = before; next.ReputationTarget = before + next.Deducted + 20; next.Reward = reward; });
        ResumeReputation(); TryGiveCard();
    }
    private static void ResumeReputation()
    {
        if (!ValidateRaw() || transacting || document!.Phase is not ("accepting" or "settling")) return;
        transacting = true;
        try
        {
            if (!reputationApplied)
            {
                var faction = StoreReputation.GetSecFaction() ?? throw new InvalidOperationException("治安部声望未就绪。");
                // Target-relative replay never adds the refund twice after an exception.
                // Raw preserves exact doubles, avoids perks, and retains native caps.
                var delta = document.ReputationTarget - faction.GetReputationExact();
                // Already below -90 means no mutation, including no native
                // normalization of a value supplied by another mod.
                if (delta != 0) faction.ModReputationRaw(delta);
                reputationResult = faction.GetReputationExact();
                if (!double.IsFinite(reputationResult)) throw new InvalidOperationException("治安部声望回读无效。");
                reputationApplied = true;
            }
            reputationLossNoticePending = document.Phase == "accepting" && document.ReputationBefore > reputationResult;
            Change(next =>
            {
                if (next.Phase == "accepting") { next.Deducted = Math.Max(0, next.ReputationBefore - reputationResult); next.Phase = "holding"; }
                else { next.Phase = "success"; next.RewardPending = true; }
            });
            reputationApplied = false;
            NotifyReputationLoss();
        }
        finally { transacting = false; }
    }
    private static void NotifyReputationLoss()
    {
        if (!reputationLossNoticePending || !Ready || document!.Phase != "holding" || document.Deducted <= 0) return;
        // Consume before native UI callbacks; a failed notice must not replay settlement.
        reputationLossNoticePending = false;
        try { ReputationUIManager.Instance?.NotifyReputation(StoreClient.FACTION_SECURITY, -1); }
        catch (Exception ex) { Core.Log?.Warning("江白入住声望提示显示失败：" + ex.Message); }
    }
    private static WorkroomItemCodec.Snapshot CaptureCard()
    {
        var item = MysteriousContactCard.Create(); WorkroomItemCodec.Candidate? candidate = null;
        try
        {
            GeneralHelper.SetItemOwned(item, true);
            var snapshot = WorkroomItemCodec.Capture(item, out var nodes);
            candidate = new WorkroomItemCodec.Candidate { Root = item, Nodes = nodes }; return snapshot;
        }
        finally { WorkroomItemCodec.Destroy(candidate ?? new WorkroomItemCodec.Candidate { Root = item, Nodes = new List<GameItem> { item } }); }
    }
    private static void TryGiveCard()
    {
        if (!ValidateRaw() || document!.Phase != "success" || !document.RewardPending || document.Reward == null) return;
        // Normal grant uses the existing single-item custody transaction. No new
        // full-counter dialogue branch; a receipt survives an interrupted grant.
        WorkroomStoryTransfer.TryGrantStoryBatch(new[] { document.Reward }, () =>
        { if (Ready && document!.RewardPending) Change(next => next.RewardPending = false); }, out _);
    }

    private static bool Bind(PlayerStore store)
    {
        if (!SaveGeneration.IsSupported(store) || !PlayerStore.IsInstanceExist() ||
            PlayerStore.instance == null || PlayerStore.instance.Pointer != store.Pointer) return false;
        if (SameOwner()) return true;
        document = null; reputationLossNoticePending = false;
        owner = store; run = store.runID; slot = store.saveSlotId;
        present = HasKey(store); raw = Read(store); error = null;
        try { document = present ? JiangbaiStoryState.Parse(raw!, run, slot) : JiangbaiStoryState.New(run, slot); }
        catch { error = "invalid_data"; throw; }
        if (!present) Publish(document);
        openedDay = -1; retryAt = 0;
        return true;
    }


    private static bool VisitExists(PlayerStore store, string token)
    {
        bool Match(StoreClient? client) => IsStory(client) && client!.IsTag(VisitPrefix + token);
        // Native GetNextClient removes the queue entry before the arrival
        // animation. The current instance owns that visit until it is cleared,
        // including entry and exit; only Update's arrival gate advances phases.
        if (Match(store.currentClientInstance?.storeClient)) return true;
        var queue = store.storeClientManager?.clientStack;
        if (queue != null) foreach (var client in queue) if (Match(client)) return true;
        return false;
    }
    private static bool HasExactVisit(StoreClient? client, string token)
    {
        if (token.Length == 0 || !IsStory(client) || client!.tag == null) return false;
        var count = 0;
        foreach (var rawTag in client.tag)
        {
            var tag = Core.Clean(rawTag);
            if (!tag.StartsWith(VisitPrefix, StringComparison.Ordinal)) continue;
            if (tag != VisitPrefix + token) return false;
            count++;
        }
        return count == 1;
    }

    private static void RemoveDuplicateQueuedVisits(PlayerStore store)
    {
        var token = document?.Visit;
        var queue = store.storeClientManager?.clientStack;
        if (string.IsNullOrEmpty(token) || queue == null) return;
        // Keep the active visitor, otherwise the first queued one. Never remove
        // the current instance, ordinary stock visits, or ambiguous identities.
        var retained = HasExactVisit(store.currentClientInstance?.storeClient, token!);
        for (var i = 0; i < queue.Count;)
        {
            if (!HasExactVisit(queue[i], token!)) { i++; continue; }
            if (retained) queue.RemoveAt(i);
            else { retained = true; i++; }
        }
    }

    private static void Change(Action<JiangbaiStoryState.Document> change)
    {
        if (!Ready) throw new InvalidOperationException("江白剧情未准备好。");
        var next = JiangbaiStoryState.Clone(document!); change(next); next.Revision = checked(next.Revision + 1);
        Publish(next);
    }
    private static void Publish(JiangbaiStoryState.Document next)
    {
        if (owner == null || !SameOwner() || publishing || afterWrite != null || !MatchesRaw())
            throw new InvalidOperationException("江白剧情写入前会话或原文变化。");
        var after = JiangbaiStoryState.Serialize(next);
        var parsed = JiangbaiStoryState.Parse(after, run!, slot);
        var previousRaw = raw;
        beforeWrite = raw; beforePresent = present; afterWrite = after; publishing = true;
        try
        {
            Exception? failure = null;
            try { owner.modData ??= new Il2CppSystem.Collections.Generic.Dictionary<string, string>(); owner.modData[JiangbaiStoryState.SaveKey] = after; }
            catch (Exception ex) { failure = ex; }
            if (!SameOwner()) throw new InvalidOperationException("江白剧情写入时会话变化。", failure);
            var now = Read(owner); var exists = HasKey(owner);
            if (exists && now == after)
            { raw = after; present = true; document = parsed; afterWrite = beforeWrite = null; error = null; return; }
            if (exists == beforePresent && now == beforeWrite)
            { afterWrite = beforeWrite = null; throw new InvalidOperationException("江白剧情写入未生效。", failure); }
            error = "unknown_publication";
            throw new InvalidOperationException("江白剧情写入结果未知，保留前后凭据。", failure);
        }
        finally { publishing = false; if (raw != previousRaw) BusinessRuntime.Wake(); }
    }
    private static void ResolveWrite()
    {
        if (publishing || afterWrite == null || owner == null || !SameOwner()) return;
        try
        {
            var now = Read(owner); var exists = HasKey(owner);
            if (exists && now == afterWrite || exists == beforePresent && now == beforeWrite)
            {
                document = exists ? JiangbaiStoryState.Parse(now!, run!, slot) : JiangbaiStoryState.New(run!, slot);
                raw = now; present = exists; afterWrite = beforeWrite = error = null;
                NotifyReputationLoss();
                BusinessRuntime.Wake();
            }
        }
        catch { /* Keep the exact proof until an unambiguous read is possible. */ }
    }
    private static bool SameOwner() => owner != null && PlayerStore.IsInstanceExist() && PlayerStore.instance != null &&
        owner.Pointer == PlayerStore.instance.Pointer && owner.runID == run && owner.saveSlotId == slot && SaveGeneration.IsSupported(owner);
    private static bool HasKey(PlayerStore store) => store.modData != null && store.modData.ContainsKey(JiangbaiStoryState.SaveKey);
    private static string? Read(PlayerStore store) => HasKey(store) ? store.modData[JiangbaiStoryState.SaveKey] : null;
    internal static bool ValidateRaw() { try { return Ready && MatchesRaw(); } catch { return false; } }
    private static bool MatchesRaw() => owner != null && HasKey(owner) == present && Read(owner) == raw;
    private static void Warn(Exception ex)
    { if (Environment.TickCount64 < warningAt) return; warningAt = Environment.TickCount64 + 5000; Core.Log?.Warning("江白剧情暂未完成，保留当前状态：" + ex); }



    [HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.CreateStoreClient))]
    internal static class RestorePatch
    {
        private static bool Prefix(string identifier, ref StoreClient __result)
        { if (Core.Clean(identifier) != Id) return true; __result = CreateClient(); return false; }
    }
    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.LoadGame))]
    internal static class LoadPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(PlayerStore __instance, bool __runOriginal)
        { if (__runOriginal) { AfterLoad(__instance); MysteriousContact.AfterLoad(__instance); } }
    }
    [HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.OnNextClientArrived))]
    internal static class ArrivalPatch
    {
        private static void Postfix()
        { var store = PlayerStore.Instance; if (store != null) { retryAt = 0; Update(store); } }
    }
    [HarmonyPatch(typeof(DialogUIManager), "OnCurrentTextDisplayed")]
    internal static class DisplayedPatch
    {
        private static void Prefix(DialogUIManager __instance)
        {
            if (farewellLine != null && __instance.currentDialog?.Pointer == farewellLine.Pointer && Ready &&
                IsStory(PlayerStore.Instance?.currentClientInstance?.storeClient))
            { farewellDisplayed = true; farewellEnded = false; farewellUi = __instance.Pointer; farewellEpoch = __instance.conversationEpoch; }
        }
    }
    [HarmonyPatch(typeof(DialogUIManager), "EndConversation")]
    internal static class EndConversationPatch
    {
        private static void Prefix(DialogUIManager __instance, out Dialogue? __state)
        {
            __state = null;
            farewellEnded = false;
            if (farewellDisplayed && farewellLine != null && __instance.Pointer == farewellUi &&
                __instance.currentDialog?.Pointer == farewellLine.Pointer && __instance.conversationEpoch == farewellEpoch)
                __state = farewellLine;
        }
        private static void Postfix(DialogUIManager __instance, Dialogue? __state, bool __runOriginal)
        {
            if (__runOriginal && __state != null && farewellDisplayed && farewellLine?.Pointer == __state.Pointer &&
                __instance.Pointer == farewellUi && __instance.currentDialog == null &&
                __instance.conversationEpoch == unchecked(farewellEpoch + 1))
                farewellEnded = true;
        }
    }
    [HarmonyPatch(typeof(DialogUIManager), "InterruptDialog")]
    internal static class InterruptPatch
    { private static void Prefix() { farewellDisplayed = farewellEnded = false; } }
    [HarmonyPatch(typeof(LocHelper), nameof(LocHelper.GetLocalizedClientName))]
    internal static class NamePatch
    {
        private static bool Prefix(string key, ref string __result)
        { if (Core.Clean(key) != Id) return true; __result = LanguageText.Get("security.merchant.name"); return false; }
    }
}
