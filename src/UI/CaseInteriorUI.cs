using System;
using System.Collections.Generic;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>
/// 每台机箱持有独立原版机器窗口和十二个 GameSlotInventory。
/// 布局为回滚后的最简紧凑排列：无文字标签，识别靠悬停提示与物品形状。
/// 已实测 GridPixelElement 规则：元素占 1 格不跨格；列宽 = 列内最宽元素；
/// 空列塌缩；基准格 16px；窗口必须显式给尺寸，否则默认 ~256px 会压扁网格。
/// </summary>
internal static class CaseInteriorUI
{
    private static string WindowTitle => LanguageText.Get("电脑机箱 · 内部", "PC Case · Interior");

    // 布局（用户 2026-09-29 口述）：风扇/电源在左列，主板不动，CPU/散热器在主板右边，
    // 电源下方四根内存从左到右排列，显卡接内存行末端，硬盘在内存正下方，测试按钮右下角。
    // 实测规则补充：网格实际宽度 = 最宽一行的元素宽度之和；本布局最宽行为内存+显卡行
    // （32+16+64+32+32 = 176），所有元素都在该宽度内，天然零压缩。
    internal static readonly (string Tag, string Name, int Col, int Row, int W, int H)[] SlotTable =
    {
        (Components.FanTag, LanguageText.Get("散热风扇", "Case Fan"), 1, 1, 32, 32),
        (Components.PsuTag, LanguageText.Get("电源", "Power Supply"), 1, 2, 32, 32),
        (Components.MotherboardTag, LanguageText.Get("主板", "Motherboard"), 3, 1, 64, 64),
        (Components.CpuTag, "CPU", 4, 1, 32, 32),
        (Components.CoolerTag, LanguageText.Get("CPU散热器", "CPU Cooler"), 5, 1, 32, 32),
        (Components.RamTag, LanguageText.Get("内存条", "Memory Stick"), 1, 3, 16, 48),
        (Components.RamTag, LanguageText.Get("内存条", "Memory Stick"), 2, 3, 16, 48),
        (Components.RamTag, LanguageText.Get("内存条", "Memory Stick"), 3, 3, 16, 48),
        (Components.RamTag, LanguageText.Get("内存条", "Memory Stick"), 4, 3, 16, 48),
        (Components.GpuTag, LanguageText.Get("显卡", "Graphics Card"), 5, 3, 32, 64),
        (Components.HddTag, LanguageText.Get("硬盘", "Hard Drive"), 3, 4, 48, 16),
        (Components.HddTag, LanguageText.Get("硬盘", "Hard Drive"), 3, 5, 48, 16),
    };

    private const int Columns = 6;   // c1 风扇/电源/内存/硬盘，c2 内存，c3 主板/内存/硬盘，c4 CPU/内存，c5 散热器/显卡/按钮
    private const int Rows = 7;      // 6 号行最下：按钮行

    private static readonly Dictionary<IntPtr, CaseWindow> Windows = new();
    private static IntPtr currentCase;

    internal static bool IsOpen => currentCase != IntPtr.Zero;

    /// <summary>目标仍在任一机箱的可见配件槽内时视为未从机箱取出。</summary>
    internal static bool IsItemInsideAnyCase(GameItem item) => FindContainingCase(item) != null;

    internal static GameItem? FindContainingCase(GameItem item)
    {
        try
        {
            foreach (var window in Windows.Values)
            {
                foreach (var entry in window.SlotEntries)
                {
                    var child = entry.Slot.childItem;
                    if (child != null && child.Pointer == item.Pointer) return window.CaseItem;
                }
            }
        }
        catch (Exception ex)
        {
            Core.Debug("检查配件是否仍在机箱内失败：" + ex.Message);
        }
        return null;
    }

