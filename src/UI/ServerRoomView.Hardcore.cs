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

// Presentation only. All operations publish through the existing validated commands;
// operating, production, depth, maintenance and lease amounts come from the core.
internal sealed partial class ServerRoomView
{
    private enum HardcorePage { None, Forecast, Machine, Network, AutoSale, Ledger, Guide, Trade, Hardware }
    private HardcorePage hardcorePage;
    private GameObject hardcoreRoot;
    private TextMeshProUGUI hardcoreTitle, hardcoreBody, hardcoreFoot;
    private readonly List<ActionWidget> hardcoreLinks = new(), hardcoreActions = new();
    private readonly List<TMP_InputField> minimumInputs = new();
    private readonly List<TextMeshProUGUI> policyCoinLabels = new();
    private readonly List<ActionWidget> policyUp = new(), policyDown = new();
    private GameObject policyRows;
    private ActionWidget overviewMore, tradeDetails, tradeAutoSale, catalogPerformance;
    private string uiSession = "", machineRecord = "";
    private int machineTab, networkCoin, ledgerWindow, ledgerTab, ledgerDay, reportPage, guidePage;
    private bool updatingHardcoreInputs, updatingTradeInput;
    private readonly string[] tradeInputs = { "0.1", "0.1", "0.1", "0.1", "0.1" };
    private readonly long?[] exactTradeInputs = new long?[5];
    private PolicyDraft policyDraft;
    private bool HardcoreAlive => AssemblyDebugUi.Alive(hardcoreRoot) && AssemblyDebugUi.Alive(policyRows) &&
        minimumInputs.Count == 5 && minimumInputs.All(input => AssemblyDebugUi.Alive(input));

