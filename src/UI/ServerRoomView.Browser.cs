#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

using BrowserTone = PCExpansion.ServerRoomBrowserTheme.Tone;

namespace PCExpansion;

internal sealed partial class ServerRoomView
{
    private const float BrowserWidth = 1120, BrowserHeight = 640;
    private const BrowserTone WebInk = BrowserTone.Ink;
    private const BrowserTone WebMuted = BrowserTone.Muted;
    private const BrowserTone WebLine = BrowserTone.Line;
    private const BrowserTone WebPaper = BrowserTone.Paper;
    private const BrowserTone WebBack = BrowserTone.Back;
    private const BrowserTone WebBlue = BrowserTone.Blue;
    private const BrowserTone WebSelected = BrowserTone.Selected;
    private const BrowserTone WebBrown = BrowserTone.Brown;
    private readonly ServerRoomBrowserNavigation browserNavigation = new();
    private readonly List<ActionWidget> browserSites = new(), desktopSites = new(), taskbarSites = new(), managementLinks = new();
    private readonly List<ActionWidget> catalogCategories = new(), catalogProducts = new(), coinOptions = new();
    private readonly List<ActionWidget> orderActions = new(), cancelActions = new();
    private readonly List<GameObject> productCards = new();
    private readonly List<TextMeshProUGUI> productStoredCounts = new();
    private readonly List<TextMeshProUGUI> overviewValues = new(), overviewNames = new(), overviewDetails = new(), overviewStates = new();
    private readonly List<Image> overviewMarkers = new(), productImages = new();
    private readonly List<GameObject> overviewHoles = new();
    private readonly Dictionary<ActionWidget, Image> browserIcons = new();
    private readonly List<TextMeshProUGUI> walletValues = new(), productNames = new(), productSlots = new(), productPrices = new(), emptyOrders = new();
    private GameObject browserFrame, browserDesktop, browserRack, browserCatalog, browserSidebar, coinMenu;
    private TextMeshProUGUI browserAddress, browserDay, taskbarDay, overviewNote, tradeTitle, tradeAmount, tradeCash, catalogSummary, catalogNote, catalogOrdersHeading, dailyChange;
    private ActionWidget browserBack, browserForward, browserRefresh, browserHome, browserMinimize, browserExit;
    private ActionWidget taskbarExit, bulkCoin, bulkApply;
    private string catalogKind = "machines", coinMenuRecord = "";
    private bool browserMinimized, bulkMenuOpen;
    private int bulkCoinIndex;
    private static readonly string[] WebFontKeys = {
        "management", "market", "catalog", "workstation", "day", "quote_day", "ready", "refreshed", "home", "minimize", "exit",
        "overview", "mining", "operation", "cash", "night_fee", "running_leased", "wallet", "overview_note", "cabinet_left", "cabinet_right",
        "slot", "device", "state", "device_column", "power_column", "coin_column", "priority_column", "configure_column",
        "bulk_coin", "bulk_apply", "quote", "change", "no_change", "trade_title", "trade_quantity", "estimate", "available_cash",
        "holdings", "cost", "average", "valuation", "profit", "progress", "history", "history_unit", "orders",
        "catalog_summary", "catalog_slots_gpu", "catalog_slots_server", "base_price", "add_order", "replace_order", "product_locked",
        "empty_order", "catalog_note", "activation", "configured", "locked", "empty", "row_detail", "row_lease", "cash_unit", "coin_units",
        "machines", "components", "component_locked", "component_stock", "component_taken", "component_day", "discount_price", "catalog_terms", "shared_summary",
        "reservation_selected", "reservation_change", "reservation_locked", "reservation_hint",
        "theme", "theme_light", "theme_soft", "theme_dark", "overview_quick_start", "stored_count", "stored_unknown"
    };

    private static string Web(string key, params object[] arguments) => LanguageText.Get("serverroom.web." + key, arguments);

    private TextMeshProUGUI WebLabel(Transform parent, string name, string text, float x, float y, float w, float h,
        float size = 15, bool left = true, bool wrap = false, BrowserTone? color = null)
    {
        var label = Label(parent, name, text, x, y, w, h, size, left, wrap);
        label.fontStyle = FontStyles.Normal;
        SetBrowserTone(label, color ?? WebInk);
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    private GameObject WebBox(Transform parent, string name, float x, float y, float w, float h, BrowserTone color,
        bool round = false, bool raycast = false, int radius = UiRoundedGeometry.ControlRadius,
        UiRoundedGeometry.Corners corners = UiRoundedGeometry.Corners.All)
    {
        var go = BrowserBox(parent, name, x, y, w, h, color, raycast);
        if (round)
        {
            UiRoundedGeometry.Fill(go.GetComponent<Image>(), radius, corners);
        }
        return go;
    }

    private void WebBorder(Transform parent, string name, float x, float y, float w, float h, BrowserTone? color = null,
        int radius = UiRoundedGeometry.ControlRadius)
    {
        var c = color ?? WebLine;
        if (radius > 0)
        {
            var outline = BrowserBox(parent, name, x, y, w, h, c).GetComponent<Image>();
            UiRoundedGeometry.Outline(outline, radius);
            return;
        }
        BrowserBox(parent, name + "Top", x, y, w, 1, c); BrowserBox(parent, name + "Bottom", x, y + h - 1, w, 1, c);
        BrowserBox(parent, name + "Left", x, y, 1, h, c); BrowserBox(parent, name + "Right", x + w - 1, y, 1, h, c);
    }

    private GameObject CreateBrowserButton(Transform parent, string name, Color color, Action action)
    {
        var image = AssemblyDebugUi.Image(parent, name, color, true);
        UiRoundedGeometry.Fill(image);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        SetBrowserTone(image, WebPaper);
        RegisterBrowserSelectable(button);
        button.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(action));
        return image.gameObject;
    }

