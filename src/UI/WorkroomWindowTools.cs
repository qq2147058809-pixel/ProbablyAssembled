using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

internal static class WorkroomWindowTools
{
    internal static WorkroomAssemblyWindow? TopAt(IEnumerable<WorkroomAssemblyWindow> windows, Vector2 point) =>
        windows.Where(window => window.Hit(point)).OrderByDescending(window => window.Root.transform.GetSiblingIndex()).FirstOrDefault();

    internal static WorkroomAssemblyWindow? TopOpen(IEnumerable<WorkroomAssemblyWindow> windows) =>
        windows.Where(window => window.Open).OrderByDescending(window => window.Root.transform.GetSiblingIndex()).FirstOrDefault();

    internal static Rect ScreenBounds(GameObject root)
    {
        var rect = AssemblyDebugUi.Rect(root);
        var min = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.yMin, 0)));
        var max = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(rect.rect.xMax, rect.rect.yMax, 0)));
        return new Rect(min, max - min);
    }

    internal static void ShowPreview(GameObject root, Image image, Sprite? sprite, Vector2 position,
        Vector2 size, Color color, int orientation, bool flipped)
    {
        image.sprite = sprite; image.color = color;
        var rect = AssemblyDebugUi.Rect(root);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = position; rect.sizeDelta = size;
        WorkroomItemSprite.Pose(image, orientation, flipped, size);
        root.transform.SetAsLastSibling(); root.SetActive(true);
    }
}
