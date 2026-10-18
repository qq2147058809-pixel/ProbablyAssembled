using System;
using System.Collections;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>Shared Mod skin for operation notices and owned room-entry descriptions.</summary>
internal static class ModOperationNotice
{
    internal static readonly Color Background = new(.18f, .24f, .20f, 1);
    internal static readonly Color Ink = new(.93f, .92f, .81f, 1);
    internal static readonly Color Edge = new(.88f, .78f, .47f, 1);
    private static Canvas? fallback;
    private static object? expiry;
    private static long generation;

    internal static void Style(GameObject root, TextMeshProUGUI text)
    {
        var face = root.GetComponent<Image>();
        face.color = Background;
        face.raycastTarget = false;
        UiRoundedGeometry.Fill(face, UiRoundedGeometry.WindowRadius);
        var border = AssemblyDebugUi.Image(root.transform, "OperationNoticeBorder", Edge, false);
        UiRoundedGeometry.Outline(border, UiRoundedGeometry.WindowRadius);
        AssemblyDebugUi.Stretch(border.gameObject);
        text.color = Ink;
        text.raycastTarget = false;
    }

    internal static void Show(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        try
        {
            ClearFallback();
            // Reuse the live view's single notice and its existing expiry/layout path.
            if (WorkroomTrial.TryNotifyOperation(message)) return;
            ShowFallback(message);
        }
        catch (Exception ex)
        {
            ClearFallback();
            Core.Debug("操作提示显示失败：" + ex.Message);
            // Preserve the message if custom UI is unavailable during loading.
            try { StoreUIManager.Instance?.Notify(message, "default"); }
            catch (Exception native) { Core.Debug("操作提示原生显示失败：" + native.Message); }
        }
    }

    private static void ShowFallback(string message)
    {
        // This path exists only before/without a live workroom view. It has one
        // bounded, real-time expiry coroutine and no frame polling or input ownership.
        fallback = AssemblyDebugUi.NewCanvas("PCExpansion.OperationNotice", 32690);
        var font = AssemblyDebugFonts.Prepare(message, message);
        var face = AssemblyDebugUi.Image(fallback.transform, "OperationNotice", Background, false);
        var text = AssemblyDebugUi.Text(face.transform, "Message", message, font, 15);
        text.richText = false;
        text.enableWordWrapping = true;
        text.enableAutoSizing = false;
        text.fontSize = 15;
        var height = Mathf.Clamp(text.GetPreferredValues(message, 868, float.PositiveInfinity).y + 20, 48, 300);
        var scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        var rect = AssemblyDebugUi.Rect(face.gameObject);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(0, -18 * scale);
        rect.sizeDelta = new Vector2(900, height);
        rect.localScale = new Vector3(scale, scale, 1);
        AssemblyDebugUi.Place(text.gameObject, 16, 10, 868, height - 20);
        text.fontSizeMin = 12; text.fontSizeMax = 15; text.enableAutoSizing = true;
        Style(face.gameObject, text);
        var current = ++generation;
        expiry = MelonCoroutines.Start(Expire(current, fallback, Math.Max(5000, Math.Min(15000, 3000 + message.Length * 65)) / 1000f));
    }

    private static IEnumerator Expire(long current, Canvas owner, float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        if (current != generation) yield break;
        expiry = null;
        Destroy(owner);
        if (ReferenceEquals(fallback, owner)) fallback = null;
    }

    private static void ClearFallback()
    {
        generation++;
        var pending = expiry; expiry = null;
        if (pending != null)
        {
            try { MelonCoroutines.Stop(pending); }
            catch (Exception ex) { Core.Debug("操作提示等待已结束：" + ex.Message); }
        }
        var prior = fallback; fallback = null;
        Destroy(prior);
    }

    private static void Destroy(Canvas? owner)
    {
        try { if (AssemblyDebugUi.Alive(owner)) UnityEngine.Object.Destroy(owner!.gameObject); }
        catch (Exception ex) { Core.Debug("操作提示清理：" + ex.Message); }
    }
}
