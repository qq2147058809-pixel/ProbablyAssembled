#nullable disable
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BrowserTone = PCExpansion.ServerRoomBrowserTheme.Tone;

namespace PCExpansion;

internal sealed partial class ServerRoomView
{
    // References are registered as controls are built, updated in place on binding,
    // and released with this view. No hierarchy scan or per-frame theme work.
    private readonly Dictionary<Graphic, BrowserTone> browserTones = new();
    private readonly List<Selectable> browserSelectables = new();
    private ActionWidget browserTheme;
    private TextMeshProUGUI browserThemeTooltip;

    private void DrawBrowserThemeTooltip()
    {
        // Child visibility follows the theme button when minimized or closed.
        // Pointer events only: no polling, raycast scan, or business refresh.
        var tip = WebBox(browserTheme.Root.transform, "ThemeTooltip", -194, 33, 230, 28, WebPaper, true);
        WebBorder(tip.transform, "Border", 0, 0, 230, 28);
        browserThemeTooltip = WebLabel(tip.transform, "Text", "", 8, 0, 214, 28, 13);
        tip.SetActive(false);
        var trigger = browserTheme.Root.AddComponent<EventTrigger>();
        trigger.triggers = new Il2CppSystem.Collections.Generic.List<EventTrigger.Entry>();
        void On(EventTriggerType type, bool visible)
        {
            var entry = new EventTrigger.Entry { eventID = type, callback = new EventTrigger.TriggerEvent() };
            entry.callback.AddListener(DelegateSupport.ConvertDelegate<UnityAction<BaseEventData>>(
                new System.Action<BaseEventData>(_ => { if (AssemblyDebugUi.Alive(tip)) tip.SetActive(visible); })));
            trigger.triggers.Add(entry);
        }
        On(EventTriggerType.PointerEnter, true);
        On(EventTriggerType.PointerExit, false);
    }

    private void SetBrowserTone(Graphic graphic, BrowserTone tone)
    {
        browserTones[graphic] = tone;
        graphic.color = ServerRoomBrowserTheme.ColorFor(tone);
    }

    private GameObject BrowserBox(Transform parent, string name, float x, float y, float w, float h,
        BrowserTone tone, bool raycast = false)
    {
        var go = Box(parent, name, x, y, w, h, ServerRoomBrowserTheme.ColorFor(tone), raycast);
        SetBrowserTone(go.GetComponent<Image>(), tone);
        return go;
    }

    private void RegisterBrowserSelectable(Selectable selectable)
    {
        browserSelectables.Add(selectable);
        selectable.colors = ServerRoomBrowserTheme.Controls;
    }

    private void RefreshBrowserThemeLabel()
    {
        if (browserTheme == null) return;
        browserTheme.Label.text = Web("theme", ServerRoomBrowserTheme.Name);
        if (AssemblyDebugUi.Alive(browserThemeTooltip)) browserThemeTooltip.text = browserTheme.Label.text;
    }

    private void CycleBrowserTheme()
    {
        ServerRoomBrowserTheme.Cycle();
        // This intentionally does not refresh content, navigate, change focus,
        // close menus, or replace the pending transaction confirmation.
        foreach (var entry in browserTones)
            if (AssemblyDebugUi.Alive(entry.Key)) entry.Key.color = ServerRoomBrowserTheme.ColorFor(entry.Value);
        foreach (var selectable in browserSelectables)
            if (AssemblyDebugUi.Alive(selectable)) selectable.colors = ServerRoomBrowserTheme.Controls;
        if (AssemblyDebugUi.Alive(quantity))
        {
            quantity.caretColor = ServerRoomBrowserTheme.ColorFor(WebInk);
            quantity.selectionColor = ServerRoomBrowserTheme.Selection;
        }
        foreach (var input in minimumInputs)
            if (AssemblyDebugUi.Alive(input))
            { input.caretColor = ServerRoomBrowserTheme.ColorFor(WebInk); input.selectionColor = ServerRoomBrowserTheme.Selection; }
        RefreshBrowserThemeLabel();
    }

    private void ReleaseBrowserThemeReferences()
    {
        browserTones.Clear(); browserSelectables.Clear(); browserTheme = null; browserThemeTooltip = null;
    }
}