    private sealed class PolicyDraft
    {
        internal int Kind, Percent;
        internal List<int> Order;
        internal string[] Minimum;
        internal bool Dirty;
        internal static PolicyDraft Read(ServerRoomState.AutoSalePolicy policy) => new()
        {
            Kind = policy.Kind, Percent = policy.Percent, Order = new(policy.CoinOrder),
            Minimum = policy.MinimumNetPrices.Select(v => v.ToString("0.############################", CultureInfo.InvariantCulture)).ToArray()
        };
        internal ServerRoomState.AutoSalePolicy Policy()
        {
            var prices = new List<decimal>();
            for (var i = 0; i < 5; i++)
            {
                if (!decimal.TryParse(Minimum[i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value < 0 || value > 15000000m)
                    throw new InvalidOperationException(H("minimum_invalid", CoinName(ServerRoomState.CoinIds[i])));
                prices.Add(value);
            }
            return new() { Kind = Kind, Percent = Percent, CoinOrder = new(Order), MinimumNetPrices = prices };
        }
    }

    private static string H(string key, params object[] arguments) => LanguageText.Get("serverroom.hardcore." + key, arguments);
    private static string ModeName(int mode) => H("mode_" + mode);
    private static string PoolName(int pool) => H("pool_" + pool);
    private static string ReasonName(string reason) => reason.Length == 0 ? M3("running") : H("reason_" + reason);
    private static string PlanReason(Forecast forecast, string id)
    {
        var machine = forecast?.Plan.Machines.FirstOrDefault(m => m.RecordId == id);
        return machine == null ? M3("stopped") : ReasonName(machine.Reason);
    }
    private bool AnyBrowserInputFocused() => AssemblyDebugUi.Alive(quantity) && quantity.isFocused ||
        minimumInputs.Any(input => AssemblyDebugUi.Alive(input) && input.isFocused);
    private void ClearHardcoreFocus()
    {
        foreach (var input in minimumInputs)
            if (AssemblyDebugUi.Alive(input) && input.isFocused)
            { keyboardOwned = true; inputNeutralFrames = 0; input.DeactivateInputField(); }
    }
    private void EnsureHardcoreSession(ServerRoomState.Document current)
    {
        if (current == null) return;
        var identity = current.Run + "/" + current.Slot + "/" + WorkroomStorage.State.Epoch;
        if (uiSession == identity) return;
        uiSession = identity;
        CancelConfirmation(); ClearFocus(); CloseBrowserMenus(); browserNavigation.Reset();
        hardcorePage = HardcorePage.None; mode = PanelMode.Computer; browserMinimized = false;
        selectedPage = selectedCoin = minerPage = leasePage = sourcePage = installedPage = 0;
        selectedRack = -1; machineRecord = selectedServer = selectedOrderId = "";
        machineTab = networkCoin = ledgerWindow = ledgerTab = ledgerDay = reportPage = guidePage = 0;
        policyDraft = PolicyDraft.Read(current.AutoSale);
        for (var i = 0; i < 5; i++) { tradeInputs[i] = "0.1"; exactTradeInputs[i] = null; }
        RestoreTradeInput(); notice = ""; noticeIsError = false; renderedRevision = "";
    }
    private void SaveTradeInput()
    { if (AssemblyDebugUi.Alive(quantity)) tradeInputs[Math.Clamp(selectedCoin, 0, 4)] = quantity.text; }
    private void RestoreTradeInput()
    {
        if (!AssemblyDebugUi.Alive(quantity)) return;
        updatingTradeInput = true;
        try { quantity.text = tradeInputs[Math.Clamp(selectedCoin, 0, 4)]; }
        finally { updatingTradeInput = false; }
    }
    private void TradeInputChanged()
    {
        if (updatingTradeInput) return;
        SaveTradeInput(); exactTradeInputs[Math.Clamp(selectedCoin, 0, 4)] = null;
        RefreshBrowserTradeAmount();
    }
    private long TradeInputUnits() => exactTradeInputs[selectedCoin] ?? ServerRoomTradeMath.ParseUnits(quantity.text);
    private void FillTradeMaximum(long units)
    {
        exactTradeInputs[selectedCoin] = units; tradeInputs[selectedCoin] = Quantity(units);
        RestoreTradeInput(); RefreshBrowserTradeAmount();
    }

    private void DrawHardcorePages()
    {
        for (var i = 0; i < 4; i++)
        {
            var link = Widget(browserSidebar.transform, "HardcoreLink" + i, 9, 153 + i * 46, 126, 39);
            link.Label.fontSize = 15; hardcoreLinks.Add(link);
        }
        hardcoreRoot = BrowserPage("HardcoreDetails", true);
        var parent = hardcoreRoot.transform;
        hardcoreTitle = WebLabel(parent, "Title", "", 20, 10, 710, 32, 22);
        hardcoreBody = WebLabel(parent, "Body", "", 20, 100, 936, 299, 16, true, true);
        hardcoreBody.overflowMode = TextOverflowModes.Overflow;
        hardcoreFoot = WebLabel(parent, "Foot", "", 20, 420, 936, 40, 13, true, true, WebMuted);
        for (var i = 0; i < 18; i++) hardcoreActions.Add(Widget(parent, "Action" + i, 0, 0, 100, 32));
        policyRows = AssemblyDebugUi.New("AutoSaleCoinOrder", parent);
        AssemblyDebugUi.Place(policyRows, 20, 169, 936, 225);
        for (var i = 0; i < 5; i++)
        {
            var row = i;
            policyCoinLabels.Add(WebLabel(policyRows.transform, "Coin" + i, "", 0, i * 43, 220, 38, 16));
            policyUp.Add(Widget(policyRows.transform, "Up" + i, 228, i * 43, 35, 34));
            policyDown.Add(Widget(policyRows.transform, "Down" + i, 270, i * 43, 35, 34));
            var input = MakeMinimumInput(policyRows.transform, i);
            input.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityAction<string>>((string value) =>
            {
                if (updatingHardcoreInputs || policyDraft == null) return;
                policyDraft.Minimum[policyDraft.Order[row]] = value; policyDraft.Dirty = true;
                if (hardcorePage == HardcorePage.AutoSale) hardcoreFoot.text = H("draft_dirty");
            }));
            minimumInputs.Add(input);
        }
        policyRows.SetActive(false);
    }
    private void DrawHardcoreCatalog()
    {
        catalogPerformance = Widget(browserCatalog.transform, "AlgorithmPreview", 24, 12, 280, 32);
    }
    private TMP_InputField MakeMinimumInput(Transform parent, int row)
    {
        var background = AssemblyDebugUi.Image(parent, "Minimum" + row, ServerRoomBrowserTheme.ColorFor(WebPaper), true);
        UiRoundedGeometry.Fill(background);
        AssemblyDebugUi.Place(background.gameObject, 340, row * 43, 360, 34);
        SetBrowserTone(background, WebPaper); WebBorder(background.transform, "Border", 0, 0, 360, 34);
        var viewport = AssemblyDebugUi.New("Viewport", background.transform);
        AssemblyDebugUi.Place(viewport, 8, 0, 344, 34); viewport.AddComponent<RectMask2D>();
        var text = WebLabel(viewport.transform, "Text", "", 0, 0, 344, 34, 16);
        var placeholder = WebLabel(viewport.transform, "Placeholder", H("minimum_placeholder"), 0, 0, 344, 34, 14, true, false, WebMuted);
        var input = background.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background; input.textViewport = AssemblyDebugUi.Rect(viewport); input.textComponent = text;
        input.placeholder = placeholder; input.fontAsset = font; input.pointSize = 16;
        input.lineType = TMP_InputField.LineType.SingleLine; input.contentType = TMP_InputField.ContentType.Standard;
        input.characterLimit = 30; input.richText = false; input.restoreOriginalTextOnEscape = false;
        input.navigation = new Navigation { mode = Navigation.Mode.None }; input.customCaretColor = true;
        input.caretColor = ServerRoomBrowserTheme.ColorFor(WebInk); input.selectionColor = ServerRoomBrowserTheme.Selection;
        RegisterBrowserSelectable(input); return input;
    }
    private void OpenHardcore(HardcorePage page)
    {
        var route = page switch {
            HardcorePage.Forecast => new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.Forecast),
            HardcorePage.Machine => new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.Machine,
                ServerRoomRuntime.Current.Racks.Single(r => r.RecordId == machineRecord).RackIndex),
            HardcorePage.Network => new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.Network, networkCoin),
            HardcorePage.AutoSale => new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.AutoSale),
            HardcorePage.Ledger => new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.Ledger),
            HardcorePage.Guide => new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.Guide, guidePage),
            HardcorePage.Trade => new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.TradeDetails, selectedCoin),
            HardcorePage.Hardware => new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.HardwarePerformance),
            _ => new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.Overview)
        };
        reportPage = 0; NavigateBrowser(route);
    }
    private void OpenMachine(string record)
    { machineRecord = record; machineTab = reportPage = 0; OpenHardcore(HardcorePage.Machine); }
    private void RefreshHardcoreNavigation()
    {
        var pages = new[] { HardcorePage.Network, HardcorePage.AutoSale, HardcorePage.Ledger, HardcorePage.Guide };
        var keys = new[] { "network", "autosale", "ledger", "guide" };
        for (var i = 0; i < hardcoreLinks.Count; i++)
        {
            var page = pages[i];
            Bind(hardcoreLinks[i], H(keys[i]), true, () => OpenHardcore(page));
            StyleWidget(hardcoreLinks[i], hardcorePage == page ? WebSelected : BrowserTone.Sidebar, hardcorePage == page ? WebBlue : WebMuted);
        }
        if (hardcorePage != HardcorePage.None && mode == PanelMode.Computer)
            browserAddress.text = "room.pce.test/" + hardcorePage.ToString().ToLowerInvariant();
    }
    private void RefreshHardcore(ServerRoomState.Document current)
    {
        var enabled = ServerRoomRuntime.Ready && current?.Enabled == true;
        hardcoreRoot.SetActive(enabled && mode == PanelMode.Computer && hardcorePage != HardcorePage.None);
        policyRows.SetActive(false);
        if (mode == PanelMode.Catalog)
        { Bind(catalogPerformance, H("hardware_preview"), enabled, () => OpenHardcore(HardcorePage.Hardware)); StyleWidget(catalogPerformance, WebPaper, WebBrown); }
        if (!enabled || mode != PanelMode.Computer) return;
        if (hardcorePage == HardcorePage.None)
        {
            if (selectedPage == 0) Bind(overviewMore, H("forecast"), true, () => OpenHardcore(HardcorePage.Forecast));
            if (selectedPage == 2)
            {
                Bind(tradeDetails, H("trade_details"), true, () => OpenHardcore(HardcorePage.Trade));
                Bind(tradeAutoSale, H("autosale"), true, () => OpenHardcore(HardcorePage.AutoSale));
            }
            return;
        }
        overviewPage.SetActive(false); miningPage.SetActive(false); tradingPage.SetActive(false); operationPage.SetActive(false);
        hardcoreBody.gameObject.SetActive(true); hardcoreBody.text = ""; hardcoreFoot.text = "";
        hardcoreTitle.text = H(hardcorePage.ToString().ToLowerInvariant());
        Act(17, H("return"), 756, 10, 200, () => ApplyBrowserRoute(browserNavigation.CanGoBack ? browserNavigation.Back() :
            new ServerRoomBrowserNavigation.Route(ServerRoomBrowserNavigation.Page.Overview)));
        switch (hardcorePage)
        {
            case HardcorePage.Forecast: RefreshHardcoreForecast(current); break;
            case HardcorePage.Machine: RefreshHardcoreMachine(current); break;
            case HardcorePage.Network: RefreshHardcoreNetwork(current); break;
            case HardcorePage.AutoSale: RefreshHardcorePolicy(current); break;
            case HardcorePage.Ledger: RefreshHardcoreLedger(current); break;
            case HardcorePage.Guide: RefreshHardcoreGuide(); break;
            case HardcorePage.Trade: RefreshHardcoreTrade(current); break;
            case HardcorePage.Hardware: RefreshHardcoreHardware(); break;
        }
    }
    private void Act(int index, string label, float x, float y, float width, Action action, bool allowed = true, bool selected = false)
    {
        var widget = hardcoreActions[index]; AssemblyDebugUi.Place(widget.Root, x, y, width, 34);
        AssemblyDebugUi.Place(widget.Label.gameObject, 4, 0, width - 8, 34);
        Bind(widget, label, allowed, action); StyleWidget(widget, selected ? WebSelected : WebPaper, selected ? WebBlue : WebInk);
    }
    private void RepaintHardcore() { ClearFocus(); RefreshContent(); SyncInteraction(); }
    private void ShowReport(IReadOnlyList<string> lines, string foot = "", int perPage = 8, float top = 100)
    {
        var pages = Math.Max(1, (lines.Count + perPage - 1) / perPage);
        reportPage = Math.Clamp(reportPage, 0, pages - 1);
        AssemblyDebugUi.Place(hardcoreBody.gameObject, 20, top, 936, 368 - top);
        hardcoreBody.text = string.Join("\n\n", lines.Skip(reportPage * perPage).Take(perPage));
        hardcoreFoot.text = H("report_page", reportPage + 1, pages) + "  " + foot;
        Act(14, M2("previous"), 20, 380, 112, () => { reportPage--; RepaintHardcore(); }, reportPage > 0);
        Act(15, M2("next"), 144, 380, 112, () => { reportPage++; RepaintHardcore(); }, reportPage + 1 < pages);
    }

    private void RefreshHardcoreForecast(ServerRoomState.Document current)
    {
        var facts = ServerRoomOperations.MachineFacts(current); var forecast = ForecastNight(current, facts);
        var budget = ServerRoomEconomy.ForecastBudget(current, facts);
        var lines = new List<string>
        {
            H("forecast_cash", Cash(), forecast.Demand, forecast.Payable, budget),
            H("forecast_capacity", Number(forecast.Plan.DeviceWatts * 1.15m), ServerRoomEconomy.PowerLimit(current),
                Number(forecast.Plan.DeviceWatts), ServerRoomEconomy.HeatLimit(current), Number(forecast.Plan.LeaseWatts)),
            H("electricity", Number(current.Network.ElectricityFactor), current.Network.ElectricityDaysRemaining,
                current.Network.ElectricityNextDuration > 0 ? H("electricity_announced", Number(current.Network.ElectricityNextFactor), current.Network.ElectricityNextDuration) : H("electricity_unannounced"))
        };
        for (var i = 0; i < 5; i++)
            if (forecast.Produced[i] != 0) lines.Add(H("forecast_coin", CoinName(ServerRoomState.CoinIds[i]),
                PriceNumber(forecast.Produced[i]), PriceNumber(forecast.Produced[i] * current.Market.Prices[i]), PoolName(current.Coins[i].Pool)));
        lines.Add(H("forecast_estimate", PriceNumber(Enumerable.Range(0, 5).Sum(i => forecast.Produced[i] * current.Market.Prices[i]) - forecast.Payable)));
        foreach (var rack in current.Racks.Where(r => r.Kind == "mining"))
        {
            var plan = forecast.Plan.Machines.FirstOrDefault(m => m.RecordId == rack.RecordId);
            var health = ServerRoomEconomy.Health(current, rack.UniqueId);
            if (plan is { Runs: false } || health < 60 || !rack.PoweredOn)
                lines.Add(H("attention", rack.RackIndex + 1, rack.PoweredOn ? PlanReason(forecast, rack.RecordId) : M3("manual_stop"), Number(health)));
        }
        ShowReport(lines, H("forecast_disclaimer"), 4);
        Act(0, H("guide"), 20, 54, 180, () => OpenHardcore(HardcorePage.Guide));
        Act(1, H("autosale"), 212, 54, 180, () => OpenHardcore(HardcorePage.AutoSale));
    }
    private void RefreshHardcoreMachine(ServerRoomState.Document current)
    {
        var rack = current.Racks.FirstOrDefault(r => r.RecordId == machineRecord && r.Kind == "mining");
        if (rack == null) { hardcoreBody.text = H("machine_missing"); return; }
        var facts = ServerRoomOperations.MachineFacts(current); var fact = facts.Single(f => f.RecordId == rack.RecordId);
        var health = ServerRoomEconomy.Health(current, rack.UniqueId); var forecast = ForecastNight(current, facts);
        var plan = forecast.Plan.Machines.FirstOrDefault(m => m.RecordId == rack.RecordId);
        hardcoreTitle.text = H("machine_title", rack.RackIndex + 1, fact.Tier);
        Act(0, H("run_settings"), 20, 54, 180, () => { machineTab = 0; reportPage = 0; RepaintHardcore(); }, selected: machineTab == 0);
        Act(1, H("algorithm_details"), 212, 54, 180, () => { machineTab = 1; reportPage = 0; RepaintHardcore(); }, selected: machineTab == 1);
        Act(2, H("parts"), 404, 54, 180, () => OpenSlot(rack.RackIndex + 1));
        if (machineTab == 1)
        {
            var lines = new List<string>();
            for (var coin = 0; coin < 5; coin++)
            {
                var capacity = ServerRoomMiningModel.Evaluate(fact.Tier, fact.Cards, coin, rack.Mode, health);
                var production = fact.CanRun ? ServerRoomMiningModel.Produce(capacity, coin, current.Network.D[coin], current.Coins[coin].Pool, 1m, 0m) : new ServerRoomMiningModel.Production();
                lines.Add(H("machine_algorithm", CoinName(ServerRoomState.CoinIds[coin]), AlgorithmName(coin), VramMinimum(coin),
                    Number(capacity.EffectiveHash), Number(capacity.Watts), PriceNumber(production.ProducedCoin),
                    PriceNumber(production.ProducedCoin * current.Market.Prices[coin]), capacity.CanRun && fact.CanRun ? H("capability_only") : H("unavailable_capability"),
                    ServerRoomMiningModel.ElectricityCost(capacity.Watts, current.Network.ElectricityFactor),
                    PriceNumber(production.ProducedCoin * current.Market.Prices[coin] - ServerRoomMiningModel.ElectricityCost(capacity.Watts, current.Network.ElectricityFactor))));
            }
            foreach (var card in fact.Cards.Select((value, index) => (value, index)))
            {
                var c = ServerRoomMiningModel.Evaluate(fact.Tier, fact.Cards, ServerRoomMarket.CoinIndex(rack.CoinId), rack.Mode, health).Cards[card.index];
                lines.Add(H("card_detail", card.index + 1, card.value.Tier, card.value.VramGb,
                    c.Reason.Length == 0 ? H("card_eligible") : ReasonName(c.Reason), Number(c.Hash)));
            }
            ShowReport(lines, H("algorithm_disclaimer"), 3); return;
        }
        AssemblyDebugUi.Place(hardcoreBody.gameObject, 20, 100, 936, 91);
        var chosen = ServerRoomMiningModel.Evaluate(fact.Tier, fact.Cards, ServerRoomMarket.CoinIndex(rack.CoinId), rack.Mode, health);
        var currentCoin = ServerRoomMarket.CoinIndex(rack.CoinId);
        var incrementalFee = PlannedMachineFee(forecast.Plan, rack.RecordId, current.Network.ElectricityFactor);
        hardcoreBody.text = H("machine_status", CoinName(rack.CoinId), PoolName(current.Coins[currentCoin].Pool),
            ModeName(rack.Mode), plan?.Runs == true ? ModeName(plan.Mode) : "—", Number(health), rack.Priority + 1,
            rack.PoweredOn ? PlanReason(forecast, rack.RecordId) : M3("manual_stop"), Number((plan?.Runs == true ? plan.Capacity : chosen).EffectiveHash),
            Number((plan?.Runs == true ? plan.Capacity : chosen).Watts), Number((plan?.Runs == true ? plan.Capacity : chosen).RejectRate * 100m),
            PriceNumber(plan?.Runs == true ? plan.Production.ProducedCoin : 0m), incrementalFee,
            PriceNumber((plan?.Runs == true ? plan.Production.ProducedCoin * current.Market.Prices[currentCoin] : 0m) - incrementalFee));
        for (var i = 0; i < 3; i++)
        {
            var modeValue = i;
            Act(3 + i, ModeName(i), 20 + i * 192, 198, 180,
                () => Execute(() => ServerRoomOperations.Mode(rack.RecordId, modeValue, rack.AutoDownshift)),
                i != 2 || health >= 30, rack.Mode == i);
        }
        Act(6, H("downshift", rack.AutoDownshift ? M3("on") : M3("off")), 20, 243, 372,
            () => Execute(() => ServerRoomOperations.Mode(rack.RecordId, rack.Mode, !rack.AutoDownshift)), selected: rack.AutoDownshift);
        Act(7, rack.PoweredOn ? M3("stop") : M3("run"), 404, 243, 180,
            () => Execute(() => ServerRoomOperations.Power(rack.RecordId, !rack.PoweredOn)), rack.PoweredOn || fact.CanRun);
        Act(8, H("priority_up"), 596, 198, 170, () => Execute(() => ServerRoomOperations.Priority(rack.RecordId, rack.Priority - 1)), rack.Priority > 0);
        Act(9, H("priority_down"), 778, 198, 178, () => Execute(() => ServerRoomOperations.Priority(rack.RecordId, rack.Priority + 1)), rack.Priority < 9);
        Act(10, H("coin_next", CoinName(ServerRoomState.CoinIds[(ServerRoomMarket.CoinIndex(rack.CoinId) + 1) % 5])), 20, 288, 372,
            () => Execute(() => ServerRoomOperations.MineCoin(rack.RecordId, ServerRoomState.CoinIds[(ServerRoomMarket.CoinIndex(rack.CoinId) + 1) % 5])));
        Act(11, H("network_pool"), 404, 288, 180, () => { networkCoin = ServerRoomMarket.CoinIndex(rack.CoinId); OpenHardcore(HardcorePage.Network); });
        Act(12, H("maintain", ServerRoomEconomy.MaintenanceMaterials(fact.Tier), ServerRoomEconomy.MaintenanceFee(fact.Tier)), 20, 342, 564,
            () => ShowConfirmation(H("maintenance_terms", rack.RackIndex + 1, Number(health), ServerRoomEconomy.MaintenanceMaterials(fact.Tier),
                ServerRoomEconomy.MaintenanceFee(fact.Tier), Cash(), WorkroomStorage.State.Items.Count(record => WorkroomComponentParts.UsableMaterial(record, "common_electronic"))),
                () => ServerRoomOperations.Maintain(rack.RecordId)), !rack.PoweredOn && health < 100);
        hardcoreFoot.text = H("machine_hint");
    }
    private static string AlgorithmName(int coin) => H("algorithm_" + (coin == 2 ? 2 : coin == 1 || coin == 3 ? 1 : 0));
    private static int PlannedMachineFee(ServerRoomEconomy.OperatingPlan plan, string id, decimal electricity)
    {
        decimal watts = 0;
        foreach (var machine in plan.Machines)
        {
            if (!machine.Runs) continue;
            var before = ServerRoomMiningModel.ElectricityCost(watts, electricity); watts += machine.Capacity.Watts;
            if (machine.RecordId == id) return ServerRoomMiningModel.ElectricityCost(watts, electricity) - before;
        }
        return 0;
    }
    private static int VramMinimum(int coin) => coin == 2 ? 16 : coin == 1 || coin == 3 ? 8 : 4;
    private void RefreshHardcoreNetwork(ServerRoomState.Document current)
    {
        for (var i = 0; i < 5; i++)
        {
            var coin = i;
            Act(i, CoinName(ServerRoomState.CoinIds[i]), 20 + i * 188, 54, 176, () => { networkCoin = coin; reportPage = 0; RepaintHardcore(); }, selected: networkCoin == i);
        }
        var c = networkCoin; var network = current.Network; var coinState = current.Coins[c]; var today = network.Day ?? Today();
        var old = current.Ledger.Days.FirstOrDefault(day => day.Date == today - 7);
        var trend = old == null ? H("history_insufficient") : H("network_trend", Number((network.N[c] / old.NetworkN[c] - 1m) * 100m), Number((network.D[c] / old.Difficulty[c] - 1m) * 100m));
        var lines = new List<string>
        {
            H("network_current", CoinName(coinState.Id), AlgorithmName(c), VramMinimum(c), PriceNumber(current.Market.Prices[c])),
            H("network_values", PriceNumber(network.N[c]), PriceNumber(network.D[c]), trend),
            H("network_adjustment", ServerRoomNetworkModel.DaysUntilAdjustment(network, c), ServerRoomNetworkModel.AdjustmentPeriod(c),
                PriceNumber(ServerRoomNetworkModel.AdjustmentLower(network, c)), PriceNumber(ServerRoomNetworkModel.AdjustmentUpper(network, c))),
            H("network_pool_current", PoolName(coinState.Pool), PoolName(coinState.NextPool)), H("network_pool_explain")
        };
        var configured = current.Racks.Where(r => r.Kind == "mining" && r.CoinId == coinState.Id).ToArray();
        lines.Add(H("network_configured", configured.Length, string.Join("、", configured.Select(r => (r.RackIndex + 1).ToString("00")))));
        ShowReport(lines, H("network_disclaimer"), 4);
        Act(5, H("pool_next", PoolName(0)), 492, 380, 222, () => Execute(() => ServerRoomOperations.Pool(coinState.Id, 0)), selected: coinState.NextPool == 0);
        Act(6, H("pool_next", PoolName(1)), 726, 380, 230, () => Execute(() => ServerRoomOperations.Pool(coinState.Id, 1)), selected: coinState.NextPool == 1);
    }

    private void RefreshHardcorePolicy(ServerRoomState.Document current)
    {
        policyDraft ??= PolicyDraft.Read(current.AutoSale);
        for (var i = 0; i < 3; i++)
        {
            var kind = i;
            Act(i, H("policy_" + i), 20 + i * 250, 54, 238, () => { policyDraft.Kind = kind; policyDraft.Dirty = true; RepaintHardcore(); }, selected: policyDraft.Kind == i);
        }
        Act(3, H("percent", policyDraft.Percent), 20, 99, 220, null, false);
        Act(4, "−10%", 252, 99, 102, () => { policyDraft.Percent -= 10; policyDraft.Dirty = true; RepaintHardcore(); }, policyDraft.Percent > 0);
        Act(5, "+10%", 366, 99, 102, () => { policyDraft.Percent += 10; policyDraft.Dirty = true; RepaintHardcore(); }, policyDraft.Percent < 100);
        AssemblyDebugUi.Place(hardcoreBody.gameObject, 20, 139, 936, 26);
        hardcoreBody.text = H("policy_columns"); policyRows.SetActive(true);
        updatingHardcoreInputs = true;
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var row = i; var index = policyDraft.Order[i];
                policyCoinLabels[i].text = (i + 1) + ". " + CoinName(ServerRoomState.CoinIds[index]);
                Bind(policyUp[i], "↑", i > 0, () => MovePolicyCoin(row, -1));
                Bind(policyDown[i], "↓", i < 4, () => MovePolicyCoin(row, 1));
                minimumInputs[i].text = policyDraft.Minimum[index];
            }
        }
        finally { updatingHardcoreInputs = false; }
        Act(6, H("apply_draft"), 20, 382, 220, () =>
        {
            var policy = policyDraft.Policy();
            var terms = H("policy_terms", H("policy_" + policy.Kind), policy.Percent,
                string.Join(" → ", policy.CoinOrder.Select(i => CoinName(ServerRoomState.CoinIds[i])))) + "\n" +
                string.Join(" / ", Enumerable.Range(0, 5).Select(i => CoinName(ServerRoomState.CoinIds[i]) + " ≥" + PriceNumber(policy.MinimumNetPrices[i]) + "C"));
            ShowConfirmation(terms, () => { ServerRoomOperations.AutoSale(policy); policyDraft = PolicyDraft.Read(policy); });
        });
        Act(7, H("reload_draft"), 252, 382, 220, () => ShowConfirmation(H("reload_terms"), () => { policyDraft = PolicyDraft.Read(ServerRoomRuntime.Current.AutoSale); }));
        Act(8, H("policy_help"), 484, 382, 220, () => { guidePage = 2; OpenHardcore(HardcorePage.Guide); });
        hardcoreFoot.text = policyDraft.Dirty ? H("draft_dirty") : H("draft_current", H("policy_" + current.AutoSale.Kind), ServerRoomEconomy.ForecastBudget(current, ServerRoomOperations.MachineFacts(current)));
    }
    private void MovePolicyCoin(int row, int delta)
    {
        var order = policyDraft.Order; (order[row], order[row + delta]) = (order[row + delta], order[row]);
        policyDraft.Dirty = true; RepaintHardcore();
    }
    private void SyncHardcoreInputs(bool ready)
    {
        foreach (var input in minimumInputs) input.interactable = ready && mode == PanelMode.Computer && hardcorePage == HardcorePage.AutoSale;
    }

    private void RefreshHardcoreLedger(ServerRoomState.Document current)
    {
        var windows = new[] { "ledger_total", "ledger_today", "ledger_week", "ledger_month", "ledger_day" };
        for (var i = 0; i < windows.Length; i++)
        {
            var choice = i;
            Act(i, H(windows[i]), 20 + i * 188, 54, 176, () => { ledgerWindow = choice; reportPage = 0; RepaintHardcore(); }, selected: ledgerWindow == i);
        }
        var days = current.Ledger.Days.OrderByDescending(d => d.Date).ToArray(); ledgerDay = Math.Clamp(ledgerDay, 0, Math.Max(0, days.Length - 1));
        var today = current.Network.Day ?? Today();
        var amount = ledgerWindow == 0 ? current.Ledger.Total : SumLedger(days.Where(d => ledgerWindow switch
        { 1 => d.Date == today, 2 => d.Date >= today - 6, 3 => d.Date >= today - 29, _ => days.Length > 0 && d.Date == days[ledgerDay].Date }).Select(d => d.Amount));
        var entries = days.Where(d => ledgerWindow switch { 0 => true, 1 => d.Date == today, 2 => d.Date >= today - 6, 3 => d.Date >= today - 29, _ => days.Length > 0 && d.Date == days[ledgerDay].Date }).ToArray();
        var lines = new List<string>();
        if (ledgerTab == 0)
        {
            lines.Add(H("ledger_cash", PriceNumber(amount.CashDelta), PriceNumber(amount.MiningSaleCash), PriceNumber(amount.TradingSaleCash), PriceNumber(amount.BuyCash)));
            lines.Add(H("ledger_expenses", PriceNumber(amount.ElectricityCash), PriceNumber(amount.MaintenanceCash), Number(amount.MaintenanceMaterials), PriceNumber(amount.InvestmentCash)));
            lines.Add(H("ledger_rent", PriceNumber(amount.LeaseAdvanceCash), PriceNumber(amount.RentEarned), PriceNumber(amount.LeaseFeesEarned), PriceNumber(amount.LeaseRoundingEarned)));
            lines.Add(H("ledger_fees", PriceNumber(amount.TradingFees), PriceNumber(amount.TradingRealized), PriceNumber(ServerRoomEconomy.UnearnedRent(current))));
            lines.Add(H("investment_unknown"));
        }
        else if (ledgerTab == 1)
        {
            for (var i = 0; i < 5; i++)
                lines.Add(H("ledger_coin", CoinName(ServerRoomState.CoinIds[i]), PriceNumber(amount.ProducedCoins[i]), PriceNumber(amount.PoolFees[i]),
                    PriceNumber(amount.BoughtMicro[i] / 1000000m), PriceNumber(amount.SoldBoughtMicro[i] / 1000000m), PriceNumber(amount.SoldMinedMicro[i] / 1000000m),
                    Quantity(current.Coins[i].BoughtMicro), Quantity(current.Coins[i].MinedMicro)));
            lines.Add(H("ledger_source_help"));
        }
        else
        {
            foreach (var day in entries)
            {
                if (day.ProductionFromDay != null) lines.Add(H("ledger_production_day", day.Date, day.ProductionFromDay, Number(day.MiningWatts), Number(day.ElectricityFactor)));
                foreach (var note in day.Notices) lines.Add(H("ledger_notice", day.Date, LedgerNotice(current, note)));
            }
            if (lines.Count == 0) lines.Add(H("ledger_no_notice"));
        }
        ShowReport(lines, H("ledger_retention", days.Length, ledgerWindow == 4 && days.Length > 0 ? days[ledgerDay].Date.ToString() : H(windows[ledgerWindow])), ledgerTab == 1 ? 2 : 3, 145);
        for (var i = 0; i < 3; i++)
        {
            var tab = i;
            Act(5 + i, H("ledger_tab_" + i), 20 + i * 188, 99, 176, () => { ledgerTab = tab; reportPage = 0; RepaintHardcore(); }, selected: ledgerTab == i);
        }
        if (ledgerWindow == 4)
        {
            Act(8, H("older_day"), 596, 99, 170, () => { ledgerDay++; reportPage = 0; RepaintHardcore(); }, ledgerDay + 1 < days.Length);
            Act(9, H("newer_day"), 778, 99, 178, () => { ledgerDay--; reportPage = 0; RepaintHardcore(); }, ledgerDay > 0);
        }
    }
    private static ServerRoomLedger.Amounts SumLedger(IEnumerable<ServerRoomLedger.Amounts> values)
    {
        var result = new ServerRoomLedger.Amounts();
        // Add recorded report fields only; do not reconstruct trades or historical production.
        foreach (var amount in values)
            foreach (var property in typeof(ServerRoomLedger.Amounts).GetProperties())
                if (property.PropertyType == typeof(decimal))
                    property.SetValue(result, (decimal)property.GetValue(result) + (decimal)property.GetValue(amount));
                else
                {
                    var target = (List<decimal>)property.GetValue(result); var source = (List<decimal>)property.GetValue(amount);
                    for (var i = 0; i < 5; i++) target[i] += source[i];
                }
        return result;
    }
    private static string LedgerNotice(ServerRoomState.Document current, string note)
    {
        var split = note.LastIndexOf(':'); if (split < 0) return note;
        var owner = note[..split]; var code = note[(split + 1)..];
        var coin = ServerRoomState.CoinIds.ToList().IndexOf(owner);
        var rack = current.Racks.FirstOrDefault(r => r.RecordId == owner);
        var label = coin >= 0 ? CoinName(owner) : rack != null ? H("rack_number", rack.RackIndex + 1) : H("former_machine");
        return label + "：" + ReasonName(code);
    }

    private void RefreshHardcoreTradeQuote(ServerRoomState.Document current)
    {
        var units = TradeInputUnits(); var id = ServerRoomState.CoinIds[selectedCoin];
        var buyMax = ServerRoomOperations.MaximumTrade(id, true); var sellMax = ServerRoomOperations.MaximumTrade(id, false);
        string QuoteLine(bool buy, long maximum)
        {
            if (units > maximum) return H(buy ? "buy_limit" : "sell_limit", Quantity(maximum));
            try { var quote = ServerRoomOperations.TradeQuote(id, units, buy); return H(buy ? "buy_preview" : "sell_preview", Math.Abs(quote.CashDelta), PriceNumber(quote.Fee)); }
            catch (Exception ex) { return (buy ? M3("buy") : M3("sell")) + "：" + ex.Message; }
        }
        tradeAmount.text = QuoteLine(true, buyMax) + "\n" + QuoteLine(false, sellMax);
        tradingHint.text = H("trade_hint");
    }
    private void ShowTradeLimit(bool buy, long maximum)
    {
        if (maximum <= 0) throw new InvalidOperationException(H("no_trade_capacity"));
        ShowConfirmation(H("fill_maximum_terms", buy ? M3("buy") : M3("sell"), Quantity(maximum)), () => FillTradeMaximum(maximum));
    }
    private void RefreshHardcoreTrade(ServerRoomState.Document current)
    {
        var wallet = current.Coins[selectedCoin]; var buyMax = ServerRoomOperations.MaximumTrade(wallet.Id, true); var sellMax = ServerRoomOperations.MaximumTrade(wallet.Id, false);
        var lines = new List<string>
        {
            H("trade_market", CoinName(wallet.Id), PriceNumber(current.Market.Prices[selectedCoin]), ServerRoomTradeMath.Depth(selectedCoin), Quantity(wallet.DailyNetMicro)),
            H("trade_wallet", Quantity(wallet.MicroUnits), PriceNumber(wallet.MicroUnits / 1000000m * current.Market.Prices[selectedCoin]), Quantity(buyMax), Quantity(sellMax))
        };
        if (sellMax > 0)
        {
            var sell = ServerRoomOperations.TradeQuote(wallet.Id, sellMax, false);
            lines.Add(H("trade_maximum", Quantity(sellMax), PriceNumber(sell.Value), PriceNumber(sell.Fee), sell.CashDelta, PriceNumber(sell.CashDelta / sell.Quantity)));
        }
        try
        {
            var units = TradeInputUnits();
            foreach (var buy in new[] { true, false })
            {
                if (units > (buy ? buyMax : sellMax)) { lines.Add(H(buy ? "buy_limit" : "sell_limit", Quantity(buy ? buyMax : sellMax))); continue; }
                var quote = ServerRoomOperations.TradeQuote(wallet.Id, units, buy);
                lines.Add(H("trade_detail_quote", buy ? M3("buy") : M3("sell"), Quantity(units), PriceNumber(quote.Value / quote.Quantity),
                    Number((quote.Value / quote.Quantity / quote.Price - 1m) * 100m), PriceNumber(quote.Fee), Math.Abs(quote.CashDelta)));
            }
        }
        catch (Exception ex) { lines.Add(H("trade_input_error", ex.Message)); }
        lines.Add(H("trade_depth_help")); ShowReport(lines, H("trade_rounding"), 4);
        Act(0, H("fill_buy_max"), 20, 54, 270, () => { FillTradeMaximum(buyMax); SetPage(2); }, buyMax > 0);
        Act(1, H("fill_sell_max"), 302, 54, 270, () => { FillTradeMaximum(sellMax); SetPage(2); }, sellMax > 0);
        Act(2, H("autosale"), 584, 54, 180, () => OpenHardcore(HardcorePage.AutoSale));
    }
    private void RefreshHardcoreGuide()
    {
        var titles = new[] { "guide_safety", "guide_operations", "guide_policy" };
        for (var i = 0; i < 3; i++)
        {
            var page = i;
            Act(i, H(titles[i]), 20 + i * 250, 54, 238, () => { guidePage = page; RepaintHardcore(); }, selected: guidePage == i);
        }
        AssemblyDebugUi.Place(hardcoreBody.gameObject, 20, 107, 936, 296);
        hardcoreBody.text = H("guide_body_" + guidePage); hardcoreFoot.text = H("guide_manual");
    }
    private void RefreshHardcoreHardware()
    {
        var lines = new List<string> { H("hardware_explain") };
        for (var tier = 1; tier <= 5; tier++)
        {
            var family = WorkroomComponentTemplates.All.Single(template => template.Key == "gpu");
            var vram = WorkroomComponentTemplates.DefaultPart(family, "memory", tier).CapacityGB * family.DefaultCount("memory", tier);
            var cards = new[] { new ServerRoomMiningModel.Card { Tier = tier, VramGb = vram, Intact = true } };
            var capabilities = new[] { 0, 1, 2 }.Select(c => ServerRoomMiningModel.Evaluate(tier, cards, c, 1, 100));
            lines.Add(H("hardware_tier", tier, cards[0].VramGb, string.Join(" / ", capabilities.Select((cap, i) => AlgorithmName(i == 2 ? 2 : i) + " " + Number(cap.EffectiveHash)))));
        }
        ShowReport(lines, H("hardware_disclaimer"), 4);
        Act(0, Web("catalog"), 20, 54, 238, OpenBrowserCatalog);
    }
    private void ReleaseHardcoreReferences()
    {
        hardcoreRoot = policyRows = null; hardcoreTitle = hardcoreBody = hardcoreFoot = null;
        overviewMore = tradeDetails = tradeAutoSale = catalogPerformance = null;
        hardcoreLinks.Clear(); hardcoreActions.Clear(); minimumInputs.Clear(); policyCoinLabels.Clear(); policyUp.Clear(); policyDown.Clear();
        uiSession = ""; policyDraft = null; hardcorePage = HardcorePage.None;
    }
}
