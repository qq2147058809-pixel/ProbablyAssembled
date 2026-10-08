# 三个正式入口共用一个工程锁；同线程的入口嵌套复用持有者，独立进程仍互斥。
if (-not ('PCExpansion.Tools.BuildOperationLease' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace PCExpansion.Tools
{
    public sealed class BuildOperationLease : IDisposable
    {
        private sealed class HeldLock
        {
            internal FileStream Stream;
            internal int Thread;
            internal int Depth;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, HeldLock> Held =
            new Dictionary<string, HeldLock>(StringComparer.OrdinalIgnoreCase);
        private readonly string path;
        private bool released;

        private BuildOperationLease(string lockPath) { path = lockPath; }

        public static BuildOperationLease Enter(string lockPath, int timeoutMilliseconds)
        {
            string fullPath = Path.GetFullPath(lockPath);
            int thread = Thread.CurrentThread.ManagedThreadId;
            long started = Environment.TickCount64;
            Exception lastFailure = null;
            while (true)
            {
                lock (Gate)
                {
                    HeldLock existing;
                    if (Held.TryGetValue(fullPath, out existing))
                    {
                        if (existing.Thread == thread)
                        {
                            existing.Depth++;
                            return new BuildOperationLease(fullPath);
                        }
                    }
                    else
                    {
                        try
                        {
                            var stream = new FileStream(fullPath, FileMode.OpenOrCreate,
                                FileAccess.ReadWrite, FileShare.None);
                            Held.Add(fullPath, new HeldLock { Stream = stream, Thread = thread, Depth = 1 });
                            return new BuildOperationLease(fullPath);
                        }
                        catch (IOException ex) { lastFailure = ex; }
                    }
                }
                if (Environment.TickCount64 - started >= timeoutMilliseconds)
                    throw new IOException("等待工程操作独占锁超时：" + fullPath, lastFailure);
                Thread.Sleep(100);
            }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                if (released) return;
                HeldLock held;
                if (!Held.TryGetValue(path, out held) || held.Thread != Thread.CurrentThread.ManagedThreadId)
                    throw new InvalidOperationException("工程操作锁必须由持有线程释放。");
                released = true;
                if (--held.Depth == 0)
                {
                    Held.Remove(path);
                    held.Stream.Dispose();
                }
            }
        }
    }
}
'@
}

function Enter-PCExpansionBuildOperation {
    param([string]$ProjectDir)
    $lockPath = Join-Path $ProjectDir '.pcexpansion-deploy.lock'
    return [PCExpansion.Tools.BuildOperationLease]::Enter($lockPath, 120000)
}
