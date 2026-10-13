using System;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace PCExpansion;

/// <summary>Shared three-channel sound controls, with a neutral input tail before returning ownership.</summary>
internal static class RoomAudioSettingsUi
{
    private static Canvas? canvas;
    private static GameObject? reference, blocker, panel;
    private static readonly Slider?[] sliders = new Slider?[3];
    private static readonly TextMeshProUGUI?[] values = new TextMeshProUGUI?[3];
    private static bool open, tail;
    private static bool cleanupPending;
    private static int neutralFrames;
    private static long generation;
    private static long retryAt;
    private static bool canRequest;
    internal static bool BlocksInput => open || tail;
    internal static bool CanOpen => canRequest && !BlocksInput && AssemblyDebugUi.Alive(canvas) && canvas!.gameObject.activeInHierarchy;

    internal static void Open()
    {
        if (!CanOpen) return;
        EventSystem.current?.SetSelectedGameObject(null);
        Refresh();
        open = true; blocker!.SetActive(true); panel!.SetActive(true);
        RoomAudio.Play("open");
    }
    private static T Add<T>(GameObject go) where T : UnityEngine.Component =>
        go.AddComponent(Il2CppType.Of<T>()).TryCast<T>() ?? throw new InvalidOperationException("声音设置组件未创建。");

