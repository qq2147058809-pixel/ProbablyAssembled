using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Attaches a single small conversation to an existing ordinary visit. It never schedules a visitor.</summary>
internal static class NpcSmallStories
{
    private const string VisitPrefix = "PCEXPANSION_SMALL_VISIT_";
    private static PlayerStore? owner;
    private static string? run, raw, beforeWrite, afterWrite, error;
    private static int slot;
    private static bool present, beforePresent, publishing;
    private static long retryAt, warningAt;
    private static NpcSmallStoryState.Document? document;
    private sealed class Binding
    {
        internal StoreClient Client = null!;
        internal string Token = "";
        internal bool Luoxi;
    }
    private static Binding? binding;
    private sealed class Playback
    {
        internal StoreClient Client = null!;
        internal string Token = "", Node = "", ShownId = "", EndId = "";
        internal bool Luoxi, Displayed, Finished;
        internal int Epoch;
        internal Dialogue? Shown, Original, OriginalRepeat, Root;
        internal IntPtr Store, Ui;
        internal string Run = "";
        internal int Slot;
    }
    private static Playback? playback;
    internal static bool WorkPending => publishing || afterWrite != null || binding != null ||
        document != null && (document.Luoxi.Token.Length != 0 || document.Jiangbai.Token.Length != 0) || NpcSmallStoryExchange.RecoveryPending;
    internal static PlayerStore? Store => Ready ? owner : null;
    internal static NpcSmallStoryState.Document Data => document ?? throw new InvalidOperationException("小剧情尚未绑定。");
    internal static bool Ready
    { get { try { return document != null && error == null && !publishing && afterWrite == null && SameOwner(); } catch { return false; } } }
    private static bool IsOrdinary(StoreClient? client, out bool luoxi)
    {
        luoxi = PhoneAssemblerNpc.IsPhoneAssembler(client);
        return client != null && (luoxi || SecuritySeizureMerchant.IsMerchant(client)) &&
            !LuoxiStory.IsStory(client) && !JiangbaiStory.IsStory(client) && !ServerRoomOrderStock.IsDelivery(client);
    }
    private static NpcSmallStoryState.Visit Visit(NpcSmallStoryState.Document d, bool luoxi) => luoxi ? d.Luoxi : d.Jiangbai;
    private static string[] Tags(StoreClient client)
    {
        var tags = new List<string>();
        if (client.tag != null) foreach (var rawTag in client.tag)
        { var tag = Core.Clean(rawTag); if (tag.StartsWith(VisitPrefix, StringComparison.Ordinal)) tags.Add(tag); }
        return tags.ToArray();
    }
    private static bool ExactVisit(StoreClient client, string token)
    { var tags = Tags(client); return tags.Length == 1 && tags[0] == VisitPrefix + token; }

    internal static void OnWorldChanged(PlayerStore store) { retryAt = 0; Update(store); }

