using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>One custody node, independent of the phone card and its cooldown.</summary>
internal static class LuoxiStory
{
    internal const string Id = "pcrepair.luoxi_story";
    internal const string StoryTag = "PCREPAIR_LUOXI_STORY";
    private const string VisitPrefix = "PCREPAIR_LUOXI_STORY_VISIT_";
    private static PlayerStore? owner;
    private static string? run, raw, error, beforeWrite, afterWrite;
    private static bool present, beforePresent, publishing;
    private static int slot, openedDay = -1;
    private static long retryAt, warningAt;
    private static Dialogue? responseTarget;
    private static LuoxiStoryState.Document? document;
    internal static bool Ready
    { get { try { return document != null && error == null && !publishing && afterWrite == null && SameOwner(); } catch { return false; } } }
    internal static bool WorkPending => publishing || afterWrite != null || document?.Visit.Length > 0;
    internal static bool Unlocked => Ready && document!.Phase == "success";
    internal static string Phase => Ready ? document!.Phase : "";
    internal static bool Active => Ready && (document!.Visit.Length != 0 || document.Phase is "delivery_pending" or "holding" or "first" or "grace" or "final");
    internal static bool IsStory(StoreClient? client) => client != null && (client.IsTag(StoryTag) || Core.Clean(client.identifier) == Id);
    internal static string Text(string key, params object[] args) => LanguageText.Get("luoxi.story." + key, args);

    internal static void OnWorldChanged(PlayerStore store) { retryAt = 0; Update(store); }

    internal static void Update(PlayerStore store)
    {
        try
        {
            ResolveWrite();
            if (afterWrite != null || publishing || !Bind(store)) return;
            if (Environment.TickCount64 < retryAt || !ValidateRaw() || WorkroomStoryTransfer.Pending) return;
            retryAt = Environment.TickCount64 + 650;
            RemoveDuplicateQueuedVisits(store);
            var current = store.isClientArrived ? store.currentClientInstance?.storeClient : null;
            if (IsStory(current) && document!.Visit.Length != 0 && current!.IsTag(VisitPrefix + document.Visit))
            {
                if (!document.Arrived)
                {
                    Change(next =>
                    {
                        next.Arrived = true;
                        if (next.VisitKind == "first" && next.Phase == "holding") next.Phase = "first";
                        if (next.VisitKind == "final" && next.Phase == "grace") next.Phase = "final";
                    });
                    ConfigureStoryDialogue(current);
                }
            }
            if (document!.Arrived && !VisitExists(store, document.Visit)) FinishVisit();
            if (openedDay == StoreStation.GetDayCounter()) Arrange(store, openedDay);
        }
        catch (Exception ex) { Warn(ex); }
    }

    private static bool Bind(PlayerStore store)
    {
        if (!SaveGeneration.IsSupported(store) || !PlayerStore.IsInstanceExist() ||
            PlayerStore.instance == null || PlayerStore.instance.Pointer != store.Pointer) return false;
        if (SameOwner()) return true;
        document = null;
        owner = store; run = store.runID; slot = store.saveSlotId;
        present = HasKey(store); raw = Read(store); error = null;
        try { document = present ? LuoxiStoryState.Parse(raw!, run, slot) : LuoxiStoryState.New(run, slot); }
        catch { error = "invalid_data"; throw; }
        if (!present) Publish(document);
        openedDay = -1; retryAt = 0;
        return true;
    }

