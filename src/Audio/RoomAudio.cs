using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

namespace PCExpansion;

/// <summary>One region audio owner; all failures are isolated from item and cash transactions.</summary>
internal static class RoomAudio
{
    private sealed class NativeLease
    {
        internal AudioSource Source = null!;
        internal IntPtr Clip;
        internal int Cursor;
    }
    private sealed class Voice
    {
        internal AudioSource Source = null!;
        internal int Priority;
        internal long Until;
        internal float Gain;
    }
    private static GameObject? root;
    private static AudioSource[] music = Array.Empty<AudioSource>(), ambience = Array.Empty<AudioSource>();
    private static readonly List<Voice> voices = new();
    private static readonly List<NativeLease> leases = new();
    private static readonly Dictionary<string, long> cooldown = new(StringComparer.Ordinal);
    private static readonly Dictionary<IntPtr, float> nativeGains = new();
    private static MelonPreferences_Category? preferences;
    private static MelonPreferences_Entry<int>? musicSetting, ambienceSetting, effectsSetting;
    private static WorkroomTransition.Region region;
    private static readonly float[] musicGain = new float[2], musicTarget = new float[2];
    private static int activeMusic, mainThread, batchDepth;
    private static string? batchCue;
    private static long batchGeneration;
    private static long failedUntil, warnAt, observedRevision = -1, observedStorage = -1;
    private static float fanGain, fanTarget;
    private static int running;
    private static int observedCash = -1;
    private static bool forecastKnown, forecastUnpaid;
    private static bool paused, priorityTakeover;
    private static bool nativeCaptured;
    private static bool cleanupPending;
    private static bool warningLatched;
    private static string? dailyCue;
    private static long dailyEpoch;
    internal static bool Active => AssemblyDebugUi.Alive(root) && region != WorkroomTransition.Region.Shop;
    internal static bool OnGameThread => Thread.CurrentThread.ManagedThreadId == mainThread;
    internal static int MusicPercent => Percent(musicSetting?.Value ?? 60);
    internal static int AmbiencePercent => Percent(ambienceSetting?.Value ?? 35);
    internal static int EffectsPercent => Percent(effectsSetting?.Value ?? 70);
    private static int Percent(int value) => Math.Clamp(value, 0, 100);

