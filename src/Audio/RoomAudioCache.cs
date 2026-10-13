using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PCExpansion;

/// <summary>Managed IO only. The cache belongs to this mod under the player's UserData directory.</summary>
internal static class RoomAudioCache
{
    internal sealed class Profile
    {
        internal readonly string Name, Sha256;
        internal readonly int Bytes, Frames;
        internal Profile(string name, string sha256, int bytes, int frames)
        { Name = name; Sha256 = sha256; Bytes = bytes; Frames = frames; }
        internal string FileName => "pce-audio-" + Name + "-" + Sha256 + ".ogg";
    }

    // Adopted q6 music / q4 ambience; gains are already baked into the OGG files.
    internal static readonly Profile[] Profiles =
    {
        new("workroom_theme_loop", "85fb8b894646ae1b030ac9af75c9beff66389b6eec3d4e820e2785923d6cbb73", 983158, 3291429),
        new("serverroom_theme_loop", "a0179c2087b555a739f5cd8a231a8702cc94433227270c9057ab4e29edc71330", 916747, 3840000),
        new("workroom_loop", "288802b921c5c8b18da50067803f10cafa2fc5c70a6e53564cb06e6aaa5ed3d8", 195541, 720000),
        new("ventilation_loop", "00a10e0a6224319f9dc823395d02d80a7654b0ef3a5ee456e6e5f9985d832391", 194241, 720000),
        new("fan_loop", "3821b7dffe9af5a74acd1955fc622c4484a866c3c49cbaffea1e35c61ec18f25", 205435, 720000)
    };
    private static readonly object publishGate = new();

    internal static bool IsLoop(string name)
    {
        foreach (var profile in Profiles) if (profile.Name == name) return true;
        return false;
    }

    internal sealed class Job : IDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        private bool disposed;
        internal readonly Task<string> Task;
        internal Job(Profile profile, string root, Func<string, Stream> openResource)
        {
            Task = System.Threading.Tasks.Task.Run(() => Prepare(profile, root, openResource, cancellation.Token));
            // Every task is observed even when the scene cancels before the next PollLoops.
            _ = Task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        public void Dispose()
        {
            lock (publishGate)
            {
                if (disposed) return;
                disposed = true;
                cancellation.Cancel();
            }
            _ = Task.ContinueWith(_ => cancellation.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private static string Prepare(Profile profile, string root, Func<string, Stream> openResource, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(root);
        EnsureOrdinaryDirectory(root);
        var path = Path.Combine(root, profile.FileName);
        lock (publishGate)
        {
            token.ThrowIfCancellationRequested();
            RemoveObsoleteFiles(root, token);
            if (Matches(path, profile, token)) return path;
        }
        var temporary = Path.Combine(root, profile.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var resource = openResource(profile.Name))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[65536];
                long length = 0;
                int count;
                while ((count = resource.Read(buffer, 0, buffer.Length)) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    length += count;
                    if (length > profile.Bytes) throw new InvalidDataException("压缩音频长度超出清单：" + profile.Name);
                    hash.AppendData(buffer, 0, count);
                    output.Write(buffer, 0, count);
                }
                token.ThrowIfCancellationRequested();
                if (length != profile.Bytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(profile.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("内嵌压缩音频与采用清单不符：" + profile.Name);
                output.Flush(true);
            }
            lock (publishGate)
            {
                token.ThrowIfCancellationRequested();
                // A previous cancelled generation cannot publish after its Dispose returns.
                EnsureOrdinaryDirectory(root);
                EnsureOrdinaryFile(path);
                if (!Matches(path, profile, token)) File.Move(temporary, path, true);
                return path;
            }
        }
        finally
        {
            // Only this job's uniquely named temporary file is removed.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool Matches(string path, Profile profile, CancellationToken token)
    {
        EnsureOrdinaryFile(path);
        if (!File.Exists(path)) return false;
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length != profile.Bytes) return false;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            token.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, count);
        }
        token.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset()).Equals(profile.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveObsoleteFiles(string root, CancellationToken token)
    {
        foreach (var path in Directory.EnumerateFiles(root, "pce-audio-*.ogg", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            foreach (var profile in Profiles)
            {
                if (name == profile.FileName) break;
                if (!Regex.IsMatch(name, "^pce-audio-" + Regex.Escape(profile.Name) + "-[a-f0-9]{64}\\.ogg$", RegexOptions.CultureInvariant)) continue;
                EnsureOrdinaryFile(path);
                File.Delete(path);
                break;
            }
        }
    }

    private static void EnsureOrdinaryDirectory(string root)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(root)); directory != null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("音频缓存目录不能经由链接：" + directory.FullName);
    }
    private static void EnsureOrdinaryFile(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new IOException("音频缓存目标不是普通文件：" + path);
    }
}