    private void StyleWidget(ActionWidget widget, BrowserTone? background = null, BrowserTone? ink = null, bool border = false)
    {
        SetBrowserTone(widget.Root.GetComponent<Image>(), widget.Allowed ? background ?? WebPaper : WebBack);
        SetBrowserTone(widget.Label, widget.Allowed ? ink ?? WebInk : WebMuted);
        if (browserIcons.TryGetValue(widget, out var icon)) SetBrowserTone(icon, widget.Allowed ? ink ?? WebInk : WebMuted);
        if (border)
        {
            var rect = AssemblyDebugUi.Rect(widget.Root);
            WebBorder(widget.Root.transform, "Border", 0, 0, rect.sizeDelta.x, rect.sizeDelta.y);
        }
    }

    private Image BrowserIcon(Transform parent, string name, ServerRoomBrowserArt.Icon icon, float x, float y, float size, BrowserTone color)
    {
        var image = BrowserBox(parent, name, x, y, size, size, color).GetComponent<Image>();
        image.sprite = ServerRoomBrowserArt.GetIcon(icon);
        image.type = Image.Type.Simple; image.raycastTarget = false;
        return image;
    }

    private void AddBrowserWidgetIcon(ActionWidget widget, ServerRoomBrowserArt.Icon icon, bool onlyIcon = true)
    {
        var rect = AssemblyDebugUi.Rect(widget.Root);
        var x = onlyIcon ? (rect.sizeDelta.x - 16) / 2 : rect.sizeDelta.x - 23;
        browserIcons.Add(widget, BrowserIcon(widget.Root.transform, "Icon", icon, x, (rect.sizeDelta.y - 16) / 2, 16, WebInk));
        if (onlyIcon) widget.Label.gameObject.SetActive(false);
        else AssemblyDebugUi.Place(widget.Label.gameObject, 4, 0, rect.sizeDelta.x - 29, rect.sizeDelta.y);
    }

    private void WebGlyph(Transform parent, string name, float x, float y, int site, BrowserTone color)
    {
        if (site == 0)
        {
            WebBorder(parent, name, x, y, 16, 16, color, 0);
            BrowserBox(parent, name + "Row", x + 2, y + 7, 12, 1, color);
            BrowserBox(parent, name + "DotA", x + 3, y + 3, 2, 2, color);
            BrowserBox(parent, name + "DotB", x + 3, y + 11, 2, 2, color);
        }
        else if (site == 1)
        {
            BrowserBox(parent, name + "X", x, y + 15, 17, 1, color); BrowserBox(parent, name + "Y", x, y, 1, 16, color);
            var line = BrowserBox(parent, name + "Trend", x + 3, y + 12, 15, 2, color);
            AssemblyDebugUi.Rect(line).localEulerAngles = new Vector3(0, 0, 33);
        }
        else
        {
            WebBorder(parent, name, x + 1, y + 2, 14, 13, color, 0);
            BrowserBox(parent, name + "Mid", x + 8, y + 3, 1, 11, color);
            BrowserBox(parent, name + "Top", x + 2, y + 5, 12, 1, color);
        }
    }

