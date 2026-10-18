using System;
using System.Collections.Generic;
using System.Linq;

namespace PCExpansion;

// Small bounded batch planner. It works on private occupancy maps, never on
// native inventories, so a failed plan cannot move or destroy any item.
internal static class WorkroomStoryPacking
{
    internal sealed class Board
    {
        internal int Width, Height;
        internal bool[] Free = Array.Empty<bool>();
    }
    internal readonly record struct Part(int Width, int Height);
    internal readonly record struct Placement(int Board, int X, int Y, bool Rotated);

    internal static bool TryPlan(IReadOnlyList<Board> boards, IReadOnlyList<Part> parts,
        out Placement[] result)
    {
        var found = new Placement[parts.Count];
        result = Array.Empty<Placement>();
        if (parts.Count == 0 || parts.Count > 6 || boards.Count == 0 ||
            parts.Any(p => p.Width <= 0 || p.Height <= 0)) return false;
        foreach (var b in boards)
            if (b.Width <= 0 || b.Height <= 0 || (long)b.Width * b.Height > 65536 ||
                b.Free.Length != b.Width * b.Height) return false;
        var maps = boards.Select(b => (bool[])b.Free.Clone()).ToArray();
        var order = Enumerable.Range(0, parts.Count).OrderByDescending(i =>
            (long)parts[i].Width * parts[i].Height).ToArray();
        var attempts = 0;
        bool Visit(int depth)
        {
            if (depth == order.Length) return true;
            if (++attempts > 200000) return false;
            var index = order[depth]; var p = parts[index];
            for (var b = 0; b < boards.Count; b++)
            for (var rotate = 0; rotate < (p.Width == p.Height ? 1 : 2); rotate++)
            {
                var w = rotate == 0 ? p.Width : p.Height;
                var h = rotate == 0 ? p.Height : p.Width;
                var board = boards[b]; var map = maps[b];
                for (var y = 0; y <= board.Height - h; y++)
                for (var x = 0; x <= board.Width - w; x++)
                {
                    var fits = true;
                    for (var dy = 0; dy < h && fits; dy++)
                    for (var dx = 0; dx < w; dx++)
                        if (!map[(y + dy) * board.Width + x + dx]) { fits = false; break; }
                    if (!fits) continue;
                    for (var dy = 0; dy < h; dy++)
                    for (var dx = 0; dx < w; dx++) map[(y + dy) * board.Width + x + dx] = false;
                    found[index] = new Placement(b, x, y, rotate != 0);
                    if (Visit(depth + 1)) return true;
                    for (var dy = 0; dy < h; dy++)
                    for (var dx = 0; dx < w; dx++) map[(y + dy) * board.Width + x + dx] = true;
                }
            }
            return false;
        }
        if (!Visit(0)) return false;
        result = found; return true;
    }
}
