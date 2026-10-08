using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

internal static class WorkroomItemSprite
{
    // The outer rect owns global grid dimensions. Rotate an inner rect with
    // local dimensions, matching native R(orientation * 90) * FlipLocalX.
    internal static void Pose(Image image, int orientation, bool flipped, Vector2 size)
    {
        var rect = AssemblyDebugUi.Rect(image.gameObject);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = orientation % 2 == 0 ? size : new Vector2(size.y, size.x);
        rect.localScale = new Vector3(flipped ? -1 : 1, 1, 1);
        rect.localEulerAngles = new Vector3(0, 0, orientation * 90);
        image.enabled = AssemblyDebugUi.Alive(image.sprite);
    }
}
