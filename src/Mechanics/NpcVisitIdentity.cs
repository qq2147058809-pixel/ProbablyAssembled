using System;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Stateless visit checks; each story keeps its own session, phase and visit token.</summary>
internal static class NpcVisitIdentity
{
    internal static bool Exists(PlayerStore store, string token, string prefix, Func<StoreClient?, bool> isStory)
    {
        bool Match(StoreClient? client) => isStory(client) && client!.IsTag(prefix + token);
        // A current visitor owns its visit through entry and exit animation,
        // after leaving the queue and until the native reference is cleared.
        if (Match(store.currentClientInstance?.storeClient)) return true;
        var queue = store.storeClientManager?.clientStack;
        if (queue != null) foreach (var client in queue) if (Match(client)) return true;
        return false;
    }

    internal static bool HasExact(StoreClient? client, string token, string prefix, Func<StoreClient?, bool> isStory)
    {
        if (token.Length == 0 || !isStory(client) || client!.tag == null) return false;
        var count = 0;
        foreach (var rawTag in client.tag)
        {
            var tag = Core.Clean(rawTag);
            if (!tag.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (tag != prefix + token) return false;
            count++;
        }
        return count == 1;
    }

    internal static void RemoveDuplicateQueued(PlayerStore store, string? token, string prefix, Func<StoreClient?, bool> isStory)
    {
        var queue = store.storeClientManager?.clientStack;
        if (string.IsNullOrEmpty(token) || queue == null) return;
        // Keep the current visitor, otherwise the first exact queued visit.
        // Ordinary visits and missing/multiple/conflicting tokens remain intact.
        var retained = HasExact(store.currentClientInstance?.storeClient, token!, prefix, isStory);
        for (var i = 0; i < queue.Count;)
        {
            if (!HasExact(queue[i], token!, prefix, isStory)) { i++; continue; }
            if (retained) queue.RemoveAt(i);
            else { retained = true; i++; }
        }
    }
}
