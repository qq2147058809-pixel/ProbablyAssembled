#nullable disable
using System;
using System.Linq;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PCExpansion;

internal sealed partial class ServerRoomView
{
    private void NavigateBrowser(ServerRoomBrowserNavigation.Route route) =>
        ApplyBrowserRoute(browserNavigation.Navigate(route));

    private void ApplyBrowserRoute(ServerRoomBrowserNavigation.Route route)
    {
        ClearFocus(); CancelConfirmation(); CloseBrowserMenus(); browserMinimized = false;
        switch (route.Kind)
        {
            case ServerRoomBrowserNavigation.Page.Catalog:
                mode = PanelMode.Catalog;
                catalogKind = route.Parameter == 0 ? ServerRoomMachine.Mining : ServerRoomMachine.Server;
                selectedRack = -1; break;
            case ServerRoomBrowserNavigation.Page.Rack:
                if (selectedRack != route.Parameter) { sourcePage = installedPage = 0; partKind = "cpu"; }
                mode = PanelMode.Rack; selectedRack = route.Parameter; break;
            default:
                mode = PanelMode.Computer; selectedRack = -1;
                selectedPage = route.Kind switch {
                    ServerRoomBrowserNavigation.Page.Mining => 1,
                    ServerRoomBrowserNavigation.Page.Trading => 2,
                    ServerRoomBrowserNavigation.Page.Operation => 3, _ => 0
                };
                if (selectedPage == 2) selectedCoin = route.Parameter;
                break;
        }
        notice = ""; noticeIsError = false; renderedRevision = "";
        RefreshContent(); SyncInteraction();
    }

    private void SelectBrowserSite(int site) => ApplyBrowserRoute(browserNavigation.SelectSite(site));

    private void MinimizeBrowser()
    {
        ClearFocus(); CancelConfirmation(); CloseBrowserMenus(); browserMinimized = true;
        RefreshContent(); SyncInteraction();
    }

    private void OpenBrowserCatalog() => NavigateBrowser(new ServerRoomBrowserNavigation.Route(
        ServerRoomBrowserNavigation.Page.Catalog, catalogKind == ServerRoomMachine.Mining ? 0 : 1));

