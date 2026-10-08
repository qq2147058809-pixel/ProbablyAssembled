using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>独立悬停提示，复用原版信息内容，不搬移/显示/隐藏店铺的原版窗口。</summary>
internal sealed class WorkroomItemInfo : IDisposable
{
    private sealed class CachedContent
    {
        internal WorkroomItemCodec.Snapshot Snapshot = null!;
        internal WorkroomItemInformation.Content Content = null!;
        internal long Count, Value, RetryAt, LastUsed;
        internal int Orientation;
        internal bool Broken, Flipped, NativeReady;
        internal bool Matches(WorkroomItemCodec.Snapshot item) => ReferenceEquals(Snapshot, item) &&
            Count == item.Count && Value == item.Value && Broken == item.Broken &&
            Orientation == item.Orientation && Flipped == item.Flipped;
    }
    private readonly Dictionary<string, CachedContent> cache = new(StringComparer.Ordinal);
    private long epoch = -1, warnAt;
    private string? locale;
    private Canvas? canvas;
    private GameObject? face, background;
    private TextMeshProUGUI? label;
    private string? displayed;
    private Vector2 screen;

    internal void Update(WorkroomStorageState.Record? record)
    {
        if (record == null || !WorkroomStorage.State.Ready || WorkroomStorage.Busy) { Clear(); return; }
        var stage = "快照信息";
        try
        {
            if (epoch != WorkroomStorage.State.Epoch || locale != LanguageText.LocaleCode)
            {
                cache.Clear(); epoch = WorkroomStorage.State.Epoch; locale = LanguageText.LocaleCode;
            }
            if (!cache.TryGetValue(record.Id, out var saved) || !saved.Matches(record.Item))
            {
                // Records publish immutable snapshots. A move of another item
                // advances Revision but must not rebuild this nested machine.
                var fallback = WorkroomItemInformation.FromSnapshot(record.Item);
                saved = new CachedContent { Snapshot = record.Item, Content = fallback,
                    Count = record.Item.Count, Value = record.Item.Value, Broken = record.Item.Broken,
                    Orientation = record.Item.Orientation, Flipped = record.Item.Flipped };
                if (!cache.ContainsKey(record.Id) && cache.Count >= 32)
                {
                    string? oldest = null; var lastUsed = long.MaxValue;
                    foreach (var pair in cache)
                        if (pair.Value.LastUsed < lastUsed) { oldest = pair.Key; lastUsed = pair.Value.LastUsed; }
                    if (oldest != null) cache.Remove(oldest);
                }
                cache[record.Id] = saved;
            }
            saved.LastUsed = Environment.TickCount64;
            if (!saved.NativeReady && !WorkroomItemCodec.CleanupPending && Environment.TickCount64 >= saved.RetryAt)
            {
                saved.RetryAt = Environment.TickCount64 + 2000;
                try { saved.Content = WorkroomItemInformation.ReadNative(record.Item); saved.NativeReady = true; }
                catch (Exception ex) { Warn("原版内容", record.Item.Identifier, ex); }
            }
            stage = "悬停框绘制";
            var infoKey = ItemTooltipHandler.current?.key ?? KeyCode.LeftAlt;
            var text = Input.GetKey(infoKey) ? saved.Content.Advanced : saved.Content.Basic;
            Draw(text, saved.Content.Characters);
        }
        catch (Exception ex) { Clear(); Warn(stage, record.Item.Identifier, ex); }
    }

