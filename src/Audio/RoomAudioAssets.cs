using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.Networking;

namespace PCExpansion;

/// <summary>Only adopted original audio is embedded. Native game clips are borrowed, never owned.</summary>
internal static class RoomAudioAssets
{
    private static readonly Dictionary<string, AudioClip> clips = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, AudioClip> native = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, LoopLoad> pendingLoops = new(StringComparer.Ordinal);
    private static int loopGeneration;
    private sealed class LoopLoad
    {
        internal readonly RoomAudioCache.Profile Profile;
        internal readonly RoomAudioCache.Job Cache;
        internal readonly int Generation;
        internal readonly long Started = Stopwatch.GetTimestamp();
        internal UnityWebRequest? Request;
        internal AudioClip? Candidate;
        internal LoopLoad(RoomAudioCache.Profile profile, RoomAudioCache.Job cache, int generation)
        { Profile = profile; Cache = cache; Generation = generation; }
    }
    private static readonly Dictionary<string, string> nativeNames = new(StringComparer.Ordinal)
    {
        ["module_drag"] = "SFX_Module_Drag", ["module_drop"] = "SFX_Module_Drop",
        ["module_install"] = "SFX_Module_Install", ["module_remove"] = "SFX_Module_Remove",
        ["metal_machine_drag"] = "SFX_Metal_Machinery_Drag", ["metal_machine_drop"] = "SFX_Metal_Machinery_Drop",
        ["open_machine"] = "SFX_Open_Machine", ["close_machine"] = "SFX_Close_Machine",
        ["metal_scrap_drop"] = "SFX_Metal_Scrap_Drop"
    };
    internal static AudioClip Get(string name)
    {
        if (RoomAudioCache.IsLoop(name))
            throw new InvalidOperationException("长循环必须通过异步加载接口取得：" + name);
        if (clips.TryGetValue(name, out var cached) && AssemblyDebugUi.Alive(cached)) return cached;
        using var resource = typeof(Core).Assembly.GetManifestResourceStream("PCExpansion.Audio." + name + ".wav.gz")
            ?? throw new InvalidOperationException("缺少已采用音频：" + name);
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        gzip.CopyTo(buffer);
        var bytes = buffer.ToArray();
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x46464952 || reader.ReadUInt32() != bytes.Length - 8 || reader.ReadUInt32() != 0x45564157)
            throw new InvalidDataException("音频不是完整RIFF/WAVE：" + name);
        ushort format = 0, channels = 0, bits = 0;
        int rate = 0, offset = 0, length = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var chunk = reader.ReadUInt32(); var count = reader.ReadInt32(); var start = stream.Position;
            if (count < 0 || start + count > stream.Length) throw new InvalidDataException("音频块越界。");
            if (chunk == 0x20746d66 && count >= 16)
            {
                format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                reader.ReadInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (chunk == 0x61746164) { offset = checked((int)start); length = count; }
            stream.Position = start + count + (count & 1);
        }
        if (format != 1 || bits != 16 || channels is not (1 or 2) || rate is < 8000 or > 48000 ||
            length <= 0 || length % (channels * 2) != 0)
            throw new InvalidDataException("音频PCM格式不受支持：" + name);
        var values = new float[length / 2];
        // Assets carry headroom so the three 0–100 controls have useful range above their defaults.
        var gain = 1 / .70f;
        for (var i = 0; i < values.Length; i++)
            values[i] = Math.Clamp((short)(bytes[offset + i * 2] | bytes[offset + i * 2 + 1] << 8) / 32768f * gain, -1, 1);
        var clip = AudioClip.Create("PCExpansion." + name, values.Length / channels, channels, rate, false);
        try
        {
            if (!clip.SetData(new Il2CppStructArray<float>(values), 0)) throw new InvalidOperationException("音频PCM发布失败。");
            clips[name] = clip;
            return clip;
        }
        catch { UnityEngine.Object.Destroy(clip); throw; }
    }

    // Call every method in this class from the main thread. Only RoomAudioCache.Job does managed IO off-thread.
    internal static void RequestLoops()
    {
        var root = Path.Combine(MelonEnvironment.UserDataDirectory, "PCExpansion", "AudioCache");
        foreach (var profile in RoomAudioCache.Profiles)
        {
            if (TryLoop(profile.Name, out _) || pendingLoops.ContainsKey(profile.Name)) continue;
            var job = new RoomAudioCache.Job(profile, root, name =>
                typeof(Core).Assembly.GetManifestResourceStream("PCExpansion.Audio." + name + ".ogg")
                ?? throw new InvalidOperationException("缺少已采用压缩音频：" + name));
            pendingLoops.Add(profile.Name, new LoopLoad(profile, job, loopGeneration));
        }
    }

    internal static void PollLoops()
    {
        try
        {
            foreach (var load in pendingLoops.Values.ToArray())
            {
                if (load.Generation != loopGeneration) continue;
                if ((Stopwatch.GetTimestamp() - load.Started) / (double)Stopwatch.Frequency >= 60)
                    throw new TimeoutException("压缩音频加载超时：" + load.Profile.Name);
                if (load.Request == null)
                {
                    if (!load.Cache.Task.IsCompleted) continue;
                    var path = load.Cache.Task.GetAwaiter().GetResult();
                    load.Cache.Dispose();
                    load.Request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.OGGVORBIS);
                    var handler = load.Request.downloadHandler.TryCast<DownloadHandlerAudioClip>()
                        ?? throw new InvalidOperationException("压缩音频下载处理器不匹配。");
                    handler.compressed = true;
                    handler.streamAudio = false;
                    load.Request.timeout = 30;
                    load.Request.SendWebRequest();
                    continue;
                }
                if (!load.Request.isDone) continue;
                if (load.Request.result != UnityWebRequest.Result.Success)
                    throw new InvalidOperationException("压缩音频加载失败：" + load.Profile.Name + "；" + load.Request.error);
                load.Candidate ??= DownloadHandlerAudioClip.GetContent(load.Request);
                var clip = load.Candidate;
                if (!AssemblyDebugUi.Alive(clip)) throw new InvalidDataException("压缩音频未返回有效Clip：" + load.Profile.Name);
                if (clip!.loadState == AudioDataLoadState.Loading) continue;
                if (clip.loadState != AudioDataLoadState.Loaded || clip.channels != 2 || clip.frequency != 24000 ||
                    Math.Abs((long)clip.samples - load.Profile.Frames) > 24)
                    throw new InvalidDataException("压缩音频元数据不符：" + load.Profile.Name +
                        "，samples=" + clip.samples + "，channels=" + clip.channels + "，frequency=" + clip.frequency + "，state=" + clip.loadState);
                if (clip.samples != load.Profile.Frames)
                    MelonLogger.Warning("压缩音频原生帧数有微小差异：" + load.Profile.Name + "，预期=" + load.Profile.Frames + "，实际=" + clip.samples);
                clip.name = "PCExpansion." + load.Profile.Name;
                load.Request.Dispose();
                load.Request = null;
                clips[load.Profile.Name] = clip;
                load.Candidate = null;
                pendingLoops.Remove(load.Profile.Name);
            }
        }
        catch
        {
            // Remove failed work before throwing once. The controller decides when to retry.
            CancelPendingLoops();
            throw;
        }
    }

    internal static bool TryLoop(string name, out AudioClip? clip)
    {
        clip = null;
        if (!RoomAudioCache.IsLoop(name) || !clips.TryGetValue(name, out var cached) || !AssemblyDebugUi.Alive(cached) ||
            cached.loadState != AudioDataLoadState.Loaded) return false;
        clip = cached;
        return true;
    }

    internal static void CancelPendingLoops()
    {
        loopGeneration++;
        var cancelled = pendingLoops.Values.ToArray();
        pendingLoops.Clear();
        foreach (var load in cancelled)
        {
            load.Cache.Dispose();
            // A request may finish between polls; its unclaimed Clip is still ours to destroy.
            try
            {
                if (load.Candidate == null && load.Request != null && load.Request.isDone && load.Request.result == UnityWebRequest.Result.Success)
                    load.Candidate = DownloadHandlerAudioClip.GetContent(load.Request);
            }
            catch (Exception ex) { MelonLogger.Warning("取得待释放音频失败：" + ex.Message); }
            try { load.Request?.Abort(); }
            catch (Exception ex) { MelonLogger.Warning("取消音频请求失败：" + ex.Message); }
            try { load.Request?.Dispose(); }
            catch (Exception ex) { MelonLogger.Warning("释放音频请求失败：" + ex.Message); }
            if (AssemblyDebugUi.Alive(load.Candidate)) UnityEngine.Object.Destroy(load.Candidate);
            load.Request = null;
            load.Candidate = null;
        }
    }

    internal static AudioClip? Native(string identifier)
    {
        if (native.TryGetValue(identifier, out var cached) && AssemblyDebugUi.Alive(cached)) return cached;
        var directory = Il2Cpp.AudioDirectory.activeDirectory;
        if (directory == null) return null;
        directory.EnsureInit();
        AudioClip? clip = directory.HasAudioClip(identifier) ? directory.GetAudioClip(identifier) : null;
        // Some native directory IDs differ from serialized clip names. Borrow the same audited native clip.
        if (!AssemblyDebugUi.Alive(clip))
        {
            var name = nativeNames.TryGetValue(identifier, out var assetName) ? assetName : identifier;
            foreach (var assets in new[] { directory.audioAsset, directory.audioAsset2, directory.audioAsset3, directory.item1, directory.item2 })
            {
                if (assets == null) continue;
                foreach (var asset in assets)
                    if (asset != null && AssemblyDebugUi.Alive(asset.sound) && asset.sound.name == name)
                    { clip = asset.sound; break; }
                if (AssemblyDebugUi.Alive(clip)) break;
            }
        }
        if (AssemblyDebugUi.Alive(clip)) native[identifier] = clip!;
        return clip;
    }

    internal static void Release()
    {
        CancelPendingLoops();
        foreach (var clip in clips.Values) if (AssemblyDebugUi.Alive(clip)) UnityEngine.Object.Destroy(clip);
        clips.Clear();
        native.Clear();
    }
}