    internal static void Tick(bool visible, bool canOpen)
    {
        canRequest = visible && canOpen && !cleanupPending;
        try
        {
            if (cleanupPending) { if (Environment.TickCount64 >= retryAt) Reset(); return; }
            if (tail)
            {
                neutralFrames = WorkroomGameAdapter.InputNeutral() ? neutralFrames + 1 : 0;
                if (neutralFrames >= 2) { tail = false; neutralFrames = 0; }
            }
            if (!visible)
            {
                if (open) Close();
                if (AssemblyDebugUi.Alive(canvas)) canvas!.gameObject.SetActive(false);
                return;
            }
            if (!AssemblyDebugUi.Alive(canvas))
            {
                if (Environment.TickCount64 < retryAt) return;
                retryAt = Environment.TickCount64 + 5000;
                Build();
            }
            canvas!.gameObject.SetActive(true);
            WorkroomGameAdapter.ForgetLetterboxCanvases(canvas, null, null);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            WorkroomView.Fit(reference!, new Rect(0, 0, Screen.width, Screen.height));
            if (open && Input.GetKeyDown(KeyCode.Escape)) Close();
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("声音设置暂不可用：" + ex.Message);
            Reset(); retryAt = Environment.TickCount64 + 5000;
        }
    }
    private static void Build()
    {
        var text = string.Join("", RoomAudio.Text("entry"), RoomAudio.Text("title"), RoomAudio.Text("music"),
            RoomAudio.Text("ambience"), RoomAudio.Text("effects"), RoomAudio.Text("hint"), RoomAudio.Text("reset"), RoomAudio.Text("close"), RoomAudio.Text("speaker_hint"), "0123456789%");
        var font = AssemblyDebugFonts.Prepare(text, text);
        // Canvas stores sortingOrder as signed 16 bits. Above panels (32650), below transition (32700).
        canvas = AssemblyDebugUi.NewCanvas("PCExpansion.AudioSettings", 32690);
        reference = AssemblyDebugUi.New("AudioSettingsReference", canvas.transform);
        var referenceRect = AssemblyDebugUi.Rect(reference);
        referenceRect.anchorMin = referenceRect.anchorMax = referenceRect.pivot = new Vector2(.5f, .5f);
        referenceRect.sizeDelta = new Vector2(1280, 720); referenceRect.anchoredPosition = Vector2.zero;
        blocker = AssemblyDebugUi.Image(reference.transform, "AudioModalBlocker", new Color(0, 0, 0, .55f), true).gameObject;
        AssemblyDebugUi.Place(blocker, 0, 0, 1280, 720);
        panel = AssemblyDebugUi.Image(reference.transform, "AudioSettingsPanel", new Color(.10f, .16f, .16f, 1), true).gameObject;
        AssemblyDebugUi.Place(panel, 390, 224, 500, 272);
        var title = AssemblyDebugUi.Text(panel.transform, "Title", RoomAudio.Text("title"), font, 23, true);
        AssemblyDebugUi.Place(title.gameObject, 22, 10, 390, 35);
        Button(panel.transform, "CloseAudio", RoomAudio.Text("close"), font, 418, 12, 60, 30, Close);
        var labels = new[] { "music", "ambience", "effects" };
        for (var i = 0; i < 3; i++)
        {
            var channel = i;
            var sliderGeneration = generation;
            var y = 62 + i * 48;
            var label = AssemblyDebugUi.Text(panel.transform, "Channel" + i, RoomAudio.Text(labels[i]), font, 16);
            AssemblyDebugUi.Place(label.gameObject, 22, y, 136, 30);
            values[i] = AssemblyDebugUi.Text(panel.transform, "Volume" + i, "", font, 16);
            AssemblyDebugUi.Place(values[i]!.gameObject, 422, y, 56, 30);
            var track = AssemblyDebugUi.Image(panel.transform, "Slider" + i, new Color(.27f, .35f, .32f, 1), true);
            AssemblyDebugUi.Place(track.gameObject, 165, y + 10, 240, 12);
            var fill = AssemblyDebugUi.Image(track.transform, "Fill", new Color(.64f, .69f, .55f, 1), false);
            AssemblyDebugUi.Stretch(fill.gameObject);
            var handle = AssemblyDebugUi.Image(track.transform, "Handle", new Color(.82f, .82f, .68f, 1), true);
            AssemblyDebugUi.Place(handle.gameObject, 0, -4, 12, 20);
            var slider = Add<Slider>(track.gameObject);
            slider.direction = Slider.Direction.LeftToRight; slider.minValue = 0; slider.maxValue = 100; slider.wholeNumbers = true;
            slider.fillRect = AssemblyDebugUi.Rect(fill.gameObject); slider.handleRect = AssemblyDebugUi.Rect(handle.gameObject);
            slider.targetGraphic = handle; slider.transition = Selectable.Transition.None;
            slider.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<float>>((Action<float>)(value =>
            { if (sliderGeneration != generation || !open) return; RoomAudio.Set(channel, Mathf.RoundToInt(value)); Refresh(); })));
            sliders[i] = slider;
        }
        var hint = AssemblyDebugUi.Text(panel.transform, "Hint", RoomAudio.Text("hint"), font, 13);
        AssemblyDebugUi.Place(hint.gameObject, 22, 214, 334, 40); hint.enableWordWrapping = true;
        Button(panel.transform, "ResetAudio", RoomAudio.Text("reset"), font, 364, 220, 114, 30,
            () => { RoomAudio.Set(0, 60); RoomAudio.Set(1, 35); RoomAudio.Set(2, 70); Refresh(); });
        Refresh(); blocker.SetActive(false); panel.SetActive(false);
    }
    private static GameObject Button(Transform parent, string name, string text, TMP_FontAsset font,
        float x, float y, float width, float height, Action action)
    {
        var buttonGeneration = generation;
        var go = AssemblyDebugUi.Button(canvas!, parent, name, new Color(.28f, .35f, .29f, 1), () =>
        {
            if (buttonGeneration != generation || !AssemblyDebugUi.Alive(canvas) || !canvas!.gameObject.activeInHierarchy) return;
            action();
        });
        AssemblyDebugUi.Place(go, x, y, width, height);
        var label = AssemblyDebugUi.Text(go.transform, "Label", text, font, 16);
        AssemblyDebugUi.Stretch(label.gameObject);
        return go;
    }
    private static void Refresh()
    {
        var amounts = new[] { RoomAudio.MusicPercent, RoomAudio.AmbiencePercent, RoomAudio.EffectsPercent };
        for (var i = 0; i < amounts.Length; i++)
        {
            if (sliders[i] == null || values[i] == null) continue;
            sliders[i]!.SetValueWithoutNotify(amounts[i]); values[i]!.text = amounts[i] + "%";
        }
    }
    private static void Close()
    {
        if (!open) return;
        open = false; tail = true; neutralFrames = 0;
        if (AssemblyDebugUi.Alive(blocker)) blocker!.SetActive(false);
        if (AssemblyDebugUi.Alive(panel)) panel!.SetActive(false);
        RoomAudio.SaveSettings(); RoomAudio.Play("close");
    }
    internal static void Reset()
    {
        generation++;
        canRequest = false;
        if (open) RoomAudio.SaveSettings();
        open = tail = false; neutralFrames = 0;
        try
        {
            if (AssemblyDebugUi.Alive(canvas))
            {
                canvas!.gameObject.SetActive(false);
                WorkroomGameAdapter.ForgetLetterboxCanvases(canvas, null, null);
                AssemblyDebugUi.Forget(canvas);
                UnityEngine.Object.Destroy(canvas.gameObject);
            }
            cleanupPending = false;
        }
        catch (Exception ex)
        {
            cleanupPending = true; retryAt = Environment.TickCount64 + 5000;
            Core.Log?.Warning("声音设置清理稍后重试：" + ex.Message); return;
        }
        canvas = null; reference = blocker = panel = null;
        Array.Clear(sliders, 0, sliders.Length); Array.Clear(values, 0, values.Length);
    }
}