    private void DrawBrowserPanel()
    {
        panelFace = WebBox(panelSpace.transform, "BrowserWorkstation", 80, 40, BrowserWidth, BrowserHeight, WebPaper, true, true,
            UiRoundedGeometry.WindowRadius);
        panelFace.AddComponent<RectMask2D>();
        var frame = panelFace.transform;
        browserDesktop = WebBox(frame, "Desktop", 0, 0, BrowserWidth, 606, BrowserTone.Desktop, true, true,
            UiRoundedGeometry.WindowRadius, UiRoundedGeometry.Corners.Top);
        var slash = WebBox(browserDesktop.transform, "WallpaperGeometry", 470, 190, 670, 380, BrowserTone.Wallpaper);
        AssemblyDebugUi.Rect(slash).localEulerAngles = new Vector3(0, 0, 18);
        WebLabel(browserDesktop.transform, "Station", "PC expansion", 720, 278, 330, 44, 30, true, false, BrowserTone.DesktopInk);
        WebLabel(browserDesktop.transform, "StationCaption", Web("workstation"), 722, 327, 320, 26, 16, true, false, BrowserTone.DesktopInk);
        for (var i = 0; i < 3; i++)
        {
            var shortcut = Widget(browserDesktop.transform, "Shortcut" + i, 18, 22 + i * 112, 150, 92);
            SetBrowserTone(shortcut.Label, BrowserTone.TaskInk); shortcut.Label.fontSize = 15;
            AssemblyDebugUi.Place(shortcut.Label.gameObject, 0, 48, 150, 38);
            WebGlyph(shortcut.Root.transform, "Icon", 65, 17, i, BrowserTone.ShortcutInk);
            desktopSites.Add(shortcut);
        }
        browserFrame = WebBox(frame, "Browser", 0, 0, BrowserWidth, 606, WebPaper, true, true,
            UiRoundedGeometry.WindowRadius, UiRoundedGeometry.Corners.Top);
        var parent = browserFrame.transform;
        tabs = WebBox(parent, "BrowserTabs", 0, 0, BrowserWidth, 36, BrowserTone.Tabs, true, false,
            UiRoundedGeometry.WindowRadius, UiRoundedGeometry.Corners.Top);
        for (var i = 0; i < 3; i++)
        {
            var tab = Widget(tabs.transform, "Website" + i, 8 + i * 222, 4, 216, 32);
            UiRoundedGeometry.Fill(tab.Root.GetComponent<Image>(), UiRoundedGeometry.ControlRadius, UiRoundedGeometry.Corners.Top);
            tab.Label.fontSize = 14;
            AssemblyDebugUi.Place(tab.Label.gameObject, 34, 0, 175, 32);
            WebGlyph(tab.Root.transform, "Favicon", 10, 8, i, WebInk);
            browserSites.Add(tab);
        }
        browserMinimize = Widget(tabs.transform, "Minimize", 1032, 0, 42, 36);
        browserExit = Widget(tabs.transform, "Close", 1074, 0, 46, 36);
        UiRoundedGeometry.Fill(browserMinimize.Root.GetComponent<Image>(), UiRoundedGeometry.ControlRadius, UiRoundedGeometry.Corners.None);
        UiRoundedGeometry.Fill(browserExit.Root.GetComponent<Image>(), UiRoundedGeometry.WindowRadius, UiRoundedGeometry.Corners.TopRight);
        WebBox(parent, "Toolbar", 0, 36, BrowserWidth, 40, WebPaper);
        WebBorder(parent, "ToolbarBottom", 0, 75, BrowserWidth, 1);
        browserBack = Widget(parent, "Back", 8, 41, 32, 29);
        browserForward = Widget(parent, "Forward", 44, 41, 32, 29);
        browserRefresh = Widget(parent, "Refresh", 80, 41, 32, 29);
        AddBrowserWidgetIcon(browserBack, ServerRoomBrowserArt.Icon.Back);
        AddBrowserWidgetIcon(browserForward, ServerRoomBrowserArt.Icon.Forward);
        AddBrowserWidgetIcon(browserRefresh, ServerRoomBrowserArt.Icon.Refresh);
        WebBox(parent, "Address", 120, 41, 896, 29, BrowserTone.Address, true);
        BrowserIcon(parent, "AddressIcon", ServerRoomBrowserArt.Icon.Address, 136, 47, 16, WebMuted);
        browserAddress = WebLabel(parent, "AddressText", "", 166, 42, 831, 27, 13, true, false, WebMuted);
        browserHome = Widget(parent, "Home", 1072, 41, 36, 29);
        AddBrowserWidgetIcon(browserHome, ServerRoomBrowserArt.Icon.Home);
        WebBox(parent, "WebsiteHeader", 0, 76, BrowserWidth, 48, WebPaper);
        panelTitle = WebLabel(parent, "WebsiteName", "", 24, 83, 770, 33, 21);
        browserDay = WebLabel(parent, "GameDay", "", 890, 84, 206, 30, 13, false, false, WebMuted);
        BrowserBox(parent, "WebsiteDivider", 0, 123, BrowserWidth, 1, WebLine);
        DrawBrowserBusiness(parent);
        DrawBrowserRack(parent);
        DrawBrowserCatalog(parent);
        DrawHardcoreCatalog();
        WebBox(parent, "StatusBar", 0, 588, BrowserWidth, 18, BrowserTone.Subtle);
        BrowserBox(parent, "StatusDivider", 0, 588, BrowserWidth, 1, WebLine);
        noticeLabel = WebLabel(parent, "BrowserStatus", "", 12, 589, 1096, 16, 12, true, false, WebMuted);
        WebBox(frame, "Taskbar", 0, 606, BrowserWidth, 34, BrowserTone.Taskbar, true, true,
            UiRoundedGeometry.WindowRadius, UiRoundedGeometry.Corners.Bottom);
        for (var i = 0; i < 3; i++)
        {
            var task = Widget(frame, "TaskWebsite" + i, 6 + i * 46, 607, 42, 32);
            task.Label.gameObject.SetActive(false);
            WebGlyph(task.Root.transform, "Icon", 13, 8, i, BrowserTone.TaskInk);
            taskbarSites.Add(task);
        }
        WebLabel(frame, "TaskStation", Web("workstation"), 164, 610, 700, 25, 12, true, false, BrowserTone.TaskMuted);
        taskbarExit = Widget(frame, "TaskExit", 866, 608, 130, 29);
        taskbarDay = WebLabel(frame, "TaskDay", "", 1008, 610, 100, 25, 12, false, false, BrowserTone.TaskInk);
        DrawBrowserConfirmation(frame);
        // Keep the fixed theme control above the confirmation shield, so appearance
        // can be changed while preserving the pending transaction and its terms.
        browserTheme = Widget(frame, "Theme", 1024, 41, 36, 29);
        AddBrowserWidgetIcon(browserTheme, ServerRoomBrowserArt.Icon.Theme);
        DrawBrowserThemeTooltip();
        RefreshBrowserThemeLabel();
        browserDesktop.SetActive(false);
    }