    private void RefreshBrowserShell(ServerRoomState.Document current, bool navigationOnly = false)
    {
        var site = browserNavigation.Site;
        var names = new[] { Web("management"), Web("market"), Web("catalog") };
        for (var i = 0; i < 3; i++)
        {
            var target = i;
            Bind(browserSites[i], names[i], true, () => SelectBrowserSite(target));
            StyleWidget(browserSites[i], i == site ? WebPaper : new Color(.91f, .914f, .925f, 1));
            Bind(desktopSites[i], names[i], true, () => SelectBrowserSite(target));
            StyleWidget(desktopSites[i], new Color(.125f, .220f, .294f, 1), Color.white);
            Bind(taskbarSites[i], "", true, () => SelectBrowserSite(target));
            StyleWidget(taskbarSites[i], !browserMinimized && i == site ? new Color(.20f, .28f, .34f, 1) : new Color(.11f, .157f, .20f, 1));
        }
        Bind(browserBack, "", browserNavigation.CanGoBack, () => ApplyBrowserRoute(browserNavigation.Back()));
        Bind(browserForward, "", browserNavigation.CanGoForward, () => ApplyBrowserRoute(browserNavigation.Forward()));
        Bind(browserRefresh, "", true, () =>
        {
            ClearFocus(); CancelConfirmation(); CloseBrowserMenus(); renderedRevision = "";
            notice = Web("refreshed"); noticeIsError = false; RefreshContent(); SyncInteraction();
        });
        Bind(browserHome, "", true, () => SetPage(0));
        Bind(browserMinimize, "—", true, MinimizeBrowser);
        Bind(browserExit, "×", true, ClosePanels);
        Bind(taskbarExit, Web("exit"), true, ClosePanels);
        StyleWidget(browserBack); StyleWidget(browserForward); StyleWidget(browserRefresh); StyleWidget(browserHome);
        StyleWidget(browserMinimize, new Color(.91f, .914f, .925f, 1)); StyleWidget(browserExit, new Color(.91f, .914f, .925f, 1));
        StyleWidget(taskbarExit, new Color(.11f, .157f, .20f, 1), Color.white);
        var managementPages = new[] { 0, 1, 3 };
        var linkNames = new[] { Web("overview"), Web("mining"), Web("operation") };
        for (var i = 0; i < managementLinks.Count; i++)
        {
            var page = managementPages[i];
            Bind(managementLinks[i], linkNames[i], true, () => SetPage(page));
            StyleWidget(managementLinks[i], mode == PanelMode.Computer && selectedPage == page ? WebSelected : new Color(.976f, .980f, .984f, 1),
                mode == PanelMode.Computer && selectedPage == page ? WebBlue : WebMuted);
        }
        tabs.SetActive(true);
        browserFrame.SetActive(!browserMinimized); browserDesktop.SetActive(browserMinimized);
        browserSidebar.SetActive(mode == PanelMode.Computer && selectedPage != 2);
        browserCatalog.SetActive(mode == PanelMode.Catalog);
        if (mode == PanelMode.Catalog && (!ServerRoomRuntime.Ready || current == null)) catalogSummary.text = M2("order_unavailable");
        browserRack.SetActive(mode == PanelMode.Rack || mode == PanelMode.Computer &&
            (!ServerRoomRuntime.Ready || current == null || !current.Enabled));
        panelTitle.text = mode == PanelMode.Rack ? Text("slot_title", selectedRack + 1) : names[site];
        panelTitle.color = site == 2 ? WebBrown : WebBlue;
        var today = Today() < 0 ? "—" : Today().ToString();
        browserDay.text = site == 1 ? Web("quote_day", current?.Market.QuoteDay?.ToString() ?? "—", today) : Web("day", today);
        taskbarDay.text = Web("day", today);
        browserAddress.text = mode switch {
            PanelMode.Rack => "room.pce.test/slot/" + (selectedRack + 1).ToString("00"),
            PanelMode.Catalog => "luoxi.pce.test/reservations/" + catalogKind,
            _ => selectedPage switch {
                1 => "room.pce.test/mining", 2 => "exchange.pce.test/spot/" + ServerRoomState.CoinIds[selectedCoin],
                3 => "room.pce.test/operation", _ => "room.pce.test/overview"
            }
        };
        if (!navigationOnly)
        {
            RefreshBrowserCoinMenu(current);
            RefreshBrowserCoinArtwork(current);
        }
    }

    private void RefreshBrowserOverview(ServerRoomState.Document current, Forecast forecast)
    {
        overviewValues[0].text = Web("cash_unit", Cash());
        overviewValues[1].text = Web("cash_unit", forecast.Demand);
        overviewValues[2].text = current.Racks.Count(r => r.Kind == "mining" && r.PoweredOn) + " / " + current.Leases.Count;
        overviewValues[3].text = current.Market.Initialized ? Web("cash_unit", PriceNumber(Enumerable.Range(0, 5)
            .Sum(i => current.Market.Prices[i] * (current.Coins[i].Units10 / 10m)))) : "—";
        overviewNote.text = Web("overview_note", forecast.Payable, forecast.Unpaid.Count,
            ServerRoomState.UnlockedSlots(current), current.Racks.Count);
    }

