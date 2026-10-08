using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace PCExpansion;

/// <summary>
/// 分级物品体系：9 种类型（机箱 + 8 种配件）× 5 个等级（T1 最低 ~ T5 最高）× 完好/损坏。
/// ID 规则：pcrepair.&lt;词干&gt;_t&lt;n&gt;（完好）、pcrepair.&lt;词干&gt;_t&lt;n&gt;_broken（损坏），n = 1..5。
/// 贴图命名规则：&lt;词干&gt;_t&lt;n&gt;.png / &lt;词干&gt;_t&lt;n&gt;_broken.png，放在 assets/icons/。
/// </summary>
internal static class Components
{
    // 类型标签：插槽过滤与物品识别用
    internal const string CaseTag = "PCREPAIR_CASE";
    internal const string PsuTag = "PCREPAIR_PSU";
    internal const string MotherboardTag = "PCREPAIR_MOTHERBOARD";
    internal const string HddTag = "PCREPAIR_HDD";
    internal const string RamTag = "PCREPAIR_RAM";
    internal const string GpuTag = "PCREPAIR_GPU";
    internal const string FanTag = "PCREPAIR_FAN";
    internal const string CpuTag = "PCREPAIR_CPU";
    internal const string CoolerTag = "PCREPAIR_COOLER";
    internal const string MarkerTag = "PCREPAIR_COMPONENT";

    // 分级与状态标签
    internal static readonly string[] TierTags =
    {
        "PCREPAIR_T1", "PCREPAIR_T2", "PCREPAIR_T3", "PCREPAIR_T4", "PCREPAIR_T5"
    };
    internal const string BrokenTag = "PCREPAIR_BROKEN";

    internal const int TierCount = 5;

    /// <summary>一种配件类型（机箱或某类配件）的静态定义。</summary>
    internal sealed class Type
    {
        internal readonly string Stem;
        internal readonly string Tag;
        internal string NameKey => "item.type." + Stem + ".name";
        internal string Name => LanguageText.Get(NameKey);
        internal string IntactDesc => LanguageText.Get("item.type." + Stem + ".intact");
        internal string BrokenDesc => LanguageText.Get("item.type." + Stem + ".broken");

        internal readonly string[] TierModels;
        internal readonly int Width, Height;
        internal readonly long BaseValue;
        internal readonly bool IsCase;

        internal Type(string stem, string tag,
            int width, int height, long baseValue, bool isCase, params string[] tierModels)
        {
            Stem = stem;
            Tag = tag;
            if (tierModels.Length != TierCount)
                throw new ArgumentException("每种配件必须配置 T1 至 T5 五个型号。", nameof(tierModels));
            TierModels = tierModels;
            Width = width;
            Height = height;
            BaseValue = baseValue;
            IsCase = isCase;
        }
    }

    internal static readonly Type[] All =
    {
        new("computer_case", CaseTag, 4, 4, 200, true,
            "MATREXX 30", "NZXT H5", "Corsair 4000D", "Lian Li O11 EVO", "Corsair 1000D"),
        new("component_psu", PsuTag, 2, 2, 75, false,
            "MWE 450", "Corsair CX650", "Seasonic GX-750", "Corsair RM850x", "Dark Power 13"),
        new("component_motherboard", MotherboardTag, 4, 4, 100, false,
            "MSI H610M-G", "ASUS TUF B760", "MSI Z790 Tomahawk", "A-SUS R0G Z790-E", "ASUS R0G Z890 Hero"),
        new("component_hdd", HddTag, 3, 1, 65, false,
            "SSD-1 256GB", "SSD-2 512GB", "SSD-3 1TB", "SSD-4 2TB", "SSD-5 4TB"),
        new("component_ram", RamTag, 1, 3, 55, false,
            "Kingston Beast 8GB", "Corsair Vengeance 16GB", "Kingston Renegade 32GB",
            "G.SKILL Trident Z5 48GB", "Corsair Dominator 64GB"),
        new("component_gpu", GpuTag, 2, 4, 130, false,
            "rux 3050", "rux 4060", "rux 4070", "rux 4080 SUPER", "rux 5090"),
        new("component_fan", FanTag, 2, 2, 30, false,
            "ARCTIC P12", "CM Mobius 120", "be quiet! Silent Wings 4", "Noctua A12x25", "Corsair QX120"),
        new("component_cpu", CpuTag, 2, 2, 150, false,
            "Core i3-10100F", "Core i5-12400F", "Core i7-13700K", "Core i9-14900K", "Core Ultra 9 285K"),
        new("component_cooler", CoolerTag, 2, 2, 45, false,
            "DeepCool AG400", "Thermalright PA120 SE", "Noctua NH-D15", "NZXT Kraken 360", "Corsair TITAN 360 LCD"),
    };