    private GameObject BrowserPage(string name, bool management)
    {
        var root = WebBox(m3Content.transform, name, management ? 144 : 0, 0, management ? 976 : 1120, 464, WebBack);
        root.SetActive(false); return root;
    }

    private void DrawBrowserBusiness(Transform parent)
    {
        m3Content = AssemblyDebugUi.New("WebBusiness", parent);
        AssemblyDebugUi.Place(m3Content, 0, 124, 1120, 464);
        overviewPage = BrowserPage("Overview", true); miningPage = BrowserPage("Mining", true);
        tradingPage = BrowserPage("Exchange", false); operationPage = BrowserPage("Operation", true);
        browserSidebar = WebBox(m3Content.transform, "ManagementSidebar", 0, 0, 144, 464, BrowserTone.Sidebar);
        BrowserBox(browserSidebar.transform, "Divider", 143, 0, 1, 464, WebLine);
        for (var i = 0; i < 3; i++)
        {
            var link = Widget(browserSidebar.transform, "ManagementPage" + i, 9, 12 + i * 46, 126, 39);
            link.Label.fontSize = 15; managementLinks.Add(link);
        }
        WebLabel(browserSidebar.transform, "Stage", H("sidebar_hint"), 13, 346, 122, 105, 13, true, true, WebMuted);
        DrawBrowserOverview(); DrawBrowserMining(); DrawBrowserTrading(); DrawBrowserOperation();
        DrawHardcorePages();
        coinMenu = WebBox(parent, "CoinDropdown", 0, 0, 178, 180, WebPaper, true, true, UiRoundedGeometry.WindowRadius);
        WebBorder(coinMenu.transform, "DropdownBorder", 0, 0, 178, 180, radius: UiRoundedGeometry.WindowRadius);
        for (var i = 0; i < 5; i++)
        {
            var option = Widget(coinMenu.transform, "CoinOption" + i, 1, 1 + i * 35, 176, 35);
            if (i == 0 || i == 4)
                UiRoundedGeometry.Fill(option.Root.GetComponent<Image>(), UiRoundedGeometry.WindowRadius - 1,
                    i == 0 ? UiRoundedGeometry.Corners.Top : UiRoundedGeometry.Corners.Bottom);
            AddCoinWidgetArtwork(option, ServerRoomState.CoinIds[i], 16);
            coinOptions.Add(option);
        }
        coinMenu.SetActive(false); m3Content.SetActive(false);
    }

