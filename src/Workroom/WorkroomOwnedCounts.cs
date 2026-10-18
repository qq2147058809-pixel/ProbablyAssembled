using System;
using System.Collections.Generic;

namespace PCExpansion;

// The reservation catalog counts loose stock, never installed children or pending transfers.
internal static class WorkroomOwnedCounts
{
    internal static Dictionary<string, int>? Read()
    {
        if (!WorkroomStorage.State.Ready || WorkroomStorage.Busy || WorkroomItemCodec.CleanupPending) return null;
        try
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var candidate in WorkroomSelection.Candidates(WorkroomSelection.Source.All))
            {
                var item = candidate.Item;
                if (item.Count != 1 || Components.Find(item.Identifier) == null ||
                    !WorkroomComponentAssembly.Intact(item)) continue;
                counts[item.Identifier] = checked(counts.GetValueOrDefault(item.Identifier) + 1);
            }
            return WorkroomSelection.CandidatesComplete ? counts : null;
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("[预订库存数量] 无法完整读取，显示未就绪：" + ex.Message);
            return null;
        }
    }
}
