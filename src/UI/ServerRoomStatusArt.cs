using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace PCExpansion;

/// <summary>机位状态的小图形；形状与文字共同表达状态，不依赖字体符号。</summary>
internal static class ServerRoomStatusArt
{
    internal enum State { Locked, Empty, Running, Stopped, Leased }

    private static readonly Sprite?[] sprites = new Sprite?[5];
    private static readonly Color[] colors = {
        new(.396f, .443f, .455f, 1), // 深蓝灰
        new(.608f, .655f, .604f, 1), // 灰绿
        new(.475f, .737f, .616f, 1), // 青绿
        new(.773f, .553f, .408f, 1), // 锈橙
        new(.824f, .702f, .459f, 1)  // 暖黄铜
    };

    internal static Color ColorFor(State state) => colors[(int)state];

    internal static Sprite SpriteFor(State state)
    {
        var index = (int)state;
        if (index < 0 || index >= sprites.Length) throw new ArgumentOutOfRangeException(nameof(state));
        if (AssemblyDebugUi.Alive(sprites[index])) return sprites[index]!;
        const int size = 16;
        var pixels = new Color[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var visible = state switch
            {
                State.Locked => (x is 3 or 5 or 10 or 12) && y is >= 3 and <= 12,
                State.Empty => x is >= 2 and <= 13 && y is >= 2 and <= 13 &&
                    (x is <= 4 or >= 11 || y is <= 4 or >= 11),
                State.Running => (x - 7.5f) * (x - 7.5f) + (y - 7.5f) * (y - 7.5f) <= 38f,
                State.Stopped => x is >= 2 and <= 13 && y is >= 2 and <= 13 &&
                    (x is <= 4 or >= 11 || y is <= 4 or >= 11 || y is >= 7 and <= 8 && x is >= 5 and <= 10),
                State.Leased => Math.Abs(x - 7.5f) + Math.Abs(y - 7.5f) <= 6.5f,
                _ => false
            };
            pixels[y * size + x] = visible ? Color.white : Color.clear;
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        try
        {
            texture.name = "PCExpansion.RackState." + state;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(new Il2CppStructArray<Color>(pixels));
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f),
                100, 0, SpriteMeshType.FullRect, Vector4.zero);
            sprite.name = texture.name;
            sprites[index] = sprite;
            UnityEngine.Object.DontDestroyOnLoad(texture);
            UnityEngine.Object.DontDestroyOnLoad(sprite);
            return sprite;
        }
        catch { UnityEngine.Object.Destroy(texture); throw; }
    }
}