    private void RefreshBrowserOverviewRow(int index, ServerRoomState.Document current, ServerRoomState.RackReference reference,
        ServerRoomEconomy.MachineFact fact, Forecast forecast)
    {
        var locked = index >= ServerRoomState.UnlockedSlots(current);
        var lease = reference == null ? null : current.Leases.FirstOrDefault(l => l.RecordId == reference.RecordId);
        var record = reference == null ? null : WorkroomStorage.State.FindRack(index);
        var name = record != null && ServerRoomMachine.TryDefinition(record.Item.Identifier, out var definition) ? MachineName(definition) :
            reference == null ? "" : M2(reference.Kind);
        overviewNames[index].text = locked ? Web("locked") : reference == null ? Web("empty") : name;
        overviewDetails[index].text = locked ? Web("product_locked") : reference == null ? M2("body_sources") :
            lease != null ? Web("row_lease", lease.RemainingDays, lease.DueDay) :
            Web("row_detail", Number(fact.EffectiveCompute), fact.DailyFee, reference.Kind == "mining" ? CoinName(reference.CoinId) : "");
        overviewStates[index].text = locked ? Web("locked") : reference == null ? Web("empty") :
            lease != null ? M3("leased") : !fact.CanRun ? M3("cannot_run") : !reference.PoweredOn ? M3("manual_stop") :
            forecast.Unpaid.Contains(reference.RecordId) ? M3("unpaid") : M3("running");
        overviewNames[index].color = locked ? WebMuted : WebBlue;
        var marker = overviewMarkers[index];
        var state = RackVisualState(locked, reference, lease != null, fact);
        marker.sprite = ServerRoomStatusArt.SpriteFor(state);
        marker.type = Image.Type.Simple;
        marker.color = ServerRoomStatusArt.ColorFor(state);
        overviewHoles[index].SetActive(false);
        StyleWidget(overviewCards[index], WebPaper);
    }

    private void RefreshBrowserWallet(ServerRoomState.Document current, ServerRoomState.Coin wallet, decimal price, string average)
    {
        tradingSummary.text = Web("cash_unit", PriceNumber(price));
        if (current.Market.History.Count >= 2 && current.Market.History[^2].Prices[selectedCoin] > 0)
        {
            var previous = current.Market.History[^2].Prices[selectedCoin];
            var percent = (price - previous) / previous * 100;
            dailyChange.text = Web("change", percent > 0 ? "+" : "", Number(percent));
            dailyChange.color = percent >= 0 ? new Color(.61f, .33f, .18f, 1) : new Color(.14f, .51f, .40f, 1);
        }
        else dailyChange.text = Web("no_change");
        tradeTitle.text = Web("trade_title", CoinName(wallet.Id));
        tradeCash.text = Web("cash_unit", Cash());
        walletValues[0].text = Web("coin_units", Quantity(wallet.Units10));
        walletValues[1].text = Web("cash_unit", PriceNumber(wallet.Cost));
        walletValues[2].text = Web("cash_unit", average);
        walletValues[3].text = Web("cash_unit", PriceNumber(price * (wallet.Units10 / 10m)));
        walletValues[4].text = Web("cash_unit", PriceNumber(price * (wallet.Units10 / 10m) - wallet.Cost));
        walletValues[5].text = Progress(wallet.Progress) + "%";
        StyleWidget(tradeBuy, WebBlue, Color.white); StyleWidget(tradeSell, WebPaper); StyleWidget(tradeAll, WebPaper);
        RefreshBrowserTradeAmount();
    }

    private void RefreshBrowserTradeAmount()
    {
        if (!AssemblyDebugUi.Alive(tradeAmount) || !AssemblyDebugUi.Alive(quantity)) return;
        var current = ServerRoomRuntime.Current;
        try
        {
            if (current == null || !current.Market.Initialized) { tradeAmount.text = "—"; return; }
            var units = ServerRoomTradeMath.ParseUnits(quantity.text);
            tradeAmount.text = Web("cash_unit", PriceNumber(checked(current.Market.Prices[selectedCoin] * (units / 10m))));
        }
        catch { tradeAmount.text = "—"; }
    }

    private void OpenBrowserCoinMenu(string record, int row)
    {
        ClearFocus(); CancelConfirmation(); coinMenuRecord = record; bulkMenuOpen = false;
        var y = 124 + 112 + row * 54 + 44;
        if (y + 180 > 588) y -= 214;
        AssemblyDebugUi.Place(coinMenu, 144 + 433, y, 178, 180);
        RefreshContent(); SyncInteraction();
    }

