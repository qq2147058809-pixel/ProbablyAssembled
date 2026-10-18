using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace PCExpansion;

/// <summary>Opt-in, bounded startup observations written to a file. Never changes gameplay or save data.</summary>
internal static class NpcVisitPerformance
{
    internal const int CoreMain = 0, StoryLuoxi = 1, StoryJiangbai = 2, Phone = 3,
        NativeShopUI = 4, NativeClient = 5, CoreLate = 6, WorkroomUpdate = 7, WorkroomBuild = 8,
        ManualBuild = 9, SpriteColdLoad = 10;
    private const int MetricCount = 11;
    private static readonly string[] Names = { "core", "luoxi", "jiangbai", "phone", "shopUI", "client", "late", "workroom", "roomBuild", "manual", "spriteLoad" };
    private static readonly long[] Starts = new long[MetricCount], Totals = new long[MetricCount],
        Maxima = new long[MetricCount];
    private static readonly int[] Depths = new int[MetricCount], Calls = new int[MetricCount];
    private static readonly long WindowTicks = Stopwatch.Frequency * 2L,
        IdentityTicks = Stopwatch.Frequency / 2L, DurationTicks = Stopwatch.Frequency * 180L;
    private static int day, frames, slowFrames, unfocusedFrames, pausedFrames,
        wallFrames, wallOver25, wallOver50, gc0, gc1, gc2;
    private static long sampleAt, startedAt, windowAt, lastFrameAt, allocatedAt;
    private static double frameTotal, frameMax, wallTotal, wallMax;
    private static string actor = "none", maxFrameActor = "none";
    private static string scene = "startup";
    private static bool story, arrived, completed, focused = true, hasStore;
    private static float timeScale = 1;
    private static StreamWriter? output;
    private static bool enabled, failureReported;
    internal static bool Enabled => enabled;
    internal static bool Active { get; private set; }

