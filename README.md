# PC expansion — 2.6 Source

作者 / Author：阿铭  
游戏 / Game：Probably Stolen Demo  
[Mod 下载 / Download](https://www.nexusmods.com/probablystolen/mods/385) · [爱发电 / Support](https://afdian.com/a/aming567)

本仓库已同步 **PC expansion 2.6正式源码（2026-10-20）**。仓库名`ProbablyAssembled`保留，本体和独立英文补丁程序集分别为`PCExpansion`、`PCExpansionEnglishPatch`。

## 代码使用与署名

**使用、修改或改编代码前须取得作者书面许可。** 获准使用本项目代码制作 Mod 时，须在玩家可见的发布页、随 Mod 提供的说明文件或游戏内鸣谢中，注明原作者“阿铭”、原项目“PC expansion”和[本仓库链接](https://github.com/qq2147058809-pixel/ProbablyAssembled)。源码注释或提交记录中的署名不足以满足此要求；署名本身也不代替许可。完整条款见 [LICENSE](LICENSE)，版权说明见 [COPYRIGHT.md](COPYRIGHT.md)。本项目未采用 MIT、GPL 等通用开源许可。

获准后可使用：

> 本 Mod 使用了阿铭的 PC expansion 项目代码（已获作者许可）。源码：https://github.com/qq2147058809-pixel/ProbablyAssembled

## 源码范围与版本

- 工作间与共享储物箱，电脑整件/子件拆装、材料/现金维修、混合回收、估价与交易；来源选择、搜索筛选、E＋左键转移、部分自动填空及维修归还。
- T1–T5矿机与服务器、十机位供电/散热扩建、逐卡算法门槛、网络/难度/矿池、运行模式、现金排机、维护及服务器预付租约。
- 五币趋势行情与深度交易，普通输入0.0001枚、钱包微币精度，双向1%费与整单现金取整；默认关闭的自动出售、90日账本及三主题浏览器。
- 专属供应与买家、洛夕和江白核心故事及各五个小故事、外部剧情设置、硬件预订、开局特性、两类治安收缴盒、电脑行情事件及区域音频。
- 电源按1～6组混装电容合计功率分档；性价比加成30%，洛夕可收原完好/T1–T3范围刀把机。
- 中文本体与独立英文补丁各1287条文本，手册v27二十页；中文本体独立加载，英文补丁通过字典/指纹和页面契约接入，不重复注册玩法。

本体与英文补丁的AssemblyVersion/FileVersion均为`2.6.0.0`，Melon版本均为`2.6`。正式入口以源码属性生成DLL并校验资源，不以文档版本代替实际产物核验。

**2.6需要新档。** 新档写入`pcexpansion.save-generation=2.6`，仅接受格式有效且不低于2.6的标记，更高标记原样保留。缺失/无效/过低标记拒绝加载和覆盖；旧1.0档使用保留的2.3.1，不迁移、不手改玩家标记。存档代际与Mod版本分别定义。

## 资源与构建

此说明面向源码；公开源码不等于完整安装包，也不代表本地私有资源获准公开。美术、立绘、字体、音频、游戏/第三方DLL、本机配置及玩家文件不随源码授权。缺少所需资源时，正式构建校验停止。完整玩家包见下载页。

Windows构建使用PowerShell 7、.NET运行时及本机游戏安装（MelonLoader 0.7.3 Open-Beta、IL2CPP x64与已生成互操作程序集），准备匹配私有资源和本机路径配置后，从工程目录运行：

```powershell
pwsh -NoProfile -File .\build.ps1 -GameDir "D:\Games\Probably Stolen Demo"
pwsh -NoProfile -File .\build.ps1 -GameDir "D:\Games\Probably Stolen Demo" -UpdateEnglishPatch
```

产物位于`bin/Release/net6.0`。只使用正式`build.ps1`、`deploy.ps1`和`package.ps1`入口，不直接运行dotnet build；游戏依赖仅从编译者本机读取。本地部署须先备份旧DLL并保持语言选择。静态、托管及构建验证不能代替Unity实测。

## English

This repository contains the **PC expansion 2.6 source dated 2026-10-20**. Base/patch assembly and file versions are `2.6.0.0`; both Melon versions are `2.6`.

**Prior written permission from 阿铭 is required before reusing, modifying, or adapting the code.** If authorized code is used to make a mod, credit “阿铭”, “PC expansion”, and [this repository](https://github.com/qq2147058809-pixel/ProbablyAssembled) in a player-visible public description, distributed documentation, or in-game acknowledgements. Source comments and commit history alone are insufficient. Attribution alone is not permission. See [LICENSE](LICENSE) for the terms and [COPYRIGHT.md](COPYRIGHT.md) for the copyright notice. This is not an MIT, GPL, or other general open-source grant.

The source includes modular PC repairs and trading; source-aware workroom transfers and partial auto-fill; dedicated suppliers and NPC stories; rig/server assembly, three algorithms, network difficulty, pools, power/cooling, cash scheduling and maintenance; prepaid leases, finite-depth trading, optional auto-selling and a 90-day ledger. PSUs use mixed capacitor banks; Value Builds receive +30%, with Vesper accepting intact T1-T3 Bottleneck Builds. Both catalogs contain 1,287 entries, with twenty v27 manual pages. The Chinese base is independent; the English patch provides content through a validated reflection bridge without registering gameplay again.

**Start a new save for 2.6.** New saves receive generation 2.6. Valid markers at or above 2.6 are accepted without downgrading higher values. Missing, invalid or older markers reject load/write. Continue 1.0 saves with the retained 2.3.1 build; no migration or manual player-file edits.

Public source is not an installable mod or permission to publish private assets. Artwork, portraits, fonts, audio, game/third-party DLLs, local settings and player files are outside source permission. The official build stops if required private resources are absent. Prepare matching assets and a local IL2CPP game with MelonLoader and interop assemblies, then use the PowerShell commands above. Output goes to `bin/Release/net6.0`; use official build/deploy/package entries. File and hosted checks do not establish complete native gameplay or rendering acceptance.

AI assisted development; 阿铭 is the author and maintainer. See the bilingual MOD_README for player rules and installation.

公开树提供正式构建入口 build.ps1 及其依赖脚本；本机部署、打包脚本和内部工作区资料不随本次源码同步。中文译表按原字节保存以匹配独立英文补丁的 SHA256 契约；请保留 .gitattributes 中对应的换行保护。

The public tree includes the official build.ps1 entry and its required helper scripts. Local deployment/packaging scripts and internal workspace documents are outside this source snapshot. Preserve the Chinese catalog bytes and its .gitattributes override so the English patch fingerprint remains valid.

## 2026-10-20 同步说明 / Source update

版本仍为2.6。本次包含游戏0.46D恢复接口适配、NPC稳定访问与离场恢复修复、英文补丁改为初始化/晚初始化至多两次尝试，以及确认等待、场景悬停射线和三来源归还优化与重复实现合并。中文/英文1287对文本、v27二十页手册及新档门槛2.6保持。收藏室仍在设计阶段，未包含在本次实现中。

Version remains 2.6. This snapshot includes the game 0.46D restore API adjustment, stable NPC visit/departure recovery fixes, bounded English patch initialization through two Loader lifecycle callbacks, lighter confirmation/hover checks, shared three-source returns, and implementation consolidation. The 1,287 translation pairs, twenty v27 manual pages and save generation 2.6 remain unchanged. The collection room is still being designed and is not implemented in this snapshot.

本次同步复用已完成的正式配套构建及托管验证；Unity显示、完整流程和组合Mod验证缺口仍保留。This source synchronization reuses the completed official paired build and managed checks; native rendering, complete gameplay and combined-mod validation remain pending.
