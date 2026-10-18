#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BigInteger = System.Numerics.BigInteger;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using BrowserTone = PCExpansion.ServerRoomBrowserTheme.Tone;

namespace PCExpansion;

/// <summary>机房场景与经营电脑；只读状态，资产操作交给统一命令。</summary>
internal sealed partial class ServerRoomView : IDisposable
{
    private const float Width = 1280, Height = 720;
    private const float DoorShiftY = 18, DeskShiftY = 20, RackShiftY = 20;
    private const int Order = 32610;
    private static readonly Color CoverColor = new(.10f, .13f, .14f, 1);
    private static readonly string[] Pages = { "overview", "mining", "trading", "operation" };
    private static readonly string[] FontKeys = {
        "title", "stage", "exit", "computer", "empty", "locked", "preview", "close_hint",
        "computer_title", "overview", "mining", "trading", "operation", "overview_body",
        "mining_body", "trading_body", "operation_body", "slot_body", "locked_body" };
    private static readonly string[] M2FontKeys = {
        "stage", "overview_body", "operation_body", "disabled_body", "enable", "power", "cooling", "paid",
        "full_upgrade", "upgrade_body", "catalog", "catalog_body", "order_row", "order_add", "order_cancel",
        "order_locked", "order_locked_short", "order_unavailable", "order_change", "order_change_selected", "order_cancel_short", "order_replace", "order_change_hint",
        "mining", "server", "machine_name", "not_open", "unavailable", "error", "success", "locked_body",
        "rack_summary", "stopped", "running_locked", "no_machine", "body_sources", "installed", "part_sources",
        "gpu", "cpu", "ram", "take", "rack", "install", "remove", "source_row", "installed_row", "gpu_row",
        "box", "room", "empty_list", "previous", "next", "page", "back", "damaged", "no_slot", "hint" };
    private const int PageSize = 5;
    private enum PanelMode { Computer, Catalog, Rack }
    private sealed class ActionWidget
    {
        internal GameObject Root;
        internal Button Control;
        internal TextMeshProUGUI Label;
        internal Action Action;
        internal bool Allowed;
    }
    private readonly WorkroomPanelState panelState = new();
    private readonly List<Button> sceneButtons = new(), panelButtons = new();
    private readonly List<TextMeshProUGUI> allLabels = new(), rackLabels = new();
    private readonly List<Image> rackLamps = new();
    private readonly List<string> rackHoverDetails = new();
    private readonly List<Image> rackMachines = new();
    private readonly List<ActionWidget> widgets = new(), sourceRows = new(), installedRows = new();
    private readonly List<TextMeshProUGUI> sourceTexts = new(), installedTexts = new();
    private ActionWidget primaryAction, backAction, takeAction;
    private ActionWidget sourcePrevious, sourceNext, installedPrevious, installedNext, cpuChoice, ramChoice;
    private GameObject actions, lists;
    private TextMeshProUGUI noticeLabel, sourceHeading, installedHeading, sourcePageLabel, installedPageLabel;
    private Canvas canvas;
    private WorkroomEntryHover sceneHover;
    private GameObject roomSpace, panelSpace, panelMask, panelFace, tabs, slotDiagram;
    private Image backdrop, roomBackground;
    private TextMeshProUGUI panelTitle, panelBody, slotStatus;
    private TMP_FontAsset font;
    private Action returnToWorkroom;
    private bool built, disposing, visible, interactive;
    private int sampleFrame = -1, escapeFrame = -1, generation;
    private int selectedRack = -1, selectedPage, sourcePage, installedPage, lastCommandFrame = -1;
    private string partKind = "cpu", notice = "", renderedRevision = "", selectedOrderId = "";
    private PanelMode mode;
    private bool commandBusy, noticeIsError;
    private readonly List<ActionWidget> overviewCards = new(), coinTabs = new(), leaseRows = new();
    private readonly List<TextMeshProUGUI> minerTexts = new();
    private readonly List<ActionWidget> minerPower = new(), minerCoin = new(), minerUp = new(), minerDown = new(), minerConfigure = new();
    private readonly List<GameObject> chartLines = new();
    private GameObject m3Content, overviewPage, miningPage, tradingPage, operationPage, chart, confirmShield, confirmFace;
    private TextMeshProUGUI miningSummary, miningOutput, miningPager, tradingSummary, tradingHint, chartHigh, chartLow, chartDays;
    private TextMeshProUGUI operationSummary, operationUpgrade, operationLease, operationPager, confirmText;
    private TMP_InputField quantity;
    private ActionWidget miningPrevious, miningNext, tradeBuy, tradeSell, tradeAll, operationPower, operationCooling, operationCatalog;
    private ActionWidget operationPrevious, operationNext, leaseThree, leaseFive, leaseSeven, confirmAccept, confirmCancel, rackPower, rackOperate;
    private int selectedCoin, minerPage, leasePage, inputNeutralFrames;
    private string selectedServer = "";
    private bool keyboardOwned;
    private Action confirmation;
    private ActionWidget confirmDetails;
    private string confirmationSummary = "", confirmationDetails = "", confirmationAcceptLabel = "";
    private bool confirmationExpanded;
    private string confirmationRun;
    private int confirmationSlot, confirmationDay, confirmationCash;
    private long confirmationRevision, confirmationStorageEpoch, confirmationStorageRevision;
    private static readonly string[] M3FontKeys = {
        "stage", "hint", "overview_summary", "overview_card", "locked_card", "empty_card", "on", "off", "leased", "cannot_run", "unpaid", "manual_stop",
        "mining_summary", "mining_output", "mining_empty", "miner_row", "configure", "all_coin", "priority", "run", "stop",
        "trading_summary", "trading_hint", "quantity_hint", "buy", "sell", "sell_all", "history_days", "cap", "not_ready",
        "lease_row", "lease_button", "lease_choose", "lease_empty", "lease_locked", "lease_terms", "trade_terms", "confirm", "cancel",
        "confirmation_expired", "operation_summary", "upgrade", "operation_hint", "rack_summary", "manage", "running", "stopped",
        "coin.anchor", "coin.matrix", "coin.stellar", "coin.pulse", "coin.singularity" };

    internal bool OwnsTextInput => !disposing && (keyboardOwned || AnyBrowserInputFocused());

    internal bool Alive
    {
        get
        {
            if (!built || disposing || !AssemblyDebugUi.Alive(canvas) ||
                !AssemblyDebugUi.Alive(backdrop) || !AssemblyDebugUi.Alive(roomBackground) ||
                !AssemblyDebugUi.Alive(roomSpace) ||
                !AssemblyDebugUi.Alive(panelSpace) || !AssemblyDebugUi.Alive(panelMask) ||
                !AssemblyDebugUi.Alive(panelFace) || !AssemblyDebugUi.Alive(panelTitle) ||
                !AssemblyDebugUi.Alive(panelBody) || !AssemblyDebugUi.Alive(tabs) ||
                !AssemblyDebugUi.Alive(slotDiagram) || !AssemblyDebugUi.Alive(slotStatus) ||
                !AssemblyDebugUi.Alive(m3Content) || !AssemblyDebugUi.Alive(overviewPage) ||
                !AssemblyDebugUi.Alive(miningPage) || !AssemblyDebugUi.Alive(tradingPage) ||
                !AssemblyDebugUi.Alive(operationPage) || !AssemblyDebugUi.Alive(quantity) ||
                !AssemblyDebugUi.Alive(confirmShield) || !AssemblyDebugUi.Alive(confirmFace) ||
                !AssemblyDebugUi.Alive(browserFrame) || !AssemblyDebugUi.Alive(browserDesktop) ||
                !AssemblyDebugUi.Alive(browserRack) || !AssemblyDebugUi.Alive(browserCatalog) ||
                !AssemblyDebugUi.Alive(browserSidebar) || !AssemblyDebugUi.Alive(coinMenu) ||
                !HardcoreAlive ||
                !(sceneHover?.Alive ?? false)) return false;
            foreach (var button in sceneButtons) if (!AssemblyDebugUi.Alive(button)) return false;
            foreach (var button in panelButtons) if (!AssemblyDebugUi.Alive(button)) return false;
            return true;
        }
    }

    // The closing gesture remains owned for its frame and two neutral frames.
    internal bool BlocksPanelInput => disposing || panelState.BlocksInput || escapeFrame == Time.frameCount;