    internal static void Initialize(bool requested, string directory)
    {
        Reset();
        if (!requested) return;
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "startup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
                "-" + Environment.ProcessId + ".log");
            output = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            enabled = true; completed = false;
        }
        catch (Exception ex) { Fail(ex); }
    }

    internal static void Shutdown()
    {
        try { if (Active) Emit(Stopwatch.GetTimestamp(), true); }
        catch (Exception ex) { Fail(ex); }
        finally { Stop(); }
    }

    internal static void BeginFrame()
    {
        if (!enabled || completed) return;
        try { BeginFrame(PlayerStore.IsInstanceExist() ? PlayerStore.instance : null); }
        catch (Exception ex) { Fail(ex); }
    }

    // Never use the lazy PlayerStore.Instance here.
    internal static void BeginFrame(PlayerStore? store)
    {
        if (!enabled || completed) return;
        try
        {
            var now = Stopwatch.GetTimestamp();
            if (completed) return;
            // Start before the menu/store/NPC, so startup work cannot precede the trace.
            // One bounded window per process; changing scene/save does not discard evidence.
            if (!Active)
            {
                ClearWindow(); Active = true; startedAt = windowAt = now;
                allocatedAt = GC.GetTotalAllocatedBytes(false);
                gc0 = GC.CollectionCount(0); gc1 = GC.CollectionCount(1); gc2 = GC.CollectionCount(2);
            }
            if (now - startedAt >= DurationTicks) { Emit(now, true); Stop(); completed = true; return; }
            if (lastFrameAt != 0)
            {
                var elapsed = Math.Max(0, now - lastFrameAt) * 1000.0 / Stopwatch.Frequency;
                wallFrames++; wallTotal += elapsed;
                if (elapsed > wallMax) wallMax = elapsed;
                if (elapsed > 25) wallOver25++;
                if (elapsed > 50) wallOver50++;
            }
            lastFrameAt = now;
            if (now >= sampleAt)
            {
                sampleAt = now + IdentityTicks;
                hasStore = store != null;
                day = store == null ? 0 : StoreStation.GetDayCounter();
                var client = store?.currentClientInstance?.storeClient;
                actor = client == null ? "none" : SafeLabel(Core.Clean(client.identifier));
                story = LuoxiStory.IsStory(client) || JiangbaiStory.IsStory(client);
                arrived = store != null && store.isClientArrived;
                focused = Application.isFocused; timeScale = Time.timeScale;
            }
            var milliseconds = (double)Time.unscaledDeltaTime * 1000;
            if (double.IsFinite(milliseconds) && milliseconds >= 0)
            {
                frames++; frameTotal += milliseconds;
                if (!focused) unfocusedFrames++;
                if (timeScale <= 0) pausedFrames++;
                if (milliseconds > 50) slowFrames++;
                if (milliseconds > frameMax) { frameMax = milliseconds; maxFrameActor = actor; }
            }
        }
        catch (Exception ex) { Fail(ex); }
    }

    internal static void EndFrame()
    {
        try
        {
            if (!Active) return;
            var now = Stopwatch.GetTimestamp();
            if (now - windowAt >= WindowTicks) Emit(now, false);
        }
        catch (Exception ex) { Fail(ex); }
    }

    // Outermost elapsed time is counted once if the same metric reenters.
    // Different metrics overlap and must never be added as disjoint frame costs.
    internal static void Begin(int metric)
    {
        try
        {
            if (!Active || (uint)metric >= MetricCount) return;
            if (Depths[metric]++ == 0) Starts[metric] = Stopwatch.GetTimestamp();
        }
        catch { /* Observations must not block the measured operation. */ }
    }

    internal static void End(int metric)
    {
        try
        {
            if (!Active || (uint)metric >= MetricCount || Depths[metric] == 0) return;
            if (--Depths[metric] != 0) return;
            var elapsed = Math.Max(0, Stopwatch.GetTimestamp() - Starts[metric]);
            Calls[metric]++; Totals[metric] += elapsed;
            if (elapsed > Maxima[metric]) Maxima[metric] = elapsed;
        }
        catch { /* Observations must not block the measured operation. */ }
    }

    private static void Emit(long now, bool final)
    {
        var allocated = GC.GetTotalAllocatedBytes(false);
        var count0 = GC.CollectionCount(0); var count1 = GC.CollectionCount(1); var count2 = GC.CollectionCount(2);
        var text = new StringBuilder(640);
        text.Append("[NPC-PERF] v=2 window_s=").Append(Seconds(now - windowAt))
            .Append(" elapsed_s=").Append(Seconds(now - startedAt)).Append(" final=").Append(final)
            .Append(" scene=").Append(scene).Append(" store=").Append(hasStore).Append(" day=").Append(day)
            .Append(" npc=").Append(actor).Append(" story=").Append(story).Append(" arrived=").Append(arrived)
            .Append(" frames=").Append(frames).Append(" frame_ms(avg/max)=")
            .Append(Number(frames == 0 ? 0 : frameTotal / frames)).Append('/').Append(Number(frameMax))
            .Append(" slow_gt50ms=").Append(slowFrames).Append(" max_frame_npc=").Append(maxFrameActor)
            .Append(" wall_ms(count/avg/max)=").Append(wallFrames).Append('/')
            .Append(Number(wallFrames == 0 ? 0 : wallTotal / wallFrames)).Append('/').Append(Number(wallMax))
            .Append(" wall_gt25/50ms=").Append(wallOver25).Append('/').Append(wallOver50)
            .Append(" clr_alloc_MiB=").Append(Number(Math.Max(0, allocated - allocatedAt) / 1048576.0))
            .Append(" clr_gc_0/1/2=").Append(count0 - gc0).Append('/').Append(count1 - gc1).Append('/').Append(count2 - gc2)
            .Append(" focus_sample=").Append(focused).Append(" timescale_sample=").Append(Number(timeScale))
            .Append(" unfocused_frames=").Append(unfocusedFrames).Append(" paused_frames=").Append(pausedFrames)
            .Append(" method_ms(count/total/max):");
        for (var i = 0; i < MetricCount; i++)
            text.Append(' ').Append(Names[i]).Append('=').Append(Calls[i]).Append('/')
                .Append(Number(Totals[i] * 1000.0 / Stopwatch.Frequency)).Append('/')
                .Append(Number(Maxima[i] * 1000.0 / Stopwatch.Frequency));
        text.Append(" frame=Unity_previous_frame; wall=Stopwatch_Update_interval; identity/focus/pause=0.5s_sampled_unfiltered; methods=inclusive_overlap; clr=all_managed_threads_not_Unity_native; GPU=external");
        output!.WriteLine(text.ToString());
        output.Flush();
        allocatedAt = allocated; gc0 = count0; gc1 = count1; gc2 = count2;
        ClearWindow(); windowAt = now;
    }

    internal static void SceneChanged(string name)
    {
        if (!enabled || completed) return;
        try { scene = SafeLabel(name); } catch { }
    }

    private static string Number(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
    private static string Seconds(long ticks) => Number(ticks / (double)Stopwatch.Frequency);
    private static string SafeLabel(string value)
    {
        if (value.Length > 96) value = value.Substring(0, 96);
        return value.Replace('\r', '_').Replace('\n', '_').Replace('\t', '_');
    }
    private static void ClearWindow()
    {
        frames = slowFrames = unfocusedFrames = pausedFrames = 0; frameTotal = frameMax = 0; maxFrameActor = "none";
        wallFrames = wallOver25 = wallOver50 = 0; wallTotal = wallMax = 0;
        Array.Clear(Totals, 0, MetricCount); Array.Clear(Maxima, 0, MetricCount); Array.Clear(Calls, 0, MetricCount);
    }
    private static void Stop()
    {
        Active = false; completed = true; Array.Clear(Depths, 0, MetricCount); Array.Clear(Starts, 0, MetricCount);
        ClearWindow();
        enabled = false;
        var previous = output; output = null;
        try { previous?.Dispose(); } catch { }
    }
    private static void Fail(Exception ex)
    {
        Stop();
        if (failureReported) return;
        failureReported = true;
        try { Core.Log?.Warning("启动性能采样已停止：" + ex.Message); } catch { }
    }
    private static void Reset()
    {
        Stop(); sampleAt = startedAt = windowAt = lastFrameAt = 0; day = 0; hasStore = false; scene = "startup";
        actor = "none"; story = arrived = false; focused = true; timeScale = 1; failureReported = false;
    }

    private static void NativeBegin(int metric, out bool state)
    {
        state = false;
        try { if (Active) { Begin(metric); state = true; } } catch { }
    }
    private static void NativeEnd(int metric, ref bool state)
    {
        try { if (state) { state = false; End(metric); } } catch { }
    }

    [HarmonyPatch(typeof(StoreUIManager), nameof(StoreUIManager.Update))]
    internal static class ShopUpdatePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(bool __runOriginal, out bool __state)
        { __state = false; if (__runOriginal) NativeBegin(NativeShopUI, out __state); }
        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref bool __state) => NativeEnd(NativeShopUI, ref __state);
        private static Exception? Finalizer(Exception? __exception, ref bool __state)
        { NativeEnd(NativeShopUI, ref __state); return __exception; }
    }

    [HarmonyPatch(typeof(StoreClientMono), "Update")]
    internal static class ClientUpdatePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(bool __runOriginal, out bool __state)
        { __state = false; if (__runOriginal) NativeBegin(NativeClient, out __state); }
        [HarmonyPriority(Priority.First)]
        private static void Postfix(ref bool __state) => NativeEnd(NativeClient, ref __state);
        private static Exception? Finalizer(Exception? __exception, ref bool __state)
        { NativeEnd(NativeClient, ref __state); return __exception; }
    }
}