    private void DrawBrowserOverview()
    {
        var parent = overviewPage.transform;
        WebLabel(parent, "Title", Web("overview"), 20, 10, 160, 30, 22);
        WebLabel(parent, "QuickStart", Web("overview_quick_start"), 190, 12, 552, 28, 13, true, false, WebMuted);
        overviewMore = Widget(parent, "OperatingForecast", 756, 10, 200, 32);
        var metricKeys = new[] { "cash", "night_fee", "running_stopped", "estimated_net" };
        for (var i = 0; i < 4; i++)
        {
            WebLabel(parent, "MetricTitle" + i, i < 2 ? Web(metricKeys[i]) : H(metricKeys[i]), 20 + i * 234, 48, 224, 20, 13, true, false, WebMuted);
            overviewValues.Add(WebLabel(parent, "MetricValue" + i, "", 20 + i * 234, 72, 224, 36, 24));
        }
        for (var cabinet = 0; cabinet < 2; cabinet++)
        {
            var x = 20 + cabinet * 478;
            WebBox(parent, "Cabinet" + cabinet, x, 122, 458, 300, WebPaper, true);
            WebBorder(parent, "CabinetBorder" + cabinet, x, 122, 458, 300);
            WebLabel(parent, "CabinetTitle" + cabinet, Web(cabinet == 0 ? "cabinet_left" : "cabinet_right"), x + 12, 128, 432, 27, 16);
            WebBox(parent, "ColumnHeader" + cabinet, x + 1, 162, 456, 28, BrowserTone.Subtle);
            WebLabel(parent, "SlotHeader" + cabinet, Web("slot"), x + 12, 162, 46, 28, 13, true, false, WebMuted);
            WebLabel(parent, "DeviceHeader" + cabinet, Web("device"), x + 62, 162, 226, 28, 13, true, false, WebMuted);
            WebLabel(parent, "StateHeader" + cabinet, Web("state"), x + 299, 162, 148, 28, 13, true, false, WebMuted);
        }
        for (var i = 0; i < 10; i++)
        {
            var x = 20 + i / 5 * 478; var y = 190 + i % 5 * 46;
            var row = Widget(parent, "Rack" + i, x + 1, y, 456, 46);
            row.Label.gameObject.SetActive(false); overviewCards.Add(row);
            WebLabel(row.Root.transform, "Number", (i + 1).ToString("00"), 11, 0, 46, 46, 14, true, false, WebMuted);
            overviewNames.Add(WebLabel(row.Root.transform, "Name", "", 61, 2, 225, 21, 14, true, false, WebBlue));
            overviewDetails.Add(WebLabel(row.Root.transform, "Details", "", 61, 23, 225, 21, 12, true, false, WebMuted));
            var marker = BrowserBox(row.Root.transform, "StateShape", 295, 17, 10, 10, WebMuted).GetComponent<Image>();
            overviewMarkers.Add(marker);
            overviewHoles.Add(BrowserBox(marker.transform, "EmptyCenter", 1, 1, 4, 4, WebPaper));
            overviewStates.Add(WebLabel(row.Root.transform, "State", "", 312, 0, 137, 46, 13, true, true));
            BrowserBox(row.Root.transform, "Divider", 0, 45, 456, 1, WebLine);
        }
        overviewNote = WebLabel(parent, "Forecast", "", 20, 423, 936, 40, 13, true, true, WebMuted);
    }

    private void DrawBrowserMining()
    {
        var parent = miningPage.transform;
        WebLabel(parent, "Title", Web("mining"), 20, 10, 550, 30, 22);
        bulkCoin = Widget(parent, "BulkCoin", 642, 10, 167, 32);
        AddBrowserWidgetIcon(bulkCoin, ServerRoomBrowserArt.Icon.ChevronDown, false);
        AddCoinWidgetArtwork(bulkCoin, ServerRoomState.CoinIds[0], 16, selector: true);
        bulkApply = Widget(parent, "BulkApply", 820, 10, 136, 32);
        miningSummary = WebLabel(parent, "Fees", "", 20, 46, 936, 33, 14, true, true, WebMuted);
        WebBox(parent, "TableHead", 20, 84, 936, 28, BrowserTone.Subtle);
        var headers = new[] { "device_column", "power_column", "coin_column", "priority_column", "configure_column" };
        var xs = new float[] { 32, 339, 435, 590, 701 };
        for (var i = 0; i < headers.Length; i++) WebLabel(parent, "Header" + i, Web(headers[i]), xs[i], 84, i == 0 ? 295 : 150, 28, 13, true, false, WebMuted);
        for (var i = 0; i < 5; i++)
        {
            var y = 112 + i * 54;
            WebBox(parent, "MinerRow" + i, 20, y, 936, 54, WebPaper);
            BrowserBox(parent, "MinerDivider" + i, 20, y + 53, 936, 1, WebLine);
            minerTexts.Add(WebLabel(parent, "Miner" + i, "", 32, y + 3, 295, 48, 14, true, true));
            minerPower.Add(Widget(parent, "Power" + i, 337, y + 10, 82, 34));
            minerCoin.Add(Widget(parent, "Coin" + i, 433, y + 10, 142, 34));
            AddBrowserWidgetIcon(minerCoin[i], ServerRoomBrowserArt.Icon.ChevronDown, false);
            AddCoinWidgetArtwork(minerCoin[i], ServerRoomState.CoinIds[0], 16, selector: true);
            minerUp.Add(Widget(parent, "PriorityUp" + i, 588, y + 10, 32, 34));
            minerDown.Add(Widget(parent, "PriorityDown" + i, 627, y + 10, 32, 34));
            minerConfigure.Add(Widget(parent, "Configure" + i, 696, y + 10, 160, 34));
        }
        miningPrevious = Widget(parent, "Previous", 20, 389, 90, 27); miningNext = Widget(parent, "Next", 284, 389, 90, 27);
        miningPager = WebLabel(parent, "Page", "", 116, 389, 160, 27, 14, false);
        miningOutput = WebLabel(parent, "Output", "", 20, 421, 936, 38, 13, true, true, WebMuted);
    }