    internal void Build(Action returnToWorkroom)
    {
        if (returnToWorkroom == null) throw new ArgumentNullException(nameof(returnToWorkroom));
        if (built || !ReferenceEquals(canvas, null))
            throw new InvalidOperationException("机房界面必须清理完成后才能重新创建。");
        generation++;
        disposing = false;
        this.returnToWorkroom = returnToWorkroom;
        try
        {
            var fontText = Text("slot", 10) + Text("slot_title", 10) + "0123456789Esc×↑↓›▶—";
            foreach (var key in FontKeys) fontText += Text(key);
            foreach (var key in M2FontKeys) fontText += M2(key, Enumerable.Repeat<object>("", 12).ToArray());
            foreach (var key in M3FontKeys) fontText += M3(key, Enumerable.Repeat<object>("", 16).ToArray());
            foreach (var key in WebFontKeys) fontText += Web(key, Enumerable.Repeat<object>("", 12).ToArray());
            font = AssemblyDebugFonts.Prepare(Text("title"), fontText);
            canvas = AssemblyDebugUi.NewCanvas("PCExpansion.ServerRoomRegion", Order);
            canvas.gameObject.SetActive(false);
            backdrop = AssemblyDebugUi.Image(canvas.transform, "Backdrop", CoverColor, true);
            AssemblyDebugUi.Stretch(backdrop.gameObject);
            roomSpace = Space(canvas.transform, "ServerRoomReference");
            DrawRoom();
            // The modal shield spans the screen, including the fitted art's margins.
            panelMask = AssemblyDebugUi.Image(canvas.transform, "PreviewShield", new Color(0, 0, 0, .58f), true).gameObject;
            AssemblyDebugUi.Stretch(panelMask);
            panelSpace = Space(canvas.transform, "ServerRoomPanelReference");
            DrawPanel();
            panelMask.SetActive(false);
            panelFace.SetActive(false);
            built = true;
            EnsureOverlay();
        }
        catch (Exception creationFailure)
        {
            try { Dispose(); }
            catch (Exception cleanupFailure)
            {
                // Retained roots and disposing=true keep callbacks inert for a retry.
                throw new AggregateException("机房界面创建失败，部分资源仍待清理。", creationFailure, cleanupFailure);
            }
            throw;
        }
    }

    internal void Render(bool visible, bool interactive)
    {
        if (!Alive) throw new InvalidOperationException("机房界面尚未就绪或已失效。");
        this.visible = visible;
        this.interactive = visible && interactive && !RoomAudioSettingsUi.BlocksInput;
        var viewport = new Rect(0, 0, Screen.width, Screen.height);
        WorkroomView.Fit(roomSpace, viewport);
        WorkroomView.Fit(panelSpace, viewport);
        if (sampleFrame != Time.frameCount)
        {
            sampleFrame = Time.frameCount;
            var focused = AnyBrowserInputFocused();
            if (focused) { keyboardOwned = true; inputNeutralFrames = 0; }
            else if (keyboardOwned)
            {
                if (!InputNeutral()) inputNeutralFrames = 0;
                else if (++inputNeutralFrames >= 2) keyboardOwned = false;
            }
            var escape = this.interactive && panelState.IsOpen && Input.GetKeyDown(KeyCode.Escape) && Input.compositionString.Length == 0;
            var wasOpen = panelState.IsOpen;
            if (escape) escapeFrame = Time.frameCount;
            if (escape && confirmation != null) { CancelConfirmation(); escape = false; }
            else if (escape && focused) { ClearFocus(); escape = false; }
            else if (escape && (bulkMenuOpen || coinMenuRecord != "")) { CloseBrowserMenus(); escape = false; }
            // A hidden/transitioning preview never retains an open panel.
            // The sound settings overlay disables input without closing the current browser page.
            panelState.Tick(visible, visible && interactive, escape, InputNeutral());
            if (wasOpen && !panelState.IsOpen) ClearSelection();
        }
        canvas.gameObject.SetActive(visible);
        if (visible)
        {
            RefreshJiangbaiResident();
            RefreshIfChanged();
        }
        SyncInteraction();
    }

    internal void ClosePanels()
    {
        panelState.Close();
        ClearSelection();
        if (disposing) return;
        if (WorkroomUiCleanup.IsAlive(panelFace)) panelFace.SetActive(false);
        if (built) SyncInteraction();
    }

    internal void EnsureOverlay()
    {
        if (!Alive) throw new InvalidOperationException("机房覆盖界面已失效。");
        WorkroomGameAdapter.ForgetLetterboxCanvases(canvas, null, null);
        WorkroomView.Overlay(canvas);
        canvas.sortingOrder = Order;
        backdrop.enabled = true;
        backdrop.gameObject.SetActive(true);
        backdrop.color = CoverColor;
        backdrop.raycastTarget = true;
        backdrop.transform.SetAsFirstSibling();
        roomSpace.transform.SetSiblingIndex(1);
        panelMask.transform.SetSiblingIndex(2);
        panelSpace.transform.SetAsLastSibling();
    }

    private static string Text(string key, params object[] arguments) =>
        LanguageText.Get("serverroom.m1." + key, arguments);
    private static string M2(string key, params object[] arguments) =>
        LanguageText.Get("serverroom.m2." + key, arguments);
    private static string M3(string key, params object[] arguments) =>
        LanguageText.Get("serverroom.m3." + key, arguments);
    private static string CoinName(string id) => M3("coin." + id);

    private static GameObject Space(Transform parent, string name)
    {
        var go = AssemblyDebugUi.New(name, parent);
        var rect = AssemblyDebugUi.Rect(go);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(Width, Height);
        rect.anchoredPosition = Vector2.zero;
        return go;
    }

    private static GameObject Box(Transform parent, string name, float x, float y, float w, float h, Color color, bool raycast = false)
    {
        var image = AssemblyDebugUi.Image(parent, name, color, raycast);
        AssemblyDebugUi.Place(image.gameObject, x, y, w, h);
        return image.gameObject;
    }

    private TextMeshProUGUI Label(Transform parent, string name, string text, float x, float y, float w, float h,
        float size, bool left = false, bool wrap = false)
    {
        var label = AssemblyDebugUi.Text(parent, name, text, font, size, left);
        // This new fixed-layout view has its own metrics; avoid the older panel enlargement.
        label.fontSize = size;
        label.enableWordWrapping = wrap;
        if (wrap) label.alignment = (TextAlignmentOptions)257;
        AssemblyDebugUi.Place(label.gameObject, x, y, w, h);
        allLabels.Add(label);
        return label;
    }

    private GameObject Button(Transform parent, string name, float x, float y, float w, float h, Color color,
        bool panel, Action action)
    {
        var ownerGeneration = generation;
        Action click = () =>
        {
            if (ownerGeneration != generation || !built || disposing || !visible || !interactive) return;
            if (panel ? !panelState.IsOpen : panelState.BlocksInput) return;
            if (commandBusy) return;
            try { action(); }
            catch (Exception ex) { ShowError(ex); }
        };
        var go = panel ? CreateBrowserButton(parent, name, color, click) : AssemblyDebugUi.Button(canvas, parent, name, color, click);
        AssemblyDebugUi.Place(go, x, y, w, h);
        var button = go.GetComponent<Button>();
        button.interactable = false;
        (panel ? panelButtons : sceneButtons).Add(button);
        return go;
    }

    private void DrawRoom()
    {
        var parent = roomSpace.transform;
        roomBackground = AssemblyDebugUi.Image(parent, "ServerRoomBackground", Color.white, false);
        roomBackground.sprite = WorkroomArt.Get("server_room_background");
        AssemblyDebugUi.Place(roomBackground.gameObject, 0, 0, Width, Height);
        sceneHover = new WorkroomEntryHover(roomBackground.sprite);
        var exit = Button(parent, "ReturnToWorkroom", 65, 150 + DoorShiftY, 195, 415, Color.clear, false, () =>
        {
            // Disable immediately; the owner commits the actual region change under black.
            interactive = false;
            SyncInteraction();
            returnToWorkroom?.Invoke();
        });
        AttachSceneHover(exit, ReturnDoorContour, () => Text("exit"));

        Box(parent, "ControlMonitorScreen", 374, 365 + DeskShiftY, 126, 73,
            new Color(.025f, .03f, .04f, 1));
        var computer = Button(parent, "ControlComputer", 325, 320 + DeskShiftY, 220, 160, Color.clear, false, OpenComputer);
        AttachSceneHover(computer, ControlMonitorContour, () => Text("computer"));

        DrawRack(parent, 650, 1);
        DrawRack(parent, 925, 6);
        // Later sibling wins over the control-computer hit rectangle at the desk's left edge.
        var speakerRect = new Rect(321, 456, 36, 44);
        var speaker = Button(parent, "AudioSpeaker", speakerRect.x, speakerRect.y, speakerRect.width, speakerRect.height,
            Color.clear, false, RoomAudioSettingsUi.Open);
        var speakerArt = AssemblyDebugUi.Image(speaker.transform, "SpeakerArt", Color.white, false);
        speakerArt.sprite = WorkroomArt.Get("server_room_audio_speaker");
        AssemblyDebugUi.Stretch(speakerArt.gameObject);
        sceneHover.AddRoomSprite(speaker, speakerArt.sprite, speakerRect, canvas, parent, null, null,
            () => RoomAudio.Text("speaker_hint"));
        DrawJiangbaiResident();
    }