    /// <summary>一种具体物品：类型 × 等级 × 完好/损坏。</summary>
    internal sealed class Item
    {
        internal readonly Type Owner;
        internal readonly int Tier;
        internal readonly bool Broken;

        internal Item(Type owner, int tier, bool broken)
        {
            Owner = owner;
            Tier = tier;
            Broken = broken;
        }

        internal string Id => "pcrepair." + Owner.Stem + "_t" + Tier + (Broken ? "_broken" : "");
        internal string SpriteKey => Id;
        internal string DisplayName
        {
            get
            {
                var model = Owner.TierModels[Tier - 1];
                if (Owner.Tag == HddTag)
                    return LanguageText.Get(Broken ? "item.display.broken" : "item.display.intact",
                        WorkroomComponentTemplates.Text("ssd"), model, Tier);
                return LanguageText.Get(Broken ? "item.display.broken" : "item.display.intact",
                    LanguageText.Argument(Owner.NameKey), model, Tier);
            }
        }

        internal string Desc => Owner.Tag == HddTag ? WorkroomComponentTemplates.Text(Broken ? "ssd_broken_description" : "ssd_description") :
            Broken ? Owner.BrokenDesc : Owner.IntactDesc;

        internal string FlavorText => ComponentFlavorText.Get(this);

        // T1 基础价 × 分档系数 {1, 1.55, 2.4, 3.7, 5.7}；破损件按完好价 20% 回收。
        internal long Value
        {
            get => Components.ValueFor(Owner, Tier, Broken);
        }
    }

    private static Dictionary<string, Item>? _itemsById;
    private static readonly IReadOnlyList<Item> Definitions = CreateDefinitions();
    private static readonly long[] RamPrices = { 55, 85, 145, 215, 285 };
    private static bool descriptionWriteProbeLogged;

    internal static IEnumerable<Item> AllItems() => Definitions;

    private static IReadOnlyList<Item> CreateDefinitions()
    {
        var items = new List<Item>();
        foreach (var t in All)
            for (var tier = 1; tier <= TierCount; tier++)
            {
                items.Add(new Item(t, tier, false));
                items.Add(new Item(t, tier, true));
            }
        return items.AsReadOnly();
    }

    internal static Item? Find(string? id)
    {
        if (_itemsById == null)
        {
            _itemsById = new Dictionary<string, Item>();
            foreach (var item in AllItems()) _itemsById[item.Id] = item;
        }
        return _itemsById.TryGetValue(Core.Clean(id), out var found) ? found : null;
    }

    internal static bool IsComponent(GameItem? item) => item != null && item.IsTag(MarkerTag);

    internal static GameItem Create(string id)
    {
        var spec = Find(id)
            ?? throw new ArgumentOutOfRangeException(nameof(id), id, "未知的电脑机箱或配件。");
        return Create(spec);
    }

    private static GameItem Create(Item spec)
    {
        // 机箱克隆「机器舱」继承机器类型与金属音效；配件克隆「电子元件」继承电子类型。
        var templateId = spec.Owner.IsCase ? "machine_bay" : "common_electronic";
        var item = DirectoryMaster.Item(templateId, true);
        if (item == null) throw new InvalidOperationException("找不到原版物品模板：" + templateId);
        try
        {
        Apply(item, spec);
        // 损坏机箱 = 物资箱：生成时随机装填内部配件并上锁（SPAWNED 标记防止读档重掷）。
        if (spec.Owner.IsCase && spec.Broken)
            CaseUnboxing.EnsureLootAndLock(item, spec.Tier);
        item.onLoaded = (Il2CppSystem.Action<GameItem>)(loaded => Apply(loaded, spec));
        return item;
        }
        catch
        {
            // Crate contents can also fail during creation. Retain every failed
            // template in the codec cleanup path rather than leaking a half item.
            WorkroomItemCodec.Destroy(new WorkroomItemCodec.Candidate { Root = item, Nodes = new List<GameItem> { item } });
            throw;
        }
    }

