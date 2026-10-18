# 深空装机 / PC expansion

**作者：** 阿铭
**游戏：** Probably Stolen Demo
**版本：** 2.6（2026-10-19，中英文配套）
**测试状态：** 机房完整经营流程、中英文界面与存读档仍需游戏内验证。

## 安装前必读

- **2.6需要新档：新建存档写入2.6标记，仅接受有效的2.6及以上标记。** 不迁移旧档，不直接改写玩家存档。原1.0档请继续使用保留的2.3.1；2.6新档不要交给旧版继续游玩，也不要手改版本标记绕过检查。请分别保留备份。
- 中文只需 `PCExpansion.dll`；英文另加同为2.6的 `PCExpansionEnglishPatch.dll`。本体与英文补丁请配套更新。
- 开箱、拆装、维修、回收和报废都在工作间完成。点击店内桌面的 CPU 钥匙串进入；它是场景入口，不占玩家库存。

## Mod 简介

《深空装机 / PC expansion》为《Probably Stolen Demo》加入电脑拾荒、拆检、维修、组装和交易玩法。打开破损机箱寻找配件，把坏件拆到更小的部件，再决定维修、更换、回收或报废；将合适的配置组装成主板和整机，检测、估价，并向不同买家出售。

模组提供独立工作间、共用电脑储物箱、专属供应者和装机佬、二十页游戏内手册，以及可扩建的机房。组装矿机挖取五种虚拟币，配置服务器签订租约，再通过机房电脑管理机器、交易和洛夕的硬件预订。在深空当铺，一只磕碰变形的机箱里，可能还藏着能用的配件；一台缺件的电脑，也可能在重新装配后成为值得出售的整机。

## 安装、更新与语言

本项目使用 **MelonLoader 0.7.3 Open-Beta（IL2CPP x64）**，无需其他 Mod 前置。