    // Native blueprint factories can run before the story record is bound.
    // Rebuild their runtime dialogue delegates after the complete native load,
    // before Update is allowed to interpret a missing visitor as a departure.
    internal static void AfterLoad(PlayerStore store)
    {
        try
        {
            if (!Bind(store) || !Ready) return;
            var clients = new List<StoreClient>(); var seen = new HashSet<IntPtr>();
            void Add(StoreClient? client)
            { if (PhoneAssemblerNpc.IsPhoneAssembler(client) || IsStory(client)) if (seen.Add(client!.Pointer)) clients.Add(client); }
            Add(store.currentClientInstance?.storeClient);
            var queue = store.storeClientManager?.clientStack;
            if (queue != null) foreach (var client in queue) Add(client);
            var stories = clients.Where(IsStory).ToArray();
            if (document!.Visit.Length != 0)
            {
                // Only duplicates carrying the exact saved visit can be merged.
                // Untagged or conflicting visitors must still stop restoration.
                if (stories.Length > 1 && stories.Any(client => !HasExactVisit(client, document.Visit)))
                    throw new InvalidOperationException("读档中剧情访客不唯一，停止自动认领访问。");
                foreach (var client in stories)
                {
                    var visits = new List<string>();
                    if (client.tag != null)
                        foreach (var rawTag in client.tag)
                        {
                            var tag = Core.Clean(rawTag);
                            if (tag.StartsWith(VisitPrefix, StringComparison.Ordinal)) visits.Add(tag.Substring(VisitPrefix.Length));
                        }
                    if (visits.Count > 1 || visits.Count == 1 && visits[0] != document.Visit)
                        throw new InvalidOperationException("读档中的剧情访问标签与当前存档凭据冲突。");
                    // The independent native identifier identifies this story
                    // visitor even if its blueprint was constructed unbound.
                    if (visits.Count == 0) client.AddTag(VisitPrefix + document.Visit);
                    if (!client.IsTag(StoryTag)) client.AddTag(StoryTag);
                    if (!client.IsTag(PhoneAssemblerNpc.ClientTag)) client.AddTag(PhoneAssemblerNpc.ClientTag);
                }
            }
            RemoveDuplicateQueuedVisits(store);
            foreach (var client in clients)
                if (IsStory(client)) ConfigureStoryDialogue(client);
                else ConfigureOrdinary(client);
        }
        catch (Exception ex) { error = "invalid_loaded_visitor"; Warn(ex); }
    }

    internal static void OnShutterOpened(PlayerStore store)
    { Update(store); openedDay = StoreStation.GetDayCounter(); retryAt = 0; Update(store); }

    internal static bool BeforeSave()
    {
        NpcBusiness.Resume();
        ResolveWrite();
        return !publishing && afterWrite == null && (owner == null || !SameOwner() || error == null && (document == null || MatchesRaw()));
    }
    internal static bool AllowSessionChange() => BeforeSave();
    internal static void ResetSession()
    {
        if (!AllowSessionChange()) throw new InvalidOperationException("洛夕剧情发布未确认，不能切换会话。");
        owner = null; document = null; run = raw = error = beforeWrite = afterWrite = null;
        present = beforePresent = false; slot = 0; openedDay = -1; retryAt = warningAt = 0; responseTarget = null;
    }

    private static void Arrange(PlayerStore store, int day)
    {
        if (!Ready || WorkroomStoryTransfer.Pending) return;
        var queue = store.storeClientManager?.clientStack;
        if (queue == null) return;
        var value = document!;
        if (value.Phase is "success" or "failed" or "refused") return;
        string kind;
        if (value.Phase == "new" && value.LastVisitDay < 1 && value.Visit.Length == 0 && !NpcStoryProgress.LuoxiEligible(store)) return;
        if (value.Phase is "new" or "delivery_pending") kind = "offer";
        else if (value.Phase is "holding" or "first" && day >= value.DueDay) kind = "first";
        else if (value.Phase is "grace" or "final" && day >= value.DueDay) kind = "final";
        else return;
        if (value.Visit.Length == 0)
        {
            if (value.LastVisitDay == day) return;
            Change(next => { next.Visit = Guid.NewGuid().ToString("N"); next.VisitKind = kind; next.Arrived = false; next.LastVisitDay = day; });
        }
        if (VisitExists(store, document!.Visit)) { Prioritize(store); return; }
        // An arrived visitor missing from both native locations is handled by
        // FinishVisit; queue insertion failure never counts as a failed visit.
        if (document.Arrived) return;
        var visitor = CreateClient();
        store.storeClientManager!.AddNextClient(visitor);
        if (!SameOwner() || StoreStation.GetDayCounter() != day) throw new InvalidOperationException("剧情来访入队时会话变化。");
        if (!VisitExists(store, document.Visit)) throw new InvalidOperationException("剧情访客尚未确认入队，保留同一访问凭据重试。");
        Prioritize(store);
    }

