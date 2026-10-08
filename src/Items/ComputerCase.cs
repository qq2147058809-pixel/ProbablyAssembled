using Il2Cpp;

namespace PCExpansion;

/// <summary>
/// 机箱物品的判断辅助。具体机箱物品（5 级 × 完好/损坏）由 Components 统一注册，
/// 全部携带 PCREPAIR_CASE 标签，因此按标签识别即可覆盖所有分级与状态。
/// </summary>
internal static class ComputerCase
{
    internal static bool IsCase(GameItem? item) =>
        item != null && item.IsTag(Components.CaseTag);
}
