# 深空装机 / PC expansion

**作者：** 阿铭
**游戏：** Probably Stolen Demo
**版本：** 2.0，2026-10-13 更新
**测试状态：** 当前中文版本基本测试通过，暂未发现新增 bug；本次同步的英文内容仍需游戏内排版与字形复测。

## 安装前必读

- **2.0 沿用当前存档门禁：仅接受有效的 1.0 及以上版本标记。** 缺标记、无效或低版本的存档仍拒绝；版本号升级不提高存档门槛，不迁移或直接改写玩家存档。请单独保留备份。
- 中文只需 `PCExpansion.dll`；英文另装同一发布版本的 `PCExpansionEnglishPatch.dll`。两个文件必须配套，英文补丁不能单独使用。
- 开箱、拆装、维修、回收和报废都在工作间完成。点击店内桌面的 CPU 钥匙串进入；它是场景入口，不占玩家库存。

## Mod 简介

《深空装机 / PC expansion》为《Probably Stolen Demo》加入电脑拾荒、拆检、维修、组装和交易玩法。打开破损机箱寻找配件，把坏件拆到更小的部件，再决定维修、更换、回收或报废；将合适的配置组装成主板和整机，检测、估价，并向不同买家出售。

模组提供独立工作间、共用电脑储物箱、专属供应者和装机佬、十页游戏内手册，以及可扩建的机房。组装矿机挖取五种虚拟币，配置服务器签订租约，再通过机房电脑管理机器、交易和洛夕的硬件预订。在深空当铺，一只磕碰变形的机箱里，可能还藏着能用的配件；一台缺件的电脑，也可能在重新装配后成为值得出售的整机。

## 安装、更新与语言

本项目使用 **MelonLoader 0.7.3 Open-Beta（IL2CPP x64）**，无需其他 Mod 前置。

