using System;
using System.Collections.Generic;

namespace PCExpansion;

/// <summary>Shared workroom cue interface. Its version is independent of the mod release version.
/// Call on the game thread after a committed action; namespace eventId by producer and command.
/// Region music/ambience and inventory ownership remain with the provider.</summary>
public static class WorkroomAudioApi
{
    public const int ApiVersion = 1;
    private static readonly HashSet<string> cues = new(StringComparer.Ordinal)
    { "pickup", "drop", "insert", "remove", "open", "close", "store", "metal_drop", "metal_drag", "recycle", "assemble", "disassemble", "door", "ui", "confirm", "repair_complete", "lease_notice", "warning", "rotate", "machine_start", "machine_stop" };
    private static readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private static readonly Queue<string> order = new();
    public static bool Supports(string cue) => cue != null && cues.Contains(cue);
    public static bool TryPlay(string cue, string eventId)
    {
        if (!RoomAudio.OnGameThread || !Supports(cue) || string.IsNullOrWhiteSpace(eventId) || eventId.Length > 160 || !RoomAudio.Active || seen.Contains(eventId)) return false;
        if (!RoomAudio.Play(cue)) return false;
        seen.Add(eventId); order.Enqueue(eventId);
        if (order.Count > 256) seen.Remove(order.Dequeue());
        return true;
    }
    internal static void Reset() { seen.Clear(); order.Clear(); }
}