    private void DrawRack(Transform parent, float x, int firstSlot)
    {
        for (var row = 0; row < 5; row++)
        {
            var slot = firstSlot + row;
            var y = 168 + RackShiftY + row * 86;
            var machine = AssemblyDebugUi.Image(parent, "SlotMachine" + slot, Color.white, false);
            AssemblyDebugUi.Place(machine.gameObject, x + 18, y, 209, 68);
            machine.preserveAspect = true;
            machine.enabled = false;
            rackMachines.Add(machine);
            // Keep the complete 209x68 machine face clear. The existing 18px
            // inter-slot gap holds all dynamic labels and the status indicator.
            Box(parent, "SlotInfo" + slot, x + 18, y + 68, 209, 18, new Color(.08f, .13f, .13f, 1));
            var lamp = Box(parent, "SlotLamp" + slot, x + 213, y + 72, 10, 10, ServerRoomStatusArt.ColorFor(ServerRoomStatusArt.State.Locked));
            rackLamps.Add(lamp.GetComponent<Image>());
            rackHoverDetails.Add(Text("locked"));
            Label(parent, "SlotNumber" + slot, Text("slot", slot), x + 22, y + 68, 62, 18, 12, true);
            rackLabels.Add(Label(parent, "SlotStatus" + slot, Text("locked"), x + 86, y + 68, 123, 18, 12, true));
            var slotButton = Button(parent, "SlotPreview" + slot, x + 18, y, 209, 68, Color.clear, false, () => OpenSlot(slot));
            AttachSlotHover(slotButton, x + 18, y, slot);
        }
    }

    private void DrawPanel() => DrawBrowserPanel();

    private TMP_InputField MakeQuantity(Transform parent)
    {
        var background = AssemblyDebugUi.Image(parent, "Quantity", ServerRoomBrowserTheme.ColorFor(WebPaper), true);
        UiRoundedGeometry.Fill(background);
        AssemblyDebugUi.Place(background.gameObject, 856, 88, 242, 44);
        WebBorder(background.transform, "InputBorder", 0, 0, 242, 44);
        var viewport = AssemblyDebugUi.New("TextViewport", background.transform);
        AssemblyDebugUi.Place(viewport, 10, 4, 222, 36);
        viewport.AddComponent<RectMask2D>();
        var text = WebLabel(viewport.transform, "Text", "", 0, 0, 222, 36, 18);
        var placeholder = WebLabel(viewport.transform, "Placeholder", M3("quantity_hint"), 0, 0, 222, 36, 14, true, false, WebMuted);
        var input = background.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background; input.textViewport = AssemblyDebugUi.Rect(viewport);
        input.textComponent = text; input.placeholder = placeholder; input.fontAsset = font;
        input.pointSize = 18; input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.characterLimit = 21; input.richText = false; input.restoreOriginalTextOnEscape = false;
        input.navigation = new Navigation { mode = Navigation.Mode.None }; input.transition = Selectable.Transition.ColorTint;
        input.customCaretColor = true; input.caretColor = ServerRoomBrowserTheme.ColorFor(WebInk);
        SetBrowserTone(background, WebPaper);
        RegisterBrowserSelectable(input);
        input.selectionColor = ServerRoomBrowserTheme.Selection; input.text = "0.1";
        input.onValueChanged.AddListener(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<string>>(
            (string value) => TradeInputChanged()));
        return input;
    }

    private void OpenComputer()
    {
        if (!panelState.Open(true)) return;
        ClearSelection();
        panelTitle.text = Text("computer_title");
        browserMinimized = false; RefreshContent();
        SyncInteraction();
    }

    private void SetPage(int page)
    {
        if (!panelState.IsOpen || page < 0 || page >= Pages.Length) return;
        var target = page switch {
            1 => ServerRoomBrowserNavigation.Page.Mining,
            2 => ServerRoomBrowserNavigation.Page.Trading,
            3 => ServerRoomBrowserNavigation.Page.Operation,
            _ => ServerRoomBrowserNavigation.Page.Overview
        };
        NavigateBrowser(new ServerRoomBrowserNavigation.Route(target, page == 2 ? selectedCoin : 0));
    }

