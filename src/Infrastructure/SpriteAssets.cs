using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace PCExpansion;

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
            var primary = "PCExpansion.Assets." + type.Stem + "_t" + item.Tier + state + ".png";
            var fallback = type.IsCase
                ? "PCExpansion.Assets." + (item.Broken ? "computer_case.png" : "computer_case_intact.png")
                : "PCExpansion.Assets." + type.Stem + "_t" + item.Tier + ".png";
            specs[item.SpriteKey] = new Spec(primary, fallback, type.Width, type.Height);
        }

        specs[ContactCard.SpriteKey] = new Spec(
            "PCExpansion.Assets.contact_card_0504.png",
            "PCExpansion.Assets.contact_card_0504.png", 2, 1);
        specs[ComputerManual.SpriteKey] = new Spec(
            "PCExpansion.Assets.computer_manual_icon.png",
            "PCExpansion.Assets.computer_manual_icon.png", 2, 3);
        specs[ComputerSign.SpriteKey] = new Spec(
            "PCExpansion.Assets.computer_sign.png",
            "PCExpansion.Assets.computer_sign.png", 2, 2);
        specs[LowerAssemblerNpc.SpriteKey] = new Spec(
            "PCExpansion.Assets.lower_assembler.png",
            "PCExpansion.Assets.lower_assembler.png", 18, 13);
        specs[PhoneAssemblerNpc.SpriteKey] = new Spec(
            "PCExpansion.Assets.phone_assembler.png",
            "PCExpansion.Assets.phone_assembler.png", 18, 13);
        specs[UpperComputerBuyerNpc.SpriteKey] = new Spec(
            "PCExpansion.Assets.upper_computer_buyer.png",
            "PCExpansion.Assets.upper_computer_buyer.png", 18, 13);
        specs[SecuritySeizureMerchant.SpriteKey] = new Spec(
            "PCExpansion.Assets.jiang_bai.png",
            "PCExpansion.Assets.upper_computer_buyer.png", 18, 13);
        specs[SecuritySeizureBoxes.LargeSpriteKey] = new Spec(
            "PCExpansion.Assets.security_seizure_large.png",
            "PCExpansion.Assets.workroom_storage_chest.png", 10, 3);
        specs[SecuritySeizureBoxes.SmallSpriteKey] = new Spec(
            "PCExpansion.Assets.security_seizure_small.png",
            "PCExpansion.Assets.workroom_storage_chest.png", 5, 3);
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

    internal static bool IsSpriteKey(string? rawKey) => Specs.ContainsKey(Core.Clean(rawKey)) || WorkroomComponentParts.IsSpriteKey(Core.Clean(rawKey));

    internal static Sprite? PartArtwork(string artwork, int width, int height)
    {
        var resource = "PCExpansion.Assets." + artwork + ".png";
        if (Assembly.GetExecutingAssembly().GetManifestResourceInfo(resource) == null) return null;
        if (!Specs.ContainsKey(artwork)) Specs[artwork] = new Spec(resource, resource, width, height);
        return Get(artwork);
    }

    internal static Sprite? Get(string? rawKey)
    {
        var key = Core.Clean(rawKey);
        if (WorkroomComponentParts.IsSpriteKey(key)) return WorkroomComponentParts.Sprite(key);
        if (!Specs.TryGetValue(key, out var spec)) return null;
        if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
        Texture2D? texture = null;
        Sprite? sprite = null;
        var published = false;
        try
        {
            byte[]? bytes = LoadEmbedded(spec, out var usedResource);
            if (bytes == null) return null;

            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = key + "_texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            // Native inventory Images alpha-test sprite pixels during hover/drag.
            // Keep the shared texture readable so that test can sample transparent edges.
            if (!ImageConversion.LoadImage(texture, new Il2CppStructArray<byte>(bytes), false))
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
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                pivot, pixelsPerUnit);
            sprite.name = key;
            UnityEngine.Object.DontDestroyOnLoad(sprite);
            Cache[key] = sprite;
            published = true;
            if (key == LowerAssemblerNpc.SpriteKey || key == PhoneAssemblerNpc.SpriteKey ||
                key == UpperComputerBuyerNpc.SpriteKey || key == SecuritySeizureMerchant.SpriteKey)
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
        finally
        {
            if (!published)
            {
                try { if (sprite != null) UnityEngine.Object.Destroy(sprite); }
                catch (Exception ex) { Core.Log?.Warning("未发布贴图Sprite释放失败：" + ex.Message); }
                try { if (texture != null) UnityEngine.Object.Destroy(texture); }
                catch (Exception ex) { Core.Log?.Warning("未发布贴图Texture释放失败：" + ex.Message); }
            }
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
