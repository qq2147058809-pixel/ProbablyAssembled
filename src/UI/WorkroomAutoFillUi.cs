using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PCExpansion;

// An owner-local foldout. Selection and preference writes run only on clicks.
internal sealed class WorkroomAutoFillUi
{
    private static WorkroomAutoFillUi? active;
    private static bool pointerOwned;
    private static int neutralFrames, inputFrame = -1;
    private readonly WorkroomAssemblyWindow owner;
    private readonly Canvas canvas;
    private readonly TMP_FontAsset font;
    private readonly string family, savedKey;
    private readonly bool machine;
    private GameObject layer = null!, popup = null!;
    internal readonly GameObject FillButton;
    private GameObject minimum = null!, maximum = null!, preference = null!, quantity = null!, source = null!, vram = null!, ram = null!, disk = null!;
    private readonly List<GameObject> counts = new();
    private TextMeshProUGUI hint = null!;
    private WorkroomAutoFillSolver.Options options = new();
    private long epoch;
    private bool Open => layer != null && owner.Open && layer.activeSelf;
    internal static string FontText => string.Join("", new[] { "filter", "minimum", "maximum", "economic", "performance",
        "quantity_min", "quantity_full", "quantity_custom", "source_both", "source_room", "source_box", "vram", "ram", "disk",
        "capacity", "count", "fold_hint", "power_hint", "incomplete", "none", "changed" }.Select(k => WorkroomAutoFill.Text(k, 0, 0, 0))) + "T0123456789GB—/";

