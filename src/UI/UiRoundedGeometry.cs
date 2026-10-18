using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>Owner-created UI surfaces only. Cached nine-slices keep corners fixed when a panel resizes.</summary>
internal static class UiRoundedGeometry
{
    internal const int WindowRadius = 8, ControlRadius = 4;

    [Flags]
    internal enum Corners
    {
        None = 0, TopLeft = 1, TopRight = 2, BottomRight = 4, BottomLeft = 8,
        Top = TopLeft | TopRight, Bottom = BottomLeft | BottomRight, All = Top | Bottom
    }

    private static readonly Dictionary<(int Radius, Corners Corners, bool Outline), Sprite> sprites = new();

    internal static void Fill(Image image, int radius = ControlRadius, Corners corners = Corners.All)
    {
        image.sprite = GetSprite(radius, corners);
        image.type = Image.Type.Sliced;
    }

    internal static void Outline(Image image, int radius = ControlRadius, Corners corners = Corners.All)
    {
        image.sprite = GetSprite(radius, corners, true);
        image.type = Image.Type.Sliced;
    }

    internal static Sprite GetSprite(int radius, Corners corners = Corners.All, bool outline = false)
    {
        if (radius is < 1 or > WindowRadius) throw new ArgumentOutOfRangeException(nameof(radius));
        var key = (radius, corners, outline);
        if (sprites.TryGetValue(key, out var cached) && AssemblyDebugUi.Alive(cached)) return cached;
        var size = radius * 4;
        var pixels = new Color[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
            pixels[y * size + x] = new Color(1, 1, 1, Coverage(x + .5f, y + .5f, size, radius, corners, outline));

        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Sprite? sprite = null;
        try
        {
            texture.name = "PCExpansion.UiRound." + radius + "." + corners + (outline ? ".Outline" : ".Fill");
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(new Il2CppStructArray<Color>(pixels));
            texture.Apply(false, true);
            sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f),
                100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            sprite.name = texture.name;
            UnityEngine.Object.DontDestroyOnLoad(texture);
            UnityEngine.Object.DontDestroyOnLoad(sprite);
            sprites[key] = sprite;
            return sprite;
        }
        catch
        {
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
            UnityEngine.Object.Destroy(texture);
            throw;
        }
    }

    // Texture-space coordinates start at the bottom left. The one-pixel outline
    // is the difference of two inset silhouettes, including their curved corners.
    internal static float Coverage(float x, float y, int size, int radius, Corners corners, bool outline)
    {
        var half = size * .5f;
        var corner = y >= half ? x < half ? Corners.TopLeft : Corners.TopRight :
            x < half ? Corners.BottomLeft : Corners.BottomRight;
        float distance;
        if ((corners & corner) != 0)
        {
            var dx = Math.Max(0, Math.Abs(x - half) - (half - radius));
            var dy = Math.Max(0, Math.Abs(y - half) - (half - radius));
            distance = radius - MathF.Sqrt(dx * dx + dy * dy);
        }
        else distance = Math.Min(Math.Min(x, size - x), Math.Min(y, size - y));
        var outer = Math.Clamp(distance + .5f, 0, 1);
        return outline ? outer - Math.Clamp(distance - .5f, 0, 1) : outer;
    }
}
