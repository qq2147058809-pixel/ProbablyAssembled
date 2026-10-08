using System;
using Il2Cpp;

namespace PCExpansion;

/// <summary>当前盲盒的固定抽取顺序；不包含窗口或机器装配槽位布局。</summary>
internal static class CaseContents
{
    // 重复类别代表独立抽取次数，不能套用机器装配的槽位上限。
    internal static readonly string[] SlotTags =
    {
        Components.FanTag, Components.PsuTag, Components.MotherboardTag,
        Components.CpuTag, Components.CoolerTag,
        Components.RamTag, Components.RamTag, Components.RamTag, Components.RamTag,
        Components.GpuTag, Components.HddTag, Components.HddTag,
    };

    internal static bool HasAnyStoredContents(GameItem item)
    {
        try
        {
            if (WorkroomMachineFacts.Read(item) is { } machine && machine.PartCount > 0) return true;
            return WorkroomCrateContents.Read(item).Count != 0;
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("检查机箱维修前内容失败，已阻止维修：" + ex.Message);
            return true;
        }
    }
}
