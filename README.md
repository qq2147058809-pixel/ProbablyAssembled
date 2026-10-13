# PC expansion — 2.0 Source

作者 / Author：阿铭  
游戏 / Game：Probably Stolen Demo  
[Mod 下载 / Download](https://www.nexusmods.com/probablystolen/mods/385) · [爱发电 / Support](https://afdian.com/a/aming567)

这是 **PC expansion 2.0（源码更新于2026-10-13）** 的公开源码仓库。仓库名 `ProbablyAssembled` 保留；本体和独立英文补丁程序集分别为 `PCExpansion`、`PCExpansionEnglishPatch`。

## 代码使用与署名

**使用、修改或改编代码前须取得作者书面许可。** 获准使用本项目代码制作 Mod 时，须在玩家可见的发布页、随 Mod 提供的说明文件或游戏内鸣谢中，注明原作者“阿铭”、原项目“PC expansion”和[本仓库链接](https://github.com/qq2147058809-pixel/ProbablyAssembled)。源码注释或提交记录中的署名不足以满足此要求；署名本身也不代替许可。完整条款见 [LICENSE](LICENSE)，版权说明见 [COPYRIGHT.md](COPYRIGHT.md)。本项目未采用 MIT、GPL 等通用开源许可。

获准后可使用：

> 本 Mod 使用了阿铭的 PC expansion 项目代码（已获作者许可）。源码：https://github.com/qq2147058809-pixel/ProbablyAssembled

## 源码范围

- 工作间、共享储物箱、电脑装配、维修、拆解、回收、估价和交易；工作间文字与描述框、框选和拖放交互。
- 可启用并逐级扩建至十个机位的机房；T1–T5 矿机与服务器、配件装配、机架状态和供电/散热升级。
- 矿机跨日扣运行费并生产五种虚拟币；服务器支持 3/5/7 天租约，签约结算净租金并锁定设备至到期。
- 机房电脑中的管理、币市和洛夕预订三个网站；五币价格历史、0.1 枚步进与双向 1% 手续费；预订不付款，次日送货后正常议价购买。
- 工作间与机房区域音乐、氛围和动作音量控制；磁带暂停接管时保留原曲与播放进度，避免误触发下一首。
- 电脑配件供应者、装机佬和洛夕等交易规则；江白每周四出售两种治安收缴配件盒。
- 两项五游戏日行情事件：AI 需求暴涨使完整显卡和内存条报价变为 2 倍；虚拟币狂潮使完整显卡报价变为 3 倍。
- 中文本体和独立英文补丁各 737 条文本，双语十页手册 v14。英文补丁不改变中文本体的独立加载。
- `pcexpansion.save-generation` 门禁与 Brewing Expansion v3.2 共享 getter 触发路径的工作间规避代码。

本次同步当前已部署的 2.0 源码，包括机房挖矿、租赁、币市与预订送货，储物箱状态/类型筛选、行情下行偏差校正、专用机箱拖入门禁及磁带音乐接管修复。配套正式构建、737 对中英文本契约与十页手册检查已通过。用户报告当前中文版本总体测试基本通过、暂无新 bug；新英文的原生字形、排版和语言刷新仍待游戏内复核。

主程序集和英文补丁的程序集/FileVersion 属性均为 `2.0.0.0`，Melon 版本为 `2.0.0`。**带有效且不低于 1.0 的 Mod 存档版本标记的现有存档，可继续加载和保存。** 新档写入 `pcexpansion.save-generation=1.0`，更高标记保持原值；缺失、低版本或无效标记会被拒绝，旧档不自动迁移，也不要求玩家手动修改标记。首次使用或存档不受支持时请新建存档。

## 资源与构建

此仓库**只提供源码，不是可安装的 Mod 包**。未公开美术贴图、立绘、字体、音频、手册 PNG、第三方/游戏 DLL、编译产物、存档、日志、本机配置和工作区资料；`assets` 仅保留空目录标记。授权使用源码不包含未公开美术授权。正式编译脚本会校验手册 PNG，缺少资源时会停止；机房图像和区域音频同样需匹配的私有资源。玩家请从上方 Nexus 链接下载完整 Mod。

Windows 构建需 PowerShell 7、.NET 6 运行时，以及本机 Probably Stolen Demo 安装目录（MelonLoader 0.7.3 Open-Beta，IL2CPP x64，已生成互操作程序集）。准备与源码要求匹配的私有资源后，在仓库根目录运行：

```powershell
pwsh -NoProfile -File .\build.ps1 -GameDir "D:\Games\Probably Stolen Demo"
pwsh -NoProfile -File .\build.ps1 -GameDir "D:\Games\Probably Stolen Demo" -UpdateEnglishPatch
```

输出位于 `bin/Release/net6.0`。请使用上述正式入口，不要直接执行 `dotnet build`。游戏依赖仅从编译者本机读取；此仓库不提供部署、打包和美术生成工具。

本 Mod 使用过 AI 辅助编程；作者和维护者为阿铭。静态核对与编译通过不代表所有游戏行为已实机验收。

---

## English

This repository contains the **PC expansion 2.0 source snapshot updated on 2026-10-13**. The `ProbablyAssembled` repository name is retained. The base and independent English patch assemblies are `PCExpansion` and `PCExpansionEnglishPatch`; both use assembly/FileVersion attributes `2.0.0.0` and Melon version `2.0.0`.

**Prior written permission from 阿铭 is required before reusing, modifying, or adapting the code.** If authorized code is used to make a mod, credit “阿铭”, “PC expansion”, and [this repository](https://github.com/qq2147058809-pixel/ProbablyAssembled) in a player-visible public description, distributed documentation, or in-game acknowledgements. Source comments and commit history alone are insufficient. Attribution alone is not permission. See [LICENSE](LICENSE) for the terms and [COPYRIGHT.md](COPYRIGHT.md) for the copyright notice. This is not an MIT, GPL, or other general open-source grant.

This snapshot includes the workroom and shared storage, modular hardware assembly and repair, recycling, appraisal and trade, dedicated suppliers, Jiang Bai's two Thursday confiscated-parts boxes, and two five-day PC market events. AI Demand Surge doubles whole GPU and RAM prices; Crypto Boom triples whole GPU prices. The independent English patch has 737 catalog entries matching the Chinese base, with ten manual pages under contract v14. The base remains independently Chinese.

The server room expands through power and cooling upgrades from one slot to ten. T1–T5 miners use whole GPUs and pay running fees at daily settlement; servers take CPUs and RAM for 3-, 5-, or 7-day leases, paying net rent upfront and locking the machine until expiry. The control PC opens room management, the five-coin exchange, and Vesper's hardware catalog. Trades use 0.1-coin steps with a 1% fee on each side. Reservations are unpaid; Vesper delivers the next game day for normal bargaining and purchase. Area music, ambience and action volume controls cover both rooms.

This update also includes storage condition/type filters, correction of the coin market's downward compounding bias, dedicated-case drag guards, and the cassette handoff fix that preserves the current track and playback position. The official paired build, 737 matching localization entries, and ten-page manual checks passed. The user reports that the current Chinese version has broadly passed testing with no new bugs; native English glyphs, layout and language refresh still need in-game verification.

**Existing saves with valid Mod save-generation markers at or above 1.0 can continue to load and save.** New saves receive `pcexpansion.save-generation=1.0`, and higher valid markers remain unchanged. Missing, older or invalid markers are rejected; there is no automatic migration or manual marker-editing requirement. Start a new save for first-time use or an unsupported old save.

**This is source only, not an installable Mod package.** Artwork, portraits, fonts, audio, manual PNGs, game/third-party DLLs, binaries, saves, logs, local settings, and workspace history are excluded. The official build stops when the private manual images are absent; server-room art and area audio also require matching private resources. Source permission does not grant rights to unpublished art. Download the complete playable mod from [Nexus Mods](https://www.nexusmods.com/probablystolen/mods/385).

To build on Windows, prepare matching private assets, PowerShell 7, the .NET 6 runtime, and a local Probably Stolen Demo installation with MelonLoader 0.7.3 Open-Beta (IL2CPP x64) and generated interop assemblies. Run the `pwsh` commands above from the repository root; dependencies come from your own game installation. Use `build.ps1`, not direct `dotnet build`. Deployment, packaging, and artwork tools are not included.

AI assisted development; 阿铭 is the author and maintainer. Source checks and successful compilation do not establish full in-game acceptance.