    internal static void Update(PlayerStore store)
    {
        try
        {
            ResolveWrite();
            if (!Bind(store) || !ValidateRaw()) return;
            ResolveBinding();
            if (binding != null) return;
            NpcSmallStoryExchange.RetryRecovery();
            if (Environment.TickCount64 < retryAt) return;
            retryAt = Environment.TickCount64 + 500;
            FinishDeparted(store, true); FinishDeparted(store, false);
            var current = store.isClientArrived ? store.currentClientInstance?.storeClient : null;
            if (IsOrdinary(current, out var luoxi)) Observe(store, current!, luoxi);
        }
        catch (Exception ex) { Warn(ex); }
    }
    private static void FinishDeparted(PlayerStore store, bool luoxi)
    {
        var v = Visit(Data, luoxi);
        if (v.Token.Length == 0) return;
        var current = store.currentClientInstance?.storeClient;
        if (current != null && ExactVisit(current, v.Token)) return;
        var queue = store.storeClientManager?.clientStack;
        if (queue != null) foreach (var queued in queue) if (queued != null && ExactVisit(queued, v.Token)) return;
        Change(next =>
        {
            var visit = Visit(next, luoxi); visit.FinishedOnce = true;
            visit.Token = visit.Node = visit.Entry = ""; visit.StoryEnded = false;
        });
        if (playback?.Luoxi == luoxi) playback = null;
    }
    private static void Observe(PlayerStore store, StoreClient client, bool luoxi)
    {
        var day = StoreStation.GetDayCounter(); if (day < 1) return;
        var v = Visit(Data, luoxi); var tags = Tags(client);
        if (v.Token.Length != 0)
        {
            if (!ExactVisit(client, v.Token)) throw new InvalidOperationException("小剧情当前访客与访问凭据不符。");
            return;
        }
        if (tags.Length != 0) throw new InvalidOperationException("小剧情访客存在未匹配的访问标签。");
        var token = Guid.NewGuid().ToString("N");
        // A tag is only an identity marker, never a command. Publish its receipt first.
        binding = new Binding { Client = client, Token = token, Luoxi = luoxi };
        try { Change(next =>
        {
            var visit = Visit(next, luoxi); visit.Token = token; visit.Day = day;
            visit.TurnoverAtArrival = NpcStoryProgress.LuoxiTurnover;
            visit.Node = visit.Entry = ""; visit.StoryEnded = false;
        }); }
        finally { ResolveBinding(); }
        if (binding != null) throw new InvalidOperationException("小剧情访问标签等待确认。");
    }
    private static void ResolveBinding()
    {
        var pending = binding;
        if (pending == null || !Ready) return;
        var v = Visit(Data, pending.Luoxi);
        if (v.Token.Length == 0) { binding = null; return; } // Publication did not apply; no native tag was changed.
        if (v.Token != pending.Token || owner?.currentClientInstance?.storeClient?.Pointer != pending.Client.Pointer)
            throw new InvalidOperationException("小剧情访问绑定期间访客变化，保留凭据。");
        var tags = Tags(pending.Client);
        if (tags.Length == 0)
        {
            try { pending.Client.AddTag(VisitPrefix + pending.Token); }
            catch { if (!ExactVisit(pending.Client, pending.Token)) throw; }
        }
        if (!ExactVisit(pending.Client, pending.Token)) throw new InvalidOperationException("小剧情访客标签冲突。");
        binding = null;
    }
    private static bool CoreBusy(bool luoxi) => luoxi
        ? LuoxiStory.Active || LuoxiStory.Phase == "new" && owner != null && NpcStoryProgress.LuoxiEligible(owner)
        : JiangbaiStory.Active || JiangbaiStory.Phase == "new" && owner != null && NpcStoryProgress.JiangbaiEligible(owner);

