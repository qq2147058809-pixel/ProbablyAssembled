using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace ProbablyAssembled;

/// <summary>
/// 本 MOD 全部嵌入式 PNG 的统一入口；占格决定画布比例，PPU 自动计算。
/// 贴图规格由 Components 的物品表自动生成：目标文件按命名规则
/// &lt;词干&gt;_t&lt;n&gt;[_broken].png 命名并放入 assets/icons 后，重新构建即自动生效；
/// 缺图时自动回退到占位贴图（机箱用现有破损/完好图，配件用同级完好图）。
/// </summary>
internal static class SpriteAssets
{
    private sealed class Spec
    {
        internal readonly string PrimaryResource;
        internal readonly string FallbackResource;
        internal readonly int GridWidth;
        internal readonly int GridHeight;

        internal Spec(string primaryResource, string fallbackResource, int gridWidth, int gridHeight)
        {
            PrimaryResource = primaryResource;
            FallbackResource = fallbackResource;
            GridWidth = gridWidth;
            GridHeight = gridHeight;
        }
    }

    private static readonly Dictionary<string, Spec> Specs = BuildSpecs();

    private static Dictionary<string, Spec> BuildSpecs()
    {
        var specs = new Dictionary<string, Spec>();
        foreach (var item in Components.AllItems())
        {
            var type = item.Owner;
            var state = item.Broken ? "_broken" : "";
            var primary = "ProbablyAssembled.Assets." + type.Stem + "_t" + item.Tier + state + ".png";
            var fallback = type.IsCase
                ? "ProbablyAssembled.Assets." + (item.Broken ? "computer_case.png" : "computer_case_intact.png")
                : "ProbablyAssembled.Assets." + type.Stem + "_t" + item.Tier + ".png";
            specs[item.SpriteKey] = new Spec(primary, fallback, type.Width, type.Height);
        }

        // 拆机螺丝刀（非分级物品，单独登记）
        specs[UnboxTool.SpriteKey] = new Spec(
            "ProbablyAssembled.Assets.tool_screwdriver.png",
            "ProbablyAssembled.Assets.tool_screwdriver.png", 1, 3);
        specs[ContactCard.SpriteKey] = new Spec(
            "ProbablyAssembled.Assets.contact_card_0504.png",
            "ProbablyAssembled.Assets.contact_card_0504.png", 2, 1);
        specs[LowerAssemblerNpc.SpriteKey] = new Spec(
            "ProbablyAssembled.Assets.lower_assembler.png",
            "ProbablyAssembled.Assets.lower_assembler.png", 18, 13);
        specs[UpperComputerBuyerNpc.SpriteKey] = new Spec(
            "ProbablyAssembled.Assets.upper_computer_buyer.png",
            "ProbablyAssembled.Assets.upper_computer_buyer.png", 18, 13);
        return specs;
    }

    private static readonly Dictionary<string, Sprite> Cache = new();
    private static readonly Dictionary<string, Vector2> PortraitWorldSizes = new();
    private static readonly Dictionary<string, Vector2> PortraitPivots = new();

    /// <summary>高清立绘按原版 Sprite 的世界尺寸和锚点显示。</summary>
    internal static void SetPortraitReference(string? rawKey, Sprite reference)
    {
        var key = Core.Clean(rawKey);
        var rect = reference.rect;
        if (!Specs.ContainsKey(key) || reference.pixelsPerUnit <= 0 || rect.width <= 0 || rect.height <= 0) return;
        var worldSize = new Vector2(rect.width / reference.pixelsPerUnit, rect.height / reference.pixelsPerUnit);
        var pivot = new Vector2(reference.pivot.x / rect.width, reference.pivot.y / rect.height);
        if (PortraitWorldSizes.TryGetValue(key, out var previousSize) && previousSize == worldSize &&
            PortraitPivots.TryGetValue(key, out var previousPivot) && previousPivot == pivot) return;
        PortraitWorldSizes[key] = worldSize;
        PortraitPivots[key] = pivot;
        Cache.Remove(key);
    }

