using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes;

namespace PCExpansion;

/// <summary>江白的两种原生容器；内容在生成时固定，原版物品图负责保存与转手。</summary>
internal static class SecuritySeizureBoxes
{
    internal const string LargeId = "pcrepair.security_seizure_large";
    internal const string SmallId = "pcrepair.security_seizure_small";
    internal const string LargeSpriteKey = "pcrepair.security_seizure_large_sprite";
    internal const string SmallSpriteKey = "pcrepair.security_seizure_small_sprite";
    internal const string SealedTag = "PCREPAIR_SECURITY_SEALED";
    private const string FilledTag = "PCREPAIR_SECURITY_FILLED";
    // 以冷却后、无议价加成的配件基础现金估值校准；模拟均值约 760/258，目标约九成回款。
    private const int LargePrice = 850;
    private const int SmallPrice = 290;
    private static readonly Components.Type[] EligibleTypes = Components.All.Where(type =>
        type.Tag != Components.CaseTag && type.Tag != Components.MotherboardTag).ToArray();

    internal static bool IsBox(GameItem? item) => item != null &&
        Core.Clean(item.identifier) is LargeId or SmallId;

    private static void SetContentsOwned(Il2CppObjectBase? node, bool owned)
    {
        if (node == null) return;
        var graph = node.TryCast<GraphNodeStorage>();
        if (graph?.children == null) return;
        foreach (var child in graph.children)
        {
            if (child == null) continue;
            var item = child.TryCast<GameItem>();
            if (item != null) GeneralHelper.SetItemOwned(item, owned);
            SetContentsOwned(child, owned);
        }
    }

    private static GameItem Create(bool large)
    {
        var item = DirectoryMaster.Item("common_electronic", true)
            ?? throw new InvalidOperationException("找不到收缴盒物品模板。");
        try
        {
            // 创建内容窗口的尺寸直接决定可用格数；物品外形独立设置。
            var width = large ? 10 : 5;
            var height = 3;
            var content = DirectoryUtils.CreateInventoryWindow(width, height, true);
            item.SetContentWindow(content.Item1);
            Apply(item, large);
            // DirectoryMaster.Item 会清空工厂返回物品的所有子物品；这里只建立空容器。
            ContainerHelper.InitContainerItem(content.Item2, item);
            item.onLoaded = (Il2CppSystem.Action<GameItem>)(loaded => Apply(loaded, large));
            return item;
        }
        catch
        {
            WorkroomItemCodec.Destroy(new WorkroomItemCodec.Candidate
            { Root = item, Nodes = new List<GameItem> { item } });
            throw;
        }
    }

    private static GameInventory GetInventory(GameItem item)
    {
        var children = item.contentWindow?.TryCast<GraphNodeStorage>()?.children;
        if (children != null)
            foreach (var child in children)
            {
                var inventory = child?.TryCast<GameInventory>();
                if (inventory != null) return inventory;
            }
        throw new InvalidOperationException("收缴盒内容库存不存在。");
    }

    private static void Populate(GameItem item)
    {
        var inventory = GetInventory(item);
        if (inventory.childItems.Count != 0)
            throw new InvalidOperationException("收缴盒装填前库存非空，禁止覆盖原有物品。");
        var contents = Fill(inventory, Core.Clean(item.identifier) == LargeId);
        try
        {
            item.EnableTag(FilledTag, false);
            item.EnableTag(SealedTag, false);
        }
        catch
        {
            foreach (var child in contents)
                try { inventory.Expel(child); child.Destroy(); }
                catch (Exception ex) { Core.Log?.Warning("回滚收缴盒内部物品失败：" + ex.Message); }
            throw;
        }
    }

    // NPC 商品工厂返回以后、原生清空子物品的校验结束以后，才固定随机内容。
    internal static void PrepareStock(GameItem item)
    {
        if (!IsBox(item) || SaveGeneration.Decoding || WorkroomItemCodec.Restoring)
            throw new InvalidOperationException("收缴盒装填时机无效。");
        if (item.IsTag(FilledTag))
            throw new InvalidOperationException("收缴盒重复装填。");
        Populate(item);
    }

    private static void Apply(GameItem item, bool large)
    {
        item.identifier = large ? LargeId : SmallId;
        var name = LanguageText.Get(large ? "security.box.large.name" : "security.box.small.name");
        item.identifierName = name;
        item.SetName(name);
        var description = LanguageText.Get(large ? "security.box.large.description" : "security.box.small.description");
        item.shortDescription = description;
        item.longDescription = description;
        item.flavorText = LanguageText.Get(large ? "security.box.large.flavor" : "security.box.small.flavor");
        item.unitCount = 1;
        item.SetValue(item.IsTag(SealedTag) || !item.IsTag(FilledTag) ?
            large ? LargePrice : SmallPrice : 0);
        item.SetShape(new GridShapeBuilder(large ? 10 : 5, 3).SetDataFill(1).Build());
        item.spritePath = large ? LargeSpriteKey : SmallSpriteKey;
        item.spriteAtlasPath = string.Empty;
        item.spriteChanged = true;
        item.RemoveAllGameItemType();
        item.SetGameItemType("STORAGE");
        // 原版容器过滤在首次装填以后绑定，之后沿用原版可收纳与套盒限制。
    }