    private static void Attach(ref Dialogue dialogue)
    {
        try
        {
            var store = PlayerStore.Instance; var client = store?.currentClientInstance?.storeClient;
            if (store == null || dialogue == null || !IsOrdinary(client, out var luoxi) || client!.mainDialogue == null ||
                dialogue.Pointer != client.mainDialogue.Pointer || !Bind(store) || !ValidateRaw() || WorkroomStoryTransfer.Pending || CoreBusy(luoxi)) return;
            if (playback != null && playback.Client.Pointer == client.Pointer && playback.Root?.Pointer == dialogue.Pointer) return;
            Observe(store, client, luoxi);
            var visit = Visit(Data, luoxi);
            if (visit.StoryEnded) return;
            string entry = visit.Entry;
            if (entry.Length == 0)
            {
                entry = NpcSmallStoryState.Select(Data, luoxi, StoreStation.GetDayCounter(), luoxi ? LuoxiStory.Phase : JiangbaiStory.Phase, NpcStoryProgress.SecurityReached25);
                if (entry.Length == 0) { FamiliarGreeting(client, dialogue, luoxi); return; }
                Change(next =>
                {
                    var v = Visit(next, luoxi); v.Entry = entry; v.Node = entry.Substring(0, 2);
                    v.LastStoryDay = StoreStation.GetDayCounter();
                });
            }
            var node = Visit(Data, luoxi).Node;
            if (node is "L2" or "J2" or "L4" && Data.Nodes[node].Status != "new") entry = NpcSmallStoryState.Resume(Data, node);
            var p = new Playback { Client = client, Token = visit.Token, Node = node, Luoxi = luoxi,
                Original = dialogue, OriginalRepeat = client.repeatDialogue, Store = store.Pointer, Run = store.runID, Slot = store.saveSlotId };
            p.Root = NpcSmallStoryDialogue.Build(client, entry, (command, line) => Command(p, command, line));
            p.Root.isMainDialog = true; playback = p;
            client.mainDialogue = p.Root; dialogue = p.Root;
        }
        catch (Exception ex) { Warn(ex); }
    }
    private static void FamiliarGreeting(StoreClient client, Dialogue greeting, bool luoxi)
    {
        var node = luoxi ? "L5" : "J5";
        if (Data.Nodes[node].CompletedDay < 1) return;
        // Only replace the greeting, preserving native introduction, stock, and trade callbacks.
        greeting.SetText(client.displayName, NpcSmallStoryDialogue.Text(node + (StoreStation.GetDayCounter() % 2 == 0 ? "-G01" : "-G02")));
    }
    private static bool Current(Playback p)
    {
        var store = PlayerStore.Instance;
        return ValidateRaw() && store != null && store.Pointer == p.Store && store.runID == p.Run && store.saveSlotId == p.Slot &&
            store.isClientArrived && store.currentClientInstance?.storeClient?.Pointer == p.Client.Pointer &&
            ExactVisit(p.Client, p.Token) && Visit(Data, p.Luoxi).Token == p.Token && Visit(Data, p.Luoxi).Node == p.Node;
    }
    private static void Command(Playback p, string command, Dialogue line)
    {
        try
        {
            if (!Current(p)) return;
            if (command.StartsWith("shown:", StringComparison.Ordinal))
            {
                p.Shown = line; p.ShownId = command.Substring(6); p.Displayed = false; p.EndId = "";
                // Keep the ordinary introduction attached before displaying. The
                // delayed action handles a still-current line; ContinuePatch proves
                // a normal advance when native continuation runs ahead of that action.
                if (NpcSmallStoryDialogue.Graph.TryGetValue(p.ShownId, out var shown) &&
                    !shown.Player && shown.Next == null && shown.Choices.Length == 0)
                    line.nextDialogue = p.Original;
                return;
            }
            if (command.StartsWith("end:", StringComparison.Ordinal))
            {
                var ui = DialogUIManager.Instance;
                if (p.Displayed && p.Shown?.Pointer == line.Pointer && ui?.Pointer == p.Ui &&
                    ui.currentDialog?.Pointer == line.Pointer && ui.conversationEpoch == p.Epoch)
                    p.EndId = command.Substring(4);
                return;
            }
            if (command.StartsWith("complete:", StringComparison.Ordinal))
            {
                if (p.EndId.Length == 0 || p.Finished || !NpcSmallStoryDialogue.Graph.TryGetValue(p.EndId, out var spec)) return;
                if (spec.Complete)
                {
                    Change(next =>
                    {
                        var n = next.Nodes[p.Node];
                        if (p.Node is "L2" or "J2" or "L4" && n.Status is not ("done" or "paid" or "refused")) return;
                        if (n.Status != "refused") n.Status = "done";
                        if (n.CompletedDay < 1) n.CompletedDay = StoreStation.GetDayCounter();
                    });
                }
                Change(next => Visit(next, p.Luoxi).StoryEnded = true);
                p.Finished = true;
                // Continue the original ordinary introduction to preserve the merchant's stock action.
                line.nextDialogue = p.Original;
                if (p.Original != null) { p.Client.mainDialogue = p.Original; p.Client.repeatDialogue = p.OriginalRepeat; }
                return;
            }
            if (p.Finished || WorkroomStoryTransfer.Pending) return;
            var parts = command.Split(':'); if (parts.Length != 2 || parts[1] != p.Node) return;
            var node = p.Node; var status = Data.Nodes[node].Status; string result;
            switch (parts[0])
            {
                case "accept":
                    if (status == "new") Change(next => next.Nodes[node].Status = "accepted");
                    result = Data.Nodes[node].Status == "accepted" ? node + "-102" : NpcSmallStoryState.Resume(Data, node);
                    break;
                case "refuse":
                    if (status == "new") Change(next => next.Nodes[node].Status = "refused");
                    result = Data.Nodes[node].Status == "refused" ? node + "-202" : NpcSmallStoryState.Resume(Data, node);
                    break;
                case "handin": result = NpcSmallStoryExchange.HandIn(node); break;
                case "claim": result = NpcSmallStoryExchange.Claim(node, p.ShownId == "L4-101"); break;
                default: return;
            }
            NpcSmallStoryDialogue.Rewrite(line, p.Client, result, (action, target) => Command(p, action, target));
            Command(p, "shown:" + (result == "L2-paid-pending" ? "L2-310" : result), line);
        }
        catch (Exception ex) { Warn(ex); }
        finally { NpcBusiness.Resume(); }
    }

