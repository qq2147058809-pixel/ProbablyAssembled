using System;
using System.Collections.Generic;
using System.Linq;

namespace PCExpansion;

/// <summary>Translate a captured room selection without changing any item's contents or pose.</summary>
internal static class WorkroomGroupMove
{
    internal static bool CanMove(IReadOnlyList<WorkroomStorage.RoomDrop> candidates, int dx, int dy, long epoch)
        => !WorkroomStorage.Busy && Check(candidates, dx, dy, epoch, out _);

    internal static bool Move(IReadOnlyList<WorkroomStorage.RoomDrop> candidates, int dx, int dy, long epoch)
    {
        if (WorkroomStorage.Busy || !WorkroomTrial.CanCollectCurrent(true))
        { WorkroomStorage.Notify("recovery"); return false; }
        if (!Check(candidates, dx, dy, epoch, out var failure))
        { WorkroomStorage.Notify(failure); return false; }
        var moved = false;
        WorkroomStorage.Run(() =>
        {
            // Run owns Busy here. Recheck the session and the entire captured
            // command inside that ownership before constructing replacements.
            if (!WorkroomTrial.CanCollectCurrent(true)) { WorkroomStorage.Notify("recovery"); return; }
            if (!Check(candidates, dx, dy, epoch, out failure))
            { WorkroomStorage.Notify(failure); return; }
            if (dx != 0 || dy != 0)
            {
                var selected = candidates.ToDictionary(drop => drop.Id, StringComparer.Ordinal);
                var next = WorkroomStorage.State.Items.Select(record => selected.TryGetValue(record.Id, out var drop)
                    ? new WorkroomStorageState.Record { Id = record.Id, Place = record.Place, Slot = record.Slot,
                        X = drop.X + dx, Y = drop.Y + dy, Item = record.Item }
                    : record).ToList();
                // Replace validates and serializes before publishing. The live
                // records and snapshots are never edited during preparation.
                WorkroomStorage.State.Replace(next);
            }
            moved = true;
        });
        if (moved) WorkroomStorage.NotifyText(WorkroomComponentTemplates.Text("placed"));
        return moved;
    }

    private static bool Check(IReadOnlyList<WorkroomStorage.RoomDrop> candidates, int dx, int dy,
        long epoch, out string failure)
    {
        failure = "recovery";
        var state = WorkroomStorage.State;
        if (!state.Ready || state.Epoch != epoch || WorkroomItemCodec.CleanupPending || state.PendingRecords.Count != 0)
            return false;
        failure = "source_changed";
        if (candidates == null || candidates.Count == 0) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var drop in candidates)
            if (drop == null || !drop.Unchanged || !ids.Add(drop.Id)) return false;
        failure = "invalid_position";
        var occupied = new bool[WorkroomStorageState.Columns * WorkroomStorageState.Rows];
        // Exclude every selected old footprint together, so moving into another
        // selected item's former cells is legal. Fill only actual nonzero cells.
        foreach (var record in state.RoomItems)
            if (!ids.Contains(record.Id) && !Fill(occupied, record.Item.Footprint, record.X, record.Y)) return false;
        foreach (var drop in candidates)
        {
            var x = (long)drop.X + dx; var y = (long)drop.Y + dy;
            if (x < 0 || y < 0 || x > WorkroomStorageState.Columns || y > WorkroomStorageState.Rows ||
                !Fill(occupied, drop.Item.Footprint, (int)x, (int)y)) return false;
        }
        return state.Ready && state.Epoch == epoch;
    }

    private static bool Fill(bool[] occupied, WorkroomItemCodec.Shape shape, int x, int y)
    {
        if (shape == null || shape.Width <= 0 || shape.Height <= 0 || shape.Cells == null ||
            (long)shape.Width * shape.Height != shape.Cells.Length || x < 0 || y < 0 ||
            (long)x + shape.Width > WorkroomStorageState.Columns || (long)y + shape.Height > WorkroomStorageState.Rows)
            return false;
        for (var iy = 0; iy < shape.Height; iy++)
            for (var ix = 0; ix < shape.Width; ix++)
                if (shape.Cells[iy * shape.Width + ix] != 0 && occupied[(y + iy) * WorkroomStorageState.Columns + x + ix])
                    return false;
        for (var iy = 0; iy < shape.Height; iy++)
            for (var ix = 0; ix < shape.Width; ix++)
                if (shape.Cells[iy * shape.Width + ix] != 0) occupied[(y + iy) * WorkroomStorageState.Columns + x + ix] = true;
        return true;
    }
}