    private static Random SeededRandom(bool large)
    {
        var store = PlayerStore.Instance;
        if (store == null || string.IsNullOrEmpty(store.runID))
            throw new InvalidOperationException("收缴盒生成时存档身份不可读。");
        var source = "PCREPAIR_SECURITY_BOX_V1\n" + store.runID + "\n" +
            store.saveSlotId.ToString(CultureInfo.InvariantCulture) + "\n" +
            StoreStation.GetDayCounter().ToString(CultureInfo.InvariantCulture) + "\n" +
            (large ? "large" : "small");
        return new Random(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(source)), 0));
    }

    private static int RollTier(Random rng, bool large)
    {
        // 权重分母 300：大盒 T5 2%，小盒 T4 5%，削掉的概率均分给前三档。
        var roll = rng.Next(300);
        if (large) return roll < 128 ? 2 : roll < 226 ? 3 : roll < 294 ? 4 : 5;
        return roll < 125 ? 1 : roll < 220 ? 2 : roll < 285 ? 3 : 4;
    }

    private static List<GameItem> Fill(GameInventory inventory, bool large)
    {
        var rng = SeededRandom(large);
        var minimum = large ? 9 : 6;
        var maximum = large ? 21 : 10;
        var target = rng.Next(minimum, maximum + 1);
        var used = 0;
        var placed = new List<GameItem>();
        try
        {
            // 各类每轮先等权洗牌；无法放进剩余格数或实际库存时重选。
            for (var round = 0; round < 10 && used < maximum; round++)
            {
                var choices = EligibleTypes.OrderBy(_ => rng.Next()).ToArray();
                var added = false;
                foreach (var type in choices)
                {
                    var cells = type.Width * type.Height;
                    if (used + cells > maximum) continue;
                    var tier = RollTier(rng, large);
                    var id = "pcrepair." + type.Stem + "_t" + tier;
                    var part = Components.Create(id);
                    var retained = false;
                    try
                    {
                        // 原版寻位会尝试旋转；小盒中的 2×4 显卡因而以 4×2 放入。
                        var marker = inventory.TryFindOneValidInventorySlot(part);
                        if (marker == null || !marker.IsValid() || marker.targetItem != null) continue;
                        if (marker.TryAcceptOnce(-1) != 1) continue;
                        retained = true;
                        placed.Add(part);
                        if (!WorkroomStorage.Contains(inventory, part))
                            throw new InvalidOperationException("收缴配件未留在盒内。");
                        StolenHelper.InitStolenItem(part, rng.Next(10, 51));
                        used += cells;
                        added = true;
                        break;
                    }
                    finally
                    {
                        if (!retained) part.Destroy();
                    }
                }
                if (!added) break;
                if (used >= minimum && used >= target) break;
            }
            if (used < minimum || used > maximum)
                throw new InvalidOperationException("收缴盒装填未达到占格范围：" + used);
            Core.Log?.Msg("[治安收缴] " + (large ? "大" : "小") + "盒已固定内容：" +
                placed.Count + " 件，占 " + used + "/" + (large ? 30 : 15) + " 格。");
            return placed;
        }
        catch
        {
            foreach (var part in placed)
                try { inventory.Expel(part); part.Destroy(); }
                catch (Exception ex) { Core.Log?.Warning("清理收缴盒半成品失败：" + ex.Message); }
            throw;
        }
    }

    [HarmonyPatch(typeof(ModItemDirectory), "InitDirectory")]
    internal static class DirectoryPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ModItemDirectory __instance)
        {
            __instance.Set(LargeId, (Il2CppSystem.Func<GameItem>)(Func<GameItem>)(() => Create(true)));
            __instance.Set(SmallId, (Il2CppSystem.Func<GameItem>)(Func<GameItem>)(() => Create(false)));
            Core.Log?.Msg("已注册江白收缴配件盒。");
        }
    }

    [HarmonyPatch(typeof(ItemMouseDoubleClickHandler), nameof(ItemMouseDoubleClickHandler.OpenContentAction))]
    internal static class OpenGuardPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(GameItem newItem, out bool __state)
        {
            __state = false;
            if (!IsBox(newItem)) return true;
            if (!GeneralHelper.IsItemOwned(newItem))
            {
                CaseUnboxing.Notify(LanguageText.Get("security.box.not_owned"));
                return false;
            }
            if (!newItem.IsTag(FilledTag)) return false;
            __state = newItem.IsTag(SealedTag);
            return true;
        }

        private static void Postfix(GameItem newItem, bool __state)
        {
            if (!__state || !IsBox(newItem)) return;
            var window = newItem.contentWindow?._handler_k__BackingField;
            if (window?.gameObject?.activeSelf != true) return;
            newItem.DisableTag(SealedTag, false);
            newItem.SetValue(0);
        }
    }

    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetCurrentChildValue))]
    internal static class HiddenValuePatch
    {
        private static bool Prefix(GameItem __instance, ref int __result)
        {
            if (!IsBox(__instance) || !__instance.IsTag(SealedTag)) return true;
            __result = 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(ContainerHelper), nameof(ContainerHelper.CreateContainerTooltip))]
    internal static class HiddenTooltipPatch
    {
        private static bool Prefix(GameItem item) => !IsBox(item) || !item.IsTag(SealedTag);
    }

    [HarmonyPatch(typeof(GeneralHelper), nameof(GeneralHelper.SetItemOwned))]
    internal static class ContentOwnershipPatch
    {
        private static void Postfix(GameItem item, bool own)
        {
            if (!IsBox(item)) return;
            try { SetContentsOwned(item.contentWindow, own); }
            catch (Exception ex) { Core.Log?.Error("收缴盒内部归属同步失败：" + ex); }
        }
    }
}
