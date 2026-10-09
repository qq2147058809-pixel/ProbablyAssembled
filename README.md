# PC expansion — 1.1 Source

作者 / Author：阿铭  
游戏 / Game：Probably Stolen Demo  
[Mod 下载 / Download](https://www.nexusmods.com/probablystolen/mods/385) · [爱发电 / Support](https://afdian.com/a/aming567)

这是 **PC expansion 1.1（2026-10-09）** 的公开源码仓库。仓库名 `ProbablyAssembled` 保留；本体和独立英文补丁程序集分别为 `PCExpansion`、`PCExpansionEnglishPatch`。

## 代码使用与署名

**使用、修改或改编代码前须取得作者书面许可。** 获准使用本项目代码制作 Mod 时，须在玩家可见的发布页、随 Mod 提供的说明文件或游戏内鸣谢中，注明原作者“阿铭”、原项目“PC expansion”和[本仓库链接](https://github.com/qq2147058809-pixel/ProbablyAssembled)。源码注释或提交记录中的署名不足以满足此要求；署名本身也不代替许可。完整条款见 [LICENSE](LICENSE)，版权说明见 [COPYRIGHT.md](COPYRIGHT.md)。本项目未采用 MIT、GPL 等通用开源许可。

获准后可使用：

> 本 Mod 使用了阿铭的 PC expansion 项目代码（已获作者许可）。源码：https://github.com/qq2147058809-pixel/ProbablyAssembled

## 源码范围

- 工作间、共享储物箱、电脑装配、维修、拆解、回收、估价和交易；工作间文字与描述框、框选和拖放交互。
- 电脑配件供应者、装机佬和洛夕等交易规则；江白每周四出售两种治安收缴配件盒。
- 两项五游戏日行情事件：AI 需求暴涨使完整显卡和内存条报价变为 2 倍；虚拟币狂潮使完整显卡报价变为 3 倍。
- 中文本体和独立英文补丁各 509 条文本，双语十页手册 v13。英文补丁不改变中文本体的独立加载。
- `pcexpansion.save-generation` 门禁与 Brewing Expansion v3.2 共享 getter 触发路径的工作间规避代码。

主程序集和英文补丁版本元数据均为 `1.1.0`。**玩家从深空装机 1.0 及以下版本升级到 1.1 请开新档；旧档没有数据迁移。** 代码层面的门禁接受有效且不低于 1.0 的版本标记，新档写入 1.0，更高标记保持原值；缺失、低版本或无效标记会被拒绝。这是代码行为，不代表对旧版 Mod 存档升级的支持。

## 资源与构建

此仓库**只提供源码，不是可安装的 Mod 包**。未公开美术贴图、立绘、字体、手册 PNG、第三方/游戏 DLL、编译产物、存档、日志、本机配置和工作区资料；`assets` 仅保留空目录标记。授权使用源码不包含未公开美术授权。正式编译脚本会校验手册 PNG，缺少资源时会停止。玩家请从上方 Nexus 链接下载完整 Mod。

Windows 构建需 PowerShell 7、.NET 6 运行时，以及本机 Probably Stolen Demo 安装目录（MelonLoader 0.7.3 Open-Beta，IL2CPP x64，已生成互操作程序集）。准备与源码要求匹配的私有资源后，在仓库根目录运行：

```powershell
pwsh -NoProfile -File .\build.ps1 -GameDir "D:\Games\Probably Stolen Demo"
pwsh -NoProfile -File .\build.ps1 -GameDir "D:\Games\Probably Stolen Demo" -UpdateEnglishPatch
```

输出位于 `bin/Release/net6.0`。请使用上述正式入口，不要直接执行 `dotnet build`。游戏依赖仅从编译者本机读取；此仓库不提供部署、打包和美术生成工具。

本 Mod 使用过 AI 辅助编程；作者和维护者为阿铭。静态核对与编译通过不代表所有游戏行为已实机验收。

---

## English

This repository contains the **PC expansion 1.1 source snapshot dated 2026-10-09**. The `ProbablyAssembled` repository name is retained. The base and independent English patch assemblies are `PCExpansion` and `PCExpansionEnglishPatch`, both version `1.1.0`.

**Prior written permission from 阿铭 is required before reusing, modifying, or adapting the code.** If authorized code is used to make a mod, credit “阿铭”, “PC expansion”, and [this repository](https://github.com/qq2147058809-pixel/ProbablyAssembled) in a player-visible public description, distributed documentation, or in-game acknowledgements. Source comments and commit history alone are insufficient. Attribution alone is not permission. See [LICENSE](LICENSE) for the terms and [COPYRIGHT.md](COPYRIGHT.md) for the copyright notice. This is not an MIT, GPL, or other general open-source grant.

This snapshot includes the workroom and shared storage, modular hardware assembly and repair, recycling, appraisal and trade, dedicated suppliers, Jiang Bai's two Thursday confiscated-parts boxes, and two five-day PC market events. AI Demand Surge doubles whole GPU and RAM prices; Crypto Boom triples whole GPU prices. The independent English patch has 509 catalog entries and ten manual pages under contract v13. The base remains independently Chinese.

**Players updating from mod version 1.0 or earlier should start a new game; old saves are not migrated.** The code accepts valid `pcexpansion.save-generation` markers at or above 1.0 and preserves higher values. That technical check does not constitute support for upgrading an old mod save.

**This is source only, not an installable Mod package.** Artwork, portraits, fonts, manual PNGs, game/third-party DLLs, binaries, saves, logs, local settings, and workspace history are excluded. The official build stops when the private manual images are absent. Source permission does not grant rights to unpublished art. Download the complete playable mod from [Nexus Mods](https://www.nexusmods.com/probablystolen/mods/385).

To build on Windows, prepare matching private assets, PowerShell 7, the .NET 6 runtime, and a local Probably Stolen Demo installation with MelonLoader 0.7.3 Open-Beta (IL2CPP x64) and generated interop assemblies. Run the `pwsh` commands above from the repository root; dependencies come from your own game installation. Use `build.ps1`, not direct `dotnet build`. Deployment, packaging, and artwork tools are not included.

AI assisted development; 阿铭 is the author and maintainer. Source checks and successful compilation do not establish full in-game acceptance.
