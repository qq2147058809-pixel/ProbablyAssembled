using System;
using System.Collections.Generic;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using Il2CppInterop.Runtime;

namespace PCExpansion;

/// <summary>共用滚动目录，固定复用五行；店铺沿用原版拖动，工作间预览单独绘制。</summary>
internal sealed class WorkroomStorageUi
{
    internal const int Width = 540, Height = 424;
    private static readonly string[] categories = WorkroomStorageCatalog.Categories;
    private static readonly string[] conditions = { "all", "intact", "broken" };
    private sealed class Row
    {
        internal GameObject Face = null!, Button = null!;
        internal TextMeshProUGUI Label = null!, State = null!;
        internal Image Background = null!;
        internal Button Take = null!;
        internal string Id = "";
        internal long Epoch;
    }
    private readonly Row[] rows = new Row[5];
    private readonly GameObject body, face, ghostRoot, filterLayer;
    private readonly TextMeshProUGUI summary, status;
    private readonly TextMeshProUGUI hint, collectLabel;
    private readonly Button collect, previous, next;
    private readonly Canvas canvas;
    private readonly Image ghost;
    private readonly TMP_InputField search;
    private readonly TextMeshProUGUI searchHint, clearLabel, filterLabel, filterSummary;
    private readonly Button filterButton;
    private readonly GameObject[] categoryButtons = new GameObject[categories.Length], conditionButtons = new GameObject[conditions.Length];
    private readonly TextMeshProUGUI[] categoryLabels = new TextMeshProUGUI[categories.Length], conditionLabels = new TextMeshProUGUI[conditions.Length];
    private readonly TMP_FontAsset font;
    private readonly WorkroomItemInfo information = new();
    private int offset;
    private bool inRoom;
    private IReadOnlyList<WorkroomStorageCatalog.Group> catalog = Array.Empty<WorkroomStorageCatalog.Group>();
    private readonly List<WorkroomStorageCatalog.Group> items = new();
    private string query = "", pendingQuery = "", category = "all", condition = "all";
    private long queryDue;
    private bool popupOwned;
    private int popupFrame = -1, popupNeutralFrames;
    private bool filterDirty = true, keyboardOwned;
    private int inputFrame = -1, inputNeutralFrames;
    private long filterEpoch = -1;
    private int totalCount, matchingCount;
    private long revision = -1, epoch = -1;
    private int renderedOffset = -1;
    private string? locale;
    private string displayedStatus = "";
    private readonly Dictionary<string, string> textCache = new(StringComparer.Ordinal);

    private string CachedText(string key)
    {
        if (!textCache.TryGetValue(key, out var value)) textCache[key] = value = WorkroomStorage.Text(key);
        return value;
    }

