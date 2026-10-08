using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>电脑物品的拆装入口统一由工作间处理，阻止原版容器窗口接管。</summary>
internal static class ComputerItemInteraction
{
    internal static bool Intercept(GameItem? item)
    {
        if (ContactCard.IsCard(item)) return false;
        if (ComputerCase.IsCase(item))
        {
            CaseUnboxing.Notify(WorkroomMachineAssembly.Text("open_workroom"));
            return false;
        }
        return !Components.IsComponent(item);
    }
}

[HarmonyPatch(typeof(ItemMouseDoubleClickHandler), nameof(ItemMouseDoubleClickHandler.DoubleClickAction))]
internal static class ComputerDoubleClickPatch
{
    private static bool Prefix(GameItem newItem) => ComputerItemInteraction.Intercept(newItem);
}

[HarmonyPatch(typeof(ItemMouseDoubleClickHandler), nameof(ItemMouseDoubleClickHandler.OpenContentAction))]
internal static class ComputerOpenContentPatch
{
    private static bool Prefix(GameItem newItem) => ComputerItemInteraction.Intercept(newItem);
}

[HarmonyPatch(typeof(ItemMouseDoubleClickHandler), nameof(ItemMouseDoubleClickHandler.OpenExamineAction))]
internal static class ComputerOpenExaminePatch
{
    private static bool Prefix(GameItem newItem) => ComputerItemInteraction.Intercept(newItem);
}

/// <summary>禁止原版把 Mod 机箱当作嵌套容器，避免吞物品或改成储藏区贴图。</summary>
[HarmonyPatch(typeof(ContainerHelper), nameof(ContainerHelper.IsNestableDevice))]
internal static class CaseNestableGuardPatch
{
    private static void Postfix(GameItem item, ref bool __result)
    {
        if (__result && ComputerCase.IsCase(item)) __result = false;
    }
}