    internal static void Prioritize(PlayerStore store)
    {
        var queue = store.storeClientManager?.clientStack;
        if (queue == null) return;
        for (var i = 0; i < queue.Count; i++)
            if (IsStory(queue[i])) { if (i != 0) { var visitor = queue[i]; queue.RemoveAt(i); queue.Insert(0, visitor); } return; }
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
    private static void FinishVisit()
    {
        if (WorkroomStoryTransfer.Pending) return;
        Change(next =>
        {
            if (next.Phase == "first") { next.Phase = "grace"; next.DueDay = checked(StoreStation.GetDayCounter() + 2); }
            else if (next.Phase == "final") next.Phase = "failed";
            next.Visit = next.VisitKind = ""; next.Arrived = false;
        });
    }

    internal static StoreClient CreateClient()
    {
        var client = PhoneAssemblerNpc.CreateClient();
        client.identifier = Id; client.AddTag(StoryTag);
        if (Ready && document!.Visit.Length != 0) client.AddTag(VisitPrefix + document.Visit);
        ConfigureStoryDialogue(client);
        return client;
    }

    internal static void OnArrival(PlayerStore store, StoreClient client)
    {
        Update(store);
        if (!IsStory(client)) return;
        ConfigureStoryDialogue(client);
    }

    private static Dialogue Line(StoreClient client, string key) => new Dialogue().SetText(client.displayName, Text(key));
    private static Il2CppSystem.Action ActionFor(StoreClient client, Dialogue response, Action action)
    {
        var boundOwner = owner?.Pointer ?? IntPtr.Zero; var boundRun = run; var boundSlot = slot;
        var boundVisit = document?.Visit; var boundPhase = document?.Phase;
        return (Il2CppSystem.Action)(Action)(() =>
        {
            try
            {
                var store = PlayerStore.Instance;
                var current = store?.isClientArrived == true ? store.currentClientInstance?.storeClient : null;
                if (store == null || store.Pointer != boundOwner || store.runID != boundRun || store.saveSlotId != boundSlot ||
                    !ValidateRaw() || current == null || current.Pointer != client.Pointer || WorkroomStoryTransfer.Pending) return;
                Update(store);
                if (IsStory(client) && (document!.Visit != boundVisit || !client.IsTag(VisitPrefix + boundVisit))) return;
                var phase = document!.Phase;
                if (phase != boundPhase && !(boundPhase == "holding" && phase == "first") && !(boundPhase == "grace" && phase == "final")) return;
                responseTarget = response;
                action();
            }
            catch (Exception ex) { Warn(ex); ModOperationNotice.Show(Text("recovery")); }
            finally { responseTarget = null; NpcBusiness.Resume(); }
        });
    }
    private static void Choice(Dialogue line, StoreClient client, string key, Action action)
    {
        // Native AddChoice ignores a null choice item. SelectChoice also reads
        // its current dialogue again after choiceAction, so commands run on the
        // response start rather than replacing currentDialog inside that action.
        var response = Line(client, "recovery");
        response.SetStartAction(ActionFor(client, response, action));
        var selected = new Dialogue().SetText(DialogUIManager.PLAYER, Text(key));
        selected.SetNextDialogue(response);
        line.AddChoice(ItemDirectory.CreateEmptyItem("pcrepair_luoxi_choice"), Text(key), "", selected, null);
    }

    internal static void ConfigureStoryDialogue(StoreClient client)
    {
        Dialogue first;
        if (!Ready) first = Line(client, "recovery");
        else if (document!.Phase == "new")
        {
            first = Line(client, "offer_1");
            var second = Line(client, "offer_2"); first.SetNextDialogue(second);
            Choice(second, client, "accept", () => Accept(client));
            Choice(second, client, "refuse", () => { Change(next => next.Phase = "refused"); Display(client, "refused"); ConfigureStoryDialogue(client); });
        }
        else if (document.Phase == "delivery_pending")
        {
            first = Line(client, "counter_full");
            Choice(first, client, "receive", () => Accept(client));
        }
        else if (document.Phase is "first" or "final" || document.Phase == "holding" && document.VisitKind == "first" || document.Phase == "grace" && document.VisitKind == "final")
        {
            first = Line(client, document.Phase is "final" or "grace" ? "final" : "first");
            Choice(first, client, "return", () => Return(client));
            Choice(first, client, "organize", () => Display(client, "organize_reply"));
            Choice(first, client, "not_ready", () => NotReady(client));
        }
        else if (document.Phase is "holding" or "grace") first = Line(client, document.Phase == "grace" ? "first_missing" : "keeping");
        else if (document.Phase == "success")
        {
            first = Line(client, "complete_1"); var second = Line(client, "complete_2"); first.SetNextDialogue(second);
            if (document.RewardPending) Choice(second, client, "reward", () => ClaimReward(client));
        }
        else first = Line(client, document.Phase == "refused" ? "refused" : "failed_2");
        first.isMainDialog = true; client.mainDialogue = first; client.repeatDialogue = first; client.isMainDialogueStarted = false;
    }

    internal static void ConfigureOrdinary(StoreClient client)
    {
        ApplyDiscount(client);
        if (!Ready || !document!.RewardPending || client.mainDialogue == null) return;
        var last = client.mainDialogue;
        while (last.nextDialogue != null) last = last.nextDialogue;
        if (last.HasChoices()) return;
        Choice(last, client, "reward", () => ClaimReward(client));
        client.repeatDialogue = last;
    }

    private static void Accept(StoreClient client)
    {
        if (!ValidateRaw() || document!.Phase is not ("new" or "delivery_pending")) return;
        if (document.Phase == "new")
        {
            var goods = LuoxiStoryGoods.Identifiers.Select(identifier => LuoxiStoryGoods.Create(identifier, true)).ToList();
            Change(next => { next.Goods = goods; next.Phase = "delivery_pending"; });
        }
        // The user reduced this node to five goods. A saved, not-yet-granted
        // six-item invitation may shrink only when none of its identities is owned.
        if (document!.Goods.Count == LuoxiStoryGoods.Identifiers.Length + 1 &&
            WorkroomStoryTransfer.CanReduceUnissuedBatch(document.Goods))
            Change(next => next.Goods.RemoveAt(next.Goods.Count - 1));
        var acceptedDay = StoreStation.GetDayCounter();
        var received = WorkroomStoryTransfer.TryGrantStoryBatch(document!.Goods, () =>
        {
            if (document!.Phase != "delivery_pending") return;
            Change(next => { next.Phase = "holding"; next.AcceptedDay = acceptedDay; next.DueDay = checked(next.AcceptedDay + 3); });
        }, out var reason);
        if (received) Display(client, "accepted");
        else
        {
            var message = string.IsNullOrEmpty(reason) ? Text("recovery") : reason;
            if (responseTarget != null) responseTarget.SetText(client.displayName, message);
            else ModOperationNotice.Show(message);
        }
        ConfigureStoryDialogue(client);
    }

    private static void Return(StoreClient client)
    {
        if (!ValidateRaw() || document!.Phase is not ("first" or "final") || document.VisitKind != document.Phase) return;
        if (!WorkroomStoryTransfer.TryConsumeStoryBatch(document.Goods, () =>
        {
            if (document!.Phase == "success") return;
            Change(next => { next.Phase = "success"; next.RewardPending = true; });
        }, out var missing))
        {
            var message = Text("missing", string.Join("、", missing));
            ModOperationNotice.Show(message);
            if (responseTarget != null) responseTarget.SetText(client.displayName, message);
            // Keep the actual response in the story graph. Rebinding only the
            // client's main/repeat dialogue leaves this terminal response to
            // fall back into ordinary trade in the native UI. Present choices
            // after the result; never resubmit automatically or spend grace.
            ConfigureStoryDialogue(client);
            if (responseTarget != null) responseTarget.SetNextDialogue(client.mainDialogue);
            return;
        }
        ApplyDiscount(client);
        Display(client, "complete_1", "complete_2");
        ModOperationNotice.Show(Text("unlocked"));
        ClaimReward(client, false);
        ConfigureStoryDialogue(client);
    }

    private static void NotReady(StoreClient client)
    {
        if (document!.Phase == "first")
        {
            Change(next => { next.Phase = "grace"; next.DueDay = checked(StoreStation.GetDayCounter() + 2); });
            Display(client, "first_missing");
        }
        else if (document.Phase == "final")
        { Change(next => next.Phase = "failed"); Display(client, "failed_1", "failed_2"); }
        else Display(client, "keeping");
        ConfigureStoryDialogue(client);
    }

    private static void ClaimReward(StoreClient client, bool notify = true)
    {
        if (!ValidateRaw() || document!.Phase != "success" || !document.RewardPending) return;
        if (document.Reward == null)
        {
            var reward = LuoxiStoryGoods.Create("pcrepair.component_gpu_t4", false);
            Change(next => next.Reward = reward);
        }
        var claimed = WorkroomStoryTransfer.TryGrantStoryBatch(new[] { document!.Reward! }, () =>
        {
            if (document!.RewardPending) Change(next => next.RewardPending = false);
        }, out _);
        if (notify) Display(client, claimed ? "reward_received" : "reward_pending");
        if (notify || !claimed) ModOperationNotice.Show(Text(claimed ? "reward_received" : "reward_pending"));
    }

    private static void Display(StoreClient client, string first, string? second = null)
    {
        if (responseTarget != null)
        {
            responseTarget.SetText(client.displayName, Text(first));
            responseTarget.nextDialogue = second != null ? Line(client, second) : null;
            return;
        }
        var dialogue = Line(client, first);
        if (second != null) dialogue.SetNextDialogue(Line(client, second));
        DialogUIManager.Instance?.StartDialogue(dialogue, false);
    }

    internal static void ApplyDiscount(StoreClient client)
    {
        if (Unlocked && PhoneAssemblerNpc.IsPhoneAssembler(client)) client.wholesaleDiscount = -10;
    }

    private static void Change(Action<LuoxiStoryState.Document> change)
    {
        if (!Ready) throw new InvalidOperationException("洛夕剧情未准备好。");
        var next = LuoxiStoryState.Clone(document!); change(next); next.Revision = checked(next.Revision + 1);
        Publish(next);
    }
    private static void Publish(LuoxiStoryState.Document next)
    {
        if (owner == null || !SameOwner() || publishing || afterWrite != null || !MatchesRaw())
            throw new InvalidOperationException("洛夕剧情写入前会话或原文变化。");
        var after = LuoxiStoryState.Serialize(next);
        var parsed = LuoxiStoryState.Parse(after, run!, slot);
        var previousRaw = raw;
        beforeWrite = raw; beforePresent = present; afterWrite = after; publishing = true;
        try
        {
            Exception? failure = null;
            try { owner.modData ??= new Il2CppSystem.Collections.Generic.Dictionary<string, string>(); owner.modData[LuoxiStoryState.SaveKey] = after; }
            catch (Exception ex) { failure = ex; }
            if (!SameOwner()) throw new InvalidOperationException("洛夕剧情写入时会话变化。", failure);
            var now = Read(owner); var exists = HasKey(owner);
            if (exists && now == after)
            { raw = after; present = true; document = parsed; afterWrite = beforeWrite = null; error = null; return; }
            if (exists == beforePresent && now == beforeWrite)
            { afterWrite = beforeWrite = null; throw new InvalidOperationException("洛夕剧情写入未生效。", failure); }
            error = "unknown_publication";
            throw new InvalidOperationException("洛夕剧情写入结果未知，保留前后凭据。", failure);
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
                document = exists ? LuoxiStoryState.Parse(now!, run!, slot) : LuoxiStoryState.New(run!, slot);
                raw = now; present = exists; afterWrite = beforeWrite = error = null;
                BusinessRuntime.Wake();
            }
        }
        catch { /* Keep the exact proof until an unambiguous read is possible. */ }
    }
    private static bool SameOwner() => owner != null && PlayerStore.IsInstanceExist() && PlayerStore.instance != null &&
        owner.Pointer == PlayerStore.instance.Pointer && owner.runID == run && owner.saveSlotId == slot && SaveGeneration.IsSupported(owner);
    private static bool HasKey(PlayerStore store) => store.modData != null && store.modData.ContainsKey(LuoxiStoryState.SaveKey);
    private static string? Read(PlayerStore store) => HasKey(store) ? store.modData[LuoxiStoryState.SaveKey] : null;
    internal static bool ValidateRaw() { try { return Ready && MatchesRaw(); } catch { return false; } }
    private static bool MatchesRaw() => owner != null && HasKey(owner) == present && Read(owner) == raw;
    private static void Warn(Exception ex)
    { if (Environment.TickCount64 < warningAt) return; warningAt = Environment.TickCount64 + 5000; Core.Log?.Warning("洛夕剧情暂未完成，保留当前状态：" + ex); }

    [HarmonyPatch(typeof(StoreClientListDict), nameof(StoreClientListDict.CreateStoreClient))]
    internal static class RestorePatch
    {
        private static bool Prefix(string identifier, ref StoreClient __result)
        { if (Core.Clean(identifier) != Id) return true; __result = CreateClient(); return false; }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetNegociatedValue))]
    internal static class DiscountPatch
    {
        private static void Prefix(GameItem __instance)
        {
            try
            {
                var client = PlayerStore.Instance?.currentClientInstance?.storeClient;
                if (!Unlocked || !PhoneAssemblerNpc.IsPhoneAssembler(client) || GeneralHelper.IsItemOwned(__instance)) return;
                // Native stock setup and this quote path share the same feature
                // ID. Rewrite that feature; never multiply the final quote.
                ApplyDiscount(client!);
                var discount = __instance.FindItemFeatureByID("discount");
                if (discount == null) __instance.AddItemFeature(ItemFeatureList.Discount(-10));
                else discount.SetValueModifier(-10);
            }
            catch (Exception ex) { Warn(ex); }
        }
    }
}