1. 完全关闭游戏。
2. 尚未安装 MelonLoader 时，从[官方 GitHub 页面](https://github.com/LavaGang/MelonLoader)获取安装器，选择《Probably Stolen Demo》的游戏程序及上述版本安装。可在 Steam 中选择“管理 → 浏览本地文件”定位游戏程序。
3. 安装加载器后启动一次游戏，再退出，让游戏目录生成 `Mods` 文件夹。
4. 如果 `Mods` 中还有 `ProbablyAssembled.dll`、`PcRepairMod.dll`、`ProbablyAssembledEnglishPatch.dll` 或 `PcRepairEnglishPatch.dll`，先移出这些旧文件，避免重复加载。
5. 将 `PCExpansion.dll` 放入 `Mods`。中文无需英文补丁；使用英文时另放入同版本 `PCExpansionEnglishPatch.dll`。
6. 启动游戏并新建2.6存档；已有2.6测试档须通过版本检查。若模组未加载，检查 MelonLoader 日志。

更新时先完全关闭游戏，再替换DLL并重启。本体独立提供中文；配套英文补丁提供全部1287条文本与二十页英文手册。移出英文补丁并重启即可恢复中文。版本不匹配或译文无效时回退中文，手册版本不匹配时整本回退。

## 开局特性

新档可在原版特性界面选择以下三项，沿用原版点数、槽位和首次开局限制，三项之间没有互斥。

- **高效利用（2点）：**工作间回收材料的价值预算从当前总价的50%提高至125%，其余回收资格、取整与余值规则不变。
- **电脑学徒（1点）：**获得洛夕0504名片、电脑手册，以及电子零件、金属锭、金属螺丝、3D打印耗材、废金属、线缆各一份，并永久解锁洛夕的联系号码。
- **虚拟币专家（1点）：**免费解锁基础T1机房，获得一台T1矿机及一张T1显卡；扩建与运行费用照常。

只在选中特性的本次新档发放一次，旧档不补发，读档不重复发放。

## 硬件与内部拆装

九类完整硬件为机箱、电源、主板、固态硬盘、内存、显卡、机箱风扇、CPU 和 CPU 散热器，均有 T1–T5 及完好／破损两态，共 90 种；另有 41 种拆机子件规格及两态，以及名片、灯牌、手册、两种收缴盒和矿机／服务器各 T1–T5 的 10 种专用机箱。整件名称按“类别 T档·型号”显示，如“CPU T4·Core i9-14900K”，并保留容量、破损及已装配标记。

显卡、内存、固态硬盘、电源、风扇和 CPU 散热器可进一步拆成通用机体与内部子件；CPU 仍作为整件使用。破损配件首次拆检确定坏件分布，重试或读档不会重新抽取；已组装件拆开返回原来的实际子件。

显卡、固态硬盘、风扇和散热器的配置 T 由性能模块决定；内存按总额定容量分档，电源按电容额定总功率分档。通用机体不压低配置 T，显存和存储颗粒不压低对应性能档位。显存／内存／存储颗粒最多装 6／6／4 组，至少一组即可装配。额定容量与完好容量分别显示。

电源由通用机体和1～6组电容组成，可混装。额定总功率达到100／200／400／600／1000W对应T1–T5，超过1000W仍为T5；坏组计入额定功率，完好功率另列。首次拆解T1为100W×1、T2为100W×2，T3／T4／T5为200W×2／3／5；已有装配按实际组成拆出。

## 工作间与电脑储物箱

工作间有 32×10 格库存（320格）、配件工作台、主板托架、机箱面板与储物箱，使用固定出口回到店铺。储物箱只接收电脑整件、装配子件和六种维修／回收材料：电子零件、电线、螺丝、塑料、废金属、金属锭。手册、名片、灯牌和能量电池禁止新入箱；已有箱内记录仍可正常取出，取出后不能再存回。

- 物品逐件保管和取出，保留其身份、方向、状态、价格及已装配内容。未组装物品按当前名称、单件价格和完好／破损状态合并显示，数量表示实际件数，不是实体堆叠；悬停和取出始终对应一件实际物品。
- 储物目录支持名称／型号／ID 搜索，类别、完好／破损与拆机／配件类型筛选及清空条件。各条件同时生效；同档关窗、离开工作间和重建界面后保留，切档或重启清空，条件不写入存档。搜索稍停后更新，Enter 立即应用。
- 工作间支持拖放、按游戏绑定的 Q/E 旋转、双击放入兼容空槽，以及在空地框选后群拖入箱。框选物件还可保持相对位置整体平移；越界或碰到未选物件时整组不移动，不自动重排或群体旋转。
- 将配件单件拖到中央垫板并松手，可打开对应装配、拆卸或维修面板；完好配件只有在唯一兼容空槽明确时才自动入槽。替换面板目标时先归还旧物，放不下则保持原状；多件、材料和专用机箱不由垫板自动处理。
- 在六类配件、主板或机箱装配窗展开“填空筛选”，可设档位、相关容量、最低必要／填满／自定数量、节省材料／性能优先及工作间／储物箱来源。八类各自记住本机偏好；自定数量包含已放入件。点击“自动填空槽”填入可用的完好兼容件，保留已有件，缺件时先放入其他能用的物品并在顶部提示。主板“最低必要”含1块固态，0GB仅表示容量不限；自定数量可选0块。冲突或库存变化时不执行，最后仍需手动组装。
- 空槽单击选物，占槽单击查看、双击归还；来源可多选工作间／储存箱／店铺，支持名称、编号、配置搜索。浅黄描边提示当前适配件。
- 按住E＋左键，在店铺或可取容器与工作间间快捷转移；从工作间返回时优先原位，再尝试随身库存。空间不足保持原状，拾荒、拖动或输入期间禁用。机箱、主板分别拖到各自场景装配区，中间垫板处理配件。维修默认归还原来源，原处满则保留修好物品，支持关闭自动放回。
- 每日配件目录的库存数合计三来源同型号、同档位完好独立物品，已装在机器里的配件不计。
- 面板可拖动；Esc 先收起筛选浮层，再关闭窗口。关闭装配面板保留槽内物品；切换类别或模式会归还相关物品，空间不足时保持原状。

## 开箱、主板与机箱装配

破损机箱是上锁物资箱，内容和物品身份已经固定。放入工作间机箱面板主槽，选择“拆卸”，一次取出全部内容；库存空间不足时保留原箱，重试不重抽。开完的空破损机箱价值为零，开箱不需要拆机螺丝刀。

新生成的 T1–T5 普通物资箱基础价依次为 550／800／1250／2000／2800。开出物基础总值为箱价的 30%–200%，长期目标平均约 96.5%；10% 的目标落在 30%–90%、70% 在 90%–100%、18% 在 100%–130%、2% 在 130%–200%，整数金额会产生边界取整。随机档位与内容仍受对应箱体规则约束，不保证高档或完好配件。这些比例不含购入折扣、零售加价、行情、议价、固定高端加价或拆修收益；已固定内容不重抽，江白收缴盒沿用自己的规则。

主板安装 CPU、CPU 散热器、显卡、内存和固态硬盘；机箱安装主板、电源和风扇。T1–T3 主板支持单显卡，T4/T5 支持双显卡；T1/T2 有两个内存槽，T3–T5 有四个。SSD 槽按主板 T1–T5 为 1／1／1／2／2，支持各档 SSD；机箱风扇槽为 1／1／2／3／4。

允许保存部分装配，装配成功与通过检测是两回事。拆出主板时保留其内部组成，需再拆主板才能取出下一层配件；六类配件继续在配件工作台拆成子件。拆卸返回实际物品及原有价格、容量、状态和方向。

## 维修、回收与报废

所有维修均在工作间执行，维修材料可直接使用工作间库存和储物箱。先拆出内部物品，再处理本体；修复本体不会自动修好内部坏件。

**主板和机箱本体材料维修只限拆空的 T1–T3 本体：**

- 主板：T1 电子零件×1；T2 再加线缆×1；T3 再加金属螺丝×1。
- 机箱：T1 废金属×1；T2 再加金属锭×1；T3 再加打印耗材×1。

材料齐全后一次扣取，维修进度不跨次累计；修好的本体保留在当前主槽。T4/T5 主板与机箱本体不能用上述方式维修。

**现金维修：**通用机体、风扇转子、导热组件与散热器风扇模块，费用为保存的完好价的 25%，向下取整；非零价值最低收费 1。电源的每组损坏电容按额定功率维修：100／200W各需电子零件与线缆1份，400／600W各2份，1000W各3份；完好组不消耗材料。CPU、GPU 核心、SSD 主控及容量芯片不能直接维修。

**混合回收：**在原处理面板的“回收材料”页从工作间或储物箱选择物件，也可拖入工作间单件或框选多件；不同类别、规格与完好／破损状态可同时加入，逐组调整数量。逐件当前实价合计后取 50% 为预算；选中“高效利用”开局特性时提高至 125%，从电子零件、线缆、废金属、金属螺丝、3D 打印耗材中任选一种，按材料单价向下取整尽量多产出，不设每件／档位产量上限。余值不返现、不累计。拆机子件两态、破损 CPU 全档及拆空解锁的 T4/T5 坏裸主板／机箱沿用回收资格；完整配件须先拆解，零价值空坏箱不会凭空产出材料。

原面板内切换“可投入物件／本次回收清单”，显示来源、状态、单价和数量；下方选五种材料并预览总价、预算、材料单价、产量及余值，沿用原回收和取回按钮。确认前物件留在原位，只消耗明确选中的物件；关闭面板或切换处理模式清空本次清单。产物统一放工作间，先释放本批输入的占位；不足一份、全批空间不足或来源变化均不消耗，不自动部分回收或转存储物箱。数量框支持键盘输入，点击外部结束编辑。

**报废：**不可维修的坏件，价值为零或单件回收无产物时，可点击“报废”再确认。仅删除当前目标，不产出金钱或材料；换件、切换模式或关闭面板会取消确认。

## 检测、估价与买家

完整整机需要主板、CPU、CPU 散热器、显卡、内存、至少一块存储设备、电源和风扇。所有已装配件须完好，内存配置 T 相同，内部档位跨度不超过三档；额外安装的显卡、内存和 SSD 也会检查，机箱自身档位不参与内部档位比较。

裸的完好主板可作为散件出售；已装配主板须 CPU、散热器、显卡和内存齐全，并满足完好、内存同档和跨度条件，额外显卡／内存／SSD 槽可留空。

通过检测的整机只获得一种机型加价：整机 20%、性价比机器 30% 或刀把机 10%。CPU 和显卡都比其他内部配件的最高档低至少一档时，判为刀把机；仅其中一个偏低不算。性价比机器要求除 CPU、显卡和电源以外的配件均低于 CPU 和显卡，电源要求不低于两者较高档减一；机型判定先检查刀把机。

下城区装机佬收购符合条件的完好 T1–T3 商品，不收刀把机或不完整整机；洛夕普通电话来访可收 T1–T5 完好整件配件及六类全部完好拆解件（含无 T 档公共件），已装配主板仍须完整且兼容。洛夕收购普通整机、性价比机器和刀把机时，要求机箱与全部内部配件均为完好T1–T3，并通过完整性与兼容检查；普通来访整次共用 18000 预算；上城区收购商要求参与检测的实际内部配件全部完好且为 T3–T5。最终成交仍使用原版预算、声望与交易流程。

完好的 T4 CPU／显卡和 T5 配件有 20% 高端配件加价。装入完好机箱后，整机从合格内部配件继承一次，不按件数叠加；取出最后一件后取消。空机箱和破损物资箱不继承，机型与高端加价在不同计价阶段计算。

## 物品来源、供应者与洛夕

街区拾荒仍可获得低档坏电脑物品；原版拾荒客和小偷不额外混入电脑配件，电脑货源由专属供应者与装机佬提供。

博士在玩家未持有时出售 2×2 电脑灯牌，基础价值 100。电脑材料卖家从第 5 天起每日来访概率为 30%，展示灯牌后为 75%；电脑配件小偷从第 12 天起为 15%，展示后为 30%。两类开店时独立判定，各每天最多一位，可同日出现，追加到已有来客之后；同类灯牌不叠加，读档不重抽，也不显示在原版“预期的普通顾客”名单中。

**电脑材料卖家：**每访提供 1–2 件 T1–T3 完好随机配件、额外一颗 T1–T3 完好 CPU，以及四种维修材料各一份。随机配件与额外 CPU 的 T1／T2／T3 均按 50%／30%／20% 独立抽取；额外 CPU 不占随机名额，随机配件也是 CPU 时仍追加。材料固定带金属螺丝和电子零件，另两种从线缆、打印耗材、废金属和金属锭中不重复选择。

**电脑配件小偷：**每访提供 2–3 件 T4/T5 配件，档位权重 90%／10%；首件完好，后续各自按完好／破损 50% 判定。破损货不含 CPU 与主板，货物按原版标为赃物。两类供应者都不直接出售机箱或拆机子件，访客货单及实际交付保存，重试或读档不重抽、不重复上柜。

**下城区装机佬：**第 5 天首次安排，此后周五固定来访，同日随机来访合并。出售低档机箱，以及每访 3–4 类 T1–T3 坏配件；新货单从七类非 CPU 整件中抽取，不再出售坏 CPU。名片和手册报价与机械货物分开。

**洛夕（Vesper，0504）：**首次购买或实际持有名片后，在当前新档永久解锁；只将名片上柜不会解锁，随后丢失或出售名片不会取消联系。电话来访有三天冷却，实际入队后才开始记录；到店带两只 T1–T3 破损机箱和一只随机档位完好机箱。洛夕、下层装机佬和上城区收购商使用各自的立绘；装机佬不再出售旧拆机工具或库存钥匙。

**治安收缴商江白：** 每周四固定排在待访队首，大、小收缴配件盒独立供货，各最多一只。大盒售价850，小盒290；付款前看不到内部，玩家持有后随时可打开。大盒为 3×10 格，出 T2–T5；小盒为 5×3 格，出 T1–T4。每盒有一件完好电脑配件，另加最少可用拆机子件（大盒最多3件、小盒最多2件），不保证填满。单盒基础回款限制为售价的60%–170%；每盒最多三次装箱尝试，失败则本次缺货，另一盒仍独立供货。内容固定，已有盒子不重掷。物件逐件带赃物标记及10–50热度；开封后盒体价值为零，空盒仍可复用，购买增加治安部声望。

## NPC 剧情与外部设置

随着与洛夕的实际交易、治安部声望和相处进度推进，可遇到洛夕的寄存委托、江白的机房藏身故事，以及两人各五段日常小故事。核心故事和小节点有各自条件，每人每天最多一段小故事；包含少量物品委托、选择与后续来访。请留意对白中的交付要求与约定日期，相关进度随当前存档保存。

归还洛夕寄存物时，可从暗格等玩家已拥有且允许取出的储存空间，以及工作间和电脑储物箱提交。装进其他机器的配件仍需先拆出，并保持原物与内部原件完好。缺件时可以选择先整理，再于本次来访重新提交；整理不会延长期限，原有宽限与最终失败规则保持。

首次启动会在 `Mods/PCExpansion/Stories/npc-stories.json` 生成默认剧情设置；退出游戏后可修改开放的正文、对白分支和六项触发门槛，重启生效。已有自定义文件保留，修改不重置已进行的剧情。配置无效时整套使用内置默认并保留原文件；详见随附 `Stories/使用说明.txt`。配套英文补丁翻译默认剧情，玩家改过的中文正文继续显示自定义中文。

## 五日电脑行情事件

“AI需求暴涨”和“虚拟币暴涨迎来狂潮”进入原版普通事件随机池，互不同时生效。前者让完整显卡及内存条的买入、卖出和估价变为两倍，后者让完整显卡变为三倍；每次从触发当天起持续五个游戏日。完好、破损、散放或装在整机中的配件均受影响，拆解后的子件和未开封盲盒不涨价。若与原版材料行情重叠，先计原版效果，再计电脑行情倍率。

## 机房、矿机与服务器

在工作间点击墙上挂钩的钥匙进入机房，从机房门返回工作间。首次启用机房需支付 120 C，开放一个机位；随后逐阶段扩建供电与散热，两项均完成后开放更多机位和更高档机器，最多十个机位。

矿机与服务器各有 T1–T5 专用机箱，占格 5×3。先在机房的开放机位选择工作间或储物箱中的专用机箱上架，再从机架面板选件安装。矿机自带主板、电源与散热，玩家另装显卡；服务器另装 CPU 和内存。专用机箱不能使用原版机箱界面直接拖入配件，须通过机房机架面板的专用装配流程操作。已装好的机器可以放入开放机位，取回时保留实际配件与物品身份。

- 矿机可选择币种、矿池、节能／标准／超频模式及运行优先级。锚点币和奇点币使用核心计算，每卡至少4GB显存；矩阵币和脉冲币使用显存吞吐，至少8GB、16GB发挥全部能力；星核币使用大显存算法，至少16GB、32GB发挥全部能力。同机可混卡，显存不跨卡共享；坏卡与显存不足的卡不参与挖矿，也不计GPU电费。
- 产量由有效算力、网络竞争及当前难度共同决定。稳收池收取产币的2%；浮动池收0.5%，幸运系数为0.70–1.30，同日同币共享波动。切池次日生效，待清算产出不能提前卖；换币保留余额和细小产出余量。
- 机房按实际功率与散热负荷排机，有效租约先占容量，再按矿机优先级和机位顺序安排。可启用容量不足自动降档。电费先由现有现金支付，次日卖币不能垫付今夜费用；现金或容量不足的矿机会跳过。节能／标准／超频的算力倍率为0.82／1／1.12，GPU功率倍率为0.70／1／1.35，基础拒绝率为0.2%／0.5%／2%。
- 基础电价为每kW每运营日40 C，另计15%冷却；矿机整场电费向上取整。扩建一后可能提前公告次日起持续3–5日的±15%电价变化。请看今晚计划、储备现金和停机原因，再决定是否运行。
- 矿机维护度随运行下降，节能／标准／超频每日磨损0.6／1／2；低于60预警，低于30禁超频并增加3个百分点拒绝率，降至0保护停机。停机保养按T1–T5消耗电子零件1／1／2／2／3份及5／8／12／18／25 C，材料取自工作间或储物箱，齐备后一次恢复至100。搬动机器或换卡不重置维护，保养不修复坏配件。
- 服务器按实际CPU和内存决定容量，可签3／5／7日租约，租期加成为0%／3%／5%。签约预留供电与散热，锁定配置，按签约电价扣除全期电费后一次预收净款，整笔向下取整；租期内不重复收电费，不能取回或改装。账本逐日确认租金，尚未履约的预收不等于已赚利润。
- 两个机柜显示十个机位及锁定、空位、运行、停机、出租状态；悬停查看用途、配置、停机原因或租期。
- 洛夕可回收各档专用矿机与服务器，包括空机和未装满的机器；价格按实际本体、内部配件及当前行情／议价计算，不叠加普通整机机型加价。

## 机房电脑、五币与硬件预订

点击机房电脑打开工作站浏览器，进入“机房管理”“币市交易所”或“洛夕硬件预订”。浏览器支持页面导航、刷新、最小化与任务栏恢复；管理页面查看配置、费用、租约、扩建和机器状态。地址栏右侧、首页图标左边的主题图标按钮可切换标准浅色、柔和与深色，默认柔和，记住本机选择。

五种币为锚点、矩阵、星核、脉冲与奇点，各有独立图标和历史价格。币价含持续数日的上涨、盘整、下跌与随机冲击，电脑行情事件也会影响币市。低价不自动触发回涨，高风险币可能大幅贬值并长期无法回本。

**交易先看成交预览：**普通数量输入以0.0001枚为步长，钱包保留至0.000001枚，“全部卖出”可处理尾数。实际成交受当日深度和累计净买卖量影响，拆单不能消除滑点；买卖各收1%手续费，买入整笔向上、卖出整笔向下取整数C。净到账不足1 C的卖单拒绝，手动超深度订单须调整后重新确认，不自动部分成交。持仓参考市值不等于立即卖出可得的净现金。

**自动出售默认关闭：**可选择留币、支付运营或按当夜新产量的固定比例出售，编辑后应用草稿才生效。支付运营按已知计划和指定币序补足未来三夜电费目标；固定比例为0%–100%，每档10%。最低价按含滑点、手续费和取整的整笔净均价判断。自动出售在支付费用、产币到账后按新日报价执行，深度不足可部分成交，余币保留；请查看未售原因，不把预期收入当现有现金。

**经营账本：**保留最近90日明细与累计记录，区分现金流、生产、持币估值、未实现盈亏及未履约租金。买入币与矿币来源分别统计，但仍是同一钱包；电费、保养费、扩建和租约预收单列。硬件采购、修复及维护材料采购成本尚未完整归集，未知成本明确显示未知，不编造完整资产利润或精确回本日。硬件经济按约15个运营日回本的目标调校，实际会受行情、难度、配置、策略、停机和采购等待影响，没有回本保底。

洛夕硬件预订初期最多保留三个未购机器订单；完成她的寄存剧情后，解锁每日从四十种中选出的十种配件目录，机器与配件共享五个订单名额，洛夕出售的商品享九折优惠。预订本身免费，可订机器档位随机房扩建开放。下一游戏日开店时，洛夕优先送货，到店后按实际报价议价付款。柜台摆不下的订单逐日重带，本次买货腾出空间不会立即补摆；已经摆出但离店前未买的订单取消。订单进入送货队列后到本次离店前不能修改，未摆出的订单离店后可再改或取消。此送货安排与拨打 0504 的普通来访分别处理。

神秘人名片的用途说明显示在物品描述中，双击不再打开空白纸片。拨打27571，神秘人会清除当前犯罪计数与调查证据。成功处理后冷却10天；无记录或提前挂断不消耗冷却。已派出的执法人员和既成处罚不受影响。

## 工作间与机房声音

点击两处桌面音响打开共用声音设置，分别调整音乐、环境与操作音量，范围 0–100%，默认 60%／35%／70%，可恢复默认。设置保存在本机偏好，两个区域共用。

工作间与机房使用各自的音乐和环境声音，进入时接管原版音乐，离开后恢复原曲及进度。修复磁带播放时进入工作间会跳到下一首并压掉区域音乐的问题；音轨采用压缩资源及异步加载。

## 二十页电脑装机交易手册

下城区装机佬出售手册，占格 2×3，基础价值 25，实际售价按原版交易结算。已在店铺、工作间或储物箱持有手册时不补售。

双击阅读，使用原版翻页、关闭、拖动与音效。二十页介绍简介、开箱、可维修物品、维修与混合回收、开机检测、机型、示例和高端配件，并新增机房快速上手、矿机与算法、网络竞争与矿池、容量／电价／维护、服务器与深度交易、自动出售与经营账本；中文正文使用原生文字与独立配图，保留纸张和页码。二十页英文使用原版文字和共用配图。重开回到第一页；补丁不匹配或页面无法显示时整本回退中文。

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

## English Player Guide — 2.6

PC expansion adds PC salvage, modular repair and assembly, dedicated suppliers and buyers, a workroom, shared storage, and a server room with five game coins. Author: 阿铭. Requires MelonLoader 0.7.3 Open-Beta, IL2CPP x64; no other mod prerequisite.

### Install, update and saves

Close the game. Put `PCExpansion.dll` in `Mods`; add the matching **2.6** `PCExpansionEnglishPatch.dll` for English. The base alone uses Chinese. Remove the English DLL and restart to return to Chinese. Move old differently named base/patch DLLs out of `Mods` to avoid duplicate loading. A mismatched or invalid translation falls back to Chinese; incompatible manual pages fall back as a complete book.

**Start a new save for 2.6.** Saves require a valid `pcexpansion.save-generation` marker of 2.6 or higher. Old saves are not migrated. Continue 1.0 saves with the retained 2.3.1 build. Keep separate backups; never edit markers to bypass checks or continue 2.6 saves with an older build.

### Workroom and hardware

Use the CPU keychain on the shop desk to enter the workroom, and the wall key there to enter the server room. Nine whole-hardware families have T1-T5 intact/broken variants. Six families dismantle into common bodies and modules; CPUs remain whole. Installed identities, state, capacity and value survive disassembly. Damaged crate contents are fixed when generated and never reroll.

Common bodies do not lower the performance tier. GPU/RAM/SSD capacity modules have maxima of 6/6/4 and need at least one. RAM tier uses rated capacity. A PSU takes 1-6 mixed capacitor banks; total rated wattage thresholds are 100/200/400/600/1000W for T1-T5. Broken banks count toward rated wattage, with intact wattage listed separately. First disassembly gives 100W x1/x2 for T1/T2 and 200W x2/x3/x5 for T3/T4/T5; existing builds return actual contents.

Click empty assembly slots to select items, or occupied slots to inspect; double-click occupied slots to return them. Search and combine Room, Box and Shop sources. E + left-click sends shop/container items to the workroom; workroom items try their original position, then carried inventory. Full inventories cancel transfers. Scavenging, active dragging and text input disable this shortcut. Cases and motherboards have separate scene work areas; the central mat handles parts.

Auto-fill supports tier/capacity/source preferences and minimum, full or custom counts. Missing stock does not block other compatible placements. Existing items stay. Minimum Necessary for a board includes one SSD even at a 0GB capacity floor; Custom can request zero. Filling never assembles automatically. Repairs return to their original inventory by default; if full, repaired items remain in the slot. Daily catalog Stock counts intact standalone items of the same model/tier across workroom, storage and shop; installed contents are excluded.

### Repair, recycle and sell

Empty T1-T3 board bodies use Electronic Parts, adding Wire at T2 and Nuts at T3. Empty T1-T3 cases use Scrap Metal, adding Metal Ingot at T2 and Printing Plastic at T3; one of each listed material. Repairs require the whole recipe at once and do not repair internal parts. Common bodies, rotors and thermal/cooler fan modules cost 25% of intact value, rounded down, minimum 1C for nonzero value. Damaged capacitor banks use Electronic Parts and Wire: one each for 100/200W, two for 400/600W, three for 1000W. CPUs, GPU cores, SSD controllers and capacity chips cannot be repaired.

Batch recycling uses 50% of selected current value, or 125% with Efficient Use. Choose Electronic Parts, Wire, Scrap Metal, Nuts or Printing Plastic. Output rounds down; leftover value is neither refunded nor saved. All output must fit the workroom, or nothing is consumed. Whole modular parts must be dismantled first. Broken CPUs of any tier, eligible dismantled subparts and empty unlocked T4/T5 broken board/case bodies can be recycled; zero-value empty cases do not generate materials. Eligible irreparable items with no single-item output can be discarded without output.

A complete PC needs a board, CPU, cooler, GPU, RAM, storage, PSU and fans. Parts must be intact; installed RAM tiers must match, and internal tiers may span at most three. Extra parts are checked too. T1-T5 board SSD counts are 1/1/1/2/2; T4/T5 boards support two GPUs. Partial assemblies can be saved but do not qualify as complete PCs.

Profiles are exclusive: Complete PC +20%, Value Build +30%, Bottleneck Build +10%. A Bottleneck Build has both CPU and GPU at least one tier below the highest remaining internal tier. Value Builds require other parts below both core parts, excluding the PSU, which may be one tier below the higher core tier. Intact T4 CPUs/GPUs and T5 parts receive a separate 20% high-end premium, inherited once by an intact containing case. Profile and high-end percentages are not simply added.

The lower-district builder refuses Bottleneck Builds. Vesper accepts ordinary cases and internal parts that are all intact T1-T3, including Bottleneck Builds; standalone intact parts/subparts retain her broader buying rules. Her ordinary visit shares an 18,000 budget. The upper-district buyer requires inspected internal parts to be intact T3-T5. All sales still use native budget, ownership and bargaining rules.

### Server-room operations

Activate the basic room for 120C. Separate power/cooling upgrades unlock up to ten slots and higher case tiers. Rigs take GPUs; servers take CPUs and RAM. Use rack assembly, rather than the ordinary case interface. Removing machines retains actual contents and identities.

Anchor/Singularity use Core (4GB per GPU); Matrix/Pulse use Memory Bandwidth (8GB minimum, full at 16GB); Stellar uses Large VRAM (16GB minimum, full at 32GB). Mixed cards are allowed; VRAM never combines between GPUs. Broken or undersized cards contribute no work or GPU power cost. Network competition and difficulty affect production. Steady Pool deducts 2% of output; Variable deducts 0.5%, with same-day/same-coin shared luck of 0.70-1.30. Pool choices change next day. Output arrives at settlement; tiny remainders and balances persist.

Eco/Standard/Overclock hash-rate multipliers are 0.82/1/1.12, GPU power 0.70/1/1.35, base rejection 0.2%/0.5%/2%. Active leases reserve capacity first; rigs follow priority then slot number. Capacity-based auto-downshifting is optional. The shop's nightly costs are paid before rig scheduling. **Tomorrow's sale cannot pay tonight's power bill.** Unaffordable rigs are skipped while later affordable rigs may run.

Power costs 40C per kW per operating day plus 15% cooling, rounded up once across rigs. Announced power-price events after expansion 1 may change prices by +/-15% for 3-5 days. Maintenance wear is 0.6/1/2 per running day. Below 60 warns; below 30 disables overclock and adds 3 percentage points of rejection; zero stops the rig. T1-T5 servicing requires 1/1/2/2/3 Electronic Parts plus 5/8/12/18/25C, restoring maintenance to 100. Stop first. Moving machines or swapping cards does not reset maintenance; servicing does not repair damaged GPUs.

Server leases run 3/5/7 days with 0%/3%/5% bonuses. Signing locks configuration and reserves capacity; term power is prepaid at the signing-day rate, and net cash rounds down. No repeated power charge during the term. Prepaid cash is available to spend, but rent is earned over service days. Machines cannot be changed or removed until expiry. Vesper accepts dedicated rig/server cases at every tier, including empty or partial machines; their price uses actual contents and market/bargaining, without ordinary PC-profile premiums.

### Coins, auto-selling and ledger

The browser has room management, the exchange and Vesper's hardware orders. Standard Light, Soft and Dark themes are available; Soft is default. Prices follow persistent trends and shocks. Low prices do not ensure rebounds; high-risk coins can lose heavily and remain below purchase cost.

Normal quantity steps are 0.0001 coin; wallet precision is 0.000001. Sell All handles remainders. Limited daily depth and cumulative net buys affect execution; splitting orders does not remove slippage. Both sides pay 1%. Whole cash rounds up for buys and down for sells; sales netting below 1C are rejected. Manual orders execute entirely or require resizing, never silently partly. Confirm actual payment/proceeds and balance; expand details for fees and execution averages.

Auto-sell is off by default. Hold Coins, Cover Operations or Fixed Share are edited as a draft and require Apply Draft plus confirmation. Cover Operations targets the next three nights' known rig power budget in your coin order. Fixed Share caps quantity at 0%-100% of new output, in 10% steps. Each minimum price checks actual net proceeds divided by whole quantity, including fees, slippage and rounding. Automatic sales follow cost payment and coin credit, may partly fill when depth is short, and retain leftovers.

The ledger retains 90 daily records and lifetime totals, separating cash, production, reference holdings value, purchased/mined sources and unearned rent. Fees are already included in cash amounts. Hardware, repair and material purchase costs are not fully tracked; include them yourself when judging returns. Reference value is not realizable cash, and no exact payback date is promised.

### Suppliers, stories and help

The materials seller can visit from day 5 (30%, or 75% with the 100C Computer Sign); the parts thief from day 12 (15%, or 30% with the sign). Each appears at most once per day. The lower builder first arrives on day 5 and returns Fridays. Vesper's 0504 contact permanently unlocks after actually acquiring her card and uses a three-day ordinary-visit cooldown. Jiang Bai brings two independent confiscated-parts boxes on Thursdays.

Vesper initially allows three machine reservations. Completing her custody story unlocks ten daily catalog parts selected from forty, five shared machine/part order slots, and 10% off her sales. Orders are free to reserve and paid by bargaining after next-day delivery. Undisplayed orders return later; displayed but unbought goods cancel on departure. Custody returns require original intact items and their original contents; accessible hidden/storage inventories count, while installed items must be dismantled first. Sorting and resubmission do not extend deadlines.

Two core stories and five small episodes per character follow actual trade, reputation and story progress. Default story settings appear in `Mods/PCExpansion/Stories/npc-stories.json`. Quit before editing and read the accompanying guide. Existing custom wording is retained, including custom Chinese when English is active. The 27571 contact clears current crime count/investigation evidence after the full call, with a ten-day cooldown only on successful cleanup. Already dispatched officers and existing penalties remain.

The 20-page in-game Computer Assembly and Trading Manual explains quick actions, builds, repairs and room operations. Efficient Use costs 2 starting points; Computer Apprentice and Crypto Expert cost 1 each. Gifts apply once to the chosen new game. Room speakers share music/ambience/effects settings. Complete native gameplay and English glyph/layout checks remain pending; source/build checks do not replace gameplay testing.

For feedback, include reproduction steps, game/MelonLoader versions and logs, plus other enabled mods: qq2147058809@gmail.com; QQ group 157249439; Discord `qq2147058809`.
