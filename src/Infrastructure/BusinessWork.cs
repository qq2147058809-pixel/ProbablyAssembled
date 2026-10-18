using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace PCExpansion;

/// <summary>Main-thread work exists only after an event or an unfinished operation requests it.</summary>
internal static class BusinessWork
{
    private sealed class Job
    {
        internal string Key = "";
        internal Func<bool> Step = null!;
        internal object? Handle;
        internal int Interval, Limit;
        internal bool Requested, Running;
    }

    private static readonly Dictionary<string, Job> jobs = new(StringComparer.Ordinal);
    private static readonly HashSet<string> exhausted = new(StringComparer.Ordinal);
    private static int runningDepth;
    internal static int PendingCount => jobs.Count;

    // A repeated request coalesces into the same job. In particular a callback
    // cannot reset its own retry budget by publishing another notification.
    internal static void Schedule(string key, Func<bool> step, int delayMs = 50,
        int intervalMs = 500, int maxAttempts = 120)
    {
        if (string.IsNullOrEmpty(key) || step == null || delayMs < 0 || intervalMs < 1 || maxAttempts < 1)
            throw new ArgumentException("业务延迟任务参数无效。");
        // A native callback inside recovery may broadcast to a different job
        // which later requests this key again. Keep exhaustion across that
        // chain; only a request outside business execution opens a new window.
        if (runningDepth == 0) exhausted.Clear();
        else if (exhausted.Contains(key)) return;
        if (jobs.TryGetValue(key, out var existing))
        {
            existing.Step = step;
            existing.Requested = true;
            return;
        }
        var job = new Job { Key = key, Step = step, Interval = intervalMs, Limit = maxAttempts };
        jobs.Add(key, job);
        try { job.Handle = MelonCoroutines.Start(Run(job, delayMs)); }
        catch (Exception ex)
        {
            Remove(job);
            Core.Log?.Warning("业务恢复未能启动，待下一次操作重试：" + key + "；" + ex.Message);
        }
    }

    internal static void Cancel(string key)
    {
        if (!jobs.TryGetValue(key, out var job)) return;
        jobs.Remove(key);
        if (job.Handle == null || job.Running) return;
        try { MelonCoroutines.Stop(job.Handle); }
        catch (Exception ex) { Core.Debug("业务等待取消：" + key + "；" + ex.Message); }
    }

    internal static void CancelAll()
    {
        foreach (var key in new List<string>(jobs.Keys)) Cancel(key);
        exhausted.Clear();
    }

    private static bool Owns(Job job) => jobs.TryGetValue(job.Key, out var current) && ReferenceEquals(current, job);
    private static void Remove(Job job) { if (Owns(job)) jobs.Remove(job.Key); }

    private static IEnumerator Run(Job job, int delayMs)
    {
        try
        {
            // Even a zero-delay request never re-enters the transaction that queued it.
            yield return new WaitForSecondsRealtime(Math.Max(1, delayMs) / 1000f);
            for (var attempt = 0; attempt < job.Limit && Owns(job); attempt++)
            {
                job.Requested = false;
                var again = false;
                job.Running = true;
                runningDepth++;
                try { again = job.Step(); }
                catch (Exception ex)
                {
                    again = true;
                    if (attempt == 0) Core.Log?.Warning("业务操作暂未完成，保留状态并延迟恢复：" + job.Key + "；" + ex.Message);
                }
                finally { job.Running = false; runningDepth--; }
                if (!Owns(job) || !again && !job.Requested) yield break;
                if (attempt + 1 == job.Limit)
                {
                    exhausted.Add(job.Key);
                    Core.Log?.Warning("业务自动重试已暂停，未完成凭据保留；下次相关操作将继续：" + job.Key);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(job.Interval / 1000f);
            }
        }
        finally { Remove(job); }
    }
}
