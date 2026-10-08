using System;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Use ownership rather than inventory location for mod tools and repairs.</summary>
internal static class PlayerItemAccess
{
    internal static bool IsOwned(GameItem? item)
    {
        if (item == null) return false;
        try
        {
            return GeneralHelper.IsItemEffectivelyOwned(item);
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("检查物品所有权失败：" + ex.Message);
            return false;
        }
    }
}