    internal static bool IsSpriteKey(string? rawKey) => Specs.ContainsKey(Core.Clean(rawKey));

    internal static Sprite? Get(string? rawKey)
    {
        var key = Core.Clean(rawKey);
        if (!Specs.TryGetValue(key, out var spec)) return null;
        if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
        try
        {
            byte[]? bytes = LoadEmbedded(spec, out var usedResource);
            if (bytes == null) return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = key + "_texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            if (!ImageConversion.LoadImage(texture, new Il2CppStructArray<byte>(bytes), true))
            {
                Core.Log?.Error("PNG 解码失败：" + usedResource);
                return null;
            }

            var pixelsPerUnit = PortraitWorldSizes.TryGetValue(key, out var referenceSize)
                ? Math.Max(texture.width / referenceSize.x, texture.height / referenceSize.y)
                : texture.width / (spec.GridWidth * 0.16f);
            var pivot = PortraitPivots.TryGetValue(key, out var referencePivot)
                ? referencePivot : new Vector2(0.5f, 0.5f);
            UnityEngine.Object.DontDestroyOnLoad(texture);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                pivot, pixelsPerUnit);
            sprite.name = key;
            UnityEngine.Object.DontDestroyOnLoad(sprite);
            Cache[key] = sprite;
            if (key == LowerAssemblerNpc.SpriteKey || key == UpperComputerBuyerNpc.SpriteKey)
                Core.Log?.Msg("[深空装机] NPC 立绘已加载：" + key + "，" + texture.width + "×" + texture.height +
                    "，PPU=" + pixelsPerUnit + "，Point 过滤，原版尺寸参照=" + PortraitWorldSizes.ContainsKey(key));
            Core.Debug("已加载贴图：" + key + "（" + texture.width + "×" + texture.height + "，来源=" + usedResource + "）");
            return sprite;
        }
        catch (Exception ex)
        {
            Core.Log?.Error("贴图加载失败（" + key + "）：" + ex);
            return null;
        }
    }

    private static byte[]? LoadEmbedded(Spec spec, out string usedResource)
    {
        usedResource = spec.PrimaryResource;
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(spec.PrimaryResource);
        if (stream == null && spec.FallbackResource != spec.PrimaryResource)
        {
            // 目标贴图尚未提供：回退到占位贴图，等图补齐后重新构建即可。
            usedResource = spec.FallbackResource;
            Core.Debug("贴图未找到，使用占位图：" + spec.PrimaryResource + " → " + spec.FallbackResource);
            stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(spec.FallbackResource);
        }
        if (stream == null)
        {
            Core.Log?.Error("DLL 中找不到贴图资源：" + spec.PrimaryResource);
            return null;
        }
        using (stream)
        {
            var data = new byte[stream.Length];
            var offset = 0;
            while (offset < data.Length)
            {
                var read = stream.Read(data, offset, data.Length - offset);
                if (read <= 0) break;
                offset += read;
            }
            return data;
        }
    }
}

[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.ResolveSpriteByName))]
internal static class ResolveSpriteByNamePatch
{
    private static bool Prefix(string name, ref Sprite __result)
    {
        if (!SpriteAssets.IsSpriteKey(name)) return true;
        var sprite = SpriteAssets.Get(name);
        if (sprite == null) return true;
        __result = sprite;
        return false;
    }
}

[HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.ResolveItemSprite))]
internal static class ResolveItemSpritePatch
{
    private static void Postfix(GameItemElement __instance)
    {
        try
        {
            if (__instance == null || !SpriteAssets.IsSpriteKey(__instance.spritePath)) return;
            var sprite = SpriteAssets.Get(__instance.spritePath);
            if (sprite == null) return;
            if (__instance.inner?.image != null) __instance.inner.image.sprite = sprite;
            if (__instance.fakeInner?.image != null) __instance.fakeInner.image.sprite = sprite;
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("刷新贴图失败：" + ex.Message);
        }
    }
}