    /// <summary>
    /// 检查可见槽位以及保存于机箱标签中的物品。后者覆盖尚未开箱、或关闭面板后的物资箱内容。
    /// </summary>
    internal static bool HasAnyStoredContents(GameItem caseItem)
    {
        try
        {
            if (Windows.TryGetValue(caseItem.Pointer, out var window))
            {
                foreach (var entry in window.SlotEntries)
                    if (entry.Slot.childItem != null) return true;
            }

            for (var slotIndex = 0; slotIndex < SlotTable.Length; slotIndex++)
            {
                var slotType = SlotTable[slotIndex].Tag;
                foreach (var type in Components.All)
                {
                    if (type.IsCase || type.Tag != slotType) continue;
                    for (var tier = 1; tier <= Components.TierCount; tier++)
                    {
                        var baseId = "pcrepair." + type.Stem + "_t" + tier;
                        if (caseItem.IsTag(SlotTagPrefix + slotIndex + "_" + baseId) ||
                            caseItem.IsTag(SlotTagPrefix + slotIndex + "_" + baseId + "_broken"))
                            return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Fail closed: if case contents cannot be inspected, don't risk erasing them in repair.
            Core.Log?.Warning("检查机箱维修前内容失败，已阻止维修：" + ex.Message);
            return true;
        }
        return false;
    }

    internal static bool Open(GameItem item)
    {
        try
        {
            if (!PlayerItemAccess.IsDirectlyOwned(item) ||
                (!PlayerItemAccess.IsOwned(item) && PlayerItemAccess.FindOwnedInventory(item) == null))
            {
                CaseUnboxing.Notify(LanguageText.Get("请先购买机箱，再打开内部面板", "Buy the PC case before opening its interior."));
                return false;
            }
            var nextCase = item.Pointer;
            // 多机箱面板可同时开启：打开新面板时不再隐藏上一个（各自独立收尾）。

            // 缓存窗口可能已被游戏销毁（关窗/跨天/切场景）——失效就重建。
            // 新建的窗口必须显式 Show（旧版缺陷：只在缓存复用分支里有 Show，
            // 导致第一次双击建窗后不可见、第二次才出现）。
            CaseWindow window;
            if (Windows.TryGetValue(nextCase, out var existing) && TryShow(existing))
            {
                window = existing;
            }
            else
            {
                Windows.Remove(nextCase);
                window = BuildWindow(item);
                if (!TryShow(window))
                {
                    Windows.Remove(nextCase);
                    Core.Log?.Warning("机箱窗口创建后显示失败，将在下次双击时重建。");
                    return false;
                }
            }

            currentCase = nextCase;
            Core.Debug("[深空装机] 已打开原生机箱窗口");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log?.Error("打开原生机箱窗口失败：" + ex);
            return false;
        }
    }

    /// <summary>隐藏窗口；窗口已被游戏销毁时静默忽略。</summary>
    private static bool TryHide(CaseWindow window)
    {
        try
        {
            window.Window.Hide();
            OnWindowClosed(window);
            return true;
        }
        catch (Exception ex)
        {
            Core.Debug("旧窗口已失效，跳过隐藏：" + ex.Message);
            return false;
        }
    }

    /// <summary>显示缓存窗口；已失效返回 false，由调用方重建。</summary>
    private static bool TryShow(CaseWindow window)
    {
        try
        {
            // Native Show() resets anchoredPosition even for a cached window.
            // A second double-click on an already visible window must remember
            // its current dragged position before that reset occurs.
            if (window.IsVisible) RememberWindowPosition(window);
            window.Window.SetTitle(WindowTitle);
            window.Window.Show();
            window.IsVisible = true;
            RestoreWindowPosition(window);
            // Hide/Show 循环后按钮回调可能失效，显示时重新绑定。
            if (window.TestButton != null && window.TestAction != null)
                window.TestButton.SetCallback(window.TestAction);
            return true;
        }
        catch (Exception ex)
        {
            Core.Debug("缓存窗口已失效，将重建：" + ex.Message);
            return false;
        }
    }

    internal static void Close()
    {
        if (currentCase != IntPtr.Zero &&
            Windows.TryGetValue(currentCase, out var window))
        {
            TryHide(window);
        }
        SelectVisibleCase();
    }

    private static void SelectVisibleCase()
    {
        currentCase = IntPtr.Zero;
        foreach (var pair in Windows)
            if (pair.Value.IsVisible) currentCase = pair.Key;
    }

    // 原版卖出物品会调用 CloseContentWindow；自建窗口也必须关闭并释放缓存，
    // 否则已售出的整机仍可通过旧窗口取件。
    internal static void CloseAndForgetCase(GameItem caseItem)
    {
        if (!Windows.TryGetValue(caseItem.Pointer, out var window)) return;
        if (window.IsVisible) TryHide(window);
        else
        {
            SyncSlotsToTags(caseItem, window);
            CaseEconomy.EvaluateCase(caseItem, window.SlotEntries);
        }
        window.IsVisible = false;
        Windows.Remove(caseItem.Pointer);
        SelectVisibleCase();
    }

    internal static void SyncAllCasesBeforeSave()
    {
        foreach (var window in new List<CaseWindow>(Windows.Values))
        {
            if (window.IsVisible) RememberWindowPosition(window);
            SyncSlotsToTags(window.CaseItem, window);
            CaseEconomy.EvaluateCase(window.CaseItem, window.SlotEntries);
        }
    }

    internal static void DiscardWindowCache()
    {
        var previousWindows = new List<CaseWindow>(Windows.Values);
        Windows.Clear();
        currentCase = IntPtr.Zero;
        foreach (var window in previousWindows)
        {
            // 先使回调失效；读另一个存档时不能把旧窗状态写到新档对象。
            window.IsVisible = false;
            try { window.Window.Hide(); }
            catch (Exception ex) { Core.Debug("释放旧机箱窗口：" + ex.Message); }
        }
    }

    /// <summary>交易前同步已打开机箱的实际槽位，避免收购判断读取旧装配标签。</summary>
    internal static void SyncCaseForTrading(GameItem caseItem)
    {
        if (Windows.TryGetValue(caseItem.Pointer, out var window) && window.IsVisible)
        {
            SyncSlotsToTags(caseItem, window);
            CaseEconomy.EvaluateCase(caseItem, window.SlotEntries);
            return;
        }
        CaseEconomy.EvaluateCase(caseItem);
    }

    /// <summary>
    /// 机箱界面打开时，把玩家背包或报价格里的配件直接放进对应空槽。
    /// 返回 false 表示未接管（未打开、非配件、槽位已满或转移失败）。
    /// </summary>
    internal static bool TryInsertComponent(GameItem? item)
    {
        if (currentCase == IntPtr.Zero || !Windows.TryGetValue(currentCase, out var window) ||
            !window.IsVisible ||
            !PlayerItemAccess.IsDirectlyOwned(window.CaseItem) ||
            (!PlayerItemAccess.IsOwned(window.CaseItem) &&
             PlayerItemAccess.FindOwnedInventory(window.CaseItem) == null)) return false;
        var tag = Components.GetTag(item);
        if (tag == null) return false;

        foreach (var entry in window.SlotEntries)
        {
            if (entry.Tag != tag) continue;
            if (entry.Slot.childItem != null) continue;

            var source = FindComponentSource(item);
            if (source == null) return false;

            if (!source.Expel(item!))
            {
                Core.Log?.Warning("双击插入失败：无法从背包取出配件。");
                return false;
            }
            if (!entry.Slot.UncheckedAccept(item))
            {
                // 转移失败必须把配件放回背包，避免凭空消失。
                var back = source.TryFindOneValidInventorySlot(item);
                if (back == null || back.AcceptUnchecked() <= 0)
                    Core.Log?.Error("配件既放不进插槽也放不回背包：" + Core.Clean(item!.identifier));
                return false;
            }

            source.Validate();
            entry.Slot.Validate();
            PlayComponentInsertSound(item!);
            Core.Debug("双击插入配件：" + entry.Name);
            return true;
        }
        return false;
    }

    private static void PlayComponentInsertSound(GameItem item)
    {
        try
        {
            var audio = AudioManager.Instance;
            var sound = string.IsNullOrWhiteSpace(item.soundDragEnd) ? "module_drop" : item.soundDragEnd;
            if (audio == null || !(audio.Play(sound) || audio.Play2(sound) || audio.Play3(sound)))
                Core.Log?.Warning("配件已装入机箱，但原版模组放置音效未播放：" + sound);
        }
        catch (Exception ex) { Core.Log?.Warning("播放配件装入音效失败：" + ex.Message); }
    }

    private static bool Contains(GameGridInventory inv, GameItem? item)
    {
        if (item == null) return false;
        var children = inv.childItems;
        for (var i = 0; i < children.Count; i++)
            if (children[i] != null && children[i].Pointer == item.Pointer) return true;
        return false;
    }

    private static GameGridInventory? FindComponentSource(GameItem? item)
    {
        if (item == null) return null;

        // parentInventory 是物品真实所在容器；直接使用它，避免依赖当前客人/报价是否已创建。
        var ownedInventory = PlayerItemAccess.FindOwnedInventory(item);
        if (ownedInventory is GameGridInventory parentGrid &&
            Contains(parentGrid, item)) return parentGrid;

        if (!PlayerItemAccess.IsDirectlyOwned(item)) return null;

        // 某些版本在 UI 刷新时会短暂丢失 parentInventory，保留两个明确的玩家格子作为回退。
        var inventory = EmporiumEntry.Instance?.invElement;
        if (inventory != null && Contains(inventory, item)) return inventory;
        var sellingTable = PlayerStore.Instance?.gridInv;
        return sellingTable != null && Contains(sellingTable, item) ? sellingTable : null;
    }

    private static CaseWindow BuildWindow(GameItem item)
    {
        var pixelWindow = new PixelWindow(true, WindowTitle);
        var grid = new GridPixelElement(Columns, Rows, false);
        grid.SetSpacing(0, 0);
        grid.justifyWidth = false;
        pixelWindow.Attach(grid.Cast<PixelElement>());

        var result = new CaseWindow(pixelWindow, item);
        var slotEntries = new List<CaseWindow.SlotEntry>();

        foreach (var def in SlotTable)
        {
            var slot = new GameSlotInventory(def.W / 16, def.H / 16, false, false);
            ContainerHelper.AllowOnlyTaggedItem(slot, def.Tag, false, false);
            slot.SetTooltipName(def.Name);
            grid.Attach(slot.Cast<PixelElement>(), def.Col, def.Row, def.W, def.H);
            slotEntries.Add(new CaseWindow.SlotEntry(def.Tag, def.Name, slot));
        }

        // 空白占位列实测不计入网格实际宽度（会导致整体错位），间隔改用距离实现，
        // 不再放置任何 spacer。
        if (!item.IsTag(Components.BrokenTag))
        {
            var button = new AdvButtonElement(
                null,
                LanguageText.Get("测试", "Test Build"),
                32,
                24,
                new Action(() => RunTest(result, item)),
                "interface_click");
            // 强引用回调，防止托管侧回收导致按钮失灵。
            result.TestButton = button;
            result.TestAction = new Action(() => RunTest(result, item));
            button.SetCallback(result.TestAction);
            grid.Attach(button.Cast<PixelElement>(), 5, 6, 32, 24);
        }

        result.SetSlots(slotEntries);
        RestoreSlotsFromTags(item, result);
        BindSlotCallbacks(item, result);
        // 关面板（含右上角 X 走原版关闭流程）时，把 12 个槽位内容写进机箱物品的
        // 标签——物品标签随原版存档保存，重进游戏后按标签重建配件。
        try
        {
            var syncedWindow = result;
            result.CloseAction = new Action(() => OnWindowClosed(syncedWindow));
            pixelWindow.closeCallback = result.CloseAction;
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("挂接关窗保存回调失败：" + ex.Message);
        }
        pixelWindow.Validate();

        // 内容总宽 32+16+64+32+32 = 176（最宽行为内存+显卡行），零压缩；
        // 仅高度需要钉死（窗口默认高度不保证够）。物资箱面板无按钮行，自动矮一截。
        var gridH = GridContentHeight();
        grid.heightPixels = gridH;
        pixelWindow.heightPixels = pixelWindow.titleHeight + gridH + pixelWindow.yPadding * 2;
        pixelWindow.Validate();

        Windows[item.Pointer] = result;
        LogLayoutDiagnostics(grid, result);
        return result;
    }

    private static void OnWindowClosed(CaseWindow window)
    {
        if (!window.IsVisible || !Windows.TryGetValue(window.CaseItem.Pointer, out var active) ||
            !ReferenceEquals(active, window)) return;
        RememberWindowPosition(window);
        window.IsVisible = false;
        SyncSlotsToTags(window.CaseItem, window);
        CaseEconomy.EvaluateCase(window.CaseItem, window.SlotEntries);
        PlayCaseCloseSound(window.CaseItem);
        if (currentCase == window.CaseItem.Pointer) SelectVisibleCase();
    }

    /// <summary>按原版物品唯一ID记录位置；不把自建面板注册为可嵌套内容容器。</summary>
    private static void RememberWindowPosition(CaseWindow window)
    {
        try
        {
            var rect = window.Window.rectTransform;
            if (rect == null) return;
            var position = rect.anchoredPosition;
            if (!float.IsFinite(position.x) || !float.IsFinite(position.y)) return;
            window.LastPosition = position;
            var store = PlayerStore.Instance;
            if (store == null) return;
            store.contentWindowPositions ??=
                new Il2CppSystem.Collections.Generic.Dictionary<int, UnityEngine.Vector2>();
            store.contentWindowPositions[window.CaseItem.GetUniqueID()] = position;
        }
        catch (Exception ex) { Core.Debug("记录机箱窗口位置失败：" + ex.Message); }
    }

    private static void RestoreWindowPosition(CaseWindow window)
    {
        try
        {
            var rect = window.Window.rectTransform;
            if (rect == null) return;
            var position = window.LastPosition;
            var savedPositions = PlayerStore.Instance?.contentWindowPositions;
            if (!position.HasValue && savedPositions != null &&
                savedPositions.TryGetValue(window.CaseItem.GetUniqueID(), out var saved))
                position = saved;
            if (position.HasValue && float.IsFinite(position.Value.x) && float.IsFinite(position.Value.y))
                rect.anchoredPosition = position.Value;
            else
                window.Window.Center();
            window.Window.FixBounds();
            window.LastPosition = rect.anchoredPosition;
        }
        catch (Exception ex) { Core.Log?.Warning("恢复机箱窗口位置失败：" + ex.Message); }
    }

    private static void PlayCaseCloseSound(GameItem item)
    {
        try
        {
            var audio = AudioManager.Instance;
            if (audio == null)
            {
                Core.Log?.Warning("机箱面板已关闭，但 AudioManager.Instance 为空，关盖音效未播放。");
                return;
            }

            var sound = item.soundContainerClose;
            if (string.IsNullOrWhiteSpace(sound))
                sound = item.IsTag(Components.BrokenTag) ? "close_machine_light" : "close_machine";
            if (audio.Play(sound) || audio.Play2(sound) || audio.Play3(sound))
                Core.Debug("机箱关盖音效已触发：" + sound);
            else
                Core.Log?.Warning("机箱面板已关闭，但原版关盖音效未被播放通道接受：" + sound);
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("播放机箱关盖音效失败：" + ex.Message);
        }
    }

    /// <summary>内容总高 = 各行最高元素之和（按钮行不少于 24px）。</summary>
    private static int GridContentHeight()
    {
        var total = 0;
        for (var row = 1; row < Rows; row++)
        {
            var height = 0;
            foreach (var def in SlotTable)
                if (def.Row == row) height = Math.Max(height, def.H);
            if (row == 6) height = Math.Max(height, 24); // 按钮行
            total += height;
        }
        return total;
    }

    private static void RunTest(CaseWindow window, GameItem caseItem)
    {
        SyncSlotsToTags(caseItem, window);
        CaseEconomy.EvaluateCase(caseItem, window.SlotEntries);

        var failure = CaseEconomy.ValidateBuild(window.SlotEntries);
        if (failure == null)
        {
            var label = CaseEconomy.MachineLabel(window.SlotEntries);
            if (label == null)
            {
                var message = LanguageText.Get("装配数据同步失败，请关闭并重新打开机箱后再检测。",
                    "Build data could not be synchronized. Close and reopen the case, then test again.");
                window.Window.SetTitle(LanguageText.Get("数据同步失败", "Sync Failed"));
                CaseUnboxing.Notify(message);
                var itemIds = new List<string>();
                foreach (var entry in window.SlotEntries)
                    if (entry.Slot.childItem != null)
                        itemIds.Add(Core.Clean(entry.Slot.childItem.identifier));
                Core.Log?.Error("[装机检测] 实时插槽通过完整性检查，但无法识别机型；配件：" +
                                string.Join(", ", itemIds));
                return;
            }
            var successType = label switch
            {
                "刀把机" => "刀把",
                "性价比机器" => "性价比",
                _ => "整机",
            };
            window.Window.SetTitle(LanguageText.Get("成功：" + successType,
                "Passed: " + (successType == "刀把" ? "Budget" : successType == "性价比" ? "Value" : "Complete")));
            Core.Debug("机箱检测通过：成功：" + successType + "。各类配件齐全，内存同档，配件跨度不超过三档。");
            return;
        }

        window.Window.SetTitle(CaseEconomy.ValidationFailureShort(failure));
        CaseUnboxing.Notify(failure);
        Core.Debug("机箱检测失败：" + failure);
    }

    /// <summary>保留原版类型过滤，通过增删回调实时刷新标签与价格；内存同档由检测校验。</summary>
    private static void BindSlotCallbacks(GameItem caseItem, CaseWindow window)
    {
        foreach (var entry in window.SlotEntries)
        {
            var slot = entry.Slot;
            Action<GameItem, GameInventory, SlotMarker> refresh = (added, inventory, marker) =>
            {
                if (!Windows.TryGetValue(caseItem.Pointer, out var active) ||
                    !ReferenceEquals(active, window)) return;
                SyncSlotsToTags(caseItem, window);
                CaseEconomy.EvaluateCase(caseItem, window.SlotEntries);
            };
            slot.onSlotAddItemEndFunc += refresh;
            slot.onSlotRemoveItemEndFunc += refresh;
        }
    }

    /// <summary>槽位内容标签前缀：PCREPAIR_S{槽位号}_{物品ID}，随机箱物品存档。</summary>
    internal const string SlotTagPrefix = "PCREPAIR_S";

    /// <summary>关面板时把槽位内容写进机箱标签（原版存档会保存物品标签）。</summary>
    private static void SyncSlotsToTags(GameItem caseItem, CaseWindow window)
    {
        try
        {
            for (var i = 0; i < window.SlotEntries.Count; i++)
            {
                var entry = window.SlotEntries[i];
                var child = entry.Slot.childItem;
                var id = child == null ? null : Core.Clean(child.identifier);
                Components.Type? slotType = null;
                foreach (var type in Components.All)
                    if (!type.IsCase && type.Tag == entry.Tag) { slotType = type; break; }
                var spec = id == null ? null : Components.Find(id);
                if (spec == null || spec.Owner != slotType) id = null;

                // Rebuild this slot's complete tag set from its live child. Relying only
                // on LastWritten can preserve stale tier/part tags from older saves or
                // windows, making live validation and sale classification disagree.
                if (slotType != null)
                {
                    for (var tier = 1; tier <= Components.TierCount; tier++)
                    {
                        var baseId = "pcrepair." + slotType.Stem + "_t" + tier;
                        foreach (var broken in new[] { false, true })
                        {
                            var candidateId = baseId + (broken ? "_broken" : string.Empty);
                            var slotTag = SlotTagPrefix + i + "_" + candidateId;
                            if (caseItem.IsTag(slotTag)) caseItem.DisableTag(slotTag, false);
                        }
                    }
                }

                if (id != null)
                {
                    caseItem.EnableTag(SlotTagPrefix + i + "_" + id, false);
                }
                window.LastWritten[i] = id;
                ComponentRepair.SaveSlotProgress(caseItem, child, i);
            }
            Core.Debug("机箱槽位已写入标签。");
        }
        catch (Exception ex)
        {
            Core.Log?.Error("机箱槽位保存失败：" + ex);
        }
    }

    /// <summary>按型号标签重建配件，并恢复保存的材料投入进度。</summary>
    private static void RestoreSlotsFromTags(GameItem caseItem, CaseWindow window)
    {
        try
        {
            for (var i = 0; i < window.SlotEntries.Count; i++)
            {
                var entry = window.SlotEntries[i];
                foreach (var type in Components.All)
                {
                    if (type.Tag != entry.Tag) continue;
                    for (var tier = 1; tier <= Components.TierCount; tier++)
                    {
                        foreach (var broken in new[] { false, true })
                        {
                            var id = "pcrepair." + type.Stem + "_t" + tier + (broken ? "_broken" : "");
                            if (!caseItem.IsTag(SlotTagPrefix + i + "_" + id)) continue;

                            var item = DirectoryMaster.Item(id, true);
                            if (item == null)
                            {
                                Core.Log?.Warning("恢复槽位配件失败，物品未注册：" + id);
                                continue;
                            }
                            ComponentRepair.RestoreSlotProgress(caseItem, item, i);
                            if (entry.Slot.UncheckedAccept(item))
                            {
                                window.LastWritten[i] = id;
                                Core.Debug("已恢复槽位配件：" + id);
                            }
                            else
                            {
                                Core.Log?.Warning("恢复槽位配件失败（槽位拒绝）：" + id);
                            }
                            break;
                        }
                        if (window.LastWritten[i] != null) break;
                    }
                    if (window.LastWritten[i] != null) break;
                }
            }
        }
        catch (Exception ex)
        {
            Core.Log?.Error("机箱槽位恢复失败：" + ex);
        }
    }

    /// <summary>输出网格实测列宽/行高，用于确认布局与设计一致。</summary>
    private static void LogLayoutDiagnostics(GridPixelElement grid, CaseWindow window)
    {
        try
        {
            var columns = new List<string>();
            for (var x = 0; x < grid.gridWidth; x++) columns.Add(x + ":" + grid.GetColumWidth(x));
            var rows = new List<string>();
            for (var y = 0; y < grid.gridHeight; y++) rows.Add(y + ":" + grid.GetRowHeight(y));
            Core.Log?.Msg("[布局诊断] 列宽 " + string.Join(" ", columns));
            Core.Log?.Msg("[布局诊断] 行高 " + string.Join(" ", rows));
            Core.Log?.Msg("[布局诊断] 网格总尺寸 " + grid.widthPixels + "x" + grid.heightPixels +
                          " 窗口尺寸 " + window.Window.widthPixels + "x" + window.Window.heightPixels);
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("布局诊断输出失败：" + ex.Message);
        }
    }

    internal sealed class CaseWindow
    {
        internal CaseWindow(PixelWindow window, GameItem caseItem)
        {
            Window = window;
            CaseItem = caseItem;
        }

        internal PixelWindow Window { get; }
        internal GameItem CaseItem { get; }
        internal Action? CloseAction { get; set; }
        internal bool IsVisible { get; set; }
        internal UnityEngine.Vector2? LastPosition { get; set; }
        internal List<SlotEntry> SlotEntries { get; private set; } = new();

        /// <summary>各槽位最近一次写入存档标签的物品 ID；null 表示空槽。</summary>
        internal string?[] LastWritten { get; private set; } = new string?[12];

        // 持强引用防止回调被托管侧回收；显示窗口时重新绑定。
        internal AdvButtonElement? TestButton { get; set; }
        internal Action? TestAction { get; set; }

        internal void SetSlots(List<SlotEntry> entries)
        {
            SlotEntries = entries;
            LastWritten = new string?[entries.Count];
        }

        internal sealed class SlotEntry
        {
            internal SlotEntry(string tag, string name, GameSlotInventory slot)
            {
                Tag = tag;
                Name = name;
                Slot = slot;
            }

            internal string Tag { get; }
            internal string Name { get; }
            internal GameSlotInventory Slot { get; }
        }
    }
}
