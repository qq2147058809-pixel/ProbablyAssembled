#nullable disable
using System;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>Owned room-entry descriptions. Never reparent or restyle the shared native tooltip.</summary>
internal static class SceneEntryTooltip
{
    private static Canvas canvas;
    private static Image face;
    private static TextMeshProUGUI label;
    private static TooltipContainer owner;
    private static string content;
    private static int screenWidth, screenHeight;
    private static float scale, width, height;
    internal static TooltipContainer Owner => owner;

    internal static void Show(TooltipContainer source, TooltipManager native)
    {
        var changedOwner = ReferenceEquals(owner, null) || owner.Pointer != source.Pointer;
        try
        {
            Ensure();
            if (changedOwner && WorkroomUiCleanup.IsAlive(native)) native.HideTooltip();
            var message = source.content ?? "";
            if (content != message || screenWidth != Screen.width || screenHeight != Screen.height) Layout(message);
            owner = source;
            canvas.gameObject.SetActive(true);
            Move();
        }
        catch { Reset(); throw; }
    }

    private static void Ensure()
    {
        if (WorkroomUiCleanup.IsAlive(canvas)) return;
        canvas = AssemblyDebugUi.NewCanvas("PCExpansion.SceneEntryTooltip", 32690);
        canvas.GetComponent<GraphicRaycaster>().enabled = false;
        face = AssemblyDebugUi.Image(canvas.transform, "SceneDescription", ModOperationNotice.Background, false);
        label = AssemblyDebugUi.Text(face.transform, "Description", "", AssemblyDebugFonts.Prepare("", ""), 15, true);
        label.fontSize = 15;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.richText = false;
        label.enableWordWrapping = true;
        ModOperationNotice.Style(face.gameObject, label);
        // An empty first description must still get measured after recreation.
        content = null;
        screenWidth = screenHeight = -1;
    }

    private static void Layout(string message)
    {
        label.font = AssemblyDebugFonts.Prepare(message, message);
        label.fontSharedMaterial = label.font.material;
        label.enableAutoSizing = false;
        label.fontSize = 15;
        label.text = message;
        screenWidth = Screen.width; screenHeight = Screen.height;
        scale = Mathf.Min(screenWidth / 1280f, screenHeight / 720f);
        var maximumWidth = Mathf.Min(440, screenWidth / scale - 16);
        var preferred = label.GetPreferredValues(message, maximumWidth - 24, float.PositiveInfinity);
        width = Mathf.Clamp(preferred.x + 24, 100, maximumWidth);
        height = Mathf.Clamp(label.GetPreferredValues(message, width - 24, float.PositiveInfinity).y + 16,
            36, screenHeight / scale - 16);
        AssemblyDebugUi.Place(label.gameObject, 12, 8, width - 24, height - 16);
        label.fontSizeMin = 12; label.fontSizeMax = 15; label.enableAutoSizing = true;
        var rect = AssemblyDebugUi.Rect(face.gameObject);
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = new Vector3(scale, scale, 1);
        content = message;
    }

    // Called only while one owned description is visible, from the existing hover tail.
    internal static void Move()
    {
        if (ReferenceEquals(owner, null) || !WorkroomUiCleanup.IsAlive(canvas)) return;
        if (screenWidth != Screen.width || screenHeight != Screen.height) Layout(content ?? "");
        var point = Position(Input.mousePosition, width * scale, height * scale, scale, Screen.width, Screen.height);
        AssemblyDebugUi.Rect(face.gameObject).anchoredPosition = point;
    }

    internal static Vector2 Position(Vector3 mouse, float w, float h, float factor, float screenW, float screenH)
    {
        var gap = 16 * factor; var margin = 8 * factor;
        var x = mouse.x + gap;
        if (x + w > screenW - margin) x = mouse.x - gap - w;
        var top = mouse.y - gap;
        if (top - h < margin) top = mouse.y + gap + h;
        return new Vector2(Mathf.Clamp(x, margin, screenW - margin - w),
            Mathf.Clamp(top, h + margin, screenH - margin));
    }

    internal static void Hide(Component source = null)
    {
        if (source != null && (ReferenceEquals(owner, null) || owner.Pointer != source.Pointer)) return;
        owner = null;
        if (WorkroomUiCleanup.IsAlive(canvas)) canvas.gameObject.SetActive(false);
    }

    internal static void Reset()
    {
        Hide();
        if (WorkroomUiCleanup.IsAlive(canvas)) AssemblyDebugUi.Destroy(canvas);
        canvas = null; face = null; label = null; content = null;
    }
}