1. 完全关闭游戏。
2. 尚未安装 MelonLoader 时，从[官方 GitHub 页面](https://github.com/LavaGang/MelonLoader)获取安装器，选择《Probably Stolen Demo》的游戏程序及上述版本安装。可在 Steam 中选择“管理 → 浏览本地文件”定位游戏程序。
3. 安装加载器后启动一次游戏，再退出，让游戏目录生成 `Mods` 文件夹。
4. 如果 `Mods` 中还有 `ProbablyAssembled.dll`、`PcRepairMod.dll`、`ProbablyAssembledEnglishPatch.dll` 或 `PcRepairEnglishPatch.dll`，先移出这些旧文件，避免重复加载。
5. 将 `PCExpansion.dll` 放入 `Mods`；需要英文时，再放入配套的 `PCExpansionEnglishPatch.dll`。
6. 启动游戏，新建存档或继续通过当前存档检查的存档。若模组未加载，检查 MelonLoader 日志。

更新时先关闭游戏，再替换对应 DLL；使用英文时，本体和补丁一起更新。需要恢复中文时，移出英文补丁并完全重启。补丁只翻译本 Mod 的文字和手册，不改变游戏本身的语言设置；译文缺失、不匹配或手册无法正常显示时回退中文。

2.0 本体与英文补丁各有 737 条配套文本，手册仍为十页 v14。英文补丁需要单独安装；中文本体不依赖它。

## 硬件与内部拆装

九类完整硬件为机箱、电源、主板、固态硬盘、内存、显卡、机箱风扇、CPU 和 CPU 散热器，均有 T1–T5 及完好／破损两态，共 90 种；另有 41 种拆机子件规格及两态，共 82 个子件 ID。加上辅助物品、两种收缴盒和矿机／服务器各 T1–T5 的 10 种专用机箱，当前合计 187 个注册物品。

显卡、内存、固态硬盘、电源、风扇和 CPU 散热器可进一步拆成通用机体与内部子件；CPU 仍作为整件使用。破损配件首次拆检确定坏件分布，重试或读档不会重新抽取；已组装件拆开返回原来的实际子件。

显卡、固态硬盘、电源、风扇和散热器的配置 T 由性能模块决定；内存按总额定容量分档。通用机体不压低配置 T，显存和存储颗粒不压低对应性能档位。显存／内存／存储颗粒最多装 6／6／4 组，至少一组即可装配。额定容量与完好容量分别显示。

## 工作间与电脑储物箱

工作间有 32×10 格库存（320格）、配件工作台、主板托架、机箱面板与储物箱，使用固定出口回到店铺。储物箱只接收电脑整件、装配子件和六种维修／回收材料：电子零件、电线、螺丝、塑料、废金属、金属锭。手册、名片、灯牌和能量电池禁止新入箱；已有箱内记录仍可正常取出，取出后不能再存回。

- 物品逐件保管和取出，保留其身份、方向、状态、价格及已装配内容。未组装物品按当前名称、单件价格和完好／破损状态合并显示，数量表示实际件数，不是实体堆叠；悬停和取出始终对应一件实际物品。
- 储物目录支持名称／型号／ID 搜索，类别、完好／破损与拆机／配件类型筛选及清空条件。各条件同时生效；同档关窗、离开工作间和重建界面后保留，切档或重启清空，条件不写入存档。搜索稍停后更新，Enter 立即应用。
- 工作间支持拖放、按游戏绑定的 Q/E 旋转、双击放入兼容空槽，以及在空地框选后群拖入箱。
- 自动填槽优先取工作间库存，再从储物箱补足；只取完好兼容件，按配置 T 与容量排序，只填空槽，不替换已安装件，最后仍需手动确认组装。
- 面板可拖动；Esc 先收起筛选浮层，再关闭窗口。关闭装配面板保留槽内物品；切换类别或模式会归还相关物品，空间不足时保持原状。

## 开箱、主板与机箱装配

破损机箱是上锁物资箱，内容和物品身份已经固定。放入工作间机箱面板主槽，选择“拆卸”，一次取出全部内容；库存空间不足时保留原箱，重试不重抽。开完的空破损机箱价值为零，开箱不需要拆机螺丝刀。

主板安装 CPU、CPU 散热器、显卡、内存和固态硬盘；机箱安装主板、电源和风扇。T1–T3 主板支持单显卡，T4/T5 支持双显卡；T1/T2 有两个内存槽，T3–T5 有四个。SSD 槽按主板 T1–T5 为 1／1／1／2／2，支持各档 SSD；机箱风扇槽为 1／1／2／3／4。

允许保存部分装配，装配成功与通过检测是两回事。拆出主板时保留其内部组成，需再拆主板才能取出下一层配件；六类配件继续在配件工作台拆成子件。拆卸返回实际物品及原有价格、容量、状态和方向。

## 维修、回收与报废

所有维修均在工作间执行，维修材料可直接使用工作间库存和储物箱。先拆出内部物品，再处理本体；修复本体不会自动修好内部坏件。

**主板和机箱本体材料维修只限拆空的 T1–T3 本体：**

- 主板：T1 电子零件×1；T2 再加线缆×1；T3 再加金属螺丝×1。
- 机箱：T1 废金属×1；T2 再加金属锭×1；T3 再加打印耗材×1。

材料齐全后一次扣取，维修进度不跨次累计；修好的本体保留在当前主槽。T4/T5 主板与机箱本体不能用上述方式维修。

**现金维修：**通用机体、风扇转子、导热组件与散热器风扇模块，费用为保存的完好价的 25%，向下取整；非零价值最低收费 1。电源模块按材料维修，电子零件和线缆各自的数量按 T1–T5 为 1／1／2／2／3。CPU、GPU 核心、SSD 主控及容量芯片不能直接维修。

**批量回收：**一次处理指定正整数件数，数量包含当前目标。要求同物理规格、同完好／破损状态，来源、价格和朝向可以不同。工作间优先、储物箱补足，按本批实际当前总价的 50% 预算兑换材料；数量、残值或产出空间不足不扣物品，预算不跨次累计。破损 CPU 和拆空的 T4/T5 坏主板／坏机箱有对应材料回收路线；普通零价值空破损机箱不会凭空产出材料。

残值不足时显示本次件数、至少所需总数和还差几件。现有同规格物件足够时，按实际残值及选件顺序计算；现有全部仍不够时，按目标单件残值估算并标注。零残值不能估算时另作说明。点击数量框后，鼠标移出窗口仍可输入，点击外部结束编辑。

**报废：**不可维修的坏件，价值为零或单件回收无产物时，可点击“报废”再确认。仅删除当前目标，不产出金钱或材料；换件、切换模式或关闭面板会取消确认。

## 检测、估价与买家

完整整机需要主板、CPU、CPU 散热器、显卡、内存、至少一块存储设备、电源和风扇。所有已装配件须完好，内存配置 T 相同，内部档位跨度不超过三档；额外安装的显卡、内存和 SSD 也会检查，机箱自身档位不参与内部档位比较。

裸的完好主板可作为散件出售；已装配主板须 CPU、散热器、显卡和内存齐全，并满足完好、内存同档和跨度条件，额外显卡／内存／SSD 槽可留空。

通过检测的整机只获得一种机型加价：整机 20%、性价比机器 25% 或刀把机 10%。CPU 和显卡都比其他内部配件的最高档低至少一档时，判为刀把机；仅其中一个偏低不算。性价比机器要求除 CPU、显卡和电源以外的配件均低于 CPU 和显卡，电源要求不低于两者较高档减一；机型判定先检查刀把机。

下城区装机佬收购符合条件的完好 T1–T3 商品，不收刀把机或不完整整机；上城区收购商要求参与检测的实际内部配件全部完好且为 T3–T5。最终成交仍使用原版预算、声望与交易流程。

完好的 T4 CPU／显卡和 T5 配件有 20% 高端配件加价。装入完好机箱后，整机从合格内部配件继承一次，不按件数叠加；取出最后一件后取消。空机箱和破损物资箱不继承，机型与高端加价在不同计价阶段计算。

## 物品来源、供应者与洛夕

街区拾荒仍可获得低档坏电脑物品；原版拾荒客和小偷不额外混入电脑配件，电脑货源由专属供应者与装机佬提供。

博士在玩家未持有时出售 2×2 电脑灯牌，基础价值 100。电脑材料卖家从第 5 天起每日来访概率为 30%，展示灯牌后为 75%；电脑配件小偷从第 12 天起为 20%，展示后为 40%。两类开店时独立判定，各每天最多一位，可同日出现，追加到已有来客之后；同类灯牌不叠加，读档不重抽，也不显示在原版“预期的普通顾客”名单中。

**电脑材料卖家：**每访提供 1–2 件 T1–T3 完好随机配件、额外一颗 T1–T3 完好 CPU，以及四种维修材料各一份。随机配件与额外 CPU 的 T1／T2／T3 均按 50%／30%／20% 独立抽取；额外 CPU 不占随机名额，随机配件也是 CPU 时仍追加。材料固定带金属螺丝和电子零件，另两种从线缆、打印耗材、废金属和金属锭中不重复选择。

**电脑配件小偷：**每访提供 2–3 件 T4/T5 配件，档位权重 90%／10%；首件完好，后续各自按完好／破损 50% 判定。破损货不含 CPU 与主板，货物按原版标为赃物。两类供应者都不直接出售机箱或拆机子件，访客货单及实际交付保存，重试或读档不重抽、不重复上柜。

**下城区装机佬：**第 5 天首次安排，此后周五固定来访，同日随机来访合并。出售低档机箱，以及每访 3–4 类 T1–T3 坏配件，覆盖八类非机箱整件；名片和手册报价与机械货物分开。

**洛夕（Vesper，0504）：**首次购买或实际持有名片后，在当前新档永久解锁；只将名片上柜不会解锁，随后丢失或出售名片不会取消联系。电话来访有三天冷却，实际入队后才开始记录；到店带两只 T1–T3 破损机箱和一只随机档位完好机箱。洛夕、下层装机佬和上城区收购商使用各自的立绘；装机佬不再出售旧拆机工具或库存钥匙。

**治安收缴商江白：** 每周四固定排在待访队首，大、小收缴配件盒独立供货，各最多一只。大盒售价850，小盒290；付款前看不到内部，玩家持有后随时可打开。大盒为 3×10 格，出 T2–T5；小盒为 5×3 格，出 T1–T4。每盒有一件完好电脑配件，另加最少可用拆机子件（大盒最多3件、小盒最多2件），不保证填满。单盒基础回款限制为售价的60%–170%；每盒最多三次装箱尝试，失败则本次缺货，另一盒仍独立供货。内容固定，已有盒子不重掷。物件逐件带赃物标记及10–50热度；开封后盒体价值为零，空盒仍可复用，购买增加治安部声望。

## 五日电脑行情事件

“AI需求暴涨”和“虚拟币暴涨迎来狂潮”进入原版普通事件随机池，互不同时生效。前者让完整显卡及内存条的买入、卖出和估价变为两倍，后者让完整显卡变为三倍；每次从触发当天起持续五个游戏日。完好、破损、散放或装在整机中的配件均受影响，拆解后的子件和未开封盲盒不涨价。若与原版材料行情重叠，先计原版效果，再计电脑行情倍率。

## 机房、矿机与服务器

在工作间点击墙上挂钩的钥匙进入机房，从机房门返回工作间。首次启用机房需支付 120 C，开放一个机位；随后逐阶段扩建供电与散热，两项均完成后开放更多机位和更高档机器，最多十个机位。

矿机与服务器各有 T1–T5 专用机箱，占格 5×3。先在机房的开放机位选择工作间或储物箱中的专用机箱上架，再从机架面板选件安装。矿机自带主板、电源与散热，玩家另装显卡；服务器另装 CPU 和内存。专用机箱不能使用原版机箱界面直接拖入配件，须通过机房机架面板的专用装配流程操作。已装好的机器可以放入开放机位，取回时保留实际配件与物品身份。

- 矿机可开关、选择币种和设置运行优先级。算力按实际显卡配置与显存计算；缺件、坏件不能运行。休息跨日时结算进度与运行费，现金不足按优先级安排可支付的机器，手动停机与预计欠费分开显示。
- 服务器按实际 CPU、内存计算性能，可签订 3／5／7 日租约。签约时预收扣除运行费后的净租金，租期内锁定机器，不能取回或改装；到期后解除。报价以界面当前显示为准。
- 两个机柜显示十个机位及锁定、空位、运行、停机、出租状态；悬停查看用途、配置、停机原因或租期。
- 洛夕可回收各档专用矿机与服务器，包括空机和未装满的机器；价格按实际本体、内部配件及当前行情／议价计算，不叠加普通整机机型加价。

## 机房电脑、五币与硬件预订

点击机房电脑打开工作站浏览器，进入“机房管理”“币市交易所”或“洛夕硬件预订”。浏览器支持页面导航、刷新、最小化与任务栏恢复；管理页面查看配置、费用、租约、扩建和机器状态。

五种币为锚点、矩阵、星核、脉冲与奇点，各有独立图标和历史价格。矿机挖矿进度按币种汇总；交易按 0.1 枚的整数倍进行，买卖两侧各收 1% 手续费。钱包显示持有数量、持仓成本、平均成本、市值和盈亏；运行费用单独计算。币价会随游戏日变化，报价日与当前日分别显示，原有电脑行情事件也会影响币市；收益没有固定保证。

洛夕硬件预订最多保留三个未购订单，预订本身免费，可订档位随机房扩建开放。下一游戏日开店时，洛夕优先送货，到店后按实际报价议价付款。柜台摆不下的订单逐日重带，本次买货腾出空间不会立即补摆；已经摆出但离店前未买的订单取消。订单进入送货队列后到本次离店前不能修改，未摆出的订单离店后可再改或取消。此送货安排与拨打 0504 的普通来访分别处理。

## 工作间与机房声音

点击两处桌面音响打开共用声音设置，分别调整音乐、环境与操作音量，范围 0–100%，默认 60%／35%／70%，可恢复默认。设置保存在本机偏好，两个区域共用。

工作间与机房使用各自的音乐和环境声音，进入时接管原版音乐，离开后恢复原曲及进度。修复磁带播放时进入工作间会跳到下一首并压掉区域音乐的问题；音轨采用压缩资源及异步加载。

## 十页电脑装机交易手册

下城区装机佬出售手册，占格 2×3，基础价值 25，实际售价按原版交易结算。已在店铺、工作间或储物箱持有手册时不补售。

双击阅读，使用原版翻页、关闭、拖动与音效。十页介绍简介、开箱、可维修物品、维修与批量回收、开机检测、机型、示例和高端配件；中文正文使用原生文字与独立配图，保留纸张和页码。重开回到第一页；旧英文页面因契约不匹配时，整本回退中文。

## 卸载

关闭游戏，从 `Mods` 删除 `PCExpansion.dll`；已装英文时一并删除 `PCExpansionEnglishPatch.dll`。请单独保留存档备份；卸载后不保证含本 Mod 物品的存档可以继续使用。

## 问题反馈

同时启用会替换店铺场景的第三方 Mod 时，桌面钥匙仍可能被遮挡；这项兼容问题尚未解决。

- **邮箱：** qq2147058809@gmail.com
- **QQ 反馈群：** 157249439
- **Discord：** `qq2147058809`

请附复现步骤、游戏与 MelonLoader 版本及相关日志；多 Mod 共存问题请同时提供已启用的 Mod 列表。

## 鸣谢、版权与自愿支持

感谢《Probably Stolen》及 MelonLoader 的开发者。游戏本体与原版素材的权利归各自权利人所有。

[爱发电：aming567](https://afdian.com/a/aming567)

支持完全自愿，不影响 Mod 的免费下载和正常更新，也不用于解锁专属文件或抢先版本。

---

# PC expansion

**Author:** 阿铭
**Game:** Probably Stolen Demo
**Version:** 2.0, updated 2026-10-13.
**Test status:** Basic tests of the current Chinese build have passed, with no newly reported bugs. The English content synchronized in this update still needs in-game layout and glyph checks.

## Install and Save Requirements

Use matching `PCExpansion.dll` and `PCExpansionEnglishPatch.dll`. Version 2.0 retains the current save checks: valid version markers of 1.0 or higher are accepted, while missing, invalid, or lower markers are rejected. The version update does not raise the save threshold. It does not migrate or directly rewrite player saves; keep separate backups.

Close the game before replacing DLLs. Remove old-named base and language DLLs from `Mods` to prevent duplicate loading: `ProbablyAssembled.dll`, `PcRepairMod.dll`, `ProbablyAssembledEnglishPatch.dll`, and `PcRepairEnglishPatch.dll`.

Install the base DLL in the game's `Mods` folder. For English, install the matching English patch alongside it. The patch changes only this mod's text and manual; it does not change the game's language setting or register gameplay. Without the patch, the mod uses Chinese. Remove the patch and fully restart to return to Chinese. Incompatible translations or manual pages fall back to Chinese.

This project uses MelonLoader **0.7.3 Open-Beta (IL2CPP x64)**; no other mod is required. Use the [official MelonLoader installer](https://github.com/LavaGang/MelonLoader) for the game executable. Launch and close the game once so `Mods` is created. Restart fully after installing or updating. Install the English patch only if you want English. Check the MelonLoader log if the mod does not load.

Version 2.0 provides 737 matching catalog entries per language and retains the ten-page manual under contract v14. English remains an optional separate patch; the Chinese base has no dependency on it.

## Computer Items and the Workroom

The mod adds 90 intact/broken whole computer items, 41 part specifications with both states (82 part IDs), three utility items, two confiscated-parts boxes, and ten dedicated mining/server cases across T1-T5: 187 registered items in total. Whole-item families are cases, PSUs, motherboards, SSDs, RAM, GPUs, fans, CPUs, and CPU coolers.

Click the keychain on the shop desk to enter the workroom. It is a scene entrance, not an inventory key. The workroom has a 32x10 inventory (320 cells) and a fixed exit back to the shop. Storage admits whole PC parts, assembly subparts, and six repair/recycling materials: electronic parts, wire, screws, printer filament, scrap metal, and metal ingots. Manuals, contact cards, signs, and Energy Cells cannot be newly stored; existing stored items can still be withdrawn. Items are kept and transferred individually. Unassembled items with the same current name, unit price, and condition may share a catalog row; hovering and withdrawal always refer to an actual item with its own identity and contents.

The compact storage window supports name/model/ID search, category, condition, and subpart/whole-part filters, plus clearing filters. All conditions apply together. They persist across closing, leaving the workroom, and rebuilding the UI in the same session; changing saves or restarting clears them, and filters are not written to saves. Search updates after a short pause; Enter applies it immediately. Esc hides the filter popup first, then closes the window. Drag the title to move it. In the workroom, drag over empty space to select items; a selected group can be dragged into the box. Single-item dragging retains Q/E rotation.

The parts workbench handles six families: GPU, RAM, SSD, PSU, fan, and cooler. Place a common body in the left slot to show its assembly slots. GPU, SSD, PSU, fan, and cooler tiers follow performance modules; RAM tier follows total rated capacity. Capacities add across actual matching modules. Common bodies and GPU/SSD capacity modules do not lower performance tiers. GPU/RAM/SSD capacity slots allow up to 6/6/4 modules; one is sufficient. Auto-fill uses empty compatible slots, and final assembly remains a manual confirmation.

CPUs remain whole items. The first inspection of a damaged component fixes its faulty subparts; retries and reloads do not reroll them. Dismantling an assembled component returns its original actual subparts.

Changing workbench family or mode returns slotted items together. Insufficient space leaves the session unchanged. A finished assembly returns to workroom inventory. Esc hides the panel while keeping its stored items.

## Cases and Motherboards

Damaged cases are locked loot crates. Their contents and item identities are fixed at generation. Put one in the case panel's main slot and choose Disassemble. All contents move to workroom inventory together; insufficient space leaves the crate sealed. Retrying does not reroll loot. An opened empty damaged case is worth zero. No screwdriver or retired case UI is used.

Install CPU, cooler, GPU, RAM, and SSD on a motherboard; install the motherboard, PSU, and fans in a case. SSD slots by motherboard tier are 1/1/1/2/2 and accept any SSD tier. T4/T5 boards support two GPUs. T1/T2 boards have two RAM slots and T3-T5 have four; case fan slots across T1-T5 are 1/1/2/3/4. Partial or mixed-tier assemblies may be saved; trade eligibility is checked separately.

Disassembly returns the actual stored parts with their identities, prices, capacities, condition, and orientation. A motherboard remains an assembly when removed from a case; disassemble the motherboard to access its internal parts, then use the parts workbench to split the six families into subparts.

## Repairs, Recycling, and Discard

All repairs use the workroom. Motherboard and empty case body repairs are limited to T1-T3. Remove all internal items first. Motherboard: one electronic part at T1, plus wire at T2, plus screws at T3. Case: one scrap at T1, plus metal ingot at T2, plus printer filament at T3. All required materials are consumed together, with no progress carried between attempts. The repaired body stays in its main slot; internal faults are separate.

Common bodies, fan rotors, thermal modules, and cooler fan modules use cash repair: 25% of saved intact value, rounded down, minimum 1 for nonzero value. A PSU power module includes capacitors and uses electronic parts plus wire: one each at T1/T2, two each at T3/T4, three each at T5. CPUs, GPU cores, SSD controllers, and capacity chips cannot be repaired.

Recycling takes a positive batch quantity including the target. Items must share physical specification and intact/broken state; origin, price, and orientation may differ. Use workroom stock first, then storage. The budget is 50% of the batch's actual current total value. Insufficient quantity or output space consumes nothing; unused budget does not carry over. Broken CPUs and empty broken T4/T5 motherboards/cases have their current material-recovery routes; a normal zero-value empty broken case does not gain free materials.

When residual value is insufficient, the preview shows the current, required, and additional item counts. Exact counts follow actual owned values and selection order; beyond current stock, the target's unit value is used for an explicitly labeled estimate. Zero-value cases receive a separate explanation. Quantity input remains active when the pointer leaves the window and ends when clicking outside.

An unrepairable broken item worth zero or yielding no materials from single-item recycling can be discarded. Click Discard, then Confirm Discard. Only the current target is removed, producing no cash or materials. Changing target or mode, or closing the panel, cancels confirmation.

## Power-on Checks and Trading

A complete PC requires its necessary part types and at least one storage device. All installed parts must be intact, all RAM configuration tiers must match, and the internal tier gap must be no more than three. Extra installed parts are checked too; the case tier is excluded from the internal comparison.

A bare intact motherboard can be sold as a loose part. An assembled motherboard must pass its own check: CPU, cooler, GPU, and RAM present, all actual parts intact, matching RAM tiers, and a gap of three or less. Extra GPU, RAM, and SSD slots may stay empty.

A passing PC gets exactly one build profile: Complete PC (20%), Value Build (25%), or Bottleneck Build (10%). A Value Build has every part other than the CPU, GPU, and PSU below both CPU and GPU tiers, and the PSU must be at least the higher core tier minus one. A Bottleneck Build has both CPU and GPU at least one tier below the highest-tier other internal part. One low core alone is insufficient.

The Lower-District PC Builder buys eligible intact T1-T3 goods and rejects Bottleneck Builds. The Uptown PC Buyer requires every actual part, including nested and extra parts, to be intact and T3-T5. Trades still use the game's budgets, reputation, and deal rules.

Intact T4 CPUs/GPUs and intact T5 parts receive a 20% High-End Parts Markup. An intact case inherits it once from qualifying internal parts; multiple parts do not stack it, and removing the last qualifying part removes it. Empty cases and damaged crates do not inherit it. Build and high-end bonuses occur at separate price stages.

## Visitors, the Computer Sign, and 0504

District scavenging can yield broken low-tier computer goods. Computer stock now comes from dedicated mod suppliers and builders; the mod no longer adds bonus computer stock to vanilla scavengers or thieves.

Dr. Jackson offers the 2x2 Computer Sign, base value 100, while you do not own one. From day 5, the PC Materials Seller has a 30% daily chance, raised to 75% by a displayed Computer Sign. From day 12, the PC Parts Thief has a 20% chance, raised to 40%. Multiple signs do not stack. Both rolls occur independently when the shop opens. Successful visitors join the day's queue and do not appear in the vanilla Expected Normal Customers list.

The seller brings 1-2 random intact T1-T3 parts, one additional intact T1-T3 CPU, and one of each of four repair materials. The random parts and extra CPU independently use T1/T2/T3 weights of 50%/30%/20%. The extra CPU does not use a random-part slot and is still added if a random pick is also a CPU. Screws and electronic parts are guaranteed; two different additional materials are drawn from wire, filament, scrap, and ingots. The thief brings 2-3 T4/T5 parts (90%/10%): the first is intact and later picks independently roll intact/broken at 50% each. Broken CPUs and motherboards are excluded, and neither supplier sells cases or subparts directly. Thief goods receive the vanilla stolen-goods treatment. Per-visit plans and confirmed deliveries prevent re-rolling or duplicate stock after retries or reloads.

The Lower-District PC Builder visits on day 5 and Fridays afterward, with same-day random appearances merged. The builder sells low-tier cases and 3-4 different T1-T3 broken part families per visit, covering the eight non-case whole-component families. Buying or actually owning Vesper's 0504 business card once unlocks the contact permanently in that new save. An unsold offer alone does not unlock it; losing the card afterward does not remove access.

Vesper's phone visits have a three-day cooldown recorded only after the visitor actually enters the queue. She brings two broken T1-T3 cases and one intact case of a random tier. Builders do not sell retired screwdrivers or inventory workroom keys. Shared NPC portraits remain in the base mod; the patch supplies English names and dialogue.

Jiang Bai visits at the front of the Thursday queue. Large and small sealed parts boxes are supplied independently, at most one each, for 850 and 290 respectively. Contents remain hidden until purchase. The 3x10 large box uses T2-T5 items; the 5x3 small box uses T1-T4. Each contains one intact whole PC part plus the minimum viable subparts, up to three in the large box and two in the small box; full occupancy is not guaranteed. Base returns are limited to 60%-170% of the purchase price. Each box gets at most three filling attempts; failure leaves that size out of stock without affecting the other. Generated contents are fixed. Items carry individual stolen-goods heat of 10-50. Opening makes the reusable box itself worth zero, and purchases raise Security Department reputation.

## Five-Day PC Market Events

AI Demand Surge and Crypto Boom join the vanilla ordinary event pool and cannot run together. AI Demand Surge doubles whole GPU and RAM prices; Crypto Boom triples whole GPU prices. Each lasts five in-game days including its starting day and affects buying, selling, and estimates. Intact and broken parts count both loose and installed in a PC. Dismantled subparts and unopened loot crates are excluded. Vanilla materials events apply first, followed by the PC market multiplier.

## Server Room, Mining Rigs, and Servers

Use the key on the workroom wall to enter the server room and its door to return. Activation costs 120 C and opens one rack slot. Complete both power and cooling upgrades at each stage to unlock more slots and higher-tier hardware, up to ten slots.

Mining rigs and servers each have dedicated T1-T5 cases occupying 5x3 inventory cells. Select a case from workroom inventory or storage, place it in an unlocked server-room slot, then install parts through its rack panel. Rigs include the motherboard, PSU, and cooling but need installed GPUs; servers need CPUs and RAM. Dedicated cases reject direct part insertion through the vanilla case interface. Use their server-room rack assembly flow. Rack installation and retrieval preserve the actual machine and its parts.

- Mining rigs support power controls, coin selection, and run priority. Compute follows actual GPU configuration and VRAM; incomplete or broken machines cannot run. Sleeping settles progress and running fees. When cash is short, priority determines which rigs can be paid for. Manual shutdown and projected fee shortfalls are shown separately.
- Server performance follows actual CPUs and RAM. Lease for 3, 5, or 7 days, receiving net rent after prepaid running fees when signing. Leased machines are locked against retrieval and modification until expiry. Use the current quote shown in the UI.
- Both cabinets show locked, empty, running, stopped, and leased states. Hover for configuration, purpose, shutdown reason, or lease details.
- Vesper buys dedicated rigs and servers of all tiers, including empty and partially assembled machines. Quotes use the actual body, installed parts, current markets, and normal negotiation, without the ordinary complete-PC build bonus.

## Workstation, Five Coins, and Hardware Orders

Open the server-room computer to browse management, the coin exchange, and Vesper's hardware catalog. The workstation supports navigation, refresh, minimization, and taskbar restoration. Management shows configuration, fees, leases, upgrades, and machine state.

Anchor, Matrix, Stellar, Pulse, and Singularity each have an icon and price history. Mining progress is pooled by coin. Trades use multiples of 0.1 coin, with a 1% fee on both purchases and sales. The wallet shows holdings, cost basis, average unit cost, value, and profit/loss; machine running fees are separate. Prices change with game days, and quote day is shown separately from current day. PC market events also affect coin prices. Returns are not fixed.

Reserve up to three unpurchased machines from Vesper. Ordering is free; available tiers follow room upgrades. Vesper delivers at the front of the next in-game day's opening queue, with payment negotiated from the actual quote at arrival. Orders that do not fit on the counter return on later days. Making space by buying stock does not trigger another delivery that visit; delivered orders left unpurchased when Vesper leaves are canceled. Orders lock from delivery queue entry until that visit ends; undelivered orders can then be changed or canceled. Hardware delivery is separate from her ordinary 0504 phone visit.

## Workroom and Server-Room Audio

Click either desktop speaker to open shared music, ambience, and effects controls. Each ranges from 0-100%, with defaults of 60%/35%/70% and a reset button. Both rooms use the same locally saved preferences.

The rooms use their own music and ambience, taking over vanilla music on entry and restoring the same track and position on exit. Fixed cassette playback skipping to the next track and suppressing room music when entering the workroom. Audio uses compressed assets and asynchronous loading.

## Ten-Page Manual

The Lower-District PC Builder offers the PC Assembly and Trading Manual, a 2x3 document with base value 25. It is offered only when you do not already hold one, including workroom and storage custody. Card/manual offers do not block normal mechanical stock.

Double-click to read using the game's native book controls. Both languages use native text with separate component illustrations. The ten pages cover the introduction, case opening, repairable items, repairs and batch recycling, power-on checks, build profiles, examples, and high-end parts. Invalid, mismatched, or unrenderable English pages fall back to the complete Chinese manual. Reopening starts at page one.

## Uninstall

Close the game, then remove `PCExpansion.dll` and, if installed, `PCExpansionEnglishPatch.dll` from `Mods`. Keep a separate save backup; continued use of a save containing mod items is not guaranteed after uninstalling.

## Bug Reports

Third-party mods that replace the shop scene may still cover the desktop key. This compatibility issue remains unresolved.

Email: qq2147058809@gmail.com
QQ group: 157249439
Discord: `qq2147058809`

Include reproduction steps, game and MelonLoader versions, relevant logs, and the list of installed mods if the problem involves running mods together.

## Voluntary Support and Credits

[Support 阿铭 on 爱发电](https://afdian.com/a/aming567). Support is optional and does not change free access to the mod or unlock exclusive files.

Thanks to the developers of Probably Stolen and MelonLoader. The game and original assets remain the property of their rights holders.