    private void OpenBrowserBulkMenu()
    {
        ClearFocus(); CancelConfirmation(); coinMenuRecord = ""; bulkMenuOpen = !bulkMenuOpen;
        AssemblyDebugUi.Place(coinMenu, 144 + 642, 124 + 43, 178, 180);
        RefreshContent(); SyncInteraction();
    }

    private void CloseBrowserMenus() { coinMenuRecord = ""; bulkMenuOpen = false; if (AssemblyDebugUi.Alive(coinMenu)) coinMenu.SetActive(false); }

    private void RefreshBrowserCoinMenu(ServerRoomState.Document current)
    {
        var mining = mode == PanelMode.Computer && selectedPage == 1 && current != null && current.Enabled;
        if (!mining || browserMinimized) CloseBrowserMenus();
        var miners = mining ? current.Racks.Count(r => r.Kind == "mining") : 0;
        if (mining)
        {
            Bind(bulkCoin, CoinName(ServerRoomState.CoinIds[bulkCoinIndex]), miners > 0, OpenBrowserBulkMenu);
            Bind(bulkApply, Web("bulk_apply"), miners > 0, () => Execute(() => ServerRoomOperations.MineAll(ServerRoomState.CoinIds[bulkCoinIndex])));
            StyleWidget(bulkCoin); StyleWidget(bulkApply, WebBlue, Color.white);
        }
        if (coinMenuRecord != "" && !current.Racks.Any(r => r.RecordId == coinMenuRecord && r.Kind == "mining")) CloseBrowserMenus();
        var open = mining && (bulkMenuOpen || coinMenuRecord != "");
        coinMenu.SetActive(open);
        if (!open) return;
        coinMenu.transform.SetAsLastSibling();
        for (var i = 0; i < 5; i++)
        {
            var coin = i; var record = coinMenuRecord;
            Bind(coinOptions[i], CoinName(ServerRoomState.CoinIds[i]), true, () =>
            {
                if (record == "") { bulkCoinIndex = coin; CloseBrowserMenus(); RefreshContent(); SyncInteraction(); }
                else { CloseBrowserMenus(); Execute(() => ServerRoomOperations.MineCoin(record, ServerRoomState.CoinIds[coin])); }
            });
            StyleWidget(coinOptions[i], bulkMenuOpen && i == bulkCoinIndex ? WebSelected : WebPaper);
        }
    }