    private void Build(string characters)
    {
        var font = AssemblyDebugFonts.Prepare(characters, characters);
        canvas = AssemblyDebugUi.NewCanvas("PCExpansion.WorkroomItemInfo", 32680);
        try
        {
            canvas.GetComponent<GraphicRaycaster>().enabled = false;
            face = AssemblyDebugUi.Image(canvas.transform, "TooltipBorder", new Color(.53f,.29f,.25f,1), false).gameObject;
            face.SetActive(false);
            background = AssemblyDebugUi.Image(face.transform, "TooltipBackground", new Color(.16f,.13f,.20f,1), false).gameObject;
            label = AssemblyDebugUi.Text(face.transform, "ItemInformation", "", font, 14, true);
            label.richText = true; label.enableWordWrapping = true;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.color = new Color(.77f,.77f,.69f,1);
            label.overflowMode = TextOverflowModes.Ellipsis;
        }
        catch
        {
            AssemblyDebugUi.Destroy(canvas);
            canvas = null; face = background = null; label = null; displayed = null;
            throw;
        }
    }

    private void Draw(string text, string characters)
    {
        if (!AssemblyDebugUi.Alive(canvas)) Build(characters);
        EnsureOverlay();
        // Item information uses the smaller vanilla text scale, independently
        // of the workroom's 1280x720 interaction layout.
        var scale = Mathf.Min(Screen.width/1920f, Screen.height/1080f);
        var currentScreen = new Vector2(Screen.width, Screen.height);
        var rect = AssemblyDebugUi.Rect(face!);
        if (displayed != text || screen != currentScreen)
        {
            var font = AssemblyDebugFonts.Prepare(characters, characters);
            label!.font = font; label.fontSharedMaterial = font.material;
            label.text = text;
            var width = Mathf.Min(420, Screen.width/scale-16);
            var maxHeight = Screen.height/scale-16;
            var preferred = label.GetPreferredValues(text, width-24, float.PositiveInfinity);
            if (preferred.y+24 > maxHeight)
            {
                width = Mathf.Min(620, Screen.width/scale-16);
                preferred = label.GetPreferredValues(text, width-24, float.PositiveInfinity);
            }
            var height = Mathf.Clamp(preferred.y+24, 44, maxHeight);
            AssemblyDebugUi.Place(face!, 0, 0, width, height);
            AssemblyDebugUi.Place(background!, 2, 2, width-4, height-4);
            AssemblyDebugUi.Place(label.gameObject, 12, 12, width-24, height-24);
            rect.localScale = new Vector3(scale,scale,1);
            displayed = text; screen = currentScreen;
        }
        var mouse = (Vector2)Input.mousePosition;
        var w = rect.rect.width*scale; var h = rect.rect.height*scale;
        var x = mouse.x+16*scale;
        if (x+w > Screen.width-8) x = mouse.x-w-16*scale;
        x = Mathf.Clamp(x, 8, Screen.width-w-8);
        var top = Mathf.Clamp(mouse.y-12*scale, h+8, Screen.height-8);
        rect.anchoredPosition = new Vector2(x, top-Screen.height);
        face!.SetActive(true);
    }

    private void Warn(string stage, string identifier, Exception ex)
    {
        if (Environment.TickCount64 < warnAt) return;
        warnAt = Environment.TickCount64 + 10000;
        Core.Log?.Warning($"[工作间物品提示] stage={stage}，item={identifier}：{ex}");
    }
    internal void Clear()
    {
        WorkroomUiCleanup.Deactivate(face);
    }
    internal void EnsureOverlay()
    {
        WorkroomGameAdapter.ForgetLetterboxCanvases(canvas!, null!, null!);
        WorkroomView.Overlay(canvas!);
    }
    public void Dispose()
    {
        cache.Clear(); epoch = -1; locale = null; displayed = null; screen = Vector2.zero;
        var cleanup = new WorkroomUiCleanup();
        cleanup.Run("item_info.stop", () => WorkroomUiCleanup.DeactivateCanvas(canvas));
        cleanup.Run("item_info.canvas", () =>
        {
            if (!ReferenceEquals(canvas, null))
            {
                WorkroomGameAdapter.ForgetLetterboxCanvases(canvas!, null!, null!);
                AssemblyDebugUi.Destroy(canvas!);
            }
            canvas = null; face = background = null; label = null;
        });
        cleanup.ThrowIfFailed();
    }
}
