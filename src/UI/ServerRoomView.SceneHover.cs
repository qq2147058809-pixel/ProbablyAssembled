#nullable disable
using System;
using UnityEngine;

namespace PCExpansion;

internal sealed partial class ServerRoomView
{
    // Generated from the adopted V4 layer alpha boundaries at the scene's logical coordinates.
    private static readonly Vector2[] ReturnDoorContour = {
        new Vector2(71, 162),
        new Vector2(71, 163),
        new Vector2(70, 163),
        new Vector2(70, 588),
        new Vector2(71, 588),
        new Vector2(71, 589),
        new Vector2(254, 589),
        new Vector2(254, 588),
        new Vector2(255, 588),
        new Vector2(255, 163),
        new Vector2(254, 163),
        new Vector2(254, 162)
    };
    private static readonly Vector2[] ControlMonitorContour = {
        new Vector2(368, 378),
        new Vector2(368, 379),
        new Vector2(366, 380),
        new Vector2(366, 462),
        new Vector2(367, 462),
        new Vector2(368, 464),
        new Vector2(427, 464),
        new Vector2(427, 475),
        new Vector2(408, 475),
        new Vector2(408, 476),
        new Vector2(404, 479),
        new Vector2(404, 481),
        new Vector2(403, 481),
        new Vector2(403, 482),
        new Vector2(465, 482),
        new Vector2(465, 480),
        new Vector2(463, 479),
        new Vector2(463, 477),
        new Vector2(461, 476),
        new Vector2(461, 475),
        new Vector2(442, 475),
        new Vector2(442, 464),
        new Vector2(506, 464),
        new Vector2(507, 462),
        new Vector2(508, 462),
        new Vector2(508, 380),
        new Vector2(506, 379),
        new Vector2(506, 378)
    };

    private void AttachSceneHover(GameObject button, Vector2[] contour, Func<string> description) =>
        sceneHover.AddRoom(button, contour, canvas, roomSpace.transform, null, null, description);

    private void AttachSlotHover(GameObject button, float x, float y, int slot) =>
        AttachSceneHover(button, new[] { new Vector2(x, y), new Vector2(x + 209, y),
            new Vector2(x + 209, y + 68), new Vector2(x, y + 68) },
            () => Text("slot_title", slot) + "\n" + rackHoverDetails[slot - 1]);
}
