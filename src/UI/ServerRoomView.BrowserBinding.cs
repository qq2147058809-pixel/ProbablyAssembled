#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

using BrowserTone = PCExpansion.ServerRoomBrowserTheme.Tone;

namespace PCExpansion;

internal sealed partial class ServerRoomView
{
    private void NavigateBrowser(ServerRoomBrowserNavigation.Route route) =>
        ApplyBrowserRoute(browserNavigation.Navigate(route));

    private void ApplyBrowserRoute(ServerRoomBrowserNavigation.Route route)
    {
        SaveTradeInput(); ClearFocus(); CloseBrowserMenus(); browserMinimized = false;
        hardcorePage = HardcorePage.None;
        switch (route.Kind)
        {
            case ServerRoomBrowserNavigation.Page.Catalog:
                mode = PanelMode.Catalog;
                catalogKind = route.Parameter == 0 ? "machines" : "components";
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
                if (selectedPage == 2) { selectedCoin = route.Parameter; RestoreTradeInput(); }
                break;
        }
        hardcorePage = route.Kind switch {
            ServerRoomBrowserNavigation.Page.Forecast => HardcorePage.Forecast,
            ServerRoomBrowserNavigation.Page.Machine => HardcorePage.Machine,
            ServerRoomBrowserNavigation.Page.Network => HardcorePage.Network,
            ServerRoomBrowserNavigation.Page.AutoSale => HardcorePage.AutoSale,
            ServerRoomBrowserNavigation.Page.Ledger => HardcorePage.Ledger,
            ServerRoomBrowserNavigation.Page.Guide => HardcorePage.Guide,
            ServerRoomBrowserNavigation.Page.TradeDetails => HardcorePage.Trade,
            ServerRoomBrowserNavigation.Page.HardwarePerformance => HardcorePage.Hardware,
            _ => HardcorePage.None
        };
        if (hardcorePage == HardcorePage.Machine) machineRecord = ServerRoomRuntime.Current?.Racks.FirstOrDefault(r => r.RackIndex == route.Parameter)?.RecordId ?? "";
        if (hardcorePage == HardcorePage.Network) networkCoin = route.Parameter;
        if (hardcorePage == HardcorePage.Guide) guidePage = route.Parameter;
        if (hardcorePage == HardcorePage.Trade) { selectedPage = 2; selectedCoin = route.Parameter; RestoreTradeInput(); }
        notice = ""; noticeIsError = false; renderedRevision = "";
        RefreshContent(); SyncInteraction();
    }

    private void SelectBrowserSite(int site) => ApplyBrowserRoute(browserNavigation.SelectSite(site));

    private void MinimizeBrowser()
    {
        ClearFocus(); CloseBrowserMenus(); browserMinimized = true;
        RefreshContent(); SyncInteraction();
    }

    private void OpenBrowserCatalog() => NavigateBrowser(new ServerRoomBrowserNavigation.Route(
        ServerRoomBrowserNavigation.Page.Catalog, catalogKind == "machines" ? 0 : 1));

