using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace ProbablyAssembled;

/// <summary>把电脑物品接入原版街区拾荒掉落和上门拾荒客库存。</summary>
internal static class ScavengingIntegration
{
    private const string ModId = "pcrepairmod";
    private const string DumpingGroundGroupId = "dumpingGroundTG";
    private const string JunkTableId = "junkTable";
    private const string BrokenComputerTableId = "pcrepairmod.broken_computer_parts";
    private const string DoorScavengerStockedTag = "PCREPAIR_DOOR_SCAVENGER_STOCKED";
    private const string ThiefComponentStockedTag = "PCREPAIR_THIEF_COMPONENT_STOCKED";
    private const float JunkWeightReduction = 0.10f;
    private static readonly Random random = new();
    private static bool lootPoolRegistered;
    private static float originalJunkTableWeight = float.NaN;
    private static int scavengingActions;
    private static int scavengingRolls;
    private static int computerRolls;

    private static void RegisterBrokenComputerLoot()
    {
        if (lootPoolRegistered) return;

        var stage = "等待原版拾荒组";
        try
        {
            // Wait until the native dumping-ground group exists. The action hook
            // retries later if vanilla initializes its tables after this callback.
            var initialGroups = LootRegistry.groups;
            if (initialGroups == null || !initialGroups.ContainsKey(DumpingGroundGroupId))
            {
                Core.Debug("原版拾荒组尚未加载，等玩家触发拾荒时重试注册。");
                return;
            }

            if (float.IsNaN(originalJunkTableWeight))
            {
                if (!initialGroups.TryGetValue(DumpingGroundGroupId, out var initialGroup) || initialGroup == null)
                    throw new InvalidOperationException("原版拾荒组尚未加载。");

                foreach (var entry in initialGroup)
                {
                    if (entry != null && Core.Clean(entry.id) == JunkTableId)
                    {
                        originalJunkTableWeight = entry.weight;
                        break;
                    }
                }

                if (originalJunkTableWeight <= 0)
                    throw new InvalidOperationException("原版垃圾表 junkTable 不存在或权重无效。");
            }

            var computerGroupWeight = originalJunkTableWeight * JunkWeightReduction;
            var reducedJunkWeight = originalJunkTableWeight - computerGroupWeight;

            // ClearTable/AddEntry modify an existing table; RegisterTable is
            // required to create this custom table in LootRegistry's journal.
            LootRegistry.ClearTable(ModId, BrokenComputerTableId);
            var entries = new Il2CppSystem.Collections.Generic.List<LootEntry>();
            var expectedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Components.AllItems())
            {
                if (!item.Broken || (item.Owner.IsCase ? item.Tier != 1 : item.Tier > 2)) continue;

                // 损坏机箱只进 T1；T1 配件比 T2 常见，降低高阶掉落权重。
                var weight = item.Owner.IsCase ? 0.2f : item.Tier == 1 ? 1f : 0.5f;
                entries.Add(new LootEntry(item.Id, weight, ModId));
                expectedIds.Add(item.Id);
            }

            if (expectedIds.Count == 0) return;
            stage = "注册自定义物品表";
            var entryEnumerable = new Il2CppSystem.Collections.Generic.IEnumerable<LootEntry>(entries.Pointer);
            LootRegistry.RegisterTable(ModId, BrokenComputerTableId, entryEnumerable);
            // Make retries idempotent if vanilla loading completed in stages.
            stage = "注册拾荒组引用";
            LootRegistry.SetGroupWeight(ModId, DumpingGroundGroupId, JunkTableId, reducedJunkWeight);
            LootRegistry.RemoveGroupEntry(ModId, DumpingGroundGroupId, BrokenComputerTableId);
            LootRegistry.AddGroupEntry(ModId, DumpingGroundGroupId, BrokenComputerTableId, computerGroupWeight);
            stage = "重建有效拾荒表";
            LootRegistry.RebuildIfDirty();
            var tables = LootRegistry.tables;
            var groups = LootRegistry.groups;
            stage = "校验有效电脑物品表";
            if (tables == null)
                throw new InvalidOperationException("LootRegistry.tables 为空。");
            if (!tables.TryGetValue(BrokenComputerTableId, out var registeredParts) || registeredParts == null)
                throw new InvalidOperationException("电脑拾荒表不存在；预期物品数=" + expectedIds.Count + "。");
            if (registeredParts.Count != expectedIds.Count)
                throw new InvalidOperationException("电脑拾荒表条目数不符；预期=" + expectedIds.Count + "，实际=" + registeredParts.Count + "。");
            var registeredCount = registeredParts.Count;
            foreach (var entry in registeredParts)
                if (entry == null || !expectedIds.Remove(Core.Clean(entry.id)))
                    throw new InvalidOperationException("电脑拾荒表含有缺失或重复的物品 ID：" + Core.Clean(entry?.id));
            if (expectedIds.Count != 0)
                throw new InvalidOperationException("电脑拾荒表缺少预期物品，剩余数量=" + expectedIds.Count + "。");
            if (groups == null || !groups.TryGetValue(DumpingGroundGroupId, out var group) || group == null)
                throw new InvalidOperationException("原版拾荒组尚未加载。");
            var totalWeight = 0.0;
            var computerWeight = 0.0;
            var junkWeight = 0.0;
            var computerGroupEntries = 0;
            var junkGroupEntries = 0;
            foreach (var entry in group)
            {
                if (entry == null || entry.weight <= 0) continue;
                totalWeight += entry.weight;
                var entryId = Core.Clean(entry.id);
                if (entryId == JunkTableId)
                {
                    junkWeight += entry.weight;
                    junkGroupEntries++;
                }
                if (entryId == BrokenComputerTableId)
                {
                    computerWeight += entry.weight;
                    computerGroupEntries++;
                }
            }
            if (computerWeight <= 0 || computerGroupEntries != 1)
                throw new InvalidOperationException("电脑拾荒表未唯一进入原版有效拾荒组（命中条目=" + computerGroupEntries + "）。");
            if (junkGroupEntries != 1 || Math.Abs(junkWeight - reducedJunkWeight) > Math.Max(0.001f, originalJunkTableWeight * 0.001f))
                throw new InvalidOperationException("原版垃圾表权重调整校验失败（预期=" + reducedJunkWeight + "，实际=" + junkWeight + "，命中条目=" + junkGroupEntries + "）。");
            if (Math.Abs(computerWeight - computerGroupWeight) > Math.Max(0.001f, originalJunkTableWeight * 0.001f))
                throw new InvalidOperationException("电脑物品权重与垃圾表腾出权重不一致（预期=" + computerGroupWeight + "，实际=" + computerWeight + "）。");
            if (totalWeight <= 0) throw new InvalidOperationException("原版拾荒组权重无效。");
            lootPoolRegistered = true;
            Core.Log?.Msg("[深空装机] 已接入原版拾荒池：" + registeredCount +
                          " 种破损电脑物品（T1/T2 配件、仅 T1 机箱），有效电脑池抽中概率=" +
                          (computerWeight / totalWeight * 100).ToString("F1") + "%；垃圾表权重已降低 10%，腾出权重全部转入电脑物品表。");
        }
        catch (Exception ex)
        {
            Core.Log?.Error("接入原版拾荒掉落池失败（阶段：" + stage + "）：" + ex);
        }
    }

    private static void AddDoorScavengerStock(StoreClient? client)
    {
        if (client == null) return;
        var clientId = Core.Clean(client.identifier);
        if (clientId != "scavGeneral" && clientId != "scavCrate") return;
        if (client.IsTag(DoorScavengerStockedTag)) return;

        try
        {
            var store = PlayerStore.Instance;
            if (store == null) return;

            var pool = new List<Components.Item>();
            foreach (var item in Components.AllItems())
                if (!item.Owner.IsCase && item.Tier <= 2) pool.Add(item);
            if (pool.Count == 0) return;

            // 每位上门拾荒客随机带 1–2 件，完好/损坏和 T1/T2 都在同一个抽选池中。
            var selectedCount = random.Next(1, Math.Min(2, pool.Count) + 1);
            for (var i = pool.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            var offers = new List<GameItem>(selectedCount);
            for (var i = 0; i < selectedCount; i++)
            {
                var item = DirectoryMaster.Item(pool[i].Id, true);
                if (item == null)
                {
                    Core.Log?.Warning("拾荒客配件尚未注册，跳过本次库存追加：" + pool[i].Id);
                    return;
                }
                offers.Add(item);
            }

            foreach (var item in offers)
                store.AddDirectSellingItemToTable(item, false, false, false, 0);

            client.AddTag(DoorScavengerStockedTag);
            Core.Log?.Msg("[深空装机] 原版上门拾荒客本次额外带来 " + offers.Count +
                          " 件随机 T1/T2 电脑配件（完好或损坏）。");
        }
        catch (Exception ex)
        {
            Core.Log?.Error("为原版上门拾荒客追加电脑配件失败：" + ex);
        }
    }

    private static void AddThiefComponentStock(StoreClient? client)
    {
        if (client == null) return;

        var clientId = Core.Clean(client.identifier);
        var isThiefVisitor = clientId == "thief" || clientId == "pettyThief" || clientId == "foodThief" ||
                             clientId.StartsWith("thiefGeneric", StringComparison.Ordinal);
        if (!isThiefVisitor || client.IsTag(ThiefComponentStockedTag)) return;

        try
        {
            var store = PlayerStore.Instance;
            if (store == null) return;

            var pool = new List<Components.Item>();
            foreach (var item in Components.AllItems())
            {
                // 小偷只带 T4/T5 配件；完好与破损配件都可抽中，任何机箱都排除。
                if (item.Owner.IsCase || item.Tier < 4 || item.Tier > 5) continue;
                pool.Add(item);
            }
            if (pool.Count == 0) return;

            var selectedCount = random.Next(1, Math.Min(2, pool.Count) + 1);
            for (var i = pool.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            var offers = new List<GameItem>(selectedCount);
            for (var i = 0; i < selectedCount; i++)
            {
                var item = DirectoryMaster.Item(pool[i].Id, true);
                if (item == null)
                {
                    Core.Log?.Warning("小偷高阶配件尚未注册，取消本次追加：" + pool[i].Id);
                    return;
                }
                offers.Add(item);
            }

            foreach (var item in offers)
                store.AddDirectSellingItemToTable(item, false, false, false, 0);

            client.AddTag(ThiefComponentStockedTag);
            Core.Log?.Msg("[深空装机] 原版小偷本次额外携带 " + offers.Count + " 件随机 T4/T5 电脑配件（完好或破损，不含机箱）。");
        }
        catch (Exception ex)
        {
            Core.Log?.Error("为原版小偷追加 T4/T5 电脑配件失败：" + ex);
        }
    }

    // Register when the vanilla tables are ready; retry before the player invokes
    // the actual dumping-ground scavenging action if initialization was delayed.
    [HarmonyPatch(typeof(LootRegistry), nameof(LootRegistry.OnVanillaLoaded))]
    internal static class ScavengingTableReadyPatch
    {
        private static void Postfix()
        {
            lootPoolRegistered = false;
            RegisterBrokenComputerLoot();
        }
    }

    [HarmonyPatch(typeof(ScavHelper), nameof(ScavHelper.ScavengeDumpingGrounds))]
    internal static class ScavengingActionPatch
    {
        private static void Prefix()
        {
            try
            {
                scavengingActions++;
                RegisterBrokenComputerLoot();
                Core.Log?.Msg("[拾荒入口] 原版街区拾荒已触发，第 " + scavengingActions + " 次；电脑掉落表已注册=" + lootPoolRegistered + "。");
            }
            catch (Exception ex)
            {
                Core.Log?.Error("原版街区拾荒入口诊断失败：" + ex);
            }
        }
    }

    [HarmonyPatch(typeof(LootRegistry), nameof(LootRegistry.RollGroup))]
    internal static class ScavengingRollPatch
    {
        private static void Postfix(string __0, string __result)
        {
            if (Core.Clean(__0) != DumpingGroundGroupId) return;
            scavengingRolls++;
            var spec = Components.Find(Core.Clean(__result));
            if (spec != null) computerRolls++;
            if (scavengingRolls <= 12 || spec != null)
                Core.Log?.Msg("[拾荒抽取] " + Core.Clean(__result) + "，累计抽取=" + scavengingRolls +
                              "，电脑物品=" + computerRolls);
        }
    }

    [HarmonyPatch(typeof(ItemSpawner), nameof(ItemSpawner.SpawnFromTableGroup))]
    internal static class ScavengingSpawnPatch
    {
        private static void Prefix(string __0)
        {
            if (Core.Clean(__0) == DumpingGroundGroupId) RegisterBrokenComputerLoot();
        }

        private static void Postfix(string __0, GameItem __result)
        {
            if (Core.Clean(__0) == DumpingGroundGroupId && __result == null)
                Core.Log?.Warning("[拾荒抽取] 原版拾荒组没有生成物品，请核对抽取日志和物品目录。");
        }
    }

    [HarmonyPatch(typeof(StoreClient), nameof(StoreClient.OnIntroduced))]
    internal static class DoorScavengerStockPatch
    {
        private static void Postfix(StoreClient __instance)
        {
            AddDoorScavengerStock(__instance);
            AddThiefComponentStock(__instance);
        }
    }
}
