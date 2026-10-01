using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace ProbablyAssembled;

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
        internal readonly string NameZh;
        internal string Name => LanguageText.Get(NameZh, EnglishName);
        internal readonly string IntactDescZh;
        internal string IntactDesc => LanguageText.Get(IntactDescZh, EnglishIntactDesc);
        internal string BrokenDesc => LanguageText.Get(
            IsCase ? "破损机箱，箱体已上锁；使用拆机螺丝刀打开并查看内部电脑配件。" : "损坏的" + NameZh + "，无法正常使用。",
            IsCase ? "A damaged, locked PC case. Use the teardown screwdriver to open it and inspect the parts inside." : "A broken " + EnglishName + ". It will not work until repaired.");
        internal string EnglishName => Tag switch
        {
            CaseTag => "PC Case", PsuTag => "Power Supply", MotherboardTag => "Motherboard",
            HddTag => "Hard Drive", RamTag => "Memory Stick", GpuTag => "Graphics Card",
            FanTag => "Case Fan", CpuTag => "CPU", CoolerTag => "CPU Cooler", _ => "PC Part"
        };
        internal string EnglishIntactDesc => Tag switch
        {
            CaseTag => "A sturdy PC case with room for its components.",
            PsuTag => "A power supply that delivers power to the computer's components.",
            MotherboardTag => "A motherboard that connects the CPU, memory, graphics card, and other core components.",
            HddTag => "A mechanical hard drive for storing files and other data.",
            RamTag => "A memory stick that gives running programs room to work.",
            GpuTag => "A graphics card that renders images and sends them to the display.",
            FanTag => "A case fan that moves air through the computer case.",
            CpuTag => "A central processor that carries out the computer's instructions.",
            CoolerTag => "A CPU cooler that draws heat away from the processor.",
            _ => "A computer component in working condition."
        };

        internal string EnglishFlavorText => Tag switch
        {
            CaseTag => "The little power light has a faint scuff around its edge. Someone must have checked it often.",
            PsuTag => "The cable label is still tucked neatly under the strap, though the ink has faded at the fold.",
            MotherboardTag => "The dust around the slots has been carefully wiped away, though one corner of the warranty sticker is peeling. Its last owner seems to have looked after it.",
            HddTag => "An old capacity sticker clings to the casing. The numbers are still easy to read, even after a few trips through the drawer.",
            RamTag => "The gold contacts are clean and bright. A tiny strip of label glue remains near one end.",
            GpuTag => "A little dust is caught between the fan blades. It spins away with a gentle puff.",
            FanTag => "One blade still has a faint fingerprint near the hub, left behind by someone who gave it a careful wipe.",
            CpuTag => "The tiny markings on its top are still crisp; someone must have handled it with care.",
            CoolerTag => "A soft grey thread is caught between two fins. It is the sort of thing you only notice in good light.",
            _ => string.Empty
        };

        internal string ChineseFlavorText => Tag switch
        {
            CaseTag => "电源小灯边缘有一道浅浅的磨痕，看样子以前常有人伸手去按。",
            PsuTag => "线材标签还整齐地收在束带下面，只是折痕处的字迹淡了些。",
            MotherboardTag => "插槽边的灰擦得很干净，只有保修贴的一角翘着。上一任主人看起来挺爱惜它。",
            HddTag => "外壳上还贴着旧容量标签，数字倒是清楚，像是在抽屉里翻过几回。",
            RamTag => "金手指擦得很亮，塑料卡扣边上还留着一点点标签胶。",
            GpuTag => "风扇叶片间卡着一小撮灰，轻轻一吹，灰尘打了个转。",
            FanTag => "扇叶靠近轴心的地方留着一道浅浅的指印，像是刚被人仔细擦过。",
            CpuTag => "顶盖上的小字还很清楚，上一任主人拿它时大概挺小心。",
            CoolerTag => "两片散热鳍片之间夹着一根灰色细绒，得在光线好的时候才能看见。",
            _ => string.Empty
        };
        internal readonly string[] TierModels;
        internal readonly int Width, Height;
        internal readonly long BaseValue;
        internal readonly bool IsCase;

        internal Type(string stem, string tag, string name, string intactDesc,
            int width, int height, long baseValue, bool isCase, params string[] tierModels)
        {
            Stem = stem;
            Tag = tag;
            NameZh = name;
            IntactDescZh = intactDesc;
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
        new("computer_case", CaseTag, "电脑机箱",
            "完好的电脑机箱，用于容纳和保护内部配件；双击可打开内部面板。", 4, 4, 200, true,
            "MATREXX 30", "NZXT H5", "Corsair 4000D", "Lian Li O11 EVO", "Corsair 1000D"),
        new("component_psu", PsuTag, "电源",
            "为电脑各配件供电的电源。", 2, 2, 75, false,
            "MWE 450", "Corsair CX650", "Seasonic GX-750", "Corsair RM850x", "Dark Power 13"),
        new("component_motherboard", MotherboardTag, "主板",
            "连接处理器、内存、显卡等核心配件的电脑主板。", 4, 4, 100, false,
            "MSI H610M-G", "ASUS TUF B760", "MSI Z790 Tomahawk", "A-SUS R0G Z790-E", "ASUS R0G Z890 Hero"),
        new("component_hdd", HddTag, "硬盘",
            "用于存储文件和其他数据的机械硬盘。", 3, 1, 65, false,
            "Seagate BarraCuda 1TB", "WD Blue 2TB", "Toshiba X300 4TB", "Seagate IronWolf 12TB", "WD Gold 22TB"),
        new("component_ram", RamTag, "内存条",
            "为正在运行的程序提供临时存储空间的内存条。", 1, 3, 55, false,
            "Kingston Beast 8GB", "Corsair Vengeance 16GB", "Kingston Renegade 32GB",
            "G.SKILL Trident Z5 32GB", "Corsair Dominator 48GB"),
        new("component_gpu", GpuTag, "显卡",
            "负责图像处理和显示输出的独立显卡。", 2, 4, 130, false,
            "rux 3050", "rux 4060", "rux 4070", "rux 4080 SUPER", "rux 5090"),
        new("component_fan", FanTag, "散热风扇",
            "安装在机箱内、用于促进空气流动的散热风扇。", 2, 2, 30, false,
            "ARCTIC P12", "CM Mobius 120", "be quiet! Silent Wings 4", "Noctua A12x25", "Corsair QX120"),
        new("component_cpu", CpuTag, "CPU",
            "执行计算和指令处理工作的中央处理器。", 2, 2, 150, false,
            "Core i3-10100F", "Core i5-12400F", "Core i7-13700K", "Core i9-14900K", "Core Ultra 9 285K"),
        new("component_cooler", CoolerTag, "CPU散热风扇",
            "安装在处理器上、用于带走热量的 CPU 散热器。", 2, 2, 45, false,
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
                return LanguageText.Get(
                    Owner.NameZh + "·" + model + " T" + Tier + (Broken ? "（破损）" : ""),
                    Owner.EnglishName + " · " + model + " T" + Tier + (Broken ? " (Broken)" : ""));
            }
        }

        internal string Desc => Broken ? Owner.BrokenDesc : Owner.IntactDesc;

        internal string FlavorText => Owner.Tag == MotherboardTag && Tier == TierCount
            ? LanguageText.Get(
                "散热片摸起来凉凉的，边缘还留着一枚旧标签的胶痕。这个型号大概在柜台上转过几次手。",
                "The heatsinks feel cool to the touch, with a faint patch of old label glue along the edge. This board has probably changed hands a few times.")
            : LanguageText.Get(Owner.ChineseFlavorText, Owner.EnglishFlavorText);

        // T1 基础价 × 分档系数 {1, 1.55, 2.4, 3.7, 5.7}；破损件按完好价 20% 回收。
        internal long Value
        {
            get => Components.ValueFor(Owner, Tier, Broken);
        }
    }

    private static Dictionary<string, Item>? _itemsById;
    private static bool descriptionWriteProbeLogged;

    internal static IEnumerable<Item> AllItems()
    {
        foreach (var t in All)
            for (var tier = 1; tier <= TierCount; tier++)
            {
                yield return new Item(t, tier, false);
                yield return new Item(t, tier, true);
            }
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

    /// <summary>返回配件/机箱对应的类型标签；不是本 MOD 物品则返回 null。</summary>
    internal static string? GetTag(GameItem? item)
    {
        if (item == null) return null;
        foreach (var t in All)
            if (item.IsTag(t.Tag)) return t.Tag;
        return null;
    }

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
        Apply(item, spec);
        // 损坏机箱 = 物资箱：生成时随机装填内部配件并上锁（SPAWNED 标记防止读档重掷）。
        if (spec.Owner.IsCase && spec.Broken)
            CaseUnboxing.EnsureLootAndLock(item, spec.Tier);
        item.onLoaded = (Il2CppSystem.Action<GameItem>)(loaded => Apply(loaded, spec));
        return item;
    }

    private static void Apply(GameItem item, Item spec)
    {
        var type = spec.Owner;
        item.identifier = spec.Id;
        item.identifierName = spec.DisplayName;
        item.SetName(spec.DisplayName);
        var description = spec.Desc + ComponentRepair.RepairHint(spec);
        var flavor = spec.FlavorText;
        if (string.IsNullOrWhiteSpace(description))
        {
            description = LanguageText.Get(type.NameZh + "，电脑配件。", type.EnglishName + ". A computer component.");
            Core.Log?.Warning("物品主描述为空，已应用备用说明：" + spec.Id + " / " + LanguageText.LocaleCode);
        }
        if (string.IsNullOrWhiteSpace(flavor))
        {
            flavor = LanguageText.Get("边角留着一点旧标签的胶痕。", "A faint trace of old label glue remains near the edge.");
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
        item.EnableTag(TierTags[spec.Tier - 1], false);
        var luxuryEligible = !type.IsCase && IsLuxuryEligible(spec);
        SetTradeProperties(item, luxuryEligible, !type.IsCase && spec.Tier == 5);
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
        var intact = (long)Math.Round(type.BaseValue * tierMultiplier);
        return broken ? Math.Max(1, (long)Math.Round(intact * 0.20)) : intact;
    }

    /// <summary>T5 items remain luxury goods; at T4 only graphics cards and CPUs qualify.</summary>
    internal static bool IsLuxuryEligible(Item spec) =>
        spec.Tier >= 5 || (spec.Tier == 4 &&
            (spec.Owner.Tag == GpuTag || spec.Owner.Tag == CpuTag));

    /// <summary>Shared native type/status structure for loose parts and assembled cases.</summary>
    internal static void SetTradeProperties(GameItem item, bool luxury, bool highContraband)
    {
        if (luxury)
        {
            if (!item.IsGameItemType("LUXURY_ITEM")) item.SetGameItemType("LUXURY_ITEM");
        }
        else if (item.IsGameItemType("LUXURY_ITEM")) item.RemoveGameItemType("LUXURY_ITEM");

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

    /// <summary>
    /// 已开封物资箱的共价：按面板槽位标签累计内部配件价值（纯算术，不创建临时物品）。
    /// </summary>
    internal static long CaseContentsValue(GameItem caseItem)
        => CaseEconomy.ContentsValue(caseItem);

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