    internal static void Initialize(MelonPreferences_Category category)
    {
        mainThread = Thread.CurrentThread.ManagedThreadId;
        preferences = category;
        musicSetting = category.CreateEntry("RoomMusicVolume", 60, Text("music"));
        ambienceSetting = category.CreateEntry("RoomAmbienceVolume", 35, Text("ambience"));
        effectsSetting = category.CreateEntry("RoomEffectsVolume", 70, Text("effects"));
    }
    internal static string Text(string key) => LanguageText.Get("audio." + key);
    internal static void RefreshLabels()
    {
        if (musicSetting == null) return;
        musicSetting.DisplayName = Text("music"); ambienceSetting!.DisplayName = Text("ambience"); effectsSetting!.DisplayName = Text("effects");
    }
    internal static void Set(int channel, int value)
    {
        var entry = channel == 0 ? musicSetting : channel == 1 ? ambienceSetting : effectsSetting;
        if (entry != null) entry.Value = Percent(value);
    }
    internal static void SaveSettings()
    {
        try { preferences?.SaveToFile(false); }
        catch (Exception ex) { Warn(ex); }
    }
    private static AudioSource Source(string name)
    {
        var go = new GameObject("PCExpansion.Audio." + name);
        go.transform.SetParent(root!.transform, false);
        var source = go.AddComponent(Il2CppType.Of<AudioSource>()).TryCast<AudioSource>()
            ?? throw new InvalidOperationException("区域音源创建失败。");
        source.playOnAwake = false; source.spatialBlend = 0; source.volume = 0;
        source.ignoreListenerPause = false;
        return source;
    }
    private static void Build()
    {
        root = new GameObject("PCExpansion.RegionAudio");
        music = new[] { Source("MusicA"), Source("MusicB") };
        ambience = new[] { Source("Room"), Source("Ventilation"), Source("Fans") };
        foreach (var source in music) source.loop = true;
        foreach (var source in ambience) source.loop = true;
        for (var i = 0; i < 6; i++) voices.Add(new Voice { Source = Source("Effect" + i) });
        RoomAudioAssets.RequestLoops();
    }
    private static void Lease(AudioSource? source)
    {
        if (!AssemblyDebugUi.Alive(source) || !source!.isPlaying || !AssemblyDebugUi.Alive(source.clip) ||
            leases.Any(l => l.Source.Pointer == source.Pointer)) return;
        var lease = new NativeLease { Source = source, Clip = source.clip.Pointer };
        source.Pause(); lease.Cursor = source.timeSamples; leases.Add(lease);
    }
    private static void CaptureNative()
    {
        var manager = Il2Cpp.AudioManager.mInstance;
        var store = Il2Cpp.StoreSoundPlayer._Instance_k__BackingField;
        Lease(manager?.audioSourceMusic); Lease(manager?.audioSourceAmbient);
        Lease(store?.musicAudioSource); Lease(store?.ambianceAudioSource);
        nativeCaptured = true;
    }
    internal static bool HoldsNativePlayback(Il2Cpp.AudioManager? manager)
    {
        try
        {
            if (!OnGameThread || !AssemblyDebugUi.Alive(root) || !nativeCaptured || manager == null) return false;
            // The two native wait predicates share a body. Validate the actual
            // manager before reading its music source, including aliased calls.
            var current = Il2Cpp.AudioManager.mInstance;
            if (current == null || manager.Pointer != current.Pointer) return false;
            var source = manager.audioSourceMusic;
            if (!AssemblyDebugUi.Alive(source) || source!.isPlaying || !AssemblyDebugUi.Alive(source.clip)) return false;
            var clip = source.clip!;
            var cursor = source.timeSamples;
            return leases.Any(lease => lease.Source.Pointer == source.Pointer && lease.Clip == clip.Pointer && lease.Cursor == cursor);
        }
        catch (Exception ex) { Warn(ex); return false; }
    }
    private static void RestoreNative()
    {
        foreach (var lease in leases)
        {
            try
            {
                // Do not overwrite another owner's track or resurrect a stopped/rewound source.
                var source = lease.Source;
                if (AssemblyDebugUi.Alive(source) && AssemblyDebugUi.Alive(source.clip) && source.clip.Pointer == lease.Clip &&
                    !source.isPlaying && source.timeSamples == lease.Cursor) source.UnPause();
            }
            catch (Exception ex) { Warn(ex); }
        }
        leases.Clear();
        nativeCaptured = false;
    }
    private static bool NativePriority()
    {
        foreach (var lease in leases)
            if (AssemblyDebugUi.Alive(lease.Source) && lease.Source.isPlaying) return true;
        var store = Il2Cpp.StoreSoundPlayer._Instance_k__BackingField;
        return AssemblyDebugUi.Alive(store?.gameOverAudioSource) && store!.gameOverAudioSource.isPlaying;
    }
    private static bool SameClip(AudioClip? current, AudioClip expected) =>
        AssemblyDebugUi.Alive(current) && current!.Pointer == expected.Pointer;