    internal WorkroomStorageUi(Canvas canvas, GameObject face, TMP_FontAsset font)
    {
        this.canvas = canvas; this.face = face; this.font = font;
        body = AssemblyDebugUi.New("StorageContents", face.transform);
        AssemblyDebugUi.Stretch(body);
        hint = AssemblyDebugUi.Text(body.transform, "StorageHint", WorkroomStorage.Text("hint"), font, 11, true);
        hint.enableWordWrapping = true;
        AssemblyDebugUi.Place(hint.gameObject, 16, 62, 372, 24);
        var collectFace = WorkroomPanelSkin.Button(canvas, body.transform, "CollectCurrent", WorkroomStorage.Text("collect"), font, 13,
            () => WorkroomStorage.CollectCurrent(inRoom, WorkroomStorage.State.Epoch));
        AssemblyDebugUi.Place(collectFace, 398, 60, 126, 26);
        collect = collectFace.GetComponent<Button>();
        collectLabel = collectFace.GetComponentInChildren<TextMeshProUGUI>();
        search = MakeSearch(font, out searchHint);
        var filters = WorkroomPanelSkin.Button(canvas, body.transform, "Filters", WorkroomStorage.Text("filters"), font, 13, OpenFilters);
        AssemblyDebugUi.Place(filters, 398, 90, 126, 28);
        filterButton = filters.GetComponent<Button>();
        filterLabel = filters.GetComponentInChildren<TextMeshProUGUI>();
        filterSummary = AssemblyDebugUi.Text(body.transform, "CurrentFilters", "", font, 11, true);
        AssemblyDebugUi.Place(filterSummary.gameObject, 16, 118, 508, 18);
        filterLayer = AssemblyDebugUi.New("FilterLayer", body.transform);
        AssemblyDebugUi.Stretch(filterLayer);
        // The transparent blocker consumes the entire dismissal gesture, even
        // outside the box. It never forwards the click to a covered item.
        var blocker = AssemblyDebugUi.Image(filterLayer.transform, "DismissFilters", Color.clear, true);
        AssemblyDebugUi.Place(blocker.gameObject, -8192, -8192, 16384, 16384);
        var dismiss = blocker.gameObject.AddComponent<Button>();
        dismiss.targetGraphic = blocker; dismiss.transition = Selectable.Transition.None;
        dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
        dismiss.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)(() => CloseFilters())));
        var popup = WorkroomPanelSkin.Panel(filterLayer, "FilterPopup", 16, 120, 508, 142, new Color(.12f,.18f,.15f,1));
        popup.GetComponent<Image>().raycastTarget = true;
        var clear = WorkroomPanelSkin.Button(canvas, popup.transform, "ClearFilters", WorkroomStorage.Text("clear_filters"), font, 12, ClearFilters);
        AssemblyDebugUi.Place(clear, 370, 108, 126, 26);
        clearLabel = clear.GetComponentInChildren<TextMeshProUGUI>();
        for (var i = 0; i < categories.Length; i++)
        {
            var key = categories[i];
            var button = WorkroomPanelSkin.Button(canvas, popup.transform, "Category" + key, CategoryName(key), font, 12,
                () => { category = key; ReadQuery(true); ChangedFilter(); });
            AssemblyDebugUi.Place(button, 10 + i % 6 * 82, 10 + i / 6 * 32, 78, 28);
            categoryButtons[i] = button; categoryLabels[i] = button.GetComponentInChildren<TextMeshProUGUI>();
        }
        for (var i = 0; i < conditions.Length; i++)
        {
            var key = conditions[i];
            var button = WorkroomPanelSkin.Button(canvas, popup.transform, "Condition" + key, WorkroomStorage.Text("condition." + key), font, 12,
                () => { condition = key; ReadQuery(true); ChangedFilter(); });
            AssemblyDebugUi.Place(button, 10 + i * 164, 74, 160, 28);
            conditionButtons[i] = button; conditionLabels[i] = button.GetComponentInChildren<TextMeshProUGUI>();
        }
        for (var i = 0; i < rows.Length; i++)
        {
            var row = new Row(); rows[i] = row;
            row.Face = WorkroomPanelSkin.Panel(body, "Record" + i, 16, 136+i*44, 508, 42, new Color(.14f,.19f,.16f,.94f));
            row.Background = row.Face.GetComponent<Image>();
            row.Label = AssemblyDebugUi.Text(row.Face.transform, "Item", "", font, 13, true);
            row.Label.overflowMode = TextOverflowModes.Ellipsis;
            AssemblyDebugUi.Place(row.Label.gameObject, 10, 0, 342, 42);
            row.State = AssemblyDebugUi.Text(row.Face.transform, "State", "", font, 12);
            AssemblyDebugUi.Place(row.State.gameObject, 354, 0, 64, 42);
            row.Button = WorkroomPanelSkin.Button(canvas, row.Face.transform, "Take", WorkroomStorage.Text("take"), font, 13,
                () => { WorkroomStorage.Take(row.Id, inRoom, row.Epoch); Update(); });
            AssemblyDebugUi.Place(row.Button, 428, 6, 70, 30);
            row.Take = row.Button.GetComponent<Button>();
        }
        summary = AssemblyDebugUi.Text(body.transform, "Summary", "", font, 13);
        summary.fontSize = 12;
        AssemblyDebugUi.Place(summary.gameObject, 72, 360, 396, 27);
        var up = WorkroomPanelSkin.Button(canvas, body.transform, "Previous", "↑", font, 18,
            () => offset = Math.Max(0, offset-rows.Length));
        var down = WorkroomPanelSkin.Button(canvas, body.transform, "Next", "↓", font, 18,
            () => offset += rows.Length);
        previous = up.GetComponent<Button>(); next = down.GetComponent<Button>();
        AssemblyDebugUi.Place(up, 16, 360, 44, 27); AssemblyDebugUi.Place(down, 480, 360, 44, 27);
        status = AssemblyDebugUi.Text(body.transform, "TransferStatus", "", font, 12);
        status.enableWordWrapping = true;
        AssemblyDebugUi.Place(status.gameObject, 16, 390, 508, 20);
        filterLayer.transform.SetAsLastSibling();
        filterLayer.SetActive(false);
        ghostRoot = AssemblyDebugUi.New("StorageDragPreview", canvas.transform);
        ghostRoot.SetActive(false);
        ghost = AssemblyDebugUi.Image(ghostRoot.transform, "ItemIcon", Color.white, false);
        ghost.preserveAspect = true;
        body.SetActive(false);
    }

    internal void Open(bool room)
    {
        Cancel(); inRoom = room; renderedOffset = -1; body.SetActive(true); Update();
    }

    internal void Close() { CloseFilters(); Blur(); Cancel(); information.Clear(); body.SetActive(false); }
    internal void Release()
    {
        keyboardOwned = popupOwned = false;
        var cleanup = new WorkroomUiCleanup();
        cleanup.Run("storage.filters", () => WorkroomUiCleanup.Deactivate(filterLayer));
        cleanup.Run("storage.search", () =>
        {
            if (!WorkroomUiCleanup.IsAlive(search)) return;
            search.DeactivateInputField();
            var events = EventSystem.current;
            if (WorkroomUiCleanup.IsAlive(events) && events.currentSelectedGameObject != null &&
                events.currentSelectedGameObject.Pointer == search.gameObject.Pointer) events.SetSelectedGameObject(null);
        });
        cleanup.Run("storage.drag", Cancel);
        cleanup.Run("storage.information", information.Dispose);
        cleanup.ThrowIfFailed();
    }
    internal bool OwnsTextInput
    {
        get { SampleInput(); return keyboardOwned || popupOwned; }
    }
    internal bool FiltersOpen => AssemblyDebugUi.Alive(body) && body.activeInHierarchy &&
        AssemblyDebugUi.Alive(filterLayer) && filterLayer.activeSelf;
    private bool Focused => AssemblyDebugUi.Alive(body) && body.activeInHierarchy && AssemblyDebugUi.Alive(search) && search.isFocused;
    internal bool Composing => Focused && Input.compositionString.Length > 0;

    // Keep the final key-up out of native hotkeys; never disable EventSystem
    // navigation here, since the TMP input field needs its update-selected event.
    private void SampleInput()
    {
        if (FiltersOpen) { popupOwned = true; popupNeutralFrames = 0; }
        if (popupFrame != Time.frameCount)
        {
            popupFrame = Time.frameCount;
            if (popupOwned && !FiltersOpen)
            {
                var neutral = !Input.anyKey && Input.compositionString.Length == 0;
                for (var i = 0; i < 7; i++) neutral &= !Input.GetMouseButton(i) && !Input.GetMouseButtonDown(i) && !Input.GetMouseButtonUp(i);
                popupNeutralFrames = neutral ? popupNeutralFrames + 1 : 0;
                if (popupNeutralFrames >= 2) popupOwned = false;
            }
        }
        if (Focused) { keyboardOwned = true; inputNeutralFrames = 0; }
        if (inputFrame == Time.frameCount) return;
        inputFrame = Time.frameCount;
        if (!keyboardOwned || Focused) return;
        inputNeutralFrames = Input.anyKey || Input.compositionString.Length > 0 ? 0 : inputNeutralFrames + 1;
        if (inputNeutralFrames >= 2) keyboardOwned = false;
    }

    internal void UpdateInput(bool allowFocus)
    {
        SampleInput();
        if (!body.activeInHierarchy) return;
        if (Input.GetMouseButtonDown(0) && (!allowFocus || !RectTransformUtility.RectangleContainsScreenPoint(
                AssemblyDebugUi.Rect(search.gameObject), Input.mousePosition, null))) Blur();
        search.interactable = allowFocus && !FiltersOpen;
        filterButton.interactable = allowFocus && !(Il2Cpp.ItemMouseDragHandler.current?.isDragging ?? false);
    }

    internal void Blur()
    {
        if (!AssemblyDebugUi.Alive(search)) return;
        ReadQuery(true);
        if (Focused) { keyboardOwned = true; inputNeutralFrames = 0; }
        search.DeactivateInputField();
        var events = EventSystem.current;
        if (AssemblyDebugUi.Alive(events) && events.currentSelectedGameObject != null &&
            events.currentSelectedGameObject.Pointer == search.gameObject.Pointer) events.SetSelectedGameObject(null);
    }

    private TMP_InputField MakeSearch(TMP_FontAsset font, out TextMeshProUGUI placeholder)
    {
        var background = AssemblyDebugUi.Image(body.transform, "Search", new Color(.10f,.15f,.13f,1), true);
        background.gameObject.SetActive(false);
        AssemblyDebugUi.Place(background.gameObject, 16, 90, 370, 28);
        var viewport = AssemblyDebugUi.New("TextViewport", background.transform);
        AssemblyDebugUi.Place(viewport, 8, 2, 354, 24);
        viewport.AddComponent<RectMask2D>();
        var text = AssemblyDebugUi.Text(viewport.transform, "Text", "", font, 14, true);
        AssemblyDebugUi.Stretch(text.gameObject);
        placeholder = AssemblyDebugUi.Text(viewport.transform, "Placeholder", WorkroomStorage.Text("search_hint"), font, 13, true);
        placeholder.color = new Color(.65f,.71f,.65f,1);
        AssemblyDebugUi.Stretch(placeholder.gameObject);
        var input = background.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = AssemblyDebugUi.Rect(viewport);
        input.textComponent = text; input.placeholder = placeholder; input.fontAsset = font;
        input.pointSize = 14;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.characterLimit = 120; input.richText = false;
        input.restoreOriginalTextOnEscape = false;
        input.navigation = new Navigation { mode = Navigation.Mode.None };
        input.transition = Selectable.Transition.None;
        input.customCaretColor = true; input.caretColor = new Color(.96f,.94f,.80f,1);
        input.selectionColor = new Color(.48f,.59f,.35f,.65f);
        input.text = "";
        input.onSubmit.AddListener(DelegateSupport.ConvertDelegate<UnityAction<string>>((Action<string>)(_ => ReadQuery(true))));
        background.gameObject.SetActive(true);
        return input;
    }

    private static string CategoryName(string key) => WorkroomStorage.Text("category." + key);
    internal static string FontText
    {
        get
        {
            var value = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-. /×";
            foreach (var key in categories) value += CategoryName(key);
            foreach (var key in conditions) value += WorkroomStorage.Text("condition." + key);
            return value + WorkroomStorage.Text("search_hint") + WorkroomStorage.Text("clear_filters") + WorkroomStorage.Text("filters") +
                WorkroomStorage.Text("current_filters", CategoryName("all"), WorkroomStorage.Text("condition.all")) +
                WorkroomStorage.Text("no_matches") + WorkroomStorage.Text("group_summary", 0, 0, 0, 0, 0);
        }
    }
    private void ChangedFilter() { offset = 0; filterDirty = true; information.Clear(); }
    private void ClearFilters()
    {
        search.text = query = pendingQuery = ""; category = condition = "all"; queryDue = 0; ChangedFilter();
    }
    private void ReadQuery(bool immediate = false)
    {
        var value = (search.text ?? "").Trim();
        if (pendingQuery != value)
        {
            pendingQuery = value;
            queryDue = Environment.TickCount64 + 150;
        }
        if (query == pendingQuery || (!immediate && (Composing || Environment.TickCount64 < queryDue))) return;
        query = pendingQuery;
        // Free text is allowed to contain glyphs outside the game's font. Let
        // TMP show its fallback glyph rather than reset the whole workroom.
        font.TryAddCharacters(value, out string _, true);
        ChangedFilter();
    }
    private void OpenFilters()
    {
        if (FiltersOpen || !filterButton.interactable) return;
        Blur(); information.Clear(); Cancel();
        filterLayer.SetActive(true);
        face.transform.SetAsLastSibling();
        popupOwned = true; popupNeutralFrames = 0;
        Update();
    }
    internal bool CloseFilters()
    {
        if (!FiltersOpen) return false;
        filterLayer.SetActive(false);
        popupOwned = true; popupNeutralFrames = 0;
        return true;
    }
    internal void UpdateInformation(bool allowed)
    {
        WorkroomStorageState.Record? record = null;
        if (allowed && !FiltersOpen && !Input.GetMouseButton(0))
            foreach (var row in rows)
                if (row.Face.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(AssemblyDebugUi.Rect(row.Face), Input.mousePosition, null))
                { record = WorkroomStorage.State.Find(row.Id); break; }
        information.Update(record);
    }

    internal bool Hit(Vector2 point)
    {
        if (!body.activeInHierarchy || popupOwned) return false;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(AssemblyDebugUi.Rect(face), point, null, out var local)) return false;
        return AssemblyDebugUi.Rect(face).rect.Contains(local) && local.y <= -40;
    }

    internal void Update()
    {
        if (!inRoom) Cancel();
        if (!body.activeInHierarchy) return;
        var state = WorkroomStorage.State;
        var newLocale = LanguageText.LocaleCode;
        if (filterEpoch != state.Epoch)
        { filterEpoch = state.Epoch; CloseFilters(); Blur(); ClearFilters(); }
        ReadQuery(!Composing && OwnsTextInput && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)));
        var catalogChanged = revision != state.CatalogRevision || epoch != state.Epoch;
        var changed = catalogChanged || locale != newLocale;
        if (changed)
        {
            if (locale != newLocale) textCache.Clear();
            if (catalogChanged)
            {
                catalog = WorkroomStorageCatalog.Build(state.Items);
                totalCount = 0;
                foreach (var group in catalog) totalCount += group.Count;
            }
            revision = state.CatalogRevision; epoch = state.Epoch; locale = newLocale;
            hint.text = WorkroomStorage.Text("hint"); collectLabel.text = WorkroomStorage.Text("collect");
            searchHint.text = WorkroomStorage.Text("search_hint"); clearLabel.text = WorkroomStorage.Text("clear_filters");
            filterLabel.text = WorkroomStorage.Text("filters");
            for (var i = 0; i < categories.Length; i++) categoryLabels[i].text = CategoryName(categories[i]);
            for (var i = 0; i < conditions.Length; i++) conditionLabels[i].text = WorkroomStorage.Text("condition." + conditions[i]);
        }
        var redraw = changed || filterDirty;
        if (redraw)
        {
            items.Clear(); matchingCount = 0;
            foreach (var group in catalog)
                if (group.Matches(query, category, condition)) { items.Add(group); matchingCount += group.Count; }
            for (var i = 0; i < categories.Length; i++) WorkroomPanelSkin.Selected(categoryButtons[i], category == categories[i]);
            for (var i = 0; i < conditions.Length; i++) WorkroomPanelSkin.Selected(conditionButtons[i], condition == conditions[i]);
            filterSummary.text = WorkroomStorage.Text("current_filters", CategoryName(category), WorkroomStorage.Text("condition." + condition));
            WorkroomPanelSkin.Selected(filterButton.gameObject, category != "all" || condition != "all");
            filterDirty = false;
        }
        if (Hit(Input.mousePosition) && Input.mouseScrollDelta.y != 0)
            offset -= Math.Sign(Input.mouseScrollDelta.y);
        offset = Math.Clamp(offset, 0, Math.Max(0, items.Count-rows.Length));
        if (redraw || renderedOffset != offset)
        {
            renderedOffset = offset;
            var dynamicText = "";
            for (var i = offset; i < Math.Min(items.Count, offset + rows.Length); i++) dynamicText += items[i].Representative.Item.Name;
            AssemblyDebugFonts.Prepare(dynamicText, dynamicText);
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i]; var index = offset+i;
                row.Face.SetActive(index < items.Count);
                if (index >= items.Count) continue;
                var group = items[index]; var item = group.Representative; row.Id = item.Id; row.Epoch = state.Epoch;
                row.Label.text = WorkroomStorage.Text("row", item.Item.Name, group.Count, item.Item.Value);
                row.State.text = item.Place != "box" ? WorkroomStorage.Text("pending") :
                    item.Item.Broken ? WorkroomStorage.Text("broken") : WorkroomComponentTemplates.Text("intact_short");
                row.Background.color = new Color(.14f,.19f,.16f,.94f);
                row.State.color = item.Item.Broken ? new Color(1,.72f,.56f,1) : new Color(.76f,.85f,.71f,1);
                row.Button.GetComponentInChildren<TextMeshProUGUI>().text = WorkroomStorage.Text("take");
            }
            summary.text = totalCount == 0 ? WorkroomStorage.Text("empty") : items.Count == 0 ? WorkroomStorage.Text("no_matches") :
                WorkroomStorage.Text("group_summary", totalCount, matchingCount, items.Count, offset+1, Math.Min(items.Count, offset+rows.Length));
        }
        for (var i = 0; i < rows.Length && offset + i < items.Count; i++)
            rows[i].Take.interactable = !popupOwned && state.Ready && items[offset+i].CanTake && !WorkroomStorage.Busy && !WorkroomItemCodec.CleanupPending;
        collect.interactable = !popupOwned && state.Ready && !WorkroomStorage.Busy && !WorkroomItemCodec.CleanupPending && WorkroomTrial.CanCollectCurrent(inRoom);
        previous.interactable = !popupOwned && offset > 0;
        next.interactable = !popupOwned && offset + rows.Length < items.Count;
        var message = state.Error != null ? CachedText(state.Error) :
            WorkroomItemCodec.CleanupPending ? CachedText("recovery") :
            Environment.TickCount64 < WorkroomStorage.MessageUntil ? WorkroomStorage.Message : "";
        if (displayedStatus != message) { displayedStatus = message; status.text = message; }
    }

    internal void Cancel()
    { if (AssemblyDebugUi.Alive(ghostRoot)) ghostRoot.SetActive(false); }

    internal void RoomPreview(Sprite? sprite, Vector2 position, Vector2 size, Color color,
        int orientation, bool flipped, bool visible)
    {
        if (!visible || !inRoom || !body.activeInHierarchy || !AssemblyDebugUi.Alive(sprite))
        { Cancel(); return; }
        ghost.sprite = sprite; ghost.color = color;
        var rect = AssemblyDebugUi.Rect(ghostRoot);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f,.5f); rect.pivot = new Vector2(0,1);
        rect.sizeDelta = size; rect.anchoredPosition = position;
        WorkroomItemSprite.Pose(ghost, orientation, flipped, size);
        ghostRoot.SetActive(true);
    }
}
