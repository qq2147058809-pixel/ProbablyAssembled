using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace PCExpansion;

/// <summary>工作间界面贴图独立于物品工厂；视图重建复用同一份贴图。</summary>
internal static class WorkroomArt
{
    private static readonly Dictionary<string, Sprite> sprites = new();

    internal static Sprite Get(string name)
    {
        if (sprites.TryGetValue(name, out var cached) && AssemblyDebugUi.Alive(cached)) return cached;
        using var input = typeof(Core).Assembly.GetManifestResourceStream("PCExpansion.Assets." + name + ".png")
            ?? throw new InvalidOperationException("工作间贴图缺失：" + name);
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            texture.name = "PCExpansion." + name;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.anisoLevel = 0;
            if (!ImageConversion.LoadImage(texture, new Il2CppStructArray<byte>(bytes.ToArray()), true))
                throw new InvalidOperationException("工作间PNG解码失败：" + name);
            UnityEngine.Object.DontDestroyOnLoad(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), 100);
            sprite.name = "PCExpansion." + name;
            UnityEngine.Object.DontDestroyOnLoad(sprite);
            sprites[name] = sprite;
            return sprite;
        }
        catch { UnityEngine.Object.Destroy(texture); throw; }
    }
}
