using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Il2Cpp;

namespace PCExpansion;

/// <summary>只复用相同保存内容的验证结果；不保留原生物品、可变机器记录或快照。</summary>
internal static class WorkroomMachineFacts
{
    internal sealed class Facts
    {
        internal Facts(WorkroomMachineAssembly.MachineInfo machine, WorkroomMachineAssembly.Assessment assessment)
        {
            Value = WorkroomMachineAssembly.Value(machine);
            BodyValue = machine.Body.Value;
            BodyBroken = machine.Body.Broken;
            Complete = assessment.Complete;
            PartCount = machine.Parts.Count;
            // Flattened records have no Nested list. Copy only immutable scalar values.
            Parts = Array.AsReadOnly(assessment.Parts.Select(p =>
                new CaseEconomy.PartRecord(p.TypeTag, p.Tier, p.Broken, p.Value)).ToArray());
            ConfigurationTier = WorkroomMachineAssembly.ConfigurationTier(machine);
            StoredIdentities = Array.AsReadOnly(machine.Parts.SelectMany(p => WorkroomItemCodec.Identities(p.Item)).ToArray());
        }

        internal long Value { get; }
        internal long BodyValue { get; }
        internal bool BodyBroken { get; }
        internal bool Complete { get; }
        internal int PartCount { get; }
        internal int ConfigurationTier { get; }
        internal IReadOnlyList<CaseEconomy.PartRecord> Parts { get; }
        internal IReadOnlyList<int> StoredIdentities { get; }
    }

    private sealed record Entry(string Identifier, int Identity, int Length, string? Raw, byte[]? Digest, Facts Facts, long Used);
    private const int MaxEntries = 32, MaxCharacters = 4 * 1024 * 1024;
    private static readonly Dictionary<IntPtr, Entry> Entries = new();
    private static long epoch = -1, sequence;
    private static int characters;

    // Called on the game thread. Pointer selects a candidate only; it never establishes identity.
    internal static Facts? Read(GameItem item)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var hit = false;
        var length = 0;
        try
        {
            var currentEpoch = WorkroomStorage.State.Epoch;
            if (epoch != currentEpoch)
            {
                Entries.Clear(); characters = 0; epoch = currentEpoch;
            }
            var pointer = item.Pointer;
            if (!item.IsTag(WorkroomMachineAssembly.Tag)) { Remove(pointer); return null; }
            var identifier = Core.Clean(item.identifier);
            var identity = item.uniqueId;
            var raw = item.state.GetTag(WorkroomMachineAssembly.Tag).valueString;
            length = raw?.Length ?? 0;
            // Oversized records still cache immutable facts; retain a digest instead
            // of making their size a reason to parse the entire tree every frame.
            var digest = length > MaxCharacters ? SHA256.HashData(MemoryMarshal.AsBytes(raw.AsSpan())) : null;
            if (Entries.TryGetValue(pointer, out var entry) && entry.Identifier == identifier &&
                entry.Identity == identity && entry.Length == length && (digest == null ?
                    string.Equals(entry.Raw, raw, StringComparison.Ordinal) :
                    entry.Digest != null && digest.AsSpan().SequenceEqual(entry.Digest)))
            {
                Entries[pointer] = entry with { Used = ++sequence };
                hit = true;
                return entry.Facts;
            }
            // Invalid or changed content must never fall back to an earlier successful result.
            Remove(pointer);
            var machine = WorkroomMachineAssembly.ReadAssessed(item, out var assessment)
                ?? throw new InvalidOperationException("机器记录在读取期间变化。");
            var facts = new Facts(machine, assessment!);
            if (epoch != WorkroomStorage.State.Epoch || identifier != Core.Clean(item.identifier) || identity != item.uniqueId ||
                !item.IsTag(WorkroomMachineAssembly.Tag) ||
                !string.Equals(raw, item.state.GetTag(WorkroomMachineAssembly.Tag).valueString, StringComparison.Ordinal))
                throw new InvalidOperationException("机器记录在验证期间变化。");
            if (raw != null)
            {
                var retained = digest == null ? length : 0;
                while (Entries.Count >= MaxEntries || characters + retained > MaxCharacters)
                    Remove(Entries.OrderBy(pair => pair.Value.Used).First().Key);
                Entries.Add(pointer, new Entry(identifier, identity, length, digest == null ? raw : null, digest, facts, ++sequence));
                characters += retained;
            }
            return facts;
        }
        finally { WorkroomFeedbackDiagnostics.MachineFactsRead(start, hit, length); }
    }

    private static void Remove(IntPtr pointer)
    {
        if (!Entries.TryGetValue(pointer, out var entry)) return;
        characters -= entry.Raw?.Length ?? 0;
        Entries.Remove(pointer);
    }
}
