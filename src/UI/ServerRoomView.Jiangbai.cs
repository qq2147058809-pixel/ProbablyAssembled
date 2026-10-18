#nullable disable
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

internal sealed partial class ServerRoomView
{
    private Image jiangbaiResident;

    private void DrawJiangbaiResident()
    {
        jiangbaiResident = AssemblyDebugUi.Image(roomSpace.transform, "JiangbaiHideout", Color.white, false);
        // User chose the lower-right foreground. The computer ends at x=545.
        // Advance slightly onto the floor so the rear feet clear the rack base.
        // The overlay never owns a rack or consumes machine capacity.
        AssemblyDebugUi.Place(jiangbaiResident.gameObject, 650, 346, 625, 375);
        jiangbaiResident.preserveAspect = true;
        jiangbaiResident.gameObject.SetActive(false);
    }

    private void RefreshJiangbaiResident()
    {
        if (!AssemblyDebugUi.Alive(jiangbaiResident)) return;
        var show = JiangbaiStory.ResidentVisible;
        if (show && jiangbaiResident.sprite == null)
            jiangbaiResident.sprite = WorkroomArt.Get("jiang_bai_hideout");
        if (jiangbaiResident.gameObject.activeSelf != show) jiangbaiResident.gameObject.SetActive(show);
    }
}