    private void DrawBrowserTrading()
    {
        var parent = tradingPage.transform;
        WebBox(parent, "CoinList", 0, 0, 140, 464, BrowserTone.Subtle);
        BrowserBox(parent, "CoinDivider", 139, 0, 1, 464, WebLine);
        for (var i = 0; i < 5; i++)
        {
            var tab = Widget(parent, "Coin" + i, 9, 14 + i * 74, 122, 66);
            tab.Label.fontSize = 14; tab.Label.enableWordWrapping = true;
            AddCoinWidgetArtwork(tab, ServerRoomState.CoinIds[i], 24);
            coinTabs.Add(tab);
        }
        quoteCoinArtwork = CreateCoinArtwork(parent, ServerRoomState.CoinIds[0], 160, 21.5f, 24);
        tradingSummary = WebLabel(parent, "Quote", "", 194, 14, 356, 39, 27);
        dailyChange = WebLabel(parent, "DailyChange", "", 564, 20, 246, 28, 14, false, false, WebMuted);
        chart = WebBox(parent, "History", 160, 62, 650, 222, BrowserTone.Chart, true);
        WebLabel(chart.transform, "HistoryTitle", Web("history"), 15, 5, 480, 27, 13, true, false, BrowserTone.ChartInk);
        WebLabel(chart.transform, "HistoryUnit", Web("history_unit"), 560, 5, 80, 27, 12, false, false, BrowserTone.ChartInk);
        for (var i = 0; i < 3; i++) BrowserBox(chart.transform, "Grid" + i, 82, 38 + i * 75, 542, 1, BrowserTone.ChartGrid);
        chartHigh = WebLabel(chart.transform, "High", "", 4, 26, 71, 27, 12, false, false, BrowserTone.ChartMuted);
        chartLow = WebLabel(chart.transform, "Low", "", 4, 174, 71, 27, 12, false, false, BrowserTone.ChartMuted);
        chartDays = WebLabel(parent, "HistoryDays", "", 166, 284, 640, 24, 12, true, false, WebMuted);
        for (var i = 0; i < 29; i++) chartLines.Add(BrowserBox(chart.transform, "Segment" + i, 0, 0, 1, 2, BrowserTone.ChartLine));
        var fields = new[] { "holdings", "cost", "average", "valuation", "profit", "progress" };
        for (var i = 0; i < fields.Length; i++)
        {
            var x = 160 + i % 3 * 217; var y = 318 + i / 3 * 60;
            WebLabel(parent, "WalletTitle" + i, i == 4 ? H("maximum_net") : i == 5 ? H("residue") : Web(fields[i]), x, y, 207, 20, 13, true, false, WebMuted);
            walletValues.Add(WebLabel(parent, "WalletValue" + i, "", x, y + 23, 207, i == 4 ? 48 : 28, i == 4 ? 14 : 17, true, i == 4));
        }
        WebBox(parent, "TradeForm", 834, 0, 286, 464, WebPaper);
        BrowserBox(parent, "FormDivider", 834, 0, 1, 464, WebLine);
        tradeCoinArtwork = CreateCoinArtwork(parent, ServerRoomState.CoinIds[0], 856, 19, 24);
        tradeTitle = WebLabel(parent, "FormTitle", "", 888, 14, 210, 34, 20);
        WebLabel(parent, "QuantityLabel", Web("trade_quantity"), 856, 62, 242, 22, 13, true, false, WebMuted);
        quantity = MakeQuantity(parent);
        WebLabel(parent, "EstimateLabel", Web("estimate"), 856, 145, 242, 21, 13, true, false, WebMuted);
        tradeAmount = WebLabel(parent, "Estimate", "", 856, 169, 242, 88, 15, true, true);
        tradeCash = WebLabel(parent, "Cash", "", 856, 263, 242, 20, 16);
        tradeBuy = Widget(parent, "Buy", 856, 291, 116, 39); StyleWidget(tradeBuy, WebBlue, BrowserTone.OnAccent);
        tradeSell = Widget(parent, "Sell", 982, 291, 116, 39); StyleWidget(tradeSell, WebPaper, WebInk, true);
        tradeAll = Widget(parent, "SellAll", 856, 342, 242, 35); StyleWidget(tradeAll, WebPaper, WebInk, true);
        tradeDetails = Widget(parent, "TradeDetails", 856, 383, 116, 31);
        tradeAutoSale = Widget(parent, "AutoSale", 982, 383, 116, 31);
        tradingHint = WebLabel(parent, "Hint", "", 856, 420, 242, 40, 13, true, true, WebMuted);
    }

