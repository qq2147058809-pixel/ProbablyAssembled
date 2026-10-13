#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

internal sealed partial class ServerRoomView
{
    private readonly Dictionary<ActionWidget, Image> coinArtwork = new();
    private Image quoteCoinArtwork, tradeCoinArtwork;

    private Image CreateCoinArtwork(Transform parent, string id, float x, float y, float size)
    {
        var image = Box(parent, "CoinArtwork", x, y, size, size, Color.white).GetComponent<Image>();
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = false;
        SetCoinArtwork(image, id);
        return image;
    }

    private void AddCoinWidgetArtwork(ActionWidget widget, string id, float size, bool selector = false)
    {
        var rect = AssemblyDebugUi.Rect(widget.Root);
        coinArtwork.Add(widget, CreateCoinArtwork(widget.Root.transform, id, 8, (rect.sizeDelta.y - size) / 2, size));
        var textLeft = 8 + size + 8;
        var textRight = selector ? 29 : 8;
        AssemblyDebugUi.Place(widget.Label.gameObject, textLeft, 0, rect.sizeDelta.x - textLeft - textRight, rect.sizeDelta.y);
        widget.Label.alignment = TextAlignmentOptions.MidlineLeft;
    }

    private static void SetCoinArtwork(Image image, string id)
    {
        if (!ServerRoomState.CoinIds.Contains(id)) throw new InvalidOperationException("未知机房币种：" + id);
        image.sprite = WorkroomArt.Get("server_room_coin_" + id);
        image.color = Color.white;
        image.enabled = true;
    }

    private void RefreshBrowserCoinArtwork(ServerRoomState.Document current)
    {
        if (current == null || !current.Enabled || mode != PanelMode.Computer) return;
        if (selectedPage == 2)
        {
            var id = current.Coins[selectedCoin].Id;
            SetCoinArtwork(quoteCoinArtwork, id);
            SetCoinArtwork(tradeCoinArtwork, id);
        }
        else if (selectedPage == 1)
        {
            SetCoinArtwork(coinArtwork[bulkCoin], ServerRoomState.CoinIds[bulkCoinIndex]);
            // Match the existing mining table's priority order and current page.
            var miners = current.Racks.Where(r => r.Kind == ServerRoomMachine.Mining)
                .OrderBy(r => r.Priority).ThenBy(r => r.RackIndex).Skip(minerPage * PageSize).Take(minerCoin.Count).ToArray();
            for (var i = 0; i < minerCoin.Count; i++)
            {
                var image = coinArtwork[minerCoin[i]];
                image.enabled = i < miners.Length;
                if (i < miners.Length) SetCoinArtwork(image, miners[i].CoinId);
            }
        }
    }

    private void ReleaseCoinArtworkReferences()
    {
        coinArtwork.Clear();
        quoteCoinArtwork = tradeCoinArtwork = null;
    }
}