    private static void BindReadyLoops()
    {
        if (region == WorkroomTransition.Region.Shop || paused || priorityTakeover) return;
        var themeName = region == WorkroomTransition.Region.Workroom ? "workroom_theme_loop" : "serverroom_theme_loop";
        var ambienceName = region == WorkroomTransition.Region.Workroom ? "workroom_loop" : "ventilation_loop";
        // Keep the native track while the current region's actual layers are loading.
        if (!RoomAudioAssets.TryLoop(themeName, out var theme) ||
            !RoomAudioAssets.TryLoop(ambienceName, out _) ||
            (region == WorkroomTransition.Region.ServerRoom && !RoomAudioAssets.TryLoop("fan_loop", out _))) return;
        if (!nativeCaptured) CaptureNative();
        var target = Array.FindIndex(music, source => SameClip(source.clip, theme!));
        if (target < 0)
        {
            target = 1 - activeMusic;
            music[target].Stop();
            music[target].clip = theme;
            musicGain[target] = 0;
        }
        musicTarget[0] = musicTarget[1] = 0;
        musicTarget[target] = 1;
        activeMusic = target;
        if (!music[target].isPlaying) music[target].Play();
        var names = new[] { "workroom_loop", "ventilation_loop", "fan_loop" };
        for (var i = 0; i < ambience.Length; i++)
        {
            if (!RoomAudioAssets.TryLoop(names[i], out var clip) || SameClip(ambience[i].clip, clip!)) continue;
            ambience[i].Stop();
            ambience[i].clip = clip;
            ambience[i].Play();
        }
    }
    internal static void Tick(WorkroomTransition.Region next)
    {
        try
        {
            if (cleanupPending) { Reset(false); return; }
            if (Environment.TickCount64 < failedUntil) return;
            if (next != WorkroomTransition.Region.Shop && !AssemblyDebugUi.Alive(root)) Build();
            RoomAudioAssets.PollLoops();
            if (!AssemblyDebugUi.Alive(root)) { region = next; return; }
            if (next != region)
            {
                region = next;
                if (region != WorkroomTransition.Region.Shop)
                {
                    RoomAudioAssets.RequestLoops();
                    observedRevision = observedStorage = -1;
                    forecastKnown = false;
                    Play("door", false);
                    if (dailyCue != null && dailyEpoch == WorkroomStorage.State.Epoch) Play(dailyCue, false);
                    dailyCue = null;
                }
                else
                {
                    musicTarget[0] = musicTarget[1] = 0;
                    RoomAudioAssets.CancelPendingLoops();
                }
            }
            var isPaused = Time.timeScale <= 0 || Il2Cpp.Interactable.isPaused || AudioListener.pause;
            priorityTakeover = NativePriority();
            if (paused != isPaused)
            {
                paused = isPaused;
                foreach (var s in music.Concat(ambience).Concat(voices.Select(v => v.Source)))
                    if (paused) s.Pause(); else s.UnPause();
            }
            BindReadyLoops();
            var delta = Math.Clamp(Time.unscaledDeltaTime, 0, .1f);
            var originalMusic = Il2Cpp.EmporiumOption._Instance_k__BackingField?.musicVolume ?? 1;
            originalMusic = float.IsFinite(originalMusic) ? Math.Clamp(originalMusic, 0, 1) : 0;
            var musicScale = originalMusic * MusicPercent / 100f * .82f;
            for (var i = 0; i < music.Length; i++)
            {
                musicGain[i] = Mathf.MoveTowards(musicGain[i], musicTarget[i], delta);
                music[i].volume = paused || priorityTakeover ? 0 : Mathf.Clamp01(musicGain[i] * musicScale);
                if (musicGain[i] == 0 && musicTarget[i] == 0 && music[i].isPlaying) music[i].Stop();
            }
            ReadRunning();
            fanGain = Mathf.MoveTowards(fanGain, region == WorkroomTransition.Region.ServerRoom ? fanTarget : 0, delta);
            var ambientScale = AmbiencePercent / 100f;
            var workTarget = region == WorkroomTransition.Region.Workroom ? ambientScale : 0;
            var ventTarget = region == WorkroomTransition.Region.ServerRoom ? ambientScale : 0;
            ambience[0].volume = paused || priorityTakeover || AmbiencePercent == 0 ? 0 : Mathf.MoveTowards(ambience[0].volume, Mathf.Clamp01(workTarget), delta * 2);
            ambience[1].volume = paused || priorityTakeover || AmbiencePercent == 0 ? 0 : Mathf.MoveTowards(ambience[1].volume, Mathf.Clamp01(ventTarget), delta * 2);
            ambience[2].volume = paused || priorityTakeover ? 0 : Mathf.Clamp01(fanGain * ambientScale);
            foreach (var voice in voices) voice.Source.volume = paused || priorityTakeover ? 0 : Mathf.Clamp01(EffectsPercent / 100f * voice.Gain);
            if (region == WorkroomTransition.Region.Shop && musicGain.All(g => g == 0) && ambience[0].volume == 0 && ambience[1].volume == 0)
                Reset(false);
        }
        catch (Exception ex)
        {
            Warn(ex); Reset(false); failedUntil = Environment.TickCount64 + 5000;
        }
    }
    private static void ReadRunning()
    {
        if (region != WorkroomTransition.Region.ServerRoom) return;
        var doc = ServerRoomRuntime.Current;
        if (!ServerRoomRuntime.Ready || doc == null) { running = 0; fanTarget = 0; return; }
        var cash = Il2Cpp.PlayerStore.instance?.GetCash() ?? 0;
        if (doc.Revision == observedRevision && WorkroomStorage.State.Revision == observedStorage && cash == observedCash) return;
        observedRevision = doc.Revision; observedStorage = WorkroomStorage.State.Revision;
        observedCash = cash;
        var facts = ServerRoomOperations.MachineFacts(doc);
        running = facts.Count(f => f.CanRun && (doc.Racks.Any(r => r.RecordId == f.RecordId && r.PoweredOn) || doc.Leases.Any(l => l.RecordId == f.RecordId)));
        fanTarget = running == 0 ? 0 : running <= 2 ? .42f : running <= 5 ? .70f : 1;
        var remaining = cash; var unpaid = false;
        var byId = facts.ToDictionary(f => f.RecordId, StringComparer.Ordinal);
        foreach (var rack in doc.Racks.Where(r => r.Kind == "mining" && r.PoweredOn).OrderBy(r => r.Priority).ThenBy(r => r.RackIndex))
        {
            var fact = byId[rack.RecordId];
            if (!fact.CanRun) continue;
            if (remaining < fact.DailyFee) unpaid = true; else remaining -= fact.DailyFee;
        }
        if (forecastKnown && !forecastUnpaid && unpaid) Play("warning");
        forecastUnpaid = unpaid; forecastKnown = true;
    }
    internal static bool Play(string cue, bool allowBatch = true)
    {
        try
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThread || !Active || paused || priorityTakeover || EffectsPercent == 0) return false;
            if (allowBatch && batchDepth > 0)
            { if (batchCue == null || cue == "warning") batchCue = cue; return true; }
            var now = Environment.TickCount64;
            var throttle = cue is "open" or "close" ? "window" : cue;
            if (cooldown.TryGetValue(throttle, out var until) && now < until) return false;
            if (cue == "warning" && warningLatched) return false;
            var priority = cue == "warning" ? 3 : cue is "confirm" or "repair_complete" or "lease_notice" ? 2 : cue == "ui" ? 0 : 1;
            var voice = voices.FirstOrDefault(v => !v.Source.isPlaying) ?? voices.Where(v => v.Priority < priority).OrderBy(v => v.Priority).FirstOrDefault();
            if (voice == null) return false;
            AudioClip? clip = cue switch
            {
                "pickup" => RoomAudioAssets.Native("module_drag"), "drop" => RoomAudioAssets.Native("module_drop"),
                "insert" => RoomAudioAssets.Native("module_install"), "remove" => RoomAudioAssets.Native("module_remove"),
                "open" => RoomAudioAssets.Native("open_machine"), "close" => RoomAudioAssets.Native("close_machine"),
                "store" => RoomAudioAssets.Native("box_click"), "metal_drop" => RoomAudioAssets.Native("metal_machine_drop"),
                "metal_drag" => RoomAudioAssets.Native("metal_machine_drag"), "recycle" => RoomAudioAssets.Native("metal_scrap_drop"),
                "assemble" => RoomAudioAssets.Native("modding1"), "disassemble" => RoomAudioAssets.Native("modding2"),
                "door" => RoomAudioAssets.Native("soft_airlock"), "ui" => RoomAudioAssets.Native("click_light"),
                "confirm" or "repair_complete" or "lease_notice" or "warning" or "rotate" or "machine_start" or "machine_stop" => RoomAudioAssets.Get(cue),
                _ => null
            };
            if (!AssemblyDebugUi.Alive(clip)) clip = cue is "pickup" or "drop" or "insert" or "remove" or "store" or "assemble" or "disassemble" or "recycle" ? RoomAudioAssets.Get("rotate") : null;
            if (!AssemblyDebugUi.Alive(clip)) return false;
            voice.Source.Stop(); voice.Source.clip = clip; voice.Source.loop = false;
            var original = !clip!.name.StartsWith("PCExpansion.", StringComparison.Ordinal);
            voice.Gain = original ? NativeGain(clip) : 1;
            if (cue is "ui" or "door") voice.Gain *= .65f;
            voice.Source.volume = Mathf.Clamp01(EffectsPercent / 100f * voice.Gain);
            voice.Priority = priority; voice.Until = now + (long)(clip!.length * 1000); voice.Source.Play();
            cooldown[throttle] = now + (cue == "warning" ? 1800 : cue == "ui" ? 100 : 170);
            if (cue == "warning") warningLatched = true;
            else if (cue != "ui") warningLatched = false;
            return true;
        }
        catch (Exception ex) { Warn(ex); return false; }
    }
    private static float NativeGain(AudioClip clip)
    {
        if (nativeGains.TryGetValue(clip.Pointer, out var gain)) return gain;
        gain = .35f;
        if (clip.samples * (long)clip.channels is > 0 and < 500000)
        {
            var data = new Il2CppStructArray<float>(clip.samples * clip.channels);
            if (clip.GetData(data, 0))
            {
                double squares = 0; float peak = 0;
                foreach (var value in data) { squares += value * value; peak = Math.Max(peak, Math.Abs(value)); }
                var rms = Math.Sqrt(squares / data.Length);
                gain = (float)Math.Min(.0631 / Math.Max(rms, .00001), .2512 / Math.Max(peak, .00001)) / .70f;
            }
        }
        nativeGains[clip.Pointer] = gain;
        return gain;
    }
    internal static void Notice(string key)
    {
        switch (key)
        {
            case "assembled": Play("assemble"); break;
            case "disassembled": Play("disassemble"); break;
            case "repair_complete": case "processing_repaired": Play("repair_complete"); break;
            case "processing_recycled": Play("recycle"); break;
            case "processing_discarded": Play("confirm"); break;
            case "placed": Play("insert"); break;
            case "returned": Play("remove"); break;
            case "stored": Play("store"); break;
            case "taken": Play("drop"); break;
            case "auto_fill_complete": case "auto_fill_result": Play("insert"); break;
            case "full": case "failed": case "wrong_slot": case "missing": case "invalid_position":
            case "processing_missing_materials": case "processing_insufficient_value": case "processing_no_output":
            case "repair_missing_materials": case "unsupported_item": Play("warning"); break;
            case "processing_zero_recycle": case "processing_missing_cash": case "processing_recycle_insufficient":
            case "processing_insufficient_quantity": Play("warning"); break;
        }
    }
    private sealed class BatchScope : IDisposable
    {
        private bool done;
        private readonly long generation = batchGeneration;
        public void Dispose()
        {
            if (done) return; done = true;
            if (generation != batchGeneration || batchDepth <= 0) return;
            if (--batchDepth != 0) return;
            var cue = batchCue; batchCue = null;
            if (cue != null) Play(cue, false);
        }
    }
    internal static IDisposable Batch() { batchDepth++; return new BatchScope(); }
    internal static void DailyNotice(bool unpaid, bool expired)
    {
        if (!unpaid && !expired) return;
        dailyCue = unpaid ? "warning" : "lease_notice"; dailyEpoch = WorkroomStorage.State.Epoch;
        if (Active) { Play(dailyCue); dailyCue = null; }
    }
    internal static void Reset(bool clearSession = true)
    {
        region = WorkroomTransition.Region.Shop;
        try { RoomAudioAssets.CancelPendingLoops(); } catch (Exception ex) { Warn(ex); }
        foreach (var source in music.Concat(ambience).Concat(voices.Select(v => v.Source)))
            try { if (AssemblyDebugUi.Alive(source)) source.Stop(); } catch (Exception ex) { Warn(ex); }
        RestoreNative();
        try
        {
            if (AssemblyDebugUi.Alive(root)) { root!.SetActive(false); UnityEngine.Object.Destroy(root); }
            cleanupPending = false;
        }
        catch (Exception ex) { cleanupPending = true; Warn(ex); return; }
        root = null; music = ambience = Array.Empty<AudioSource>(); voices.Clear();
        Array.Clear(musicGain, 0, 2); Array.Clear(musicTarget, 0, 2);
        region = WorkroomTransition.Region.Shop; paused = priorityTakeover = warningLatched = false; fanGain = fanTarget = 0;
        observedRevision = observedStorage = -1; cooldown.Clear(); nativeGains.Clear(); batchDepth = 0; batchCue = null; batchGeneration++;
        forecastKnown = forecastUnpaid = false; observedCash = -1;
        if (clearSession) { dailyCue = null; WorkroomAudioApi.Reset(); }
    }
    private static void Warn(Exception exception)
    {
        if (Environment.TickCount64 < warnAt) return;
        warnAt = Environment.TickCount64 + 5000;
        Core.Log?.Warning("区域音频暂不可用，玩法继续：" + exception.Message);
    }
}
