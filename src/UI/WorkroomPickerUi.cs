using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PCExpansion;

/// <summary>Owner-local, read-only item browser. Only an explicit row click transfers custody.</summary>
internal sealed class WorkroomPickerUi
{
    private sealed class Row
    {
        internal GameObject Root = null!, Frame = null!;
        internal Image Icon = null!;
        internal TextMeshProUGUI Name = null!, Detail = null!, Reason = null!;
        internal WorkroomSelection.Candidate? Candidate;
        internal WorkroomAssemblySlotUi? Missing;
        internal string Failure = "";
        internal string HoverText = "";
    }
    private static readonly List<WorkroomPickerUi> instances = new();
    private static WorkroomPickerUi? active;
    private static bool pointerOwned;
    private static int neutralFrames, inputFrame = -1, sourceRevision;
    private static long session = -1;
    internal static WorkroomSelection.Source Sources = WorkroomSelection.Source.All;
    private static readonly string[] Keys = { "picker", "replace", "return", "close", "search", "clear", "tier_all", "tier", "only_available",
        "show_all", "state_all", "state_intact", "state_broken", "page", "empty_sources", "empty_filter", "empty_available", "available_count",
        "source_room", "source_box", "source_shop", "auto_return_on", "auto_return_off", "missing", "missing_row", "missing_none", "select_hint", "instance", "configuration", "incomplete_sources" };
    internal static string FontText => string.Join("", Keys.Select(key => Text(key, 0, 0, 0, 0))) +
        string.Join("", WorkroomStorageCatalog.Categories.Select(key => WorkroomStorage.Text("category." + key))) +
        string.Join("", new[] { "all", "dismantled", "component" }.Select(key => WorkroomStorage.Text("type." + key))) + "T0123456789↑↓✓—·GB";
    private static string Text(string key, params object[] args) => WorkroomSelection.Text(key, args);
    private readonly Canvas canvas;
    private readonly WorkroomAssemblyWindow owner;
    private readonly TMP_FontAsset font;
    private readonly List<WorkroomAssemblySlotUi> slots = new();
    private readonly List<GameObject> sourceButtons = new();
    private readonly List<GameObject> popupSources = new();
    private readonly List<Row> rows = new();
    private GameObject layer = null!, popup = null!, returnButton = null!, clear = null!, categoryButton = null!, typeButton = null!, tierButton = null!, stateButton = null!, availableButton = null!;
    private GameObject? missingButton, autoReturnButton;
    private GameObject choices = null!;
    private readonly List<GameObject> choiceButtons = new();
    private Action<int>? choose;
    private int choicesClosedFrame = -1;
    private static int escapeFrame = -1;
    private bool ChoicesOpen => WorkroomUiCleanup.IsAlive(choices) && choices.activeSelf;
    private TextMeshProUGUI title = null!, footer = null!, empty = null!;
    private TMP_InputField search = null!;
    private GameObject tooltip = null!;
    private TextMeshProUGUI tooltipLabel = null!;
    private Row? hovered;
    private string hoverText = "";
    private bool displayedAutoReturn;
    private string autoReturnLocale = "";
    private string target = "", targetName = "", query = "", locale = "";
    private int page, category, type, tier, condition;
    private bool availableOnly = true, missingMode, candidatesComplete = true, repairSelection;
    private long epoch, revision = -1, countedRevision = -1;
    private int seenSources = -1, countedSources = -1;
    private string countedLocale = "";
    private List<WorkroomSelection.Candidate> candidates = new();
    private readonly List<(WorkroomSelection.Candidate Candidate, string Reason)> visible = new();
    private readonly List<(WorkroomAssemblySlotUi Slot, int Available)> missing = new();
    private bool Open => WorkroomUiCleanup.IsAlive(layer) && layer.activeSelf && owner.Open;