    private static void Apply(GameItem item, Item spec)
    {
        var type = spec.Owner;
        item.identifier = spec.Id;
        item.identifierName = spec.DisplayName;
        item.SetName(spec.DisplayName);
        var description = spec.Desc;
        var flavor = spec.FlavorText;
        if (string.IsNullOrWhiteSpace(description))
        {
            description = LanguageText.Get("item.fallback.description", LanguageText.Argument(type.NameKey));
            Core.Log?.Warning("物品主描述为空，已应用备用说明：" + spec.Id + " / " + LanguageText.LocaleCode);
        }
        if (string.IsNullOrWhiteSpace(flavor))
        {
            flavor = LanguageText.Get("item.fallback.flavor", LanguageText.Argument(type.NameKey));
            Core.Log?.Warning("物品浅灰描述为空，已应用备用文本：" + spec.Id + " / " + LanguageText.LocaleCode);
        }
        item.shortDescription = description;
        item.longDescription = description;
        item.flavorText = flavor;
        if (string.IsNullOrWhiteSpace(Core.Clean(item.shortDescription)) ||
            string.IsNullOrWhiteSpace(Core.Clean(item.longDescription)) ||
            string.IsNullOrWhiteSpace(Core.Clean(item.flavorText)))
            Core.Log?.Error("物品提示字段写入后仍为空：" + spec.Id + " / " + LanguageText.LocaleCode);
        else
            Core.Debug("物品文案已写入：" + spec.Id + " / " + LanguageText.LocaleCode +
                       " / 主文=" + item.shortDescription.Length + " / 灰字=" + item.flavorText.Length);
        if (!descriptionWriteProbeLogged)
        {
            descriptionWriteProbeLogged = true;
            Core.Log?.Msg("[文案检查] " + LanguageText.LocaleCode + " / " + spec.Id +
                          " / shortDescription=" + item.shortDescription.Length +
                          " / longDescription=" + item.longDescription.Length +
                          " / flavorText=" + item.flavorText.Length);
        }
        item.unitCount = 1;
        // 价值规则：配件与完好机箱使用统一的非线性 T 度系数，损坏配件回收价为完好价 20%；
        // 损坏机箱是物资箱，锁定时按当前掉落期望估价，已生成且解锁后价值为 0。
        var value = spec.Value;
        if (type.IsCase && spec.Broken)
        {
            value = item.IsTag(CaseUnboxing.SpawnedTagName) &&
                    !item.IsTag(CaseUnboxing.LockedTagName)
                ? 0
                : CaseEconomy.CratePrice(spec.Tier);
        }
        item.SetValue(value);
        item.SetShape(new GridShapeBuilder(type.Width, type.Height).SetDataFill(1).Build());
        item.spritePath = spec.SpriteKey;
        item.spriteAtlasPath = string.Empty;
        item.spriteChanged = true;
        item.RemoveAllGameItemType();
        item.SetGameItemType(type.IsCase ? "MACHINE" : "MATERIAL");
        // 原版类型列表负责提示中的方括号标签；创建和读档都追加，保留原有分类。
        item.SetGameItemType(ComputerSuppliesType.Identifier);
        if (type.IsCase)
        {
            // 保留原版设备标记；CaseDropGuardPatch 阻止嵌套，双击由本 MOD 接管。
            item.EnableTag("MACHINE_TAG", false);
            item.EnableTag("NESTABLE_DEVICE_TAG", false);
            item.EnableTag(CaseTag, false);
            // 保留两种机箱各自的原版开盖音，并为面板关闭提供对应原版音效。
            item.soundContainerOpen = spec.Broken ? "food_can_open" : "open_machine";
            item.soundContainerClose = spec.Broken ? "close_machine_light" : "close_machine";
            // 机箱拖动音效：原版机器拖动/放下音（machine_bay 克隆的自带字段为空）
            item.soundDragStart = "metal_machine_drag";
            item.soundDragEnd = "metal_machine_drop";
        }
        else
        {
            item.EnableTag(MarkerTag, false);
            // 克隆电子零件只借用物品骨架和材质；清掉其源物品修复回调。
            // 否则显卡等电脑散件也会被当作电子零件，用来修原版模块或升级机器。
            item.mayThisTargetItemFunc = null;
            item.canThisTargetItemFunc = null;
            item.onThisTargetItemFunc = null;
            // 配件拖动音效：原版模组拖动/放下音（与电子元件类物品一致）
            item.soundDragStart = "module_drag";
            item.soundDragEnd = "module_drop";
        }
        item.EnableTag(type.Tag, false);
        foreach (var tag in TierTags) item.DisableTag(tag, false);
        item.EnableTag(TierTags[spec.Tier - 1], false);
        var highEndEligible = !type.IsCase && IsHighEndEligible(spec);
        SetTradeProperties(item, highEndEligible, !type.IsCase && spec.Tier == 5);
        if (spec.Broken)
        {
            item.EnableTag(BrokenTag, false);
            // 原版破损机器标记仅用于机箱的螺丝刀路由。电脑散件不能进入
            // 原版拆螺丝/焊接维修，否则会绕过本 MOD 的材料配方和等级限制。
            if (type.IsCase) item.EnableTag("BROKEN_MACHINE", false);
            else item.DisableTag("BROKEN_MACHINE", false);
        }
        else
        {
            // 完好状态不得残留损坏标记。
            item.DisableTag(BrokenTag, false);
            item.DisableTag("BROKEN_MACHINE", false);
        }
        // 原版恢复保存标签后调用 onLoaded。整机在这里按内部配件重新估价，
        // 避免重启后退回空机箱基础价，直到再次打开面板才恢复。
        if (type.IsCase && !spec.Broken) CaseEconomy.EvaluateCase(item);
        else LowerAssemblerNpc.RefreshPurchaseEligibility(item);
        WorkroomMachineAssembly.Refresh(item);
        if (WorkroomComponentTemplates.TryWhole(spec.Id, out var family, out _, out _))
        {
            WorkroomComponentParts.EnsureSeed(item, family.Key);
            WorkroomComponentParts.RefreshComposition(item);
        }
    }

