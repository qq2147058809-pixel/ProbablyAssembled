using HarmonyLib;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>双击拦截：机箱 → 打开/切换原生窗口；机箱界面开着时双击配件 → 直接插入对应空槽。</summary>
internal static class CaseOpenHelper
{
    internal static bool Intercept(GameItem? item)
    {
        if (ContactCard.IsCard(item))
        {
            // 名片是展示物品，不提供双击打开行为，也不进入原版便笺查看器。
            return false;
        }

        // 拆机螺丝刀：双击解锁背包里上锁的物资箱。
        if (UnboxTool.IsUnboxTool(item))
        {
            CaseUnboxing.TryUnlockLockedCaseInInventory(item);
            return false;
        }

        if (ComputerCase.IsCase(item))
        {
            // 未解锁的物资箱：双击不开面板（用拆机螺丝刀解锁后才能互动）。
            if (item!.IsTag(CaseUnboxing.LockedTagName))
            {
                CaseUnboxing.NotifyClosedCase();
                return false;
            }
            if (CaseInteriorUI.Open(item!))
            {
                PlayCaseOpenSound(item!);
                Core.Log?.Msg("[深空装机] 打开机箱内部：" + Core.Clean(item?.identifier));
            }
            return false;
        }

        if (Components.IsComponent(item))
        {
            // 配件只可通过机箱面板插入，不调用原版“使用物品”行为；面板未开或无法插入时也消费双击。
            if (CaseInteriorUI.IsOpen) CaseInteriorUI.TryInsertComponent(item);
            return false;
        }
        return true;
    }

    private static void PlayCaseOpenSound(GameItem item)
    {
        try
        {
            var audio = AudioManager.Instance;
            if (audio == null)
            {
                Core.Log?.Warning("机箱已打开，但 AudioManager.Instance 为空，开盖音效未播放。");
                return;
            }

            var sound = item.soundContainerOpen;
            if (string.IsNullOrWhiteSpace(sound)) sound = "food_can_open";
            if (TryPlay(audio, sound) || (sound != "open_machine" && TryPlay(audio, "open_machine")))
            {
                Core.Debug("机箱开盖音效已触发：" + sound);
                return;
            }

            Core.Log?.Warning("机箱已打开，但原版开盖音效未被播放通道接受：" + sound);
        }
        catch (System.Exception ex)
        {
            Core.Log?.Warning("播放机箱开盖音效失败：" + ex.Message);
        }
    }

    private static bool TryPlay(AudioManager audio, string sound) =>
        audio.Play(sound) || audio.Play2(sound) || audio.Play3(sound);
}

[HarmonyPatch(typeof(ItemMouseDoubleClickHandler), nameof(ItemMouseDoubleClickHandler.DoubleClickAction))]
internal static class CaseDoubleClickPatch
{
    private static bool Prefix(GameItem newItem)
    {
        Core.Debug("[诊断] 双击触发：" + Core.Clean(newItem?.identifier));
        return CaseOpenHelper.Intercept(newItem);
    }
}

[HarmonyPatch(typeof(ItemMouseDoubleClickHandler), nameof(ItemMouseDoubleClickHandler.OpenContentAction))]
internal static class CaseOpenContentPatch
{
    private static bool Prefix(GameItem newItem) => CaseOpenHelper.Intercept(newItem);
}

[HarmonyPatch(typeof(ItemMouseDoubleClickHandler), nameof(ItemMouseDoubleClickHandler.OpenExamineAction))]
internal static class CaseOpenExaminePatch
{
    private static bool Prefix(GameItem newItem) => CaseOpenHelper.Intercept(newItem);
}

/// <summary>
/// 机箱不参与原版"可嵌套设备"行为：否则拖拽配件到机箱上会被原版当作放入容器，
/// 吞掉配件并把机箱切成储藏区贴图。开箱交互由上面的双击补丁负责。
/// </summary>
[HarmonyPatch(typeof(ContainerHelper), nameof(ContainerHelper.IsNestableDevice))]
internal static class CaseNestableGuardPatch
{
    private static void Postfix(GameItem item, ref bool __result)
    {
        if (__result && ComputerCase.IsCase(item))
        {
            __result = false;
        }
    }
}