    private void OpenSlot(int slot)
    {
        if (slot < 1 || slot > 10) return;
        if (!panelState.IsOpen)
        {
            if (!panelState.Open(true)) return;
            ClearSelection();
        }
        NavigateBrowser(new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.Rack, slot - 1));
    }

    private ActionWidget Widget(Transform parent, string name, float x, float y, float w, float h)
    {
        var widget = new ActionWidget();
        widget.Root = Button(parent, name, x, y, w, h, ServerRoomBrowserTheme.ColorFor(WebPaper), true,
            () => { if (widget.Allowed) widget.Action?.Invoke(); });
        widget.Control = widget.Root.GetComponent<Button>();
        widget.Label = WebLabel(widget.Root.transform, "Label", "", 4, 0, w - 8, h, h < 36 ? 14 : 16, false);
        widgets.Add(widget);
        return widget;
    }

    private static int ClampPage(int page, int count) => Math.Clamp(page, 0, Math.Max(0, (count - 1) / PageSize));
    private static bool Movable(ServerRoomState.Document current, int rack) =>
        current.Racks.FirstOrDefault(entry => entry.RackIndex == rack) is { } entry && !entry.PoweredOn &&
        !current.Leases.Any(lease => lease.RecordId == entry.RecordId);
    private static string MachineName(ServerRoomMachine.Definition definition) => M2("machine_name", M2(definition.Kind), definition.Tier);
    private static string Display(WorkroomItemCodec.Snapshot item) => string.IsNullOrWhiteSpace(item.Name) ? item.Identifier : item.Name;
    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private void Bind(ActionWidget widget, string text, bool allowed, Action action, bool show = true)
    {
        widget.Action = action; widget.Allowed = allowed;
        widget.Label.text = text; StyleWidget(widget); widget.Root.SetActive(show);
    }

    private void Execute(Action action)
    {
        if (commandBusy || lastCommandFrame == Time.frameCount) return;
        CloseBrowserMenus();
        if (!ServerRoomRuntime.Ready)
        { RoomAudio.Play("warning"); ShowError(new InvalidOperationException(M2("unavailable"))); RefreshContent(); SyncInteraction(); return; }
        lastCommandFrame = Time.frameCount; commandBusy = true;
        SyncInteraction();
        var before = ServerRoomRuntime.Current;
        var beforeRevision = before?.Revision;
        var beforeOrders = ServerRoomOrders.CurrentRevision;
        var beforeStorage = WorkroomStorage.State.Revision;
        var beforeCount = WorkroomStorage.State.Items.Count;
        try
        {
            action(); notice = M2("success"); noticeIsError = false;
            var after = ServerRoomRuntime.Current;
            if (!ServerRoomRuntime.Pending && !WorkroomCashRepair.Pending &&
                (after?.Revision != beforeRevision || ServerRoomOrders.CurrentRevision != beforeOrders || WorkroomStorage.State.Revision != beforeStorage))
            {
                var cue = "confirm";
                if (before != null && after != null)
                {
                    if (after.Racks.Count > before.Racks.Count) cue = "metal_drop";
                    else if (after.Racks.Count < before.Racks.Count) cue = "metal_drag";
                    else if (after.Leases.Count > before.Leases.Count) cue = "lease_notice";
                    else if (after.Racks.Count(r => r.PoweredOn) > before.Racks.Count(r => r.PoweredOn)) cue = "machine_start";
                    else if (after.Racks.Count(r => r.PoweredOn) < before.Racks.Count(r => r.PoweredOn)) cue = "machine_stop";
                    else if (WorkroomStorage.State.Revision != beforeStorage)
                        cue = WorkroomStorage.State.Items.Count > beforeCount ? "remove" : "insert";
                }
                RoomAudio.Play(cue);
            }
        }
        catch (Exception ex) { RoomAudio.Play("warning"); ShowError(ex); }
        finally
        {
            commandBusy = false; renderedRevision = "";
            RefreshContent(); SyncInteraction();
        }
    }

    private void ShowError(Exception exception)
    {
        notice = M2("error", exception.Message);
        noticeIsError = true;
        if (AssemblyDebugUi.Alive(noticeLabel))
        { noticeLabel.text = notice; SetBrowserTone(noticeLabel, BrowserTone.Error); }
        Core.Log?.Warning("机房界面操作失败：" + exception);
    }

    private void ClearFocus()
    {
        ClearHardcoreFocus();
        if (AssemblyDebugUi.Alive(quantity) && quantity.isFocused)
        { keyboardOwned = true; inputNeutralFrames = 0; quantity.DeactivateInputField(); }
        var events = EventSystem.current;
        if (!AssemblyDebugUi.Alive(events) || events.currentSelectedGameObject == null ||
            !AssemblyDebugUi.Alive(canvas)) return;
        for (var current = events.currentSelectedGameObject.transform; current != null; current = current.parent)
            if (current.Pointer == canvas.transform.Pointer) { events.SetSelectedGameObject(null); return; }
    }

    private void ClearSelection()
    {
        // Closing a window is not a command to discard a draft or pending order.
        ClearFocus(); CloseBrowserMenus();
        renderedRevision = "";
    }

    private void RefreshIfChanged()
    {
        try
        {
            var current = ServerRoomRuntime.Current;
            EnsureHardcoreSession(current);
            var ready = ServerRoomRuntime.Ready && current != null;
            var visit = ready ? ServerRoomOperations.ActiveVisitId() : "";
            var revision = (current?.Run ?? "") + "/" + current?.Slot + "/" + current?.Revision + "/" +
                WorkroomStorage.State.Epoch + "/" + WorkroomStorage.State.Revision + "/" + ready + "/" +
                ServerRoomRuntime.Error + "/" + visit + "/" + ServerRoomOrders.CurrentRevision + "/" + selectedOrderId + "/" +
                LanguageText.LocaleCode + "/" + Cash() + "/" + Today() + "/" + ServerRoomDaily.AllowCommands + "/" + LuoxiStory.Unlocked;
            if (revision == renderedRevision) return;
            renderedRevision = revision;
            var unlocked = ready ? ServerRoomState.UnlockedSlots(current) : 0;
            var facts = ready ? ServerRoomOperations.MachineFacts(current).ToDictionary(fact => fact.RecordId, StringComparer.Ordinal) : null;
            var forecast = ready ? ForecastNight(current, facts.Values.ToArray()) : null;
            for (var rack = 0; rack < rackLabels.Count; rack++)
            {
                var entry = ready ? current.Racks.FirstOrDefault(item => item.RackIndex == rack) : null;
                var lease = entry == null ? null : current.Leases.FirstOrDefault(item => item.RecordId == entry.RecordId);
                var fact = entry == null ? null : facts[entry.RecordId];
                var unpaid = entry != null && forecast.Unpaid.Contains(entry.RecordId);
                var state = RackVisualState(rack >= unlocked, entry, lease != null, fact, forecast);
                RefreshRackArtwork(rack, rack < unlocked ? entry : null);
                var status = rack >= unlocked ? Text("locked") : entry == null ? Text("empty") :
                    lease != null ? M3("leased") : !fact.CanRun ? M3("cannot_run") :
                    !entry.PoweredOn ? M3("manual_stop") : PlanReason(forecast, entry.RecordId);
                rackLabels[rack].text = status;
                rackHoverDetails[rack] = entry == null || rack >= unlocked ? status :
                    M2(entry.Kind) + " · " + (lease != null ? M3("lease_locked", lease.RemainingDays, lease.DueDay) :
                    unpaid ? M3("on") + " · " + M3("unpaid") : status);
                rackLamps[rack].sprite = ServerRoomStatusArt.SpriteFor(state);
                rackLamps[rack].color = ServerRoomStatusArt.ColorFor(state);
            }
            if (panelState.IsOpen) RefreshContent();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private static ServerRoomStatusArt.State RackVisualState(bool locked, ServerRoomState.RackReference entry,
        bool leased, ServerRoomEconomy.MachineFact fact, Forecast forecast)
    {
        if (locked) return ServerRoomStatusArt.State.Locked;
        if (entry == null) return ServerRoomStatusArt.State.Empty;
        if (leased) return ServerRoomStatusArt.State.Leased;
        return entry.PoweredOn && fact.CanRun && forecast != null && !forecast.Stopped.Contains(entry.RecordId)
            ? ServerRoomStatusArt.State.Running : ServerRoomStatusArt.State.Stopped;
    }

    private void RefreshRackArtwork(int rack, ServerRoomState.RackReference entry)
    {
        var image = rackMachines[rack];
        image.enabled = false;
        image.sprite = null;
        if (entry == null) return;
        var record = WorkroomStorage.State.FindRack(rack);
        if (record == null || record.Id != entry.RecordId || record.Item.Nodes.Count == 0 ||
            record.Item.Nodes[0].UniqueId != entry.UniqueId ||
            !ServerRoomMachine.TryDefinition(record.Item.Identifier, out var definition) || definition.Kind != entry.Kind)
            throw new InvalidOperationException(M2("unavailable", ""));
        image.sprite = WorkroomArt.Get(ServerRoomHardware.ArtworkName(definition, rack: true));
        image.enabled = true;
    }

    private void RefreshContent()
    {
        if (!built || disposing || !panelState.IsOpen) return;
        EnsureHardcoreSession(ServerRoomRuntime.Current);
        foreach (var widget in widgets) { widget.Root.SetActive(false); widget.Allowed = false; widget.Action = null; }
        foreach (var label in sourceTexts.Concat(installedTexts)) { label.text = ""; label.gameObject.SetActive(false); }
        sourceHeading.text = installedHeading.text = sourcePageLabel.text = installedPageLabel.text = "";
        actions.SetActive(false); lists.SetActive(false); slotDiagram.SetActive(false);
        tabs.SetActive(true);
        var m3 = mode == PanelMode.Computer && ServerRoomRuntime.Ready && ServerRoomRuntime.Current?.Enabled == true;
        m3Content.SetActive(m3); panelBody.gameObject.SetActive(!m3 && mode != PanelMode.Catalog);
        overviewPage.SetActive(m3 && selectedPage == 0); miningPage.SetActive(m3 && selectedPage == 1);
        tradingPage.SetActive(m3 && selectedPage == 2); operationPage.SetActive(m3 && selectedPage == 3);
        try
        {
            var current = ServerRoomRuntime.Current;
            EnsureHardcoreSession(current);
            if (!ServerRoomRuntime.Ready || current == null)
                panelBody.text = M2("unavailable", ServerRoomRuntime.Error ?? "");
            else if (mode == PanelMode.Computer) RefreshComputer(current);
            else if (mode == PanelMode.Catalog) RefreshCatalog(current);
            else RefreshRack(current);
            RefreshHardcore(current);
            RefreshConfirmation();
            RefreshBrowserShell(current);
            noticeLabel.text = notice != "" ? notice : ServerRoomRuntime.Ready && current != null ? Web("ready") : M3("not_ready");
            SetBrowserTone(noticeLabel, noticeIsError ? BrowserTone.Error : WebMuted);
            // Source models and error strings can introduce characters absent from
            // the static controls; prepare the actual displayed text as well.
            AssemblyDebugFonts.Prepare(Text("title"), string.Join("", allLabels.Select(label => label.text)));
        }
        catch (Exception ex)
        {
            foreach (var widget in widgets) { widget.Allowed = false; widget.Action = null; }
            CancelConfirmation();
            CloseBrowserMenus();
            RefreshBrowserShell(ServerRoomRuntime.Current, true);
            ShowError(ex);
        }
    }

    private void RefreshComputer(ServerRoomState.Document current)
    {
        panelTitle.text = Text("computer_title");
        if (current.Enabled)
        {
            if (hardcorePage != HardcorePage.None) return;
            if (selectedPage == 0) RefreshOverview(current);
            else if (selectedPage == 1) RefreshMining(current);
            else if (selectedPage == 2) RefreshTrading(current);
            else RefreshOperation(current);
            return;
        }
        panelBody.text = M2("disabled_body", ServerRoomState.EnableCost);
        actions.SetActive(true);
        Bind(primaryAction, M2("enable", ServerRoomState.EnableCost), Cash() >= ServerRoomState.EnableCost,
            () => Execute(ServerRoomOperations.Enable));
        StyleWidget(primaryAction, WebBlue, BrowserTone.OnAccent);
        slotStatus.text = Web("available_cash") + "：" + Web("cash_unit", Cash());
    }

    private static int Cash() => PlayerStore.IsInstanceExist() && PlayerStore.instance != null ? PlayerStore.instance.GetCash() : 0;
    private static int Today() => StoreStation.Instance != null ? StoreStation.GetDayCounter() : -1;
    private static string PriceNumber(decimal value) => value != 0m && Math.Abs(value) < .01m
        ? value.ToString("0.###E+0", CultureInfo.InvariantCulture)
        : decimal.Round(value, 2, MidpointRounding.AwayFromZero)
            .ToString(Math.Abs(value) > 1000000000m ? "0.##E+0" : "0.##", CultureInfo.InvariantCulture);
    private static string AveragePrice(decimal cost, long microUnits)
    {
        if (microUnits == 0) return "0";
        try { return PriceNumber(cost / (microUnits / 1000000m)); }
        catch (OverflowException)
        {
            // A valid total cost divided by a fractional holding can exceed decimal's range.
            return ((double)cost / (microUnits / 1000000d)).ToString("0.##E+0", CultureInfo.InvariantCulture);
        }
    }
    private static string Quantity(long units) => (units / 1000000m).ToString("0.######", CultureInfo.InvariantCulture);
    private sealed class Forecast
    {
        internal int Demand, Payable;
        internal HashSet<string> Unpaid = new(StringComparer.Ordinal);
        internal decimal[] Produced = new decimal[5];
        internal HashSet<string> Stopped = new(StringComparer.Ordinal);
        internal ServerRoomEconomy.OperatingPlan Plan = new();
    }
    private static Forecast ForecastNight(ServerRoomState.Document current, IReadOnlyList<ServerRoomEconomy.MachineFact> machines)
    {
        var result = new Forecast();
        if (current.Network.Day == null) return result;
        result.Demand = ServerRoomEconomy.Plan(current, machines, int.MaxValue).Fees;
        var plan = ServerRoomEconomy.Plan(current, machines, Math.Max(0, Cash()));
        result.Plan = plan;
        result.Payable = plan.Fees;
        foreach (var machine in plan.Machines)
        {
            if (!machine.Runs) result.Stopped.Add(machine.RecordId);
            if (machine.Reason == "cash") result.Unpaid.Add(machine.RecordId);
            if (machine.Runs) result.Produced[machine.Coin] += machine.Production.ProducedCoin;
        }
        return result;
    }
    private void RefreshOverview(ServerRoomState.Document current)
    {
        var machines = ServerRoomOperations.MachineFacts(current);
        var forecast = ForecastNight(current, machines);
        RefreshBrowserOverview(current, forecast);
        var facts = machines.ToDictionary(fact => fact.RecordId, StringComparer.Ordinal);
        for (var i = 0; i < 10; i++)
        {
            var rack = i; var reference = current.Racks.FirstOrDefault(item => item.RackIndex == rack);
            string text;
            if (i >= ServerRoomState.UnlockedSlots(current)) text = M3("locked_card", i + 1);
            else if (reference == null) text = M3("empty_card", i + 1);
            else
            {
                var fact = facts[reference.RecordId];
                var lease = current.Leases.FirstOrDefault(item => item.RecordId == reference.RecordId);
                var status = lease != null ? M3("lease_locked", lease.RemainingDays, lease.DueDay) : !fact.CanRun ? M3("cannot_run") :
                    !reference.PoweredOn ? M3("manual_stop") : forecast.Unpaid.Contains(reference.RecordId) ? M3("unpaid") : forecast.Stopped.Contains(reference.RecordId) ? M3("cannot_run") : M3("running");
                text = M3("overview_card", i + 1, M2(reference.Kind), status, Number(fact.EffectiveCompute), fact.DailyFee,
                    reference.Kind == "mining" ? CoinName(reference.CoinId) : "");
            }
            Bind(overviewCards[i], text, i < ServerRoomState.UnlockedSlots(current), () => OpenSlot(rack + 1));
            RefreshBrowserOverviewRow(i, current, reference, reference == null ? null : facts[reference.RecordId], forecast);
        }
    }
    private void RefreshMining(ServerRoomState.Document current)
    {
        var machines = ServerRoomOperations.MachineFacts(current);
        var forecast = ForecastNight(current, machines);
        miningSummary.text = M3("mining_summary", forecast.Demand, forecast.Payable, Cash(), forecast.Unpaid.Count);
        var facts = machines.ToDictionary(fact => fact.RecordId, StringComparer.Ordinal);
        var miners = current.Racks.Where(item => item.Kind == "mining").OrderBy(item => item.Priority).ThenBy(item => item.RackIndex).ToArray();
        foreach (var label in minerTexts) label.gameObject.SetActive(false);
        minerPage = ClampPage(minerPage, miners.Length);
        for (var i = 0; i < 5; i++)
        {
            var index = minerPage * 5 + i;
            if (index >= miners.Length) continue;
            var rack = miners[index]; var fact = facts[rack.RecordId];
            var status = !rack.PoweredOn ? M3("manual_stop") : PlanReason(forecast, rack.RecordId);
            minerTexts[i].gameObject.SetActive(true);
            minerTexts[i].text = H("miner_row", rack.RackIndex + 1, ModeName(rack.Mode), Number(ServerRoomEconomy.Health(current, rack.UniqueId)), status);
            Bind(minerPower[i], rack.PoweredOn ? M3("stop") : M3("run"), rack.PoweredOn || fact.CanRun,
                () => Execute(() => ServerRoomOperations.Power(rack.RecordId, !rack.PoweredOn)));
            var row = i;
            Bind(minerCoin[i], CoinName(rack.CoinId), true, () => OpenBrowserCoinMenu(rack.RecordId, row));
            Bind(minerUp[i], "↑", rack.Priority > 0, () => Execute(() => ServerRoomOperations.Priority(rack.RecordId, rack.Priority - 1)));
            Bind(minerDown[i], "↓", rack.Priority < 9, () => Execute(() => ServerRoomOperations.Priority(rack.RecordId, rack.Priority + 1)));
            Bind(minerConfigure[i], H("machine"), true, () => OpenMachine(rack.RecordId));
            StyleWidget(minerPower[i], rack.PoweredOn ? WebSelected : WebPaper, rack.PoweredOn ? WebBlue : WebMuted);
            StyleWidget(minerCoin[i]); StyleWidget(minerUp[i]); StyleWidget(minerDown[i]); StyleWidget(minerConfigure[i], WebPaper, WebBlue);
        }
        if (miners.Length == 0) miningSummary.text += "\n" + M3("mining_empty");
        miningPager.text = M2("page", minerPage + 1, Math.Max(1, (miners.Length + 4) / 5));
        Bind(miningPrevious, M2("previous"), minerPage > 0, () => { minerPage--; RefreshContent(); SyncInteraction(); });
        Bind(miningNext, M2("next"), (minerPage + 1) * 5 < miners.Length, () => { minerPage++; RefreshContent(); SyncInteraction(); });
        var gains = Enumerable.Range(0, 5).Where(i => forecast.Produced[i] > 0)
            .Select(i => CoinName(ServerRoomState.CoinIds[i]) + "+" + PriceNumber(forecast.Produced[i]));
        miningOutput.text = M3("mining_output", string.Join(" / ", gains));
    }
    private bool TradingReady(ServerRoomState.Document current)
    {
        try { ServerRoomDaily.RequireToday(current); return true; }
        catch { return false; }
    }
    private void RefreshTrading(ServerRoomState.Document current)
    {
        selectedCoin = Math.Clamp(selectedCoin, 0, 4);
        for (var i = 0; i < 5; i++)
        {
            var coin = i;
            Bind(coinTabs[i], CoinName(ServerRoomState.CoinIds[i]), true, () =>
            { NavigateBrowser(new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.Trading, coin)); });
            coinTabs[i].Label.text = CoinName(ServerRoomState.CoinIds[i]) + "\n" +
                (current.Market.Initialized ? Web("cash_unit", PriceNumber(current.Market.Prices[i])) : "—");
            StyleWidget(coinTabs[i], i == selectedCoin ? WebSelected : WebPaper, i == selectedCoin ? WebBlue : WebInk);
        }
        var ready = TradingReady(current); var wallet = current.Coins[selectedCoin];
        var price = current.Market.Initialized ? current.Market.Prices[selectedCoin] : 0;
        var average = AveragePrice(wallet.Cost, wallet.MicroUnits);
        tradingHint.text = ready ? H("trade_hint") : M3("not_ready");
        quantity.interactable = ready;
        Bind(tradeBuy, M3("buy"), ready, () => PreviewTrade(true, false));
        Bind(tradeSell, M3("sell"), ready && wallet.MicroUnits > 0, () => PreviewTrade(false, false));
        Bind(tradeAll, M3("sell_all", Quantity(wallet.MicroUnits)), ready && wallet.MicroUnits > 0, () => PreviewTrade(false, true));
        RefreshBrowserWallet(current, wallet, price, average);
        DrawHistory(current.Market);
    }
    private void DrawHistory(ServerRoomState.MarketState market)
    {
        foreach (var segment in chartLines) segment.SetActive(false);
        if (!market.Initialized || market.History.Count == 0)
        { chartHigh.text = chartLow.text = ""; chartDays.text = M3("not_ready"); return; }
        var prices = market.History.Select(quote => quote.Prices[selectedCoin]).ToArray();
        var min = prices.Min(); var max = prices.Max(); var span = max - min;
        chartHigh.text = PriceNumber(max); chartLow.text = PriceNumber(min);
        chartDays.text = M3("history_days", market.History[0].Day, market.History.Last().Day, prices.Length);
        if (prices.Length == 1)
        {
            AssemblyDebugUi.Place(chartLines[0], 82, 113, 542, 2);
            AssemblyDebugUi.Rect(chartLines[0]).localEulerAngles = Vector3.zero;
            chartLines[0].SetActive(true); return;
        }
        for (var i = 0; i < prices.Length - 1; i++)
        {
            var x = 82 + i * 542f / (prices.Length - 1); var nextX = 82 + (i + 1) * 542f / (prices.Length - 1);
            var y = span == 0 ? 113 : 38 + (float)((max - prices[i]) / span) * 150;
            var nextY = span == 0 ? 113 : 38 + (float)((max - prices[i + 1]) / span) * 150;
            var dx = nextX - x; var dy = nextY - y;
            AssemblyDebugUi.Place(chartLines[i], x, y, MathF.Sqrt(dx * dx + dy * dy), 2);
            AssemblyDebugUi.Rect(chartLines[i]).localEulerAngles = new Vector3(0, 0, -MathF.Atan2(dy, dx) * 180 / MathF.PI);
            chartLines[i].SetActive(true);
        }
    }
    private void PreviewTrade(bool buy, bool all)
    {
        var current = ServerRoomRuntime.Current ?? throw new InvalidOperationException(M3("not_ready"));
        var id = ServerRoomState.CoinIds[selectedCoin];
        var units = all ? current.Coins[selectedCoin].MicroUnits : TradeInputUnits();
        var maximum = ServerRoomOperations.MaximumTrade(id, buy);
        if (units > maximum) { ShowTradeLimit(buy, maximum); return; }
        var quote = ServerRoomOperations.TradeQuote(id, units, buy);
        var day = current.Market.QuoteDay.Value; var price = quote.Price;
        var cashBefore = Cash();
        var summary = H("trade_summary", buy ? M3("buy") : M3("sell"), CoinName(id), Quantity(units),
            H(buy ? "trade_payment" : "trade_receipt"), Math.Abs(quote.CashDelta), checked(cashBefore + quote.CashDelta));
        if (buy && quote.Value + quote.Fee < 1m)
            summary += "\n\n" + H("trade_small_order", Math.Abs(quote.CashDelta));
        var details = H("trade_terms", buy ? M3("buy") : M3("sell"), CoinName(id), day,
            PriceNumber(price), Quantity(units), PriceNumber(quote.Value), PriceNumber(quote.Fee),
            Math.Abs(quote.CashDelta), cashBefore, PriceNumber(quote.Value / quote.Quantity),
            PriceNumber(Math.Abs(quote.CashDelta) / quote.Quantity));
        ShowConfirmation(summary,
            () => ServerRoomOperations.TradeConfirmed(id, units, buy, day, price, quote.Revision, quote.NetBefore),
            details, H(buy ? "trade_confirm_buy" : "trade_confirm_sell"));
    }
    private void RefreshOperation(ServerRoomState.Document current)
    {
        operationSummary.text = M3("operation_summary", ServerRoomState.UnlockedSlots(current), ServerRoomState.HighestTier(current), Cash());
        var stage = ServerRoomState.CompletedStages(current);
        if (stage < current.Upgrades.Count)
        {
            var upgrade = current.Upgrades[stage];
            var price = -(upgrade.Power ? ServerRoomState.PrepareUpgrade(current, false) : ServerRoomState.PrepareUpgrade(current, true)).CashDelta;
            Bind(operationPower, upgrade.Power ? M2("paid") : M2("power", price), !upgrade.Power && Cash() >= price,
                () => Execute(() => ServerRoomOperations.Upgrade(true)));
            Bind(operationCooling, upgrade.Cooling ? M2("paid") : M2("cooling", price), !upgrade.Cooling && Cash() >= price,
                () => Execute(() => ServerRoomOperations.Upgrade(false)));
            operationUpgrade.text = M3("upgrade", stage + 1, upgrade.Power ? M2("paid") : "—", upgrade.Cooling ? M2("paid") : "—");
        }
        else operationUpgrade.text = M2("full_upgrade");
        StyleWidget(operationPower, WebPaper); StyleWidget(operationCooling, WebPaper);
        Bind(operationCatalog, M2("catalog"), true, OpenBrowserCatalog);
        var servers = current.Racks.Where(rack => rack.Kind == "server").OrderBy(rack => rack.RackIndex).ToArray();
        if (servers.All(rack => rack.RecordId != selectedServer)) selectedServer = servers.FirstOrDefault()?.RecordId ?? "";
        leasePage = ClampPage(leasePage, servers.Length);
        for (var i = 0; i < 5; i++)
        {
            var index = leasePage * 5 + i;
            if (index >= servers.Length) continue;
            var rack = servers[index]; var fact = ServerRoomOperations.MachineFact(current, rack.RecordId);
            var lease = current.Leases.FirstOrDefault(item => item.RecordId == rack.RecordId);
            var dailyFee = lease?.DailyFee ?? ServerRoomMiningModel.ElectricityCost(fact.Watts, current.Network.ElectricityFactor);
            Bind(leaseRows[i], M3("lease_row", rack.RecordId == selectedServer ? "▶" : "", rack.RackIndex + 1,
                Number(fact.EffectiveCompute), dailyFee, lease != null ? M3("lease_locked", lease.RemainingDays, lease.DueDay) :
                    fact.CanRun ? M3("stopped") : M3("cannot_run")), true,
                () => { selectedServer = rack.RecordId; RefreshContent(); SyncInteraction(); });
            StyleWidget(leaseRows[i], rack.RecordId == selectedServer ? WebSelected : WebPaper);
        }
        operationPager.text = M2("page", leasePage + 1, Math.Max(1, (servers.Length + 4) / 5));
        Bind(operationPrevious, M2("previous"), leasePage > 0, () => { leasePage--; RefreshContent(); SyncInteraction(); });
        Bind(operationNext, M2("next"), (leasePage + 1) * 5 < servers.Length, () => { leasePage++; RefreshContent(); SyncInteraction(); });
        var selected = servers.FirstOrDefault(rack => rack.RecordId == selectedServer);
        operationLease.text = selected == null ? M3("lease_empty") : M3("lease_choose", selected.RackIndex + 1);
        RefreshLeaseButton(current, selected, leaseThree, 3);
        RefreshLeaseButton(current, selected, leaseFive, 5);
        RefreshLeaseButton(current, selected, leaseSeven, 7);
        StyleWidget(leaseThree, WebSelected, WebBlue); StyleWidget(leaseFive, WebSelected, WebBlue); StyleWidget(leaseSeven, WebSelected, WebBlue);
    }
    private void RefreshLeaseButton(ServerRoomState.Document current, ServerRoomState.RackReference selected, ActionWidget widget, int days)
    {
        if (selected == null || current.Leases.Any(lease => lease.RecordId == selected.RecordId))
        { Bind(widget, M3("lease_button", days, "—"), false, null); return; }
        try
        {
            var quote = ServerRoomOperations.LeaseQuote(selected.RecordId, days);
            Bind(widget, M3("lease_button", days, quote.NetIncome), true, () =>
            {
                var fresh = ServerRoomOperations.LeaseQuote(selected.RecordId, days);
                ShowConfirmation(M3("lease_terms", selected.RackIndex + 1, fresh.Days, Number(fresh.DailyRent),
                    Number(fresh.Bonus * 100), Number(fresh.GrossRent), fresh.PrepaidFees, fresh.NetIncome, fresh.DueDay),
                    () => ServerRoomOperations.Lease(selected.RecordId, days));
            });
        }
        catch { Bind(widget, M3("lease_button", days, "—"), false, null); }
    }
    private void ShowConfirmation(string terms, Action action, string details = "", string acceptLabel = "")
    {
        ClearFocus();
        var current = ServerRoomRuntime.Current ?? throw new InvalidOperationException(M3("not_ready"));
        confirmationRun = current.Run; confirmationSlot = current.Slot; confirmationRevision = current.Revision;
        confirmationDay = Today(); confirmationCash = Cash();
        confirmationStorageEpoch = WorkroomStorage.State.Epoch; confirmationStorageRevision = WorkroomStorage.State.Revision;
        confirmation = action; confirmationSummary = terms; confirmationDetails = details;
        confirmationAcceptLabel = acceptLabel; confirmationExpanded = false;
        RefreshConfirmation(); SyncInteraction();
        AssemblyDebugFonts.Prepare(Text("title"), terms + details + acceptLabel + H("trade_details_show") + H("trade_details_hide") + M3("confirm") + M3("cancel"));
    }
    private bool ConfirmationCurrent()
    {
        var current = ServerRoomRuntime.Current;
        return ServerRoomRuntime.Ready && ServerRoomDaily.AllowCommands && current != null && current.Run == confirmationRun && current.Slot == confirmationSlot &&
            current.Revision == confirmationRevision && Today() == confirmationDay && Cash() == confirmationCash &&
            WorkroomStorage.State.Epoch == confirmationStorageEpoch && WorkroomStorage.State.Revision == confirmationStorageRevision;
    }
    private void RefreshConfirmation()
    {
        if (confirmation != null && !ConfirmationCurrent())
        { CancelConfirmation(); notice = M3("confirmation_expired"); noticeIsError = true; }
        confirmShield.SetActive(confirmation != null && !browserMinimized);
        if (confirmation == null) return;
        RefreshConfirmationLayout();
        Bind(confirmAccept, string.IsNullOrEmpty(confirmationAcceptLabel) ? M3("confirm") : confirmationAcceptLabel, true, () =>
        {
            if (!ConfirmationCurrent()) { CancelConfirmation(); ShowError(new InvalidOperationException(M3("confirmation_expired"))); SyncInteraction(); return; }
            var action = confirmation; CancelConfirmation(); Execute(action);
        });
        Bind(confirmCancel, M3("cancel"), true, () => { CancelConfirmation(); SyncInteraction(); });
        StyleWidget(confirmAccept, WebBlue, BrowserTone.OnAccent); StyleWidget(confirmCancel);
    }
    private void RefreshConfirmationLayout()
    {
        var hasDetails = !string.IsNullOrEmpty(confirmationDetails);
        var expanded = hasDetails && confirmationExpanded;
        var width = expanded ? 820f : 580f;
        var height = expanded ? 530f : 420f;
        AssemblyDebugUi.Place(confirmFace, (BrowserWidth - width) / 2, expanded ? 18 : 74, width, height);
        AssemblyDebugUi.Place(confirmText.gameObject, 24, 66, width - 48, expanded ? 348 : hasDetails ? 240 : 260);
        confirmText.fontSize = expanded ? 16 : 17;
        confirmText.richText = hasDetails;
        confirmText.text = confirmationSummary + (expanded ? "\n\n" + confirmationDetails : "");
        AssemblyDebugUi.Place(confirmCancel.Root, width - 318, height - 62, 128, 38);
        AssemblyDebugUi.Place(confirmAccept.Root, width - 176, height - 62, 152, 38);
        AssemblyDebugUi.Place(confirmDetails.Root, 24, height - 106, width - 48, 32);
        AssemblyDebugUi.Place(confirmDetails.Label.gameObject, 4, 0, width - 56, 32);
        Bind(confirmDetails, H(expanded ? "trade_details_hide" : "trade_details_show"), hasDetails, () =>
        {
            if (!ConfirmationCurrent()) { RefreshConfirmation(); SyncInteraction(); return; }
            confirmationExpanded = !confirmationExpanded;
            RefreshConfirmationLayout(); SyncInteraction();
        }, hasDetails);
        StyleWidget(confirmDetails, WebPaper, WebBlue);
    }
    private void CancelConfirmation()
    {
        confirmation = null;
        confirmationSummary = confirmationDetails = confirmationAcceptLabel = ""; confirmationExpanded = false;
        if (confirmDetails != null && AssemblyDebugUi.Alive(confirmDetails.Root)) confirmDetails.Root.SetActive(false);
        if (AssemblyDebugUi.Alive(confirmShield)) confirmShield.SetActive(false);
    }

    private void RefreshCatalog(ServerRoomState.Document current) => RefreshBrowserCatalog(current);

    private void RefreshRack(ServerRoomState.Document current)
    {
        panelTitle.text = Text("slot_title", selectedRack + 1);
        Bind(backAction, M2("back"), true, () => ApplyBrowserRoute(browserNavigation.Back()));
        if (selectedRack < 0 || selectedRack >= ServerRoomState.UnlockedSlots(current))
        { panelBody.text = M2("locked_body"); return; }
        var reference = current.Racks.FirstOrDefault(entry => entry.RackIndex == selectedRack);
        lists.SetActive(true);
        if (reference == null)
        {
            panelBody.text = M2("no_machine");
            sourceHeading.text = M2("body_sources");
            FillSources(ServerRoomOperations.AvailableBodies(), true, null, true);
            return;
        }
        var record = WorkroomStorage.State.FindRack(selectedRack);
        if (record == null || record.Id != reference.RecordId || record.Item.Nodes[0].UniqueId != reference.UniqueId ||
            !ServerRoomMachine.TryDefinition(record.Item.Identifier, out var definition))
            throw new InvalidOperationException(M2("unavailable", ""));
        var info = record.Item.Nodes[0].Machine ?? new WorkroomMachineAssembly.MachineInfo
            { Version = 3, Kind = definition.Kind, Body = record.Item };
        var capacity = ServerRoomCapacity.Read(info);
        var movable = Movable(current, selectedRack);
        var lease = current.Leases.FirstOrDefault(item => item.RecordId == reference.RecordId);
        panelBody.text = M3("rack_summary", MachineName(definition), info.Parts.Count, Number(capacity.BaseCompute),
            Number(capacity.EffectiveCompute), capacity.MemoryGB, capacity.RequiredMemoryGB, Number(capacity.Fraction * 100),
            capacity.DailyFee, lease != null ? M3("lease_locked", lease.RemainingDays, lease.DueDay) :
            reference.PoweredOn ? M3("running") : capacity.CanRun ? M3("manual_stop") : M3("cannot_run"));
        ServerRoomMiningModel.Capability miningCapacity = null;
        if (reference.Kind == ServerRoomMachine.Mining)
        {
            var facts = ServerRoomOperations.MachineFacts(current); var fact = facts.Single(f => f.RecordId == reference.RecordId);
            var forecast = ForecastNight(current, facts); var health = ServerRoomEconomy.Health(current, reference.UniqueId);
            var planned = forecast.Plan.Machines.FirstOrDefault(m => m.RecordId == reference.RecordId);
            miningCapacity = ServerRoomMiningModel.Evaluate(fact.Tier, fact.Cards, ServerRoomMarket.CoinIndex(reference.CoinId), reference.Mode, health);
            var shown = planned?.Runs == true ? planned.Capacity : miningCapacity;
            panelBody.text = H("parts_summary", MachineName(definition), CoinName(reference.CoinId), ModeName(reference.Mode),
                planned?.Runs == true ? ModeName(planned.Mode) : "—", Number(shown.EffectiveHash), Number(shown.Watts), Number(health),
                reference.PoweredOn ? PlanReason(forecast, reference.RecordId) : M3("manual_stop"));
        }
        slotDiagram.SetActive(true);
        Bind(takeAction, M2("take"), movable, () => Execute(() => ServerRoomOperations.TakeBody(selectedRack)));
        if (reference.Kind == "mining")
            Bind(rackPower, reference.PoweredOn ? M3("stop") : M3("run"), reference.PoweredOn || capacity.CanRun,
                () => Execute(() => ServerRoomOperations.Power(reference.RecordId, !reference.PoweredOn)));
        Bind(rackOperate, M3("manage"), true, () =>
        { if (reference.Kind == "mining") OpenMachine(reference.RecordId); else { selectedServer = reference.RecordId; SetPage(3); } });
        if (definition.Kind == ServerRoomMachine.Server)
        {
            Bind(cpuChoice, M2("cpu"), true, () => SetPartKind("cpu"));
            Bind(ramChoice, M2("ram"), true, () => SetPartKind("ram"));
            if (partKind is not ("cpu" or "ram")) partKind = "cpu";
        }
        else partKind = "gpu";
        installedHeading.text = M2("installed"); sourceHeading.text = M2("part_sources", M2(partKind));
        var installed = info.Parts.OrderBy(part => part.Kind, StringComparer.Ordinal).ThenBy(part => part.Index).ToArray();
        installedPage = ClampPage(installedPage, installed.Length);
        for (var row = 0; row < PageSize; row++)
        {
            var index = installedPage * PageSize + row;
            if (index >= installed.Length) continue;
            var part = installed[index]; var label = installedTexts[row]; label.gameObject.SetActive(true);
            var gpu = part.Kind == "gpu" ? capacity.Gpus.FirstOrDefault(card => card.Index == part.Index) : null;
            var gpuPosition = gpu == null ? -1 : capacity.Gpus.ToList().FindIndex(card => card.Index == part.Index);
            var selectedCard = gpuPosition >= 0 ? miningCapacity?.Cards.ElementAtOrDefault(gpuPosition) : null;
            label.text = gpu == null ? M2("installed_row", M2(part.Kind), part.Index + 1, Display(part.Item), part.Item.Value) :
                H("installed_gpu", part.Index + 1, Display(part.Item), gpu.MemoryGB, VramMinimum(ServerRoomMarket.CoinIndex(reference.CoinId)),
                    selectedCard != null && selectedCard.Reason.Length > 0 ? ReasonName(selectedCard.Reason) : H("card_eligible"));
            Bind(installedRows[row], M2("remove"), movable,
                () => Execute(() => ServerRoomOperations.Remove(selectedRack, part.Kind, part.Index)));
        }
        if (installed.Length == 0) installedHeading.text = M2("empty_list");
        Pager(false, installed.Length);
        FillSources(ServerRoomOperations.AvailableParts(partKind), false, info, movable);
    }

    private void SetPartKind(string kind)
    { ClearFocus(); partKind = kind; sourcePage = 0; RefreshContent(); SyncInteraction(); }

    private void FillSources(IReadOnlyList<WorkroomStorageState.Record> records, bool bodies,
        WorkroomMachineAssembly.MachineInfo info, bool movable)
    {
        var sources = records.OrderBy(record => record.Place == "room" ? 0 : 1).ThenBy(record => record.Item.Name, StringComparer.Ordinal).ToArray();
        sourcePage = ClampPage(sourcePage, sources.Length);
        var space = true;
        if (!bodies && info != null)
        {
            if (!ServerRoomMachine.TryDefinition(info.Body.Identifier, out var definition))
                throw new InvalidOperationException(M2("unavailable", ""));
            space = info.Parts.Count(part => part.Kind == partKind) < definition.Slots(partKind);
        }
        for (var row = 0; row < PageSize; row++)
        {
            var index = sourcePage * PageSize + row;
            if (index >= sources.Length) continue;
            var record = sources[index]; var label = sourceTexts[row]; label.gameObject.SetActive(true);
            var intact = bodies || ServerRoomMachine.IntactComponent(record.Item);
            label.text = M2("source_row", Display(record.Item), M2(record.Place), record.Item.Value);
            if (bodies && record.Item.Nodes.Count > 0 && SavedMaintenance(record.Item.Nodes[0].UniqueId) is { } savedHealth)
                label.text += H("source_health", Number(savedHealth.Health));
            Bind(sourceRows[row], !intact ? M2("damaged") : !space ? M2("no_slot") : M2(bodies ? "rack" : "install"),
                movable && intact && space, () => Execute(() =>
                { if (bodies) ServerRoomOperations.RackBody(selectedRack, record.Id); else ServerRoomOperations.Install(selectedRack, record.Id); }));
        }
        if (sources.Length == 0) sourceHeading.text = M2("empty_list");
        Pager(true, sources.Length);
    }

    private static ServerRoomState.Maintenance SavedMaintenance(int uniqueId) =>
        ServerRoomRuntime.Current?.Maintenance.FirstOrDefault(m => m.UniqueId == uniqueId);

    private void Pager(bool source, int count)
    {
        var page = source ? sourcePage : installedPage;
        var previous = source ? sourcePrevious : installedPrevious; var next = source ? sourceNext : installedNext;
        var label = source ? sourcePageLabel : installedPageLabel;
        label.text = M2("page", page + 1, Math.Max(1, (count + PageSize - 1) / PageSize));
        Bind(previous, M2("previous"), page > 0, () =>
        { if (source) sourcePage--; else installedPage--; ClearFocus(); RefreshContent(); SyncInteraction(); });
        Bind(next, M2("next"), (page + 1) * PageSize < count, () =>
        { if (source) sourcePage++; else installedPage++; ClearFocus(); RefreshContent(); SyncInteraction(); });
    }

    private void SyncInteraction()
    {
        var ready = built && !disposing && visible && interactive;
        sceneHover?.Update(ready && !panelState.BlocksInput, false);
        foreach (var button in sceneButtons) button.interactable = ready && !panelState.BlocksInput;
        var modal = confirmation != null;
        foreach (var button in panelButtons) button.interactable = ready && panelState.IsOpen && !modal;
        foreach (var widget in widgets)
            widget.Control.interactable = ready && panelState.IsOpen && !commandBusy && widget.Allowed &&
                (!modal || ReferenceEquals(widget, confirmAccept) || ReferenceEquals(widget, confirmCancel) || ReferenceEquals(widget, confirmDetails) || ReferenceEquals(widget, browserTheme) ||
                    ReferenceEquals(widget, browserMinimize) || ReferenceEquals(widget, browserExit) || ReferenceEquals(widget, taskbarExit) ||
                    browserMinimized && (desktopSites.Contains(widget) || taskbarSites.Contains(widget)));
        if (AssemblyDebugUi.Alive(quantity))
            quantity.interactable = ready && panelState.IsOpen && !browserMinimized && !modal && mode == PanelMode.Computer && selectedPage == 2 &&
                hardcorePage == HardcorePage.None &&
                ServerRoomRuntime.Current is { } current && TradingReady(current);
        SyncHardcoreInputs(ready && panelState.IsOpen && !browserMinimized && !modal);
        panelMask.SetActive(panelState.BlocksInput);
        panelFace.SetActive(panelState.IsOpen);
    }

    private static bool InputNeutral() => WorkroomGameAdapter.InputNeutral() &&
        !Input.GetKey(KeyCode.Escape) && !Input.GetKeyDown(KeyCode.Escape) && !Input.GetKeyUp(KeyCode.Escape);

    public void Dispose()
    {
        ClearFocus();
        generation++;
        disposing = true;
        visible = interactive = false;
        returnToWorkroom = null;
        var cleanup = new WorkroomUiCleanup();
        cleanup.Run("serverroom.entry-hover", () => { sceneHover?.Dispose(); sceneHover = null; });
        cleanup.Run("serverroom.canvas.stop", () => WorkroomUiCleanup.DeactivateCanvas(canvas));
        cleanup.Run("serverroom.canvas.destroy", () =>
        {
            WorkroomGameAdapter.ForgetLetterboxCanvases(canvas, null, null);
            AssemblyDebugUi.Destroy(canvas);
            canvas = null;
        });
        cleanup.ThrowIfFailed();
        // A failed stage preserves all remaining references until a later retry.
        roomSpace = panelSpace = panelMask = panelFace = tabs = slotDiagram = null;
        backdrop = roomBackground = null;
        panelTitle = panelBody = slotStatus = null;
        font = null;
        sceneButtons.Clear(); panelButtons.Clear();
        allLabels.Clear(); rackLabels.Clear(); rackLamps.Clear(); rackHoverDetails.Clear(); rackMachines.Clear(); widgets.Clear(); sourceRows.Clear(); installedRows.Clear();
        sourceTexts.Clear(); installedTexts.Clear();
        actions = lists = null;
        m3Content = overviewPage = miningPage = tradingPage = operationPage = chart = confirmShield = confirmFace = null;
        quantity = null; confirmation = null; keyboardOwned = false; inputNeutralFrames = 0;
        confirmDetails = null; confirmationSummary = confirmationDetails = confirmationAcceptLabel = ""; confirmationExpanded = false;
        overviewCards.Clear(); coinTabs.Clear(); leaseRows.Clear(); minerTexts.Clear();
        minerPower.Clear(); minerCoin.Clear(); minerUp.Clear(); minerDown.Clear(); minerConfigure.Clear(); chartLines.Clear();
        miningSummary = miningOutput = miningPager = tradingSummary = tradingHint = chartHigh = chartLow = chartDays = null;
        operationSummary = operationUpgrade = operationLease = operationPager = confirmText = null;
        miningPrevious = miningNext = tradeBuy = tradeSell = tradeAll = operationPower = operationCooling = operationCatalog = null;
        operationPrevious = operationNext = leaseThree = leaseFive = leaseSeven = confirmAccept = confirmCancel = rackPower = rackOperate = null;
        noticeLabel = sourceHeading = installedHeading = sourcePageLabel = installedPageLabel = null;
        primaryAction = backAction = takeAction = null;
        sourcePrevious = sourceNext = installedPrevious = installedNext = cpuChoice = ramChoice = null;
        ReleaseBrowserReferences();
        panelState.Reset();
        sampleFrame = escapeFrame = -1;
        built = disposing = false;
        selectedRack = -1; sourcePage = installedPage = selectedPage = 0;
        notice = renderedRevision = ""; commandBusy = noticeIsError = false; lastCommandFrame = -1;
    }
}