    internal static long ValueFor(Type type, int tier, bool broken)
    {
        var tierMultiplier = Math.Clamp(tier, 1, TierCount) switch
        {
            1 => 1.0,
            2 => 1.55,
            3 => 2.4,
            4 => 3.7,
            _ => 5.7,
        };
        var intact = type.Tag == RamTag ? RamPrices[Math.Clamp(tier,1,5)-1] :
            (long)Math.Round(type.BaseValue * tierMultiplier);
        return broken ? Math.Max(1, (long)Math.Round(intact * 0.20)) : intact;
    }

    /// <summary>T5 parts are high-end; at T4 only graphics cards and CPUs qualify.</summary>
    internal static bool IsHighEndEligible(Item spec) =>
        spec.Tier >= 5 || (spec.Tier == 4 &&
            (spec.Owner.Tag == GpuTag || spec.Owner.Tag == CpuTag));

    /// <summary>Shared native type/status structure for loose parts and assembled cases.</summary>
    internal static void SetTradeProperties(GameItem item, bool highEnd, bool highContraband)
    {
        HighEndParts.Apply(item, highEnd);

        if (highContraband)
        {
            if (!item.IsGameItemType("CONTRABAND")) item.SetGameItemType("CONTRABAND");
            item.EnableTag("CONTRABAND_ITEM_TAG", false);
            item.EnableTag("CONTRABAND_LEVEL", false);
            // InitContrabandItem was unsafe on cloned mod items. Use the same
            // native status format via the regular tag APIs instead.
            item.ModifyTag("CONTRABAND_LEVEL",
                (Il2CppSystem.Action<TagState>)(state => state?.SetString("CONTRABAND_LEVEL_HIGH")), false);
        }
        else
        {
            if (item.IsGameItemType("CONTRABAND")) item.RemoveGameItemType("CONTRABAND");
            foreach (var tag in new[] { "CONTRABAND_ITEM_TAG", "CONTRABAND_LEVEL", "CONTRABAND_LEVEL_NONE",
                "CONTRABAND_LEVEL_LOW", "CONTRABAND_LEVEL_MID", "CONTRABAND_LEVEL_HIGH", "CONTRABAND_LEVEL_CRITICAL" })
                if (item.IsTag(tag)) item.DisableTag(tag, false);
            // Removing the last T5 part must also remove a previous native price bonus.
            item.RemoveFeatureByCategory("contrabandMarkUp");
            item.RemoveItemFeatureByID("discountedHighContraband");
        }
    }

    internal static int TierOf(GameItem? item)
    {
        if (item == null) return 0;
        for (var i = 0; i < TierTags.Length; i++)
            if (item.IsTag(TierTags[i])) return i + 1;
        return 0;
    }

    /// <summary>按 ID 把物品规格重新应用到物品实例上（用于损坏机箱切换为打开形态）。</summary>
    internal static void ApplySpec(GameItem item, string id)
    {
        var spec = Find(id);
        if (spec != null) Apply(item, spec);
    }
}

[HarmonyPatch(typeof(ModItemDirectory), "InitDirectory")]
internal static class ComponentDirectoryPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ModItemDirectory __instance)
    {
        var registered = 0;
        foreach (var spec in Components.AllItems())
        {
            try
            {
                var capturedId = spec.Id;
                Il2CppSystem.Func<GameItem> factory = (Func<GameItem>)(() => Components.Create(capturedId));
                __instance.Set(capturedId, factory);
                registered++;
            }
            catch (Exception ex)
            {
                Core.Log?.Error("注册物品失败（" + spec.Id + "）：" + ex);
            }
        }

        Core.Log?.Msg("物品工厂注册完成：分级物品 " + registered + " 个。");
    }
}