    private void DrawBrowserOperation()
    {
        var parent = operationPage.transform;
        WebLabel(parent, "Title", Web("operation"), 20, 10, 640, 30, 22);
        operationSummary = WebLabel(parent, "Summary", "", 20, 46, 936, 28, 14, true, false, WebMuted);
        operationPower = Widget(parent, "Power", 20, 84, 456, 46);
        operationCooling = Widget(parent, "Cooling", 500, 84, 456, 46);
        StyleWidget(operationPower, WebPaper, WebInk, true); StyleWidget(operationCooling, WebPaper, WebInk, true);
        operationCatalog = Widget(parent, "Catalog", 762, 10, 194, 32);
        operationUpgrade = WebLabel(parent, "Upgrade", "", 20, 141, 936, 24, 14, true, false, WebMuted);
        WebBox(parent, "LeaseHeader", 20, 171, 936, 25, BrowserTone.Subtle);
        WebLabel(parent, "LeaseTitle", Web("operation"), 32, 171, 924, 25, 13, true, false, WebMuted);
        for (var i = 0; i < 5; i++) leaseRows.Add(Widget(parent, "LeaseRow" + i, 20, 196 + i * 35, 936, 33));
        operationPrevious = Widget(parent, "Previous", 20, 380, 90, 27); operationNext = Widget(parent, "Next", 284, 380, 90, 27);
        operationPager = WebLabel(parent, "Page", "", 116, 380, 160, 27, 14, false);
        operationLease = WebLabel(parent, "Lease", "", 388, 380, 568, 27, 14, true);
        leaseThree = Widget(parent, "LeaseThree", 20, 422, 296, 35); leaseFive = Widget(parent, "LeaseFive", 340, 422, 296, 35);
        leaseSeven = Widget(parent, "LeaseSeven", 660, 422, 296, 35);
        StyleWidget(leaseThree, WebSelected); StyleWidget(leaseFive, WebSelected); StyleWidget(leaseSeven, WebSelected);
    }

    private void DrawBrowserRack(Transform parent)
    {
        browserRack = WebBox(parent, "DeviceDetails", 0, 124, 1120, 464, WebBack);
        var root = browserRack.transform;
        panelBody = WebLabel(root, "DeviceSummary", "", 24, 12, 1072, 76, 16, true, true);
        actions = AssemblyDebugUi.New("ActivationActions", root); AssemblyDebugUi.Place(actions, 24, 113, 1072, 202);
        primaryAction = Widget(actions.transform, "Primary", 0, 0, 300, 44);
        StyleWidget(primaryAction, WebBlue, BrowserTone.OnAccent);
        slotStatus = WebLabel(actions.transform, "ActivationStatus", "", 0, 64, 1048, 100, 17, true, true);
        slotDiagram = AssemblyDebugUi.New("DeviceTools", root); AssemblyDebugUi.Place(slotDiagram, 24, 101, 1072, 40);
        takeAction = Widget(slotDiagram.transform, "Take", 0, 0, 190, 36);
        rackPower = Widget(slotDiagram.transform, "Power", 206, 0, 110, 36); rackOperate = Widget(slotDiagram.transform, "Manage", 328, 0, 152, 36);
        cpuChoice = Widget(slotDiagram.transform, "CPU", 538, 0, 170, 36); ramChoice = Widget(slotDiagram.transform, "RAM", 722, 0, 170, 36);
        backAction = Widget(root, "BackToPage", 960, 102, 136, 36);
        lists = AssemblyDebugUi.New("PartsLists", root); AssemblyDebugUi.Place(lists, 24, 154, 1072, 293);
        installedHeading = WebLabel(lists.transform, "InstalledHeading", "", 0, 0, 518, 29, 17);
        sourceHeading = WebLabel(lists.transform, "SourceHeading", "", 550, 0, 522, 29, 17);
        for (var i = 0; i < PageSize; i++)
        {
            var y = 36 + i * 40;
            WebBox(lists.transform, "InstalledRow" + i, 0, y, 518, 39, WebPaper, true);
            WebBox(lists.transform, "SourceRow" + i, 550, y, 522, 39, WebPaper, true);
            installedTexts.Add(WebLabel(lists.transform, "Installed" + i, "", 8, y, 405, 39, 14, true, true));
            sourceTexts.Add(WebLabel(lists.transform, "Source" + i, "", 558, y, 414, 39, 14, true, true));
            installedRows.Add(Widget(lists.transform, "Remove" + i, 425, y + 4, 86, 31));
            sourceRows.Add(Widget(lists.transform, "Install" + i, 983, y + 4, 86, 31));
        }
        installedPrevious = Widget(lists.transform, "InstalledPrevious", 0, 253, 90, 27); installedNext = Widget(lists.transform, "InstalledNext", 428, 253, 90, 27);
        installedPageLabel = WebLabel(lists.transform, "InstalledPage", "", 96, 253, 326, 27, 14, false);
        sourcePrevious = Widget(lists.transform, "SourcePrevious", 550, 253, 90, 27); sourceNext = Widget(lists.transform, "SourceNext", 982, 253, 90, 27);
        sourcePageLabel = WebLabel(lists.transform, "SourcePage", "", 646, 253, 330, 27, 14, false);
    }