    private static bool Bind(PlayerStore store)
    {
        if (!SaveGeneration.IsSupported(store) || !PlayerStore.IsInstanceExist() || PlayerStore.instance?.Pointer != store.Pointer) return false;
        if (SameOwner()) return true;
        if (afterWrite != null || publishing || binding != null) return false;
        owner = store; run = store.runID; slot = store.saveSlotId; document = null; error = null;
        present = HasKey(store); raw = Read(store);
        try { document = present ? NpcSmallStoryState.Parse(raw!, run, slot) : NpcSmallStoryState.New(run, slot); }
        catch { error = "invalid_data"; throw; }
        if (!present) Publish(document);
        retryAt = 0; playback = null; return true;
    }
    internal static void Change(Action<NpcSmallStoryState.Document> action)
    {
        if (!Ready) throw new InvalidOperationException("小剧情状态尚未就绪。");
        var next = NpcSmallStoryState.Clone(Data); action(next); next.Revision = checked(next.Revision + 1); Publish(next);
    }
    private static void Publish(NpcSmallStoryState.Document next)
    {
        if (owner == null || !SameOwner() || publishing || afterWrite != null || !MatchesRaw()) throw new InvalidOperationException("小剧情写入前会话或原文变化。");
        var after = NpcSmallStoryState.Serialize(next); var parsed = NpcSmallStoryState.Parse(after, run!, slot);
        var previousRaw = raw;
        beforeWrite = raw; beforePresent = present; afterWrite = after; publishing = true;
        try
        {
            Exception? failure = null;
            try { owner.modData ??= new Il2CppSystem.Collections.Generic.Dictionary<string, string>(); owner.modData[NpcSmallStoryState.SaveKey] = after; }
            catch (Exception ex) { failure = ex; }
            if (!SameOwner()) throw new InvalidOperationException("小剧情写入时会话变化。", failure);
            var now = Read(owner); var exists = HasKey(owner);
            if (exists && now == after) { raw = after; present = true; document = parsed; afterWrite = beforeWrite = error = null; return; }
            if (exists == beforePresent && now == beforeWrite) { afterWrite = beforeWrite = null; throw new InvalidOperationException("小剧情写入未生效。", failure); }
            error = "unknown_publication"; throw new InvalidOperationException("小剧情写入结果未知，保留凭据。", failure);
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
                document = exists ? NpcSmallStoryState.Parse(now!, run!, slot) : NpcSmallStoryState.New(run!, slot);
                raw = now; present = exists; afterWrite = beforeWrite = error = null;
                BusinessRuntime.Wake();
            }
        }
        catch { }
    }
    internal static bool BeforeSave()
    {
        NpcBusiness.Resume();
        try { ResolveWrite(); ResolveBinding(); }
        catch (Exception ex) { Warn(ex); return false; }
        return binding == null && !publishing && afterWrite == null && !NpcSmallStoryExchange.Pending &&
            (owner == null || !SameOwner() || error == null && MatchesRaw());
    }
    internal static bool AllowSessionChange() => BeforeSave();
    internal static void ResetSession()
    {
        if (!AllowSessionChange()) throw new InvalidOperationException("小剧情结算未决，暂不能切档。");
        NpcSmallStoryExchange.ResetSession(); owner = null; document = null; playback = null; binding = null;
        run = raw = error = beforeWrite = afterWrite = null; present = beforePresent = publishing = false; slot = 0; retryAt = warningAt = 0;
    }
    private static bool SameOwner() => owner != null && PlayerStore.IsInstanceExist() && PlayerStore.instance?.Pointer == owner.Pointer &&
        owner.runID == run && owner.saveSlotId == slot && SaveGeneration.IsSupported(owner);
    private static bool HasKey(PlayerStore store) => store.modData != null && store.modData.ContainsKey(NpcSmallStoryState.SaveKey);
    private static string? Read(PlayerStore store) => HasKey(store) ? store.modData[NpcSmallStoryState.SaveKey] : null;
    internal static bool ValidateRaw() { try { return Ready && MatchesRaw(); } catch { return false; } }
    private static bool MatchesRaw() => owner != null && HasKey(owner) == present && Read(owner) == raw;
    private static void Warn(Exception ex)
    { if (Environment.TickCount64 < warningAt) return; warningAt = Environment.TickCount64 + 5000; Core.Log?.Warning("小剧情保留当前状态，等待重试：" + ex); }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.StartDialogue), new[] { typeof(Dialogue), typeof(bool) })]
    internal static class StartPatch
    { [HarmonyPriority(Priority.Last)] private static void Prefix(ref Dialogue __0) => Attach(ref __0); }
    [HarmonyPatch(typeof(DialogUIManager), "OnCurrentTextDisplayed")]
    internal static class DisplayedPatch
    {
        // Fast-forward can execute the end action inside this completion callback.
        // Record completion at entry, before the native skip path invokes it.
        private static void Prefix(DialogUIManager __instance)
        {
            var p = playback;
            if (p != null && Current(p) && p.Shown?.Pointer == __instance.currentDialog?.Pointer)
            { p.Displayed = true; p.Epoch = __instance.conversationEpoch; p.Ui = __instance.Pointer; }
        }
    }
    // Native continuation can leave a fully displayed line before its delayed end
    // action runs. Commit only the observed transition to this visit's original
    // trade dialogue, never an arbitrary change of currentDialog or epoch.
    private sealed class Advance
    {
        internal Playback Playback = null!;
        internal Dialogue Line = null!, Next = null!;
        internal string Id = "";
        internal IntPtr Ui;
        internal int Epoch;
    }
    [HarmonyPatch(typeof(DialogUIManager), "ContinueConversation")]
    internal static class ContinuePatch
    {
        private static void Prefix(DialogUIManager __instance, out Advance? __state)
        {
            __state = null;
            var p = playback;
            if (p == null || p.Finished || !p.Displayed || !Current(p) || p.Ui != __instance.Pointer ||
                p.Epoch != __instance.conversationEpoch || p.Shown == null || p.Original == null ||
                p.Shown.Pointer != __instance.currentDialog?.Pointer || p.Shown.nextDialogue?.Pointer != p.Original.Pointer ||
                !NpcSmallStoryDialogue.Graph.TryGetValue(p.ShownId, out var spec) ||
                spec.Player || spec.Next != null || spec.Choices.Length != 0) return;
            __state = new Advance { Playback = p, Line = p.Shown, Next = p.Original,
                Id = p.ShownId, Ui = p.Ui, Epoch = p.Epoch };
        }
        private static void Postfix(DialogUIManager __instance, Advance? __state, bool __runOriginal)
        {
            if (!__runOriginal || __state == null) return;
            var a = __state; var p = a.Playback;
            if (!ReferenceEquals(playback, p) || p.Finished || !p.Displayed || !Current(p) ||
                p.Shown?.Pointer != a.Line.Pointer || p.ShownId != a.Id || p.Ui != a.Ui || p.Epoch != a.Epoch ||
                __instance.Pointer != a.Ui || __instance.conversationEpoch != a.Epoch ||
                __instance.currentDialog?.Pointer != a.Next.Pointer) return;
            p.EndId = a.Id;
            Command(p, "complete:" + p.Node, a.Line);
        }
    }
    [HarmonyPatch(typeof(DialogUIManager), "InterruptDialog")]
    internal static class InterruptPatch
    { private static void Prefix() { if (playback != null) { playback.Displayed = false; playback.EndId = ""; } } }
}