    internal WorkroomPickerUi(Canvas canvas, WorkroomAssemblyWindow owner, TMP_FontAsset font)
    {
        this.canvas = canvas; this.owner = owner; this.font = font;
        instances.Add(this);
        AddSources(owner.Root.transform, sourceButtons);
    }
    internal static WorkroomPickerUi? For(GameObject root) => instances.FirstOrDefault(instance => instance.owner.Root.Pointer == root.Pointer);
    internal static bool IsOpen => active?.Open == true;
    private void AddSources(Transform parent, List<GameObject> destination)
    {
        for (var i = 0; i < 3; i++)
        {
            var source = (WorkroomSelection.Source)(1 << i);
            var key = new[] { "source_room", "source_box", "source_shop" }[i];
            destination.Add(Button(parent, "PickerSource" + i, 18 + i * 46, 252, 42, 22, Text(key), () =>
            {
                Sources ^= source; sourceRevision++;
                foreach (var instance in instances) instance.PaintSources();
                if (active != null) { active.page = 0; active.Rebuild(true); }
            }, 10));
        }
        PaintSources();
    }
    private void PaintSources()
    {
        foreach (var buttons in new[] { sourceButtons, popupSources })
            for (var i = 0; i < buttons.Count; i++)
            {
                Set(buttons[i], Text(new[] { "source_room", "source_box", "source_shop" }[i]));
                WorkroomPanelSkin.Selected(buttons[i], (Sources & (WorkroomSelection.Source)(1 << i)) != 0);
            }
    }
    internal void Register(WorkroomAssemblySlotUi slot) => slots.Add(slot);
    internal void EnableRepairSelection() => repairSelection = true;
    internal void EnableMissing(float x, float y, float width)
    {
        missingButton = Button(owner.Root.transform, "MissingParts", x, y, width, 26, Text("missing", 0), () => ShowMissing());
    }
    internal void EnableAutoReturn()
    {
        autoReturnButton = Button(owner.Root.transform, "AutoReturn", 18, 282, 134, 26, "", () =>
        { WorkroomSelection.AutoReturn = !WorkroomSelection.AutoReturn; PaintAutoReturn(); }, 10);
        PaintAutoReturn();
    }
    private void PaintAutoReturn()
    {
        if (autoReturnButton == null) return;
        if (autoReturnLocale == LanguageText.LocaleCode && displayedAutoReturn == WorkroomSelection.AutoReturn) return;
        displayedAutoReturn = WorkroomSelection.AutoReturn; autoReturnLocale = LanguageText.LocaleCode;
        Set(autoReturnButton, Text(WorkroomSelection.AutoReturn ? "auto_return_on" : "auto_return_off"));
        WorkroomPanelSkin.Selected(autoReturnButton, WorkroomSelection.AutoReturn);
    }
    internal void ShowSlot(WorkroomAssemblySlotUi slot)
    {
        if (!owner.Open || !slot.Enabled || (Il2Cpp.ItemMouseDragHandler.current?.isDragging ?? false)) return;
        target = slot.Target; targetName = slot.EmptyName;
        missingMode = false; page = 0; OpenPopup();
    }
    private void ShowMissing()
    {
        if (!owner.Open || (Il2Cpp.ItemMouseDragHandler.current?.isDragging ?? false)) return;
        missingMode = true; page = 0; OpenPopup();
    }
    private void OpenPopup()
    {
        CloseActive(); WorkroomAutoFillUi.CloseActive(); EnsurePopup();
        epoch = WorkroomStorage.State.Epoch; revision = -1;
        active = this; pointerOwned = true; neutralFrames = 0;
        owner.Root.transform.SetAsLastSibling(); layer.SetActive(true); layer.transform.SetAsLastSibling();
        WorkroomAssemblyWindow.PreviewSlot = null; Rebuild(true);
    }
    private void EnsurePopup()
    {
        if (WorkroomUiCleanup.IsAlive(layer)) return;
        layer = AssemblyDebugUi.New("PickerLayer", owner.Root.transform); AssemblyDebugUi.Stretch(layer);
        var blocker = AssemblyDebugUi.Image(layer.transform, "DismissPicker", Color.clear, true);
        AssemblyDebugUi.Place(blocker.gameObject, -8192, -8192, 16384, 16384);
        var dismiss = blocker.gameObject.AddComponent<Button>(); dismiss.targetGraphic = blocker;
        dismiss.transition = Selectable.Transition.None; dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
        dismiss.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)(() => CloseActive())));
        popup = WorkroomPanelSkin.Panel(layer, "Picker", 176, 46, 528, 340, new Color(.10f,.16f,.14f,1), UiRoundedGeometry.WindowRadius);
        popup.GetComponent<Image>().raycastTarget = true;
        title = Label(popup.transform, "Title", 12, 4, 330, 24, 13);
        returnButton = Button(popup.transform, "ReturnSlot", 350, 4, 114, 24, Text("return"), () =>
        {
            if (epoch != WorkroomStorage.State.Epoch) { CloseActive(); return; }
            var slot = slots.FirstOrDefault(s => s.Target == target);
            if (WorkroomSelection.ReturnSlots(new[] { slot?.Slot ?? target }, epoch)) CloseActive(); else Rebuild(true);
        });
        Button(popup.transform, "ClosePicker", 470, 4, 46, 24, "Esc", () => CloseActive());
        search = MakeSearch(popup.transform);
        clear = Button(popup.transform, "ClearPickerFilters", 390, 34, 126, 28, Text("clear"), ClearFilters);
        categoryButton = Button(popup.transform, "Category", 12, 68, 100, 26, "", () => ShowChoices(
            WorkroomStorageCatalog.Categories.Select(key => WorkroomStorage.Text("category." + key)).ToArray(), category, value => category = value), 10);
        typeButton = Button(popup.transform, "Type", 118, 68, 86, 26, "", () => { type = (type + 1) % 3; Filter(); }, 10);
        tierButton = Button(popup.transform, "Tier", 210, 68, 66, 26, "", () => ShowChoices(
            Enumerable.Range(0, 6).Select(value => value == 0 ? Text("tier_all") : Text("tier", value)).ToArray(), tier, value => tier = value), 10);
        stateButton = Button(popup.transform, "State", 282, 68, 100, 26, "", () => { condition = (condition + 1) % 3; Filter(); }, 10);
        availableButton = Button(popup.transform, "Available", 388, 68, 128, 26, "", () => { availableOnly = !availableOnly; Filter(); }, 10);
        for (var i = 0; i < 4; i++)
        {
            var row = new Row();
            row.Root = Button(popup.transform, "PickerRow" + i, 12, 100 + i * 51, 504, 49, "", () => Pick(row), 10);
            row.Icon = AssemblyDebugUi.Image(row.Root.transform, "Icon", Color.white, false); row.Icon.preserveAspect = true;
            AssemblyDebugUi.Place(row.Icon.gameObject, 4, 3, 42, 41);
            row.Name = Label(row.Root.transform, "Name", 52, 0, 444, 18, 12);
            row.Detail = Label(row.Root.transform, "Detail", 52, 18, 444, 15, 10);
            row.Reason = Label(row.Root.transform, "Reason", 52, 33, 444, 15, 10);
            row.Frame = WorkroomCandidateHighlight.Frame(row.Root.transform, "PickerCompatible"); row.Frame.SetActive(false);
            rows.Add(row);
        }
        empty = Label(popup.transform, "Empty", 24, 122, 480, 160, 12); empty.enableWordWrapping = true;
        Button(popup.transform, "Previous", 12, 310, 46, 24, "↑", () => { page--; RenderRows(); });
        Button(popup.transform, "Next", 470, 310, 46, 24, "↓", () => { page++; RenderRows(); });
        footer = Label(popup.transform, "Page", 64, 310, 400, 24, 11);
        AddSources(layer.transform, popupSources);
        tooltip = WorkroomPanelSkin.Panel(layer, "PickerItemDetails", 0, 0, 620, 120, new Color(.12f,.14f,.14f,1), UiRoundedGeometry.WindowRadius);
        tooltipLabel = Label(tooltip.transform, "Details", 12, 10, 596, 100, 12);
        tooltipLabel.enableWordWrapping = true; tooltipLabel.alignment = TextAlignmentOptions.TopLeft;
        tooltip.SetActive(false);
        choices = AssemblyDebugUi.New("PickerChoices", layer.transform); AssemblyDebugUi.Stretch(choices);
        var choiceBlocker = AssemblyDebugUi.Image(choices.transform, "DismissChoices", Color.clear, true);
        AssemblyDebugUi.Place(choiceBlocker.gameObject, -8192, -8192, 16384, 16384);
        var closeChoices = choiceBlocker.gameObject.AddComponent<Button>(); closeChoices.targetGraphic = choiceBlocker;
        closeChoices.transition = Selectable.Transition.None; closeChoices.navigation = new Navigation { mode = Navigation.Mode.None };
        closeChoices.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)HideChoices));
        var choicePanel = WorkroomPanelSkin.Panel(choices, "Options", 188, 144, 504, 140, new Color(.12f,.19f,.16f,1), UiRoundedGeometry.WindowRadius);
        choicePanel.GetComponent<Image>().raycastTarget = true;
        for (var i = 0; i < WorkroomStorageCatalog.Categories.Length; i++)
        {
            var index = i;
            choiceButtons.Add(Button(choicePanel.transform, "Option" + i, 6 + i % 3 * 164, 6 + i / 3 * 32, 160, 28, "", () =>
            {
                var selection = choose; HideChoices(); selection?.Invoke(index); Filter();
            }, 11));
        }
        choices.SetActive(false);
        layer.SetActive(false);
    }
    private void ShowChoices(string[] labels, int selected, Action<int> selection)
    {
        if (!Open || missingMode) return;
        Blur(); choose = selection; tooltip.SetActive(false); hovered = null;
        for (var i = 0; i < choiceButtons.Count; i++)
        {
            choiceButtons[i].SetActive(i < labels.Length);
            if (i >= labels.Length) continue;
            Set(choiceButtons[i], labels[i]); WorkroomPanelSkin.Selected(choiceButtons[i], i == selected);
        }
        AssemblyDebugUi.Place(choices.transform.Find("Options").gameObject, 188, 144, 504, 12 + ((labels.Length + 2) / 3) * 32);
        choices.SetActive(true); choices.transform.SetAsLastSibling();
    }
    private void HideChoices()
    {
        if (!ChoicesOpen) return;
        choicesClosedFrame = Time.frameCount; choose = null; WorkroomUiCleanup.Deactivate(choices);
    }
    private TMP_InputField MakeSearch(Transform parent)
    {
        var background = AssemblyDebugUi.Image(parent, "PickerSearch", new Color(.07f,.12f,.10f,1), true);
        UiRoundedGeometry.Fill(background); background.gameObject.SetActive(false);
        AssemblyDebugUi.Place(background.gameObject, 12, 34, 372, 28);
        var viewport = AssemblyDebugUi.New("TextViewport", background.transform); AssemblyDebugUi.Place(viewport, 8, 2, 356, 24);
        viewport.AddComponent<RectMask2D>();
        var text = Label(viewport.transform, "Text", 0, 0, 356, 24, 13);
        var placeholder = Label(viewport.transform, "Placeholder", 0, 0, 356, 24, 12); placeholder.text = Text("search");
        placeholder.color = new Color(.65f,.71f,.65f,1);
        var input = background.gameObject.AddComponent<TMP_InputField>(); input.targetGraphic = background;
        input.textViewport = AssemblyDebugUi.Rect(viewport); input.textComponent = text; input.placeholder = placeholder; input.fontAsset = font;
        input.pointSize = 13; input.lineType = TMP_InputField.LineType.SingleLine; input.contentType = TMP_InputField.ContentType.Standard;
        input.characterLimit = 120; input.richText = false; input.restoreOriginalTextOnEscape = false;
        input.navigation = new Navigation { mode = Navigation.Mode.None }; input.transition = Selectable.Transition.None;
        input.customCaretColor = true; input.caretColor = new Color(.96f,.94f,.80f,1); input.selectionColor = new Color(.48f,.59f,.35f,.65f);
        input.text = ""; background.gameObject.SetActive(true); return input;
    }
    private TextMeshProUGUI Label(Transform parent, string name, float x, float y, float width, float height, int size)
    {
        var label = AssemblyDebugUi.Text(parent, name, "", font, size, true); label.richText = false;
        label.overflowMode = TextOverflowModes.Ellipsis; label.enableWordWrapping = false;
        AssemblyDebugUi.Place(label.gameObject, x, y, width, height); return label;
    }
    private GameObject Button(Transform parent, string name, float x, float y, float width, float height, string text, Action click, int size = 11)
    {
        var button = WorkroomPanelSkin.Button(canvas, parent, name, text, font, size, click);
        AssemblyDebugUi.Place(button, x, y, width, height); return button;
    }
    private static void Set(GameObject button, string text) => button.GetComponentInChildren<TextMeshProUGUI>().text = text;
    private void ClearFilters()
    {
        category = type = tier = condition = 0; availableOnly = true; query = ""; search.text = "";
        if (Sources == 0)
        {
            Sources = WorkroomSelection.Source.All; sourceRevision++; PaintSources();
            page = 0; Rebuild(true); return;
        }
        Filter();
    }
    private void Filter() { page = 0; Rebuild(false); }
    private void Rebuild(bool capture)
    {
        if (!Open) return;
        if (epoch != WorkroomStorage.State.Epoch) { CloseActive(); return; }
        if (capture) { candidates = WorkroomSelection.Candidates(Sources); candidatesComplete = WorkroomSelection.CandidatesComplete; }
        revision = WorkroomStorage.State.Revision; seenSources = sourceRevision; locale = LanguageText.LocaleCode;
        visible.Clear();
        if (missingMode) CountMissing(candidates);
        else foreach (var candidate in candidates)
        {
            var item = candidate.Item;
            if (category > 0 && WorkroomStorageCatalog.CategoryFor(item.Identifier) != WorkroomStorageCatalog.Categories[category]) continue;
            if (type > 0 && WorkroomStorageCatalog.TypeFor(item) != new[] { "all", "dismantled", "component" }[type]) continue;
            if (tier > 0 && Tier(item) != tier) continue;
            if (condition == 1 && !WorkroomComponentAssembly.Intact(item) || condition == 2 && WorkroomComponentAssembly.Intact(item)) continue;
            if (query.Length > 0 && item.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                item.Identifier.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 && Details(candidate).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var reason = WorkroomSelection.Reason(target, candidate, repairSelection);
            if (!availableOnly || reason.Length == 0) visible.Add((candidate, reason));
        }
        visible.Sort((a, b) => { var order = (a.Reason.Length > 0).CompareTo(b.Reason.Length > 0); return order != 0 ? order : StringComparer.Ordinal.Compare(a.Candidate.Item.Name, b.Candidate.Item.Name); });
        var occupied = slots.FirstOrDefault(s => s.Target == target);
        title.text = missingMode ? Text("missing", missing.Count) : Text(occupied != null && WorkroomComponentAssembly.At(occupied.Slot) != null ? "replace" : "picker", targetName);
        returnButton.SetActive(!missingMode && occupied != null && WorkroomComponentAssembly.At(occupied.Slot) != null);
        Set(categoryButton, WorkroomStorage.Text("category." + WorkroomStorageCatalog.Categories[category]));
        Set(typeButton, WorkroomStorage.Text("type." + new[] { "all", "dismantled", "component" }[type]));
        Set(tierButton, tier == 0 ? Text("tier_all") : Text("tier", tier));
        Set(stateButton, Text(new[] { "state_all", "state_intact", "state_broken" }[condition]));
        Set(availableButton, Text(availableOnly ? "only_available" : "show_all")); WorkroomPanelSkin.Selected(availableButton, availableOnly);
        foreach (var control in new[] { search.gameObject, clear, categoryButton, typeButton, tierButton, stateButton, availableButton }) control.SetActive(!missingMode);
        RenderRows();
    }
    private static int Tier(WorkroomItemCodec.Snapshot item) => item.Nodes.Count > 0 ? WorkroomComponentAssembly.Configuration(item) : Components.Find(item.Identifier)?.Tier ?? 0;
    private static string Details(WorkroomSelection.Candidate candidate)
    {
        var item = candidate.Item;
        var capacity = item.Nodes.Count > 0 ? WorkroomComponentAssembly.Capacity(item) : 0;
        var configuration = item.Nodes.FirstOrDefault()?.Machine?.Parts.Select(p => p.Item.Name) ??
            item.Nodes.FirstOrDefault()?.Composition?.Select(p => p.Name) ?? Enumerable.Empty<string>();
        return Text("tier", Tier(item)) + " · " + Text(WorkroomComponentAssembly.Intact(item) ? "state_intact" : "state_broken") + " · " + candidate.SourceName +
            " · " + Text("instance", candidate.Id.Length <= 8 ? candidate.Id : candidate.Id.Substring(candidate.Id.Length - 8)) +
            (capacity > 0 ? " · " + capacity + "GB" : "") + (configuration.Any() ? " · " + Text("configuration", string.Join("/", configuration)) : "");
    }
    private void RenderRows()
    {
        if (!Open) return;
        var total = missingMode ? missing.Count : visible.Count;
        page = Math.Clamp(page, 0, Math.Max(0, (total - 1) / rows.Count));
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; var index = page * rows.Count + i;
            row.Root.SetActive(index < total); row.Candidate = null; row.Missing = null; row.Frame.SetActive(false);
            if (index >= total) continue;
            AssemblyDebugUi.Place(row.Root, 12, (missingMode ? 40 : 100) + i * (missingMode ? 66 : 51), 504, missingMode ? 60 : 49);
            if (missingMode)
            {
                var entry = missing[index]; row.Missing = entry.Slot;
                row.Icon.gameObject.SetActive(false); row.Name.text = entry.Slot.EmptyName;
                row.Detail.text = Text("missing_row", entry.Available); row.Reason.text = Text("select_hint");
                row.HoverText = row.Name.text + "\n" + row.Detail.text;
                WorkroomPanelSkin.Selected(row.Root, entry.Available > 0); continue;
            }
            var entryItem = visible[index]; row.Candidate = entryItem.Candidate; row.Failure = entryItem.Reason;
            row.Icon.sprite = WorkroomInventoryUi.Icon(entryItem.Candidate.Item); row.Icon.gameObject.SetActive(AssemblyDebugUi.Alive(row.Icon.sprite));
            if (row.Icon.gameObject.activeSelf) WorkroomItemSprite.Pose(row.Icon, entryItem.Candidate.Item.Orientation, entryItem.Candidate.Item.Flipped, new Vector2(42,41));
            row.Name.text = entryItem.Candidate.Item.Name; row.Detail.text = Details(entryItem.Candidate);
            var item = entryItem.Candidate.Item;
            var configuration = item.Nodes.FirstOrDefault()?.Machine?.Parts.Select(p => p.Item.Name) ??
                item.Nodes.FirstOrDefault()?.Composition?.Select(p => p.Name) ?? Enumerable.Empty<string>();
            row.Reason.text = entryItem.Reason.Length == 0 ? (configuration.Any() ? Text("configuration", string.Join("/", configuration)) : Text("select_hint")) : entryItem.Reason;
            row.HoverText = item.Name + "\n" + Details(entryItem.Candidate).Replace(" · ", "\n") + "\n" +
                (entryItem.Reason.Length == 0 ? Text("select_hint") : entryItem.Reason);
            row.Reason.color = entryItem.Reason.Length == 0 ? new Color(.65f,1,.75f,1) : new Color(1,.64f,.45f,1);
            WorkroomPanelSkin.Selected(row.Root, false);
            row.Frame.SetActive(entryItem.Reason.Length == 0 && target != WorkroomComponentAssembly.Disassembly);
        }
        empty.gameObject.SetActive(total == 0);
        empty.text = !candidatesComplete ? Text("incomplete_sources") : missingMode ? Text("missing_none") : Sources == 0 ? Text("empty_sources") : candidates.Count == 0 ? Text("empty_available") : Text("empty_filter");
        footer.text = Text("page", page + 1, Math.Max(1, (total + rows.Count - 1) / rows.Count)) + " · " +
            Text("available_count", missingMode ? missing.Sum(m => m.Available) : visible.Count(v => v.Reason.Length == 0), total);
        if (!candidatesComplete) footer.text += " · " + Text("incomplete_sources");
        var text = title.text + footer.text + empty.text + string.Join("", rows.Where(r => r.Root.activeSelf).Select(r => r.Name.text + r.Detail.text + r.Reason.text));
        AssemblyDebugFonts.Prepare(text, text);
        hovered = null; tooltip.SetActive(false);
    }
    private void Pick(Row row)
    {
        if (!Open || epoch != WorkroomStorage.State.Epoch) { CloseActive(); return; }
        if (row.Missing != null) { ShowSlot(row.Missing); return; }
        if (row.Candidate == null) return;
        if (revision != WorkroomStorage.State.Revision) { Rebuild(true); return; }
        var reason = WorkroomSelection.Reason(target, row.Candidate, repairSelection);
        if (reason.Length > 0) { WorkroomStorage.NotifyText(reason); Rebuild(true); return; }
        if (WorkroomSelection.Select(row.Candidate, target, epoch, repairSelection)) CloseActive(); else Rebuild(true);
    }
    private IEnumerable<WorkroomAssemblySlotUi> EmptyInternalSlots => slots.Where(slot => slot.Root.activeInHierarchy && slot.Enabled &&
        slot.Slot != WorkroomComponentAssembly.Disassembly && !slot.IsBody &&
        WorkroomComponentAssembly.At(slot.Slot) == null);
    private void CountMissing(List<WorkroomSelection.Candidate> all)
    {
        missing.Clear();
        foreach (var slot in EmptyInternalSlots.GroupBy(s => s.Slot).Select(g => g.First()))
            missing.Add((slot, all.Count(candidate => WorkroomSelection.Reason(slot.Target, candidate).Length == 0)));
        countedRevision = WorkroomStorage.State.Revision; countedSources = sourceRevision; countedLocale = LanguageText.LocaleCode;
        if (missingButton != null) Set(missingButton, Text("missing", missing.Count));
    }
    internal static IEnumerable<string> HighlightSlots => instances.Where(instance => instance.owner.Open && instance.slots.Any(slot =>
        slot.IsBody && slot.Slot != WorkroomComponentAssembly.Disassembly && WorkroomComponentAssembly.At(slot.Slot) != null))
        .SelectMany(instance => instance.EmptyInternalSlots).Select(slot => slot.Target).Distinct(StringComparer.Ordinal);
    internal static string? HighlightSlot => active != null && !active.missingMode && HighlightSlots.Contains(active.target) ? active.target : null;
    internal static void UpdateAll(bool allowPress)
    {
        if (session != WorkroomStorage.State.Epoch)
        {
            if (session >= 0) { Sources = WorkroomSelection.Source.All; sourceRevision++; CloseActive(); }
            session = WorkroomStorage.State.Epoch;
        }
        foreach (var instance in instances)
        {
            if (!instance.owner.Open) continue;
            instance.PaintAutoReturn();
            if (instance.countedRevision != WorkroomStorage.State.Revision || instance.countedSources != sourceRevision || instance.countedLocale != LanguageText.LocaleCode)
            {
                instance.PaintSources();
                // The badge is cheap. Native candidates are captured only when the missing list is opened.
                if (instance.missingButton != null) Set(instance.missingButton, Text("missing", instance.EmptyInternalSlots.Count()));
                instance.countedRevision = WorkroomStorage.State.Revision; instance.countedSources = sourceRevision; instance.countedLocale = LanguageText.LocaleCode;
            }
        }
        var current = active;
        if (current == null || !current.Open) return;
        if (allowPress && !Composing && Input.GetKeyDown(KeyCode.Escape)) { CloseTop(); return; }
        if (current.epoch != WorkroomStorage.State.Epoch) { CloseActive(); return; }
        if (current.revision != WorkroomStorage.State.Revision || current.seenSources != sourceRevision || current.locale != LanguageText.LocaleCode) current.Rebuild(true);
        if (Input.GetMouseButtonDown(0) && !RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(current.search.gameObject), Input.mousePosition, null)) current.Blur();
        current.search.interactable = allowPress && !current.missingMode && !current.ChoicesOpen && current.choicesClosedFrame != Time.frameCount;
        if (!current.missingMode && Input.compositionString.Length == 0)
        {
            var value = (current.search.text ?? "").Trim();
            if (value != current.query) { current.query = value; current.Filter(); }
        }
        if (allowPress && !current.ChoicesOpen && current.choicesClosedFrame != Time.frameCount && Input.mouseScrollDelta.y != 0 && RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(current.popup), Input.mousePosition, null))
        { current.page += Input.mouseScrollDelta.y < 0 ? 1 : -1; current.RenderRows(); }
        current.UpdateTooltip(allowPress && !current.ChoicesOpen && current.choicesClosedFrame != Time.frameCount);
    }
    private void UpdateTooltip(bool allow)
    {
        var next = allow ? rows.FirstOrDefault(row => row.Root.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(row.Root), Input.mousePosition, null)) : null;
        if (next == null) { hovered = null; tooltip.SetActive(false); return; }
        if (!ReferenceEquals(hovered, next) || hoverText != next.HoverText)
        {
            hovered = next; hoverText = next.HoverText;
            tooltipLabel.font = AssemblyDebugFonts.Prepare(hoverText, hoverText); tooltipLabel.text = hoverText;
            var height = Math.Min(620, Math.Max(60, tooltipLabel.GetPreferredValues(hoverText, 596, float.PositiveInfinity).y + 20));
            AssemblyDebugUi.Place(tooltip, 0, 0, 620, height); AssemblyDebugUi.Place(tooltipLabel.gameObject, 12, 10, 596, height - 20);
        }
        var reference = owner.Root.transform.parent.TryCast<RectTransform>();
        if (reference == null) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(reference, Input.mousePosition, null, out var point)) return;
        var rect = AssemblyDebugUi.Rect(tooltip); var x = point.x - reference.rect.xMin + 16; var y = reference.rect.yMax - point.y + 12;
        if (x + rect.rect.width > 1272) x = point.x - reference.rect.xMin - rect.rect.width - 16;
        x = Mathf.Clamp(x, 8, 1272 - rect.rect.width); y = Mathf.Clamp(y, 8, 712 - rect.rect.height);
        rect.position = reference.TransformPoint(new Vector3(reference.rect.xMin + x, reference.rect.yMax - y, 0));
        tooltip.SetActive(true); tooltip.transform.SetAsLastSibling();
    }
    private void Blur()
    {
        if (!WorkroomUiCleanup.IsAlive(search)) return;
        search.DeactivateInputField();
        var events = EventSystem.current;
        if (AssemblyDebugUi.Alive(events) && events.currentSelectedGameObject != null && events.currentSelectedGameObject.Pointer == search.gameObject.Pointer)
            events.SetSelectedGameObject(null);
    }
    internal static bool CloseActive()
    {
        var current = active; if (current == null) return false;
        active = null; current.HideChoices(); current.Blur(); pointerOwned = true; neutralFrames = 0; inputFrame = Time.frameCount;
        WorkroomUiCleanup.Deactivate(current.layer); return true;
    }
    internal static bool CloseTop()
    {
        if (escapeFrame == Time.frameCount) return true;
        if (active == null) return false;
        escapeFrame = Time.frameCount;
        if (active.ChoicesOpen) { active.HideChoices(); return true; }
        return CloseActive();
    }
    internal static void CloseFor(WorkroomAssemblyWindow window)
    { if (active != null && ReferenceEquals(active.owner, window)) CloseActive(); }
    internal static bool Composing => active?.Open == true && active.search.isFocused && Input.compositionString.Length > 0;
    internal static bool OwnsTextInput => OwnsInput;
    internal static bool OwnsInput
    {
        get
        {
            if (active != null)
            {
                if (!active.Open || active.epoch != WorkroomStorage.State.Epoch) CloseActive();
                else return true;
            }
            if (!pointerOwned || inputFrame == Time.frameCount) return pointerOwned;
            inputFrame = Time.frameCount;
            var neutral = !Input.anyKey && Input.compositionString.Length == 0;
            for (var i = 0; i < 7; i++) neutral &= !Input.GetMouseButton(i) && !Input.GetMouseButtonDown(i) && !Input.GetMouseButtonUp(i);
            neutralFrames = neutral ? neutralFrames + 1 : 0;
            if (neutralFrames >= 2) pointerOwned = false;
            return pointerOwned;
        }
    }
    internal static bool AllowsButton(GameObject button) => !OwnsInput || active != null && active.choicesClosedFrame != Time.frameCount &&
        button.transform.IsChildOf(active.ChoicesOpen ? active.choices.transform : active.layer.transform);
    internal static void Release()
    { CloseActive(); instances.Clear(); pointerOwned = false; neutralFrames = 0; inputFrame = -1; }
}
