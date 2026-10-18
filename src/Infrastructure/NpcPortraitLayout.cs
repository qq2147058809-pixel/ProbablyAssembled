using UnityEngine;

namespace PCExpansion;

/// <summary>只改变立绘的世界尺寸和锚点，保留原始纹理及完整 Sprite 矩形。</summary>
internal static class NpcPortraitLayout
{
    internal static void Apply(string key, ref float pixelsPerUnit, ref Vector2 pivot)
    {
        // 主体范围取当前 288×211 原图的 alpha>=128 轮廓，三位主体均贴画布底边。
        // 以此基准调整显示占比，不裁切、重采样或重新编码 PNG。
        float left, width, scale;
        switch (key)
        {
            case LowerAssemblerNpc.SpriteKey:
                left = 52; width = 184; scale = 0.85f;
                break;
            case UpperComputerBuyerNpc.SpriteKey:
                left = 50; width = 187; scale = 0.85f;
                break;
            case PhoneAssemblerNpc.SpriteKey:
                left = 30; width = 227; scale = 0.80f;
                break;
            default:
                return;
        }

        // 缩小后主体居于参照画布中心，底边保持参照位置；兼容非中心原版锚点。
        pivot = new Vector2((left + width * 0.5f) / 288f - (0.5f - pivot.x) / scale,
            pivot.y / scale);
        pixelsPerUnit /= scale;
    }
}