    private void DrawBrowserCatalog(Transform parent)
    {
        browserCatalog = WebBox(parent, "HardwareWebsite", 0, 124, 1120, 464, BrowserTone.CatalogBack);
        var root = browserCatalog.transform;
        catalogCategories.Add(Widget(root, "Machines", 316, 12, 172, 32));
        catalogCategories.Add(Widget(root, "Components", 500, 12, 120, 32));
        catalogSummary = WebLabel(root, "Summary", "", 636, 12, 460, 32, 14, true, false, WebMuted);
        for (var i = 0; i < 10; i++)
        {
            var x = 24 + i % 2 * 362; var y = 57 + i / 2 * 70;
            var card = WebBox(root, "Product" + i, x, y, 350, 68, WebPaper, true); productCards.Add(card);
            WebBorder(card.transform, "Border", 0, 0, 350, 68, BrowserTone.CatalogLine);
            WebBox(card.transform, "ImageGround", 5, 8, 52, 52, BrowserTone.ArtworkGround);
            var image = AssemblyDebugUi.Image(card.transform, "Artwork", Color.white, false);
            AssemblyDebugUi.Place(image.gameObject, 7, 10, 48, 48); image.preserveAspect = true; productImages.Add(image);
            productNames.Add(WebLabel(card.transform, "Name", "", 63, 2, 196, 30, 13, true, true));
            productSlots.Add(WebLabel(card.transform, "Slots", "", 63, 32, 196, 16, 12, true, false, WebMuted));
            productPrices.Add(WebLabel(card.transform, "BasePrice", "", 63, 48, 196, 18, 12, true, false, WebMuted));
            var order = Widget(card.transform, "Reserve", 265, 18, 78, 32); order.Label.fontSize = 13;
            StyleWidget(order, WebBrown, BrowserTone.OnAccent); catalogProducts.Add(order);
            var stored = WebLabel(card.transform, "StoredCount", "", 263, 50, 82, 16, 11, false, false, WebMuted);
            productStoredCounts.Add(stored);
        }
        catalogOrdersHeading = WebLabel(root, "MyOrders", Web("orders"), 764, 57, 332, 24, 16);
        for (var i = 0; i < 5; i++)
        {
            var x = 764; var y = 85 + i * 63;
            WebBox(root, "OrderCard" + i, x, y, 332, 58, WebPaper, true);
            WebBorder(root, "OrderBorder" + i, x, y, 332, 58, BrowserTone.CatalogLine);
            var change = Widget(root, "ChangeOrder" + i, x + 5, y + 4, 252, 50); change.Label.enableWordWrapping = true; change.Label.fontSize = 13;
            var cancel = Widget(root, "CancelOrder" + i, x + 262, y + 12, 65, 34); cancel.Label.fontSize = 13;
            orderActions.Add(change); cancelActions.Add(cancel);
            emptyOrders.Add(WebLabel(root, "EmptyOrder" + i, Web("empty_order", i + 1), x + 10, y + 4, 312, 50, 14, true, true, WebMuted));
        }
        catalogNote = WebLabel(root, "Terms", "", 24, 414, 1072, 47, 13, true, true, BrowserTone.CatalogMuted);
        browserCatalog.SetActive(false);
    }

    private void DrawBrowserConfirmation(Transform parent)
    {
        // Keep title-bar minimize/close and the taskbar reachable while an order
        // is pending. Browser content remains modal under the raycast shield.
        confirmShield = WebBox(parent, "ConfirmationShield", 0, 36, 1120, 570, BrowserTone.Shield, false, true);
        confirmFace = WebBox(confirmShield.transform, "Confirmation", 270, 74, 580, 420, WebPaper, true, true,
            UiRoundedGeometry.WindowRadius);
        WebLabel(confirmFace.transform, "Title", M3("confirm"), 24, 16, 532, 34, 22);
        confirmText = WebLabel(confirmFace.transform, "Terms", "", 24, 66, 532, 260, 17, true, true);
        confirmDetails = Widget(confirmFace.transform, "Details", 24, 314, 532, 32);
        confirmDetails.Root.SetActive(false);
        confirmCancel = Widget(confirmFace.transform, "Cancel", 262, 358, 128, 38);
        confirmAccept = Widget(confirmFace.transform, "Accept", 404, 358, 152, 38);
        StyleWidget(confirmCancel, WebPaper, WebInk, true); StyleWidget(confirmAccept, WebBlue, BrowserTone.OnAccent);
        confirmShield.SetActive(false);
    }
}