    internal WorkroomAutoFillUi(Canvas canvas, WorkroomAssemblyWindow owner, TMP_FontAsset font, string family, bool machine)
    {
        this.canvas = canvas; this.owner = owner; this.font = font; this.family = family; this.machine = machine;
        savedKey = (machine ? "machine." : "component.") + family;
        var fillX = machine ? 261 : 272; var fillWidth = machine ? 100 : 110;
        var toggle = Button(owner.Root.transform, "FillFilter", 176, 396, machine ? 80 : 90, Toggle);
        Set(toggle, WorkroomAutoFill.Text("filter"));
        FillButton = Button(owner.Root.transform, "AutoFill", fillX, 396, fillWidth, Fill);
        Set(FillButton, WorkroomComponentTemplates.Text("auto_fill"));
    }
    private void EnsurePopup()
    {
        if (layer != null) return;
        var fillX = machine ? 261 : 272; var fillWidth = machine ? 100 : 110;
        layer = AssemblyDebugUi.New("FillFilterLayer", owner.Root.transform); AssemblyDebugUi.Stretch(layer);
        // Own the complete outside-click gesture, as the storage filter does.
        var blocker = AssemblyDebugUi.Image(layer.transform, "DismissFillFilter", Color.clear, true);
        AssemblyDebugUi.Place(blocker.gameObject, -8192, -8192, 16384, 16384);
        var dismiss = blocker.gameObject.AddComponent<Button>();
        dismiss.targetGraphic = blocker; dismiss.transition = Selectable.Transition.None;
        dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
        dismiss.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)(() => CloseActive())));
        popup = WorkroomPanelSkin.Panel(layer, "FillFilterPopup", 176, 200, 528, 190, new Color(.12f,.18f,.15f,1), UiRoundedGeometry.WindowRadius);
        popup.GetComponent<Image>().raycastTarget = true;
        minimum = Option("Minimum", () => { options.Minimum = options.Minimum % 5 + 1; options.Maximum = Math.Max(options.Minimum, options.Maximum); });
        maximum = Option("Maximum", () => { options.Maximum = options.Maximum % 5 + 1; options.Minimum = Math.Min(options.Minimum, options.Maximum); });
        preference = Option("Preference", () => options.Performance = !options.Performance);
        quantity = Option("Quantity", () => options.Quantity = (options.Quantity + 1) % 3);
        source = Option("Source", () => options.Source = (options.Source + 1) % 3);
        vram = Option("Vram", () => options.Vram = Cycle(options.Vram, new long[] { 0, 4, 8, 12, 16, 24, 32, 48, 64, 96 }));
        ram = Option("Ram", () => options.Ram = Cycle(options.Ram, new long[] { 0, 4, 8, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384 }));
        disk = Option("Disk", () => options.Disk = Cycle(options.Disk, new long[] { 0, 256, 512, 1024, 2048, 4096, 8192 }));
        foreach (var kind in WorkroomAutoFill.Kinds(family, machine))
            counts.Add(Option("Count_" + kind, () => options.Counts[kind] =
                (options.Counts.GetValueOrDefault(kind, DefaultCount(kind)) + 1) % (WorkroomAutoFill.Maximum(family, machine, kind) + 1)));
        hint = Label("Hint"); hint.text = WorkroomAutoFill.Text(!machine && family == "psu" ? "power_hint" : "fold_hint");
        var foldedToggle = Button(layer.transform, "CloseFillFilter", 176, 396, machine ? 80 : 90, Toggle);
        Set(foldedToggle, WorkroomAutoFill.Text("filter"));
        WorkroomPanelSkin.Selected(foldedToggle, true);
        var foldedFill = Button(layer.transform, "FillFiltered", fillX, 396, fillWidth, Fill);
        Set(foldedFill, WorkroomComponentTemplates.Text("auto_fill"));
        layer.SetActive(false);
    }
    private GameObject Button(Transform parent, string name, float x, float y, float width, Action click)
    {
        var button = WorkroomPanelSkin.Button(canvas, parent, name, "", font, 12, click);
        AssemblyDebugUi.Place(button, x, y, width, 26); return button;
    }
    private GameObject Option(string name, Action edit) => Button(popup.transform, name, 0, 0, 246, () =>
    {
        if (!Open || epoch != WorkroomStorage.State.Epoch) { CloseActive(); return; }
        edit();
        WorkroomAutoFillPreferences.Remember(savedKey, options, true); RenderSettings();
    });
    private TextMeshProUGUI Label(string name)
    {
        var label = AssemblyDebugUi.Text(popup.transform, name, "", font, 11, true);
        label.enableWordWrapping = true; label.alignment = TextAlignmentOptions.TopLeft; return label;
    }
    private static long Cycle(long current, long[] values) => values[(Array.IndexOf(values, current) + 1) % values.Length];
    private static void Set(GameObject button, string text) => button.GetComponentInChildren<TextMeshProUGUI>().text = text;
    private int DefaultCount(string kind) => machine && family == "motherboard" && kind == "ssd" ? 0 : 1;
    private void Load()
    {
        EnsurePopup();
        epoch = WorkroomStorage.State.Epoch; options = WorkroomAutoFillPreferences.Get(savedKey);
        if (!machine)
        {
            if (family != "gpu") options.Vram = 0;
            if (family != "ram") options.Ram = 0;
            if (family != "ssd") options.Disk = 0;
        }
    }
    private void Toggle()
    {
        if (Open) { CloseActive(); return; }
        if (!owner.Open || (Il2Cpp.ItemMouseDragHandler.current?.isDragging ?? false)) return;
        CloseActive(); Load(); Show();
    }
    private void Show()
    {
        active = this; pointerOwned = true; neutralFrames = 0;
        layer.SetActive(true); layer.transform.SetAsLastSibling(); owner.Root.transform.SetAsLastSibling();
        WorkroomAssemblyWindow.PreviewSlot = null; RenderSettings();
    }
    private void RenderSettings()
    {
        Set(minimum, WorkroomAutoFill.Text("minimum", options.Minimum)); Set(maximum, WorkroomAutoFill.Text("maximum", options.Maximum));
        Set(preference, WorkroomAutoFill.Text(options.Performance ? "performance" : "economic"));
        Set(quantity, WorkroomAutoFill.Text(new[] { "quantity_min", "quantity_full", "quantity_custom" }[options.Quantity]));
        Set(source, WorkroomAutoFill.Text(new[] { "source_both", "source_room", "source_box" }[options.Source]));
        Set(vram, WorkroomAutoFill.Text("capacity", WorkroomAutoFill.Text("vram"), options.Vram));
        Set(ram, WorkroomAutoFill.Text("capacity", WorkroomAutoFill.Text("ram"), options.Ram));
        Set(disk, WorkroomAutoFill.Text("capacity", WorkroomAutoFill.Text("disk"), options.Disk));
        AssemblyDebugUi.Place(minimum, 12, 10, 246, 26); AssemblyDebugUi.Place(maximum, 270, 10, 246, 26);
        AssemblyDebugUi.Place(preference, 12, 42, 246, 26); AssemblyDebugUi.Place(quantity, 270, 42, 246, 26);
        AssemblyDebugUi.Place(source, 12, 74, 504, 26);
        var extra = new List<GameObject>();
        vram.SetActive(machine || family == "gpu"); ram.SetActive(machine || family == "ram"); disk.SetActive(machine || family == "ssd");
        foreach (var item in new[] { vram, ram, disk }) if (item.activeSelf) extra.Add(item);
        var kinds = WorkroomAutoFill.Kinds(family, machine);
        for (var i = 0; i < counts.Count; i++)
        {
            counts[i].SetActive(options.Quantity == 2);
            if (options.Quantity != 2) continue;
            Set(counts[i], WorkroomAutoFill.Text("count", WorkroomAutoFill.Name(family, machine, kinds[i]), options.Counts.GetValueOrDefault(kinds[i], DefaultCount(kinds[i]))));
            extra.Add(counts[i]);
        }
        for (var i = 0; i < extra.Count; i++) AssemblyDebugUi.Place(extra[i], 12 + i % 2 * 258, 106 + i / 2 * 32, 246, 26);
        var y = 106 + (extra.Count + 1) / 2 * 32;
        AssemblyDebugUi.Place(hint.gameObject, 12, y, 504, 34); y += 38;
        AssemblyDebugUi.Place(popup, 176, 390 - y, 528, y);
        var text = FontText + string.Join("", counts.Select(b => b.GetComponentInChildren<TextMeshProUGUI>().text));
        AssemblyDebugFonts.Prepare(text, text);
    }
    private void Fill()
    {
        if (!owner.Open) return;
        if (!Open) Load();
        if (epoch != WorkroomStorage.State.Epoch) { CloseActive(); return; }
        // Missing stock does not veto compatible placements into the other slots.
        // Keep the missing requirements visible after committing a partial plan.
        var plan = WorkroomAutoFill.Preview(family, machine, epoch, options);
        if (plan.CanFill && WorkroomAutoFill.Apply(plan, true))
        {
            if (plan.Complete) { CloseActive(); return; }
            WorkroomStorage.NotifyText(WorkroomSelection.Text("partial_filled", plan.Added.Count) + "\n" + string.Join("\n", plan.Messages));
            Show(); return;
        }
        var message = plan.CanFill ? WorkroomAutoFill.Text("changed") : plan.Complete ? WorkroomAutoFill.Text("none") :
            WorkroomAutoFill.Text("incomplete") + "\n" + string.Join("\n", plan.Messages);
        WorkroomStorage.NotifyText(message); Show();
    }
    internal static bool CloseActive()
    {
        var current = active; if (current == null) return false;
        active = null; pointerOwned = true; neutralFrames = 0; inputFrame = Time.frameCount;
        WorkroomUiCleanup.Deactivate(current.layer); return true;
    }
    internal static void CloseFor(WorkroomAssemblyWindow window)
    {
        if (active != null && ReferenceEquals(active.owner, window)) CloseActive();
    }
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
    internal static bool AllowsButton(GameObject button) => !OwnsInput ||
        (active != null && button.transform.IsChildOf(active.layer.transform));
    internal static void Release() { CloseActive(); pointerOwned = false; neutralFrames = 0; inputFrame = -1; }
}
