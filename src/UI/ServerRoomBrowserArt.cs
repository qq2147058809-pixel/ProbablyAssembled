using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace PCExpansion;

/// <summary>缓存浏览器圆角和导航图标；图标不依赖字体，不增加整页贴图。</summary>
internal static class ServerRoomBrowserArt
{
    private static Sprite? rounded;
    internal enum Icon { Back, Forward, Refresh, Home, Address, ChevronDown }
    private static readonly Sprite?[] icons = new Sprite?[6];
    internal static Sprite Rounded => GetRounded();

    internal static Sprite GetIcon(Icon icon)
    {
        var index = (int)icon;
        if (index < 0 || index >= icons.Length) throw new ArgumentOutOfRangeException(nameof(icon));
        if (AssemblyDebugUi.Alive(icons[index])) return icons[index]!;
        const int size = 32;
        var lines = new List<(float x1, float y1, float x2, float y2)>();
        void Line(float x1, float y1, float x2, float y2) => lines.Add((x1, y1, x2, y2));
        switch (icon)
        {
            case Icon.Back:
            case Icon.Forward:
                var flip = icon == Icon.Forward;
                float X(float x) => flip ? size - x : x;
                Line(X(24), 16, X(8), 16); Line(X(15), 9, X(8), 16); Line(X(8), 16, X(15), 23);
                break;
            case Icon.Home:
                Line(5, 15, 16, 6); Line(16, 6, 27, 15);
                Line(8, 14, 8, 26); Line(8, 26, 24, 26); Line(24, 26, 24, 14);
                Line(13, 26, 13, 19); Line(13, 19, 19, 19); Line(19, 19, 19, 26);
                break;
            case Icon.ChevronDown:
                Line(9, 12, 16, 19); Line(16, 19, 23, 12);
                break;
            default:
                var start = icon == Icon.Refresh ? 45 : 0;
                var end = icon == Icon.Refresh ? 315 : 360;
                for (var angle = start; angle < end; angle += 15)
                {
                    var a = angle * MathF.PI / 180; var b = Math.Min(angle + 15, end) * MathF.PI / 180;
                    Line(16 + 10 * MathF.Cos(a), 16 + 10 * MathF.Sin(a), 16 + 10 * MathF.Cos(b), 16 + 10 * MathF.Sin(b));
                }
                if (icon == Icon.Refresh) { Line(23, 9, 17, 9); Line(23, 9, 23, 3); }
                else { Line(6, 16, 26, 16); Line(16, 6, 16, 26); }
                break;
        }
        var pixels = new Color[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var alpha = 0f;
            foreach (var line in lines)
            {
                var dx = line.x2 - line.x1; var dy = line.y2 - line.y1;
                var t = Math.Clamp(((x + .5f - line.x1) * dx + (y + .5f - line.y1) * dy) / (dx * dx + dy * dy), 0, 1);
                var px = x + .5f - line.x1 - t * dx; var py = y + .5f - line.y1 - t * dy;
                alpha = Math.Max(alpha, Math.Clamp(2f - MathF.Sqrt(px * px + py * py), 0, 1));
            }
            pixels[(size - 1 - y) * size + x] = new Color(1, 1, 1, alpha);
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        try
        {
            texture.name = "PCExpansion.WebIcon." + icon;
            texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(new Il2CppStructArray<Color>(pixels)); texture.Apply(false, true);
            icons[index] = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f),
                100, 0, SpriteMeshType.FullRect, Vector4.zero);
            icons[index]!.name = texture.name;
            UnityEngine.Object.DontDestroyOnLoad(texture); UnityEngine.Object.DontDestroyOnLoad(icons[index]);
            return icons[index]!;
        }
        catch { UnityEngine.Object.Destroy(texture); throw; }
    }

    private static Sprite GetRounded()
    {
        if (AssemblyDebugUi.Alive(rounded)) return rounded!;
        const int size = 32;
        var pixels = new Color[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            const float radius = 8f;
            var dx = Math.Max(0, Math.Abs(x + .5f - 16) - 8);
            var dy = Math.Max(0, Math.Abs(y + .5f - 16) - 8);
            var alpha = Math.Clamp(radius + .5f - MathF.Sqrt(dx * dx + dy * dy), 0, 1);
            pixels[y * size + x] = new Color(1, 1, 1, alpha);
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        try
        {
            texture.name = "PCExpansion.WebRounded";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(new Il2CppStructArray<Color>(pixels));
            texture.Apply(false, true);
            rounded = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f),
                100, 0, SpriteMeshType.FullRect, new Vector4(8, 8, 8, 8));
            rounded.name = texture.name;
            UnityEngine.Object.DontDestroyOnLoad(texture);
            UnityEngine.Object.DontDestroyOnLoad(rounded);
            return rounded;
        }
        catch { UnityEngine.Object.Destroy(texture); throw; }
    }
}
