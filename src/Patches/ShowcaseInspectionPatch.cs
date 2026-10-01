using System;
using HarmonyLib;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>原版检查先读取箱体状态；展示窗买家候选仍由随后原版流程决定。</summary>
[HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.IsShowCaseContainSeriousContraband))]
internal static class ShowcaseCasePropertiesPatch
{
    private static void Prefix()
    {
        try
        {
            var items = EmporiumEntry.Instance?.showcaseElement?.childItems;
            if (items == null) return;
            foreach (var item in items)
                if (ComputerCase.IsCase(item)) CaseInteriorUI.SyncCaseForTrading(item);
        }
        catch (Exception ex) { Core.Log?.Warning("展示窗检查前同步机箱失败：" + ex.Message); }
    }
}

/// <summary>仅调整原版已排入队列的检查员，不绕过原版检查及告密者规则。</summary>
[HarmonyPatch(typeof(StoreClientManager), nameof(StoreClientManager.GenerateClient))]
internal static class ComputerVisitorInspectionPriorityPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(StoreClientManager __instance)
    {
        try
        {
            var stack = __instance.clientStack;
            if (stack == null || !ShowcaseHelper.IsShowCaseContainSeriousContraband()) return;
            var inspectorIndex = -1;
            var computerIndex = -1;
            for (var i = 0; i < stack.Count; i++)
            {
                var client = stack[i];
                if (client == null) continue;
                if (Core.Clean(client.identifier) == "securityInspector" && inspectorIndex < 0) inspectorIndex = i;
                if (computerIndex < 0 && (UpperComputerBuyerNpc.IsBuyer(client) || LowerAssemblerNpc.IsLowerAssembler(client)))
                    computerIndex = i;
            }
            if (inspectorIndex <= 0 || computerIndex < 0 || computerIndex > inspectorIndex) return;
            var inspector = stack[inspectorIndex];
            stack.RemoveAt(inspectorIndex);
            stack.Insert(0, inspector);
            Core.Debug("展示窗违禁品检查员已排在电脑商人之前。");
        }
        catch (Exception ex) { Core.Log?.Warning("同步电脑访客检查顺序失败：" + ex.Message); }
    }
}