    private void RefreshBrowserCatalog(ServerRoomState.Document current)
    {
        var ready = ServerRoomOrders.Ready;
        var orders = ready ? ServerRoomOrders.List() : Array.Empty<ServerRoomOrderState.Order>();
        var selectedOrder = orders.FirstOrDefault(o => o.Id == selectedOrderId);
        if (selectedOrder != null && !ServerRoomOrderStock.CanEdit(selectedOrder) || selectedOrder == null && selectedOrderId != "") selectedOrderId = "";
        catalogSummary.text = ready ? Web("catalog_summary", current.Enabled ? ServerRoomState.HighestTier(current) : 0,
            orders.Count, ServerRoomOrderState.Limit) : M2("order_unavailable");
        for (var category = 0; category < 2; category++)
        {
            var parameter = category; var kind = category == 0 ? ServerRoomMachine.Mining : ServerRoomMachine.Server;
            Bind(catalogCategories[category], M2(kind), true, () => NavigateBrowser(new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.Catalog, parameter)));
            StyleWidget(catalogCategories[category], kind == catalogKind ? new Color(.933f, .886f, .827f, 1) : WebPaper, WebBrown);
        }
        var highest = current.Enabled ? ServerRoomState.HighestTier(current) : 0;
        for (var i = 0; i < 5; i++)
        {
            var definition = ServerRoomMachine.DefinitionFor(catalogKind, i + 1);
            productImages[i].sprite = WorkroomArt.Get(ServerRoomHardware.ArtworkName(definition));
            productImages[i].color = definition.Tier <= highest ? Color.white : new Color(1, 1, 1, .45f);
            productNames[i].text = MachineName(definition);
            productSlots[i].text = definition.Kind == ServerRoomMachine.Mining ? Web("catalog_slots_gpu", definition.GpuSlots) :
                Web("catalog_slots_server", definition.CpuSlots, definition.RamSlots);
            productPrices[i].text = Web("base_price", definition.BaseValue);
            var replacement = selectedOrderId;
            var unlocked = definition.Tier <= highest;
            Bind(catalogProducts[i], !unlocked ? Web("product_locked") : Web(replacement == "" ? "add_order" : "replace_order"),
                ready && current.Enabled && unlocked && (replacement != "" || orders.Count < ServerRoomOrderState.Limit),
                () => Execute(() =>
                {
                    if (replacement == "") ServerRoomOrders.Add(definition.Kind, definition.Tier);
                    else ServerRoomOrders.Replace(replacement, definition.Kind, definition.Tier);
                    selectedOrderId = "";
                }));
            StyleWidget(catalogProducts[i], WebBrown, Color.white);
        }
        var changes = new[] { firstOrderAction, secondOrderAction, thirdOrderAction };
        var cancels = new[] { firstOrderCancel, secondOrderCancel, thirdOrderCancel };
        for (var i = 0; i < 3; i++)
        {
            emptyOrders[i].gameObject.SetActive(i >= orders.Count);
            if (i >= orders.Count) continue;
            var order = orders[i]; var editable = ServerRoomOrderStock.CanEdit(order); var selected = selectedOrderId == order.Id;
            var definition = ServerRoomMachine.DefinitionFor(order.Kind, order.Tier);
            Bind(changes[i], editable ? M2(selected ? "order_change_selected" : "order_change", i + 1, MachineName(definition)) :
                M2("order_locked_short", i + 1), editable, () => { selectedOrderId = selected ? "" : order.Id; RefreshContent(); SyncInteraction(); });
            StyleWidget(changes[i], selected ? new Color(.933f, .886f, .827f, 1) : WebPaper, WebBrown);
            Bind(cancels[i], M2("order_cancel_short"), editable, () => Execute(() =>
            {
                ServerRoomOrders.Cancel(order.Id); if (selectedOrderId == order.Id) selectedOrderId = "";
            }));
            StyleWidget(cancels[i]);
        }
        catalogNote.text = selectedOrderId == "" ? Web("catalog_note") : M2("order_change_hint", Array.FindIndex(orders.ToArray(), o => o.Id == selectedOrderId) + 1) + "\n" + Web("catalog_note");
    }

    private void ClearBrowserSelection()
    {
        browserNavigation.Reset(); browserMinimized = false; CloseBrowserMenus(); catalogKind = ServerRoomMachine.Mining; bulkCoinIndex = 0;
    }

    private void ReleaseBrowserReferences()
    {
        ReleaseCoinArtworkReferences();
        browserFrame = browserDesktop = browserRack = browserCatalog = browserSidebar = coinMenu = null;
        browserAddress = browserDay = taskbarDay = overviewNote = tradeTitle = tradeAmount = tradeCash = catalogSummary = catalogNote = dailyChange = null;
        browserBack = browserForward = browserRefresh = browserHome = browserMinimize = browserExit = taskbarExit = bulkCoin = bulkApply = null;
        browserSites.Clear(); desktopSites.Clear(); taskbarSites.Clear(); managementLinks.Clear(); catalogCategories.Clear(); catalogProducts.Clear(); coinOptions.Clear(); browserIcons.Clear();
        overviewValues.Clear(); overviewNames.Clear(); overviewDetails.Clear(); overviewStates.Clear(); overviewMarkers.Clear(); overviewHoles.Clear();
        productImages.Clear(); productNames.Clear(); productSlots.Clear(); productPrices.Clear(); emptyOrders.Clear(); walletValues.Clear();
        browserNavigation.Reset(); browserMinimized = bulkMenuOpen = false; coinMenuRecord = ""; catalogKind = ServerRoomMachine.Mining; bulkCoinIndex = 0;
    }
}
