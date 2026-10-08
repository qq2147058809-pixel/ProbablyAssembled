using System;

namespace PCExpansion;

/// <summary>与原版 GridShape 相同的方向和全局占格旋转；不修改原记录。</summary>
internal static class WorkroomItemPose
{
    internal static int Turn(int orientation, bool clockwise)
    {
        if (orientation < 0 || orientation > 3) throw new InvalidOperationException("物品方向无效。");
        return (orientation + (clockwise ? 3 : 1)) % 4;
    }

    internal static WorkroomItemCodec.Shape Rotate(WorkroomItemCodec.Shape shape, bool clockwise)
    {
        if (shape.Width <= 0 || shape.Height <= 0 || (long)shape.Width * shape.Height > 65536 ||
            shape.Cells == null || shape.Cells.Length != (long)shape.Width * shape.Height)
            throw new InvalidOperationException("物品占格无效。");
        var result = new WorkroomItemCodec.Shape
        { Width = shape.Height, Height = shape.Width, Cells = new byte[shape.Cells.Length] };
        for (var y = 0; y < shape.Height; y++)
            for (var x = 0; x < shape.Width; x++)
            {
                var nextX = clockwise ? shape.Height - 1 - y : y;
                var nextY = clockwise ? x : shape.Width - 1 - x;
                result.Cells[nextY * result.Width + nextX] = shape.Cells[y * shape.Width + x];
            }
        return result;
    }
}
