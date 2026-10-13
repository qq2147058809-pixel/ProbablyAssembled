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
    // 回款按冷却后、无议价加成的配件基础现金估值计算；
    // 每盒 60%–170%，目标分布众数 100%、均值 110%。
    private const int LargePrice = 850;
    private const int SmallPrice = 290;
    private static readonly Components.Type[] EligibleTypes = Components.All.Where(type =>
        type.Tag != Components.CaseTag && type.Tag != Components.MotherboardTag).ToArray();
    private const int MinimumReturnPercent = 60;
    private const int MaximumReturnPercent = 170;
    private const int RandomPlanAttempts = 3;
    private static readonly LeafChoice[] LargeLeafChoices = BuildLeafChoices(true);
    private static readonly LeafChoice[] SmallLeafChoices = BuildLeafChoices(false);
    private static readonly double[,] LargeLeafWays = BuildLeafWays(LargeLeafChoices, 3,
        LargePrice * MaximumReturnPercent / 100);
    private static readonly double[,] SmallLeafWays = BuildLeafWays(SmallLeafChoices, 2,
        SmallPrice * MaximumReturnPercent / 100);

    private sealed record WholeChoice(Components.Type Type, int Tier);
    private sealed record WholeCandidate(WholeChoice Choice, int Value, double Weight);
    private sealed record LeafChoice(WorkroomComponentTemplates.PartDefinition Definition,
        bool Broken, int Value, double Weight);
    private sealed record BoxPlan(List<WholeChoice> Whole, List<LeafChoice> Leaves, long Value);
    private sealed class NativePlacementException : InvalidOperationException
    {
        internal NativePlacementException() : base("江白箱子计划中的物品未能装入原生库存。") { }
    }
    private sealed class StockExhaustedException : InvalidOperationException
    {
        internal StockExhaustedException(Exception? inner) : base("江白箱子三次装箱均未完成，本次不予上柜。", inner) { }
    }

    internal static bool IsStockExhausted(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
            if (current is StockExhaustedException) return true;
        return false;
    }

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

    // 权重分母 300：大盒 T5 2%，小盒 T4 5%，削掉的概率均分给前三档。
    private static int TierWeight(bool large, int tier) => large ? tier switch
    {
        2 => 128, 3 => 98, 4 => 68, 5 => 6, _ => 0
    } : tier switch
    {
        1 => 125, 2 => 95, 3 => 65, 4 => 15, _ => 0
    };

    // First choose one of the six disassemblable whole-part families, then a
    // non-body kind and the box's original tier roll. Fixed-spec leaves use
    // the default specification for that tier.
    private static LeafChoice[] BuildLeafChoices(bool large)
    {
        var families = WorkroomComponentTemplates.All;
        var choices = new List<LeafChoice>();
        foreach (var family in families)
        {
            var kinds = family.Kinds.Where(kind => kind != "body").ToArray();
            foreach (var kind in kinds)
                for (var tier = 1; tier <= Components.TierCount; tier++)
                {
                    var tierWeight = TierWeight(large, tier);
                    if (tierWeight == 0) continue;
                    var definition = WorkroomComponentTemplates.DefaultPart(family, kind, tier);
                    var intact = checked((int)(definition.FixedValue ??
                        throw new InvalidOperationException("拆机子件缺少固定价值。")));
                    var weight = tierWeight / (300.0 * families.Count * kinds.Length * 2);
                    choices.Add(new LeafChoice(definition, false, intact, weight));
                    choices.Add(new LeafChoice(definition, true,
                        checked((int)WorkroomComponentParts.BrokenValue(intact)), weight));
                }
        }
        if (choices.Count == 0 || Math.Abs(choices.Sum(choice => choice.Weight) - 1) > 0.000001)
            throw new InvalidOperationException("江白子件权重未完整覆盖原有档位。");
        return choices.ToArray();
    }

    // Only a few one-cell parts are needed once occupancy is no longer a goal.
    // Precompute the original-weight probability of each exact top-up amount.
    private static double[,] BuildLeafWays(LeafChoice[] choices, int maxCount, int maxValue)
    {
        var ways = new double[maxCount + 1, maxValue + 1];
        ways[0, 0] = 1;
        for (var count = 1; count <= maxCount; count++)
            for (var sum = count; sum <= maxValue; sum++)
                foreach (var choice in choices)
                    if (sum >= choice.Value)
                        ways[count, sum] += choice.Weight * ways[count - 1, sum - choice.Value];
        return ways;
    }

    // A triangular target makes 100% the most common result while giving
    // a mean of 110% and rare outcomes near the 60% and 170% boundaries.
    private static int RollTargetValue(Random rng, int price)
    {
        const double low = 0.60, mode = 1.00, high = 1.70;
        var u = rng.NextDouble();
        var split = (mode - low) / (high - low);
        var ratio = u < split
            ? low + Math.Sqrt(u * (high - low) * (mode - low))
            : high - Math.Sqrt((1 - u) * (high - low) * (high - mode));
        return Math.Clamp((int)Math.Round(price * ratio),
            price * MinimumReturnPercent / 100, price * MaximumReturnPercent / 100);
    }

    // Condition the original leaf weights on the exact remaining value.
    private static List<LeafChoice> PlanLeaves(Random rng, LeafChoice[] choices,
        double[,] ways, int count, int targetValue)
    {
        if (count < 0 || count >= ways.GetLength(0) || targetValue < 0 ||
            targetValue >= ways.GetLength(1) || ways[count, targetValue] <= 0)
            throw new InvalidOperationException("江白子件金额没有可行组合。");
        var leaves = new List<LeafChoice>(count);
        var remaining = targetValue;
        for (var n = count; n > 0; n--)
        {
            var roll = rng.NextDouble() * ways[n, remaining];
            LeafChoice? selected = null;
            foreach (var choice in choices)
            {
                if (choice.Value > remaining) continue;
                var weight = choice.Weight * ways[n - 1, remaining - choice.Value];
                if (weight <= 0) continue;
                selected = choice;
                roll -= weight;
                if (roll <= 0) break;
            }
            if (selected == null)
                throw new InvalidOperationException("江白子件组合概率与回款目标不一致。");
            leaves.Add(selected);
            remaining -= selected.Value;
        }
        if (remaining != 0) throw new InvalidOperationException("江白子件组合未达到回款目标。");
        return leaves;
    }

    private static BoxPlan? PlanBox(Random rng, bool large)
    {
        var price = large ? LargePrice : SmallPrice;
        var width = large ? 10 : 5;
        var slots = width * 3;
        var minimumValue = price * MinimumReturnPercent / 100;
        var maximumValue = price * MaximumReturnPercent / 100;
        var choices = large ? LargeLeafChoices : SmallLeafChoices;
        var ways = large ? LargeLeafWays : SmallLeafWays;
        var targetValue = RollTargetValue(rng, price);
        // Use the fewest leaves that can meet this target. A single whole part
        // plus at most three/two leaves fits either native inventory size.
        for (var count = 0; count < ways.GetLength(0); count++)
        {
            var candidates = new List<WholeCandidate>();
            double totalWeight = 0;
            foreach (var type in EligibleTypes)
            {
                if (!((type.Width <= width && type.Height <= 3) ||
                    (type.Height <= width && type.Width <= 3)) ||
                    type.Width * type.Height + count > slots) continue;
                for (var tier = 1; tier <= Components.TierCount; tier++)
                {
                    var tierWeight = TierWeight(large, tier);
                    if (tierWeight == 0) continue;
                    var value = checked((int)Components.ValueFor(type, tier, false));
                    var missing = targetValue - value;
                    if (missing < 0 || missing >= ways.GetLength(1)) continue;
                    var weight = tierWeight / (300.0 * EligibleTypes.Length) * ways[count, missing];
                    if (weight <= 0) continue;
                    candidates.Add(new WholeCandidate(new WholeChoice(type, tier), value, weight));
                    totalWeight += weight;
                }
            }
            if (candidates.Count == 0) continue;
            var roll = rng.NextDouble() * totalWeight;
            var selected = candidates[^1];
            foreach (var candidate in candidates)
            {
                roll -= candidate.Weight;
                if (roll > 0) continue;
                selected = candidate;
                break;
            }
            var leaves = PlanLeaves(rng, choices, ways, count, targetValue - selected.Value);
            var total = checked(selected.Value + leaves.Sum(leaf => leaf.Value));
            if (total != targetValue || total < minimumValue || total > maximumValue ||
                selected.Choice.Type.Width * selected.Choice.Type.Height + leaves.Count > slots)
                throw new InvalidOperationException("江白箱子计划未满足回款或容积边界。");
            return new BoxPlan(new List<WholeChoice> { selected.Choice }, leaves, total);
        }
        return null;
    }

    private static void Place(GameInventory inventory, GameItem part, List<GameItem> placed,
        Random rng, long expectedValue)
    {
        var retained = false;
        try
        {
            var marker = inventory.TryFindOneValidInventorySlot(part);
            if (marker == null || !marker.IsValid() || marker.targetItem != null ||
                marker.TryAcceptOnce(-1) != 1)
                throw new NativePlacementException();
            retained = true;
            placed.Add(part);
            if (!WorkroomStorage.Contains(inventory, part))
                throw new InvalidOperationException("江白箱子物品未留在盒内。");
            StolenHelper.InitStolenItem(part, rng.Next(10, 51));
            if (part.unitValue != expectedValue)
                throw new InvalidOperationException("江白箱子物品实际价值与装填计划不一致。");
        }
        finally { if (!retained) part.Destroy(); }
    }

    private static List<GameItem> Fill(GameInventory inventory, bool large)
    {
        var rng = SeededRandom(large);
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < RandomPlanAttempts; attempt++)
        {
            var plan = PlanBox(rng, large);
            if (plan == null)
            {
                lastFailure = new InvalidOperationException("江白箱子随机计划未形成合规内容。");
                Core.Log?.Warning(lastFailure.Message);
                continue;
            }
            var placed = new List<GameItem>();
            try
            {
                foreach (var whole in plan.Whole)
                {
                    var id = "pcrepair." + whole.Type.Stem + "_t" + whole.Tier;
                    Place(inventory, Components.Create(id), placed, rng,
                        Components.ValueFor(whole.Type, whole.Tier, false));
                }
                foreach (var leaf in plan.Leaves)
                    Place(inventory, WorkroomComponentParts.Create(leaf.Definition, leaf.Broken),
                        placed, rng, leaf.Value);
                if (placed.Sum(part => (long)part.unitValue) != plan.Value)
                    throw new InvalidOperationException("江白箱子实际回款与装填计划不一致。");
                var occupied = plan.Whole.Sum(whole => whole.Type.Width * whole.Type.Height) + plan.Leaves.Count;
                Core.Log?.Msg("[治安收缴] " + (large ? "大" : "小") + "盒已固定内容：" +
                    placed.Count + " 件，占 " + occupied + "/" + (large ? 30 : 15) +
                    " 格，基础回款 " + plan.Value + "/" + (large ? LargePrice : SmallPrice) + "。");
                return placed;
            }
            catch (Exception ex)
            {
                var cleanupFailed = false;
                foreach (var part in placed)
                    try { inventory.Expel(part); part.Destroy(); }
                    catch (Exception cleanup)
                    {
                        cleanupFailed = true;
                        Core.Log?.Warning("清理收缴盒半成品失败：" + cleanup.Message);
                    }
                if (cleanupFailed)
                    throw new InvalidOperationException("江白箱子半成品清理失败，停止重抽。", ex);
                if (ex is not NativePlacementException) throw;
                lastFailure = ex;
                Core.Log?.Warning("江白箱子原生寻位与计划不一致，重新规划：" + ex.Message);
            }
        }
        throw new StockExhaustedException(lastFailure);
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
