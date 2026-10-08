using System;
using System.Diagnostics;
using Il2Cpp;
using UnityEngine;

namespace PCExpansion;

// Temporary, bounded probes for player-only reproduction. No item mutation.
internal static class WorkroomFeedbackDiagnostics
{
    private static long caseNoticeAt;
    private static int caseCalls;
    private static double caseMilliseconds, caseMaximum;
    private static long factsNoticeAt;
    private static int factsHits, factsMisses, factsCharacters;
    private static double factsMilliseconds;

    internal static void MachineFactsRead(long start, bool hit, int characters)
    {
        if (characters == 0) return;
        if (hit) factsHits++; else factsMisses++;
        factsCharacters = Math.Max(factsCharacters, characters);
        factsMilliseconds += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        var now = Environment.TickCount64;
        if (now < factsNoticeAt) return;
        Core.Log?.Msg($"[PCFeedback-facts] hits={factsHits}; misses={factsMisses}; totalMs={factsMilliseconds:F1}; maxChars={factsCharacters}; frame={Time.frameCount}");
        factsNoticeAt = now + 2000;
        factsHits = factsMisses = factsCharacters = 0; factsMilliseconds = 0;
    }

    internal sealed class Operation : IDisposable
    {
        private readonly string name;
        private readonly long start = Stopwatch.GetTimestamp();
        private long previous = Stopwatch.GetTimestamp();
        private readonly System.Text.StringBuilder stages = new();
        internal Operation(string name) { this.name = name; }
        internal void Mark(string stage)
        {
            var now = Stopwatch.GetTimestamp();
            stages.Append($" {stage}={(now - previous) * 1000.0 / Stopwatch.Frequency:F1}ms;");
            previous = now;
        }
        public void Dispose()
        {
            var elapsed = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            if (elapsed >= 30) Core.Log?.Msg($"[PCFeedback-operation] {name}; totalMs={elapsed:F1};{stages}");
        }
    }

    internal static void CaseEvaluated(GameItem item, long start)
    {
        var elapsed = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        caseCalls++;
        caseMilliseconds += elapsed;
        caseMaximum = Math.Max(caseMaximum, elapsed);
        var now = Environment.TickCount64;
        if (now < caseNoticeAt) return;
        if (caseMilliseconds >= 30)
            Core.Log?.Msg($"[PCFeedback-case] item={item.identifier}; calls={caseCalls}; totalMs={caseMilliseconds:F1}; maxMs={caseMaximum:F1}; frame={Time.frameCount}");
        caseNoticeAt = now + 2000;
        caseCalls = 0; caseMilliseconds = caseMaximum = 0;
    }

}