    private void RefreshBrowserShell(ServerRoomState.Document current, bool navigationOnly = false)
    {
        var site = browserNavigation.Site;
        var names = new[] { Web("management"), Web("market"), Web("catalog") };
        for (var i = 0; i < 3; i++)
        {
            var target = i;
            Bind(browserSites[i], names[i], true, () => SelectBrowserSite(target));
            StyleWidget(browserSites[i], i == site ? WebPaper : BrowserTone.Tabs);
            Bind(desktopSites[i], names[i], true, () => SelectBrowserSite(target));
            StyleWidget(desktopSites[i], BrowserTone.Desktop, BrowserTone.TaskInk);
            Bind(taskbarSites[i], "", true, () => SelectBrowserSite(target));
            StyleWidget(taskbarSites[i], !browserMinimized && i == site ? BrowserTone.TaskSelected : BrowserTone.Taskbar);
        }
        Bind(browserBack, "", browserNavigation.CanGoBack, () => ApplyBrowserRoute(browserNavigation.Back()));
        Bind(browserForward, "", browserNavigation.CanGoForward, () => ApplyBrowserRoute(browserNavigation.Forward()));
        Bind(browserRefresh, "", true, () =>
        {
            ClearFocus(); CloseBrowserMenus(); renderedRevision = "";
            notice = Web("refreshed"); noticeIsError = false; RefreshContent(); SyncInteraction();
        });
        Bind(browserHome, "", true, () => SetPage(0));
        Bind(browserTheme, Web("theme", ServerRoomBrowserTheme.Name), true, CycleBrowserTheme);
        StyleWidget(browserTheme, BrowserTone.Tabs);
        browserTheme.Root.SetActive(!browserMinimized);
        Bind(browserMinimize, "—", true, MinimizeBrowser);
        Bind(browserExit, "×", true, ClosePanels);
        Bind(taskbarExit, Web("exit"), true, ClosePanels);
        StyleWidget(browserBack); StyleWidget(browserForward); StyleWidget(browserRefresh); StyleWidget(browserHome);
        StyleWidget(browserMinimize, BrowserTone.Tabs); StyleWidget(browserExit, BrowserTone.Tabs);
        StyleWidget(taskbarExit, BrowserTone.Taskbar, BrowserTone.TaskInk);
        var managementPages = new[] { 0, 1, 3 };
        var linkNames = new[] { Web("overview"), Web("mining"), Web("operation") };
        for (var i = 0; i < managementLinks.Count; i++)
        {
            var page = managementPages[i];
            Bind(managementLinks[i], linkNames[i], true, () => SetPage(page));
            StyleWidget(managementLinks[i], mode == PanelMode.Computer && hardcorePage == HardcorePage.None && selectedPage == page ? WebSelected : BrowserTone.Sidebar,
                mode == PanelMode.Computer && hardcorePage == HardcorePage.None && selectedPage == page ? WebBlue : WebMuted);
        }
        tabs.SetActive(true);
        browserFrame.SetActive(!browserMinimized); browserDesktop.SetActive(browserMinimized);
        browserSidebar.SetActive(mode == PanelMode.Computer && selectedPage != 2 && site == 0);
        browserCatalog.SetActive(mode == PanelMode.Catalog);
        if (mode == PanelMode.Catalog && (!ServerRoomRuntime.Ready || current == null)) catalogSummary.text = M2("order_unavailable");
        browserRack.SetActive(mode == PanelMode.Rack || mode == PanelMode.Computer &&
            (!ServerRoomRuntime.Ready || current == null || !current.Enabled));
        panelTitle.text = mode == PanelMode.Rack ? Text("slot_title", selectedRack + 1) : names[site];
        SetBrowserTone(panelTitle, site == 2 ? WebBrown : WebBlue);
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
        RefreshHardcoreNavigation();
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
        overviewValues[2].text = forecast.Plan.Machines.Count(m => m.Runs) + " / " + forecast.Stopped.Count;
        overviewValues[3].text = current.Market.Initialized ? Web("cash_unit", PriceNumber(Enumerable.Range(0, 5)
            .Sum(i => current.Market.Prices[i] * forecast.Produced[i]) - forecast.Payable)) : "—";
        overviewNote.text = H("overview_note", forecast.Payable, current.Leases.Count, Number(forecast.Plan.DeviceWatts * 1.15m),
            ServerRoomEconomy.PowerLimit(current), Number(forecast.Plan.DeviceWatts), ServerRoomEconomy.HeatLimit(current));
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
            reference.Kind == "mining" ? H("row_detail", CoinName(reference.CoinId), ModeName(reference.Mode), Number(ServerRoomEconomy.Health(current, reference.UniqueId))) : H("server_idle");
        overviewStates[index].text = locked ? Web("locked") : reference == null ? Web("empty") :
            lease != null ? M3("leased") : !fact.CanRun ? M3("cannot_run") : !reference.PoweredOn ? M3("manual_stop") :
            PlanReason(forecast, reference.RecordId);
        SetBrowserTone(overviewNames[index], locked ? WebMuted : WebBlue);
        var marker = overviewMarkers[index];
        var state = RackVisualState(locked, reference, lease != null, fact, forecast);
        marker.sprite = ServerRoomStatusArt.SpriteFor(state);
        marker.type = Image.Type.Simple;
        SetBrowserTone(marker, state switch {
            ServerRoomStatusArt.State.Running => BrowserTone.Loss,
            ServerRoomStatusArt.State.Stopped => BrowserTone.Gain,
            ServerRoomStatusArt.State.Leased => WebBrown, _ => WebMuted
        });
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
            SetBrowserTone(dailyChange, percent >= 0 ? BrowserTone.Gain : BrowserTone.Loss);
        }
        else { dailyChange.text = Web("no_change"); SetBrowserTone(dailyChange, WebMuted); }
        tradeTitle.text = Web("trade_title", CoinName(wallet.Id));
        tradeCash.text = H("cash_available", Cash());
        walletValues[0].text = Web("coin_units", Quantity(wallet.MicroUnits));
        walletValues[1].text = Web("cash_unit", PriceNumber(wallet.Cost));
        walletValues[2].text = Web("cash_unit", average);
        walletValues[3].text = Web("cash_unit", PriceNumber(price * (wallet.MicroUnits / 1000000m)));
        var maximum = TradingReady(current) ? ServerRoomOperations.MaximumTrade(wallet.Id, false) : 0;
        walletValues[4].text = maximum > 0 ? H("sellable_value", Quantity(maximum), ServerRoomOperations.TradeQuote(wallet.Id, maximum, false).CashDelta) : H("no_sellable");
        walletValues[5].text = PriceNumber(wallet.ResidueCoin);
        StyleWidget(tradeBuy, WebBlue, BrowserTone.OnAccent); StyleWidget(tradeSell, WebPaper); StyleWidget(tradeAll, WebPaper);
        RefreshBrowserTradeAmount();
    }

    private void RefreshBrowserTradeAmount()
    {
        if (!AssemblyDebugUi.Alive(tradeAmount) || !AssemblyDebugUi.Alive(quantity)) return;
        var current = ServerRoomRuntime.Current;
        try
        {
            if (current == null || !current.Market.Initialized) { tradeAmount.text = "—"; return; }
            RefreshHardcoreTradeQuote(current);
        }
        catch (Exception ex) { tradeAmount.text = H("invalid_quantity"); if (AssemblyDebugUi.Alive(tradingHint)) tradingHint.text = ex.Message; }
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
            StyleWidget(bulkCoin); StyleWidget(bulkApply, WebBlue, BrowserTone.OnAccent);
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
        if (ready && LuoxiStory.Unlocked) ServerRoomOrders.EnsureCatalog();
        var catalog = ready ? ServerRoomOrders.Current : null;
        var orders = ready ? ServerRoomOrders.List() : Array.Empty<ServerRoomOrderState.Order>();
        var selectedOrder = orders.FirstOrDefault(o => o.Id == selectedOrderId);
        catalogOrdersHeading.text = Web("orders");
        if (selectedOrder != null && !ServerRoomOrderStock.CanEdit(selectedOrder) || selectedOrder == null && selectedOrderId != "") selectedOrderId = "";
        catalogSummary.text = ready ? Web("shared_summary", orders.Count, ServerRoomOrders.CurrentLimit,
            current.Enabled ? ServerRoomState.HighestTier(current) : 0) : M2("order_unavailable");
        for (var category = 0; category < catalogCategories.Count; category++)
        {
            var parameter = category; var kind = category == 0 ? "machines" : "components";
            Bind(catalogCategories[category], Web(kind), true, () => NavigateBrowser(new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.Catalog, parameter)));
            StyleWidget(catalogCategories[category], kind == catalogKind ? BrowserTone.CatalogSelected : WebPaper, WebBrown);
        }
        var highest = current.Enabled ? ServerRoomState.HighestTier(current) : 0;
        var components = catalogKind == "components";
        // Read only when the catalog is opened/refreshed or its existing revision changes.
        // Never scan native inventories from a frame callback.
        var storedCounts = components ? WorkroomOwnedCounts.Read() : null;
        var count = components ? LuoxiStory.Unlocked && catalog != null ? catalog.Catalog.Count : 0 : 10;
        for (var i = 0; i < 10; i++)
        {
            productCards[i].SetActive(i < count);
            if (i >= count) continue;
            var replacement = selectedOrderId;
            // Cards are laid out row-first: each tier has mining on the left and a server on the right.
            var kind = components ? catalog.Catalog[i].Kind : i % 2 == 0 ? ServerRoomMachine.Mining : ServerRoomMachine.Server;
            var tier = components ? catalog.Catalog[i].Tier : i / 2 + 1;
            productStoredCounts[i].gameObject.SetActive(components);
            if (components)
            {
                var identifier = ServerRoomOrderCatalog.Identifier(kind, tier);
                productStoredCounts[i].text = storedCounts == null ? Web("stored_unknown") :
                    Web("stored_count", storedCounts.GetValueOrDefault(identifier));
            }
            var unlocked = components ? LuoxiStory.Unlocked : tier <= highest;
            var available = !components || ServerRoomOrderState.Available(catalog, kind, tier, Today(), replacement);
            long baseValue;
            if (components)
            {
                var item = Components.Find(ServerRoomOrderCatalog.Identifier(kind, tier)) ?? throw new InvalidOperationException("配件目录型号未注册。");
                productImages[i].sprite = SpriteAssets.Get(item.SpriteKey); productNames[i].text = item.DisplayName;
                productSlots[i].text = Web(available ? "component_stock" : "component_taken"); baseValue = item.Value;
            }
            else
            {
                var definition = ServerRoomMachine.DefinitionFor(kind, tier);
                productImages[i].sprite = WorkroomArt.Get(ServerRoomHardware.ArtworkName(definition));
                productNames[i].text = MachineName(definition);
                productSlots[i].text = definition.Kind == ServerRoomMachine.Mining ? Web("catalog_slots_gpu", definition.GpuSlots) : Web("catalog_slots_server", definition.CpuSlots, definition.RamSlots);
                baseValue = definition.BaseValue;
            }
            productImages[i].color = unlocked && available ? Color.white : new Color(1, 1, 1, .45f);
            productPrices[i].text = LuoxiStory.Unlocked ? Web("discount_price", PriceNumber(baseValue * .9m)) : Web("base_price", baseValue);
            Bind(catalogProducts[i], !unlocked ? Web("product_locked") : !available ? Web("component_taken") : Web(replacement == "" ? "add_order" : "replace_order"),
                ready && current.Enabled && unlocked && available && (replacement != "" || orders.Count < ServerRoomOrders.CurrentLimit),
                () => Execute(() =>
                {
                    if (replacement == "") ServerRoomOrders.Add(kind, tier);
                    else ServerRoomOrders.Replace(replacement, kind, tier);
                    selectedOrderId = "";
                }));
            StyleWidget(catalogProducts[i], WebBrown, BrowserTone.OnAccent);
        }
        for (var i = 0; i < 5; i++)
        {
            emptyOrders[i].text = Web("empty_order", i + 1);
            emptyOrders[i].gameObject.SetActive(i >= orders.Count);
            if (i >= orders.Count) continue;
            var order = orders[i]; var editable = ServerRoomOrderStock.CanEdit(order); var selected = selectedOrderId == order.Id;
            var name = ServerRoomOrderCatalog.IsComponent(order.Kind) ? Components.Find(ServerRoomOrderCatalog.Identifier(order.Kind, order.Tier))!.DisplayName : MachineName(ServerRoomMachine.DefinitionFor(order.Kind, order.Tier));
            Bind(orderActions[i], editable ? Web(selected ? "reservation_selected" : "reservation_change", i + 1, name) :
                Web("reservation_locked", i + 1), editable, () => { selectedOrderId = selected ? "" : order.Id; RefreshContent(); SyncInteraction(); });
            StyleWidget(orderActions[i], selected ? BrowserTone.CatalogSelected : WebPaper, WebBrown);
            Bind(cancelActions[i], M2("order_cancel_short"), editable, () => Execute(() =>
            {
                ServerRoomOrders.Cancel(order.Id); if (selectedOrderId == order.Id) selectedOrderId = "";
            }));
            StyleWidget(cancelActions[i]);
        }
        catalogNote.text = components && !LuoxiStory.Unlocked ? Web("component_locked") :
            (selectedOrderId == "" ? "" : Web("reservation_hint", Array.FindIndex(orders.ToArray(), o => o.Id == selectedOrderId) + 1) + " ") +
            (components ? Web("component_day", catalog?.CatalogDay ?? -1) + " " : "") + Web("catalog_terms");
    }

    private void ReleaseBrowserReferences()
    {
        ReleaseBrowserThemeReferences();
        ReleaseHardcoreReferences();
        ReleaseCoinArtworkReferences();
        browserFrame = browserDesktop = browserRack = browserCatalog = browserSidebar = coinMenu = null;
        browserAddress = browserDay = taskbarDay = overviewNote = tradeTitle = tradeAmount = tradeCash = catalogSummary = catalogNote = catalogOrdersHeading = dailyChange = null;
        browserBack = browserForward = browserRefresh = browserHome = browserMinimize = browserExit = taskbarExit = bulkCoin = bulkApply = null;
        browserSites.Clear(); desktopSites.Clear(); taskbarSites.Clear(); managementLinks.Clear(); catalogCategories.Clear(); catalogProducts.Clear(); coinOptions.Clear(); browserIcons.Clear();
        orderActions.Clear(); cancelActions.Clear(); productCards.Clear();
        overviewValues.Clear(); overviewNames.Clear(); overviewDetails.Clear(); overviewStates.Clear(); overviewMarkers.Clear(); overviewHoles.Clear();
        productImages.Clear(); productNames.Clear(); productSlots.Clear(); productPrices.Clear(); emptyOrders.Clear(); walletValues.Clear();
        productStoredCounts.Clear();
        browserNavigation.Reset(); browserMinimized = bulkMenuOpen = false; coinMenuRecord = ""; catalogKind = "machines"; bulkCoinIndex = 0;
    }
}
