using System;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>Use ownership rather than inventory location for mod tools and repairs.</summary>
internal static class PlayerItemAccess
{
    /// <summary>返回处于玩家自有库存中的物品容器；不依赖当前是否有客人或报价。</summary>
    internal static GameInventory? FindOwnedInventory(GameItem? item)
    {
        if (item == null) return null;
        try
        {
            // 容器位置本身不能证明所有权：NPC 库存也可能出现在交易界面。
            // 使用游戏原生所有权判断，未付款的 NPC 商品必须拒绝访问。
            if (!IsDirectlyOwned(item)) return null;
            var inventory = item.parentInventory;
            if (inventory == null) return null;
            var children = inventory.childItems;
            for (var i = 0; i < children.Count; i++)
                if (children[i] != null && children[i].Pointer == item.Pointer) return inventory;
        }
        catch (Exception ex)
        {
            Core.Debug("检查玩家物品所在容器失败：" + ex.Message);
        }
        return null;
    }

    internal static bool IsDirectlyOwned(GameItem? item)
    {
        if (item == null) return false;
        try { return GeneralHelper.IsItemOwned(item); }
        catch (Exception ex)
        {
            Core.Debug("检查物品原生所有权失败：" + ex.Message);
            return false;
        }
    }

    internal static bool IsOwned(GameItem? item)
    {
        if (item == null) return false;
        try
        {
            // Our detached UI slots are not native descendants of the case. Their
            // contents follow the case's ownership, including reconstructed loot.
            var containingCase = CaseInteriorUI.FindContainingCase(item);
            return GeneralHelper.IsItemEffectivelyOwned(containingCase ?? item);
        }
        catch (Exception ex)
        {
            Core.Log?.Warning("检查物品所有权失败：" + ex.Message);
            return false;
        }
    }
}
