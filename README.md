# PC expansion — 1.0 Source

作者 / Author：阿铭  
游戏 / Game：Probably Stolen Demo  
[Mod 下载 / Download](https://www.nexusmods.com/probablystolen/mods/385) · [爱发电 / Support](https://afdian.com/a/aming567)

本说明作为 GitHub 源码仓库的 README 使用。原仓库名称 `ProbablyAssembled` 与网址保留，当前 Mod 显示名为 **PC expansion**，本体和英文补丁程序集分别为 `PCExpansion`、`PCExpansionEnglishPatch`。

## 代码使用与署名

**复用须先取得作者书面许可。使用、修改或改编本项目代码制作 Mod 时，必须在该 Mod 的说明或鸣谢中注明原作者“阿铭”、原项目“PC expansion”及本仓库链接：**

https://github.com/qq2147058809-pixel/ProbablyAssembled

说明或鸣谢至少须在发布页、随 Mod 提供的说明文件或游戏内的一处向玩家可见；仅保留源码注释不能替代署名。署名不等于已取得许可。完整条件见 [COPYRIGHT.md](COPYRIGHT.md)。

获得作者许可后，可使用：

> 本 Mod 使用了阿铭的 PC expansion 项目代码（已获作者许可）。源码：https://github.com/qq2147058809-pixel/ProbablyAssembled

本仓库为公开源码留档，保留原有需作者许可的使用限制，未改为 MIT、GPL 等通用开源许可证。

## 当前源码范围

2026-10-08 的 **1.0 功能基线**，包含当前本体 C# 源码、独立英文补丁、中文与英文文本及八页手册的 JSON 数据、工程文件和正式编译入口。

主要内容：

- 独立工作间、桌面钥匙串入口、共享电脑箱及单件库存。
- 九类电脑硬件、六类内部部件拆装、主板与机箱的分层装配。
- 材料维修、现金维修、单次批量回收和报废。
- 检测、估价、交易资格及电脑供应者；包含商贩额外 CPU 的最新规则。
- 独立英文补丁：489 对文本、八页手册 v12；中文本体不依赖英文程序集。
- 最新工作间读取播放器字段的兼容修正，规避已定位的 Brewing Expansion v3.2 共享 getter 触发路径；共存实机验证仍待反馈。

只支持安装本轮版本后新建的存档；不提供旧档迁移。上述 1.0 指功能发布基线，源码中的程序集版本仍沿用当前开发值 `0.1.0`，本次公开更新没有改写该元数据。

## 不公开的内容与构建边界

仓库不包含 Mod 美术贴图、立绘、图标、字体或原始美术文件，也不包含生成流程、游戏/Unity/MelonLoader DLL、编译产物、发布包、本机配置、存档、日志或工作区历史资料。`assets` 只保留空目录标记。

**此仓库不是可直接安装的完整 Mod 包。** 正式编译入口会检查手册配图；公开仓库没有这些私有 PNG，缺少资源时构建会停止。获得或自行准备符合源码所需文件名与布局的资源后，才可完成构建。源码使用许可不包含未公开美术的授权。玩家请从上述 Nexus 链接下载完整 Mod。

## 编译入口

Windows 需要 PowerShell 7、.NET 6 运行时，以及已安装 MelonLoader **0.7.3 Open-Beta（IL2CPP x64）** 并生成互操作程序集的 Probably Stolen Demo 游戏目录。不要直接使用 `dotnet build` 或 MSBuild，也不需要为此下载 .NET SDK。游戏依赖只从编译者本机读取。

在仓库根目录运行（还须具备上文所述资源）：

```powershell
# 中文本体
pwsh -NoProfile -File .\build.ps1 -GameDir "D:\Games\Probably Stolen Demo"

# 同时编译本体和独立英文补丁
pwsh -NoProfile -File .\build.ps1 -GameDir "D:\Games\Probably Stolen Demo" -UpdateEnglishPatch
```

产物位于 `bin/Release/net6.0`。本源码仓库提供编译入口，未提供正式工作区的部署、打包或美术生成工具。

英文运行资源已同步；`EnglishPatch/README.md` 的供货概述仍欠“每访额外固定一颗 CPU”的文档补充，其编译命令中的 `..\pwsh\pwsh.exe` 是原工作区路径，在公开仓库请使用本页的 `pwsh` 命令。实际当前供货规则以源码为准：1–2 个随机完好 T1–T3 配件、额外一颗独立抽档的完好 T1–T3 CPU，以及四种材料。

## 开发说明

本 Mod 使用过 AI 辅助编程，作者和维护者为阿铭。静态核对和编译通过不代表全部游戏行为已实机验收。

---

## English

This is the **PC expansion 1.0 source snapshot as of 2026-10-08**, by 阿铭, for Probably Stolen Demo. The existing `ProbablyAssembled` repository URL is retained. The assemblies are named `PCExpansion` and `PCExpansionEnglishPatch`.

**Prior written permission is required to reuse the code. If you use, modify, or adapt it to make a mod, clearly credit “阿铭”, “PC expansion”, and https://github.com/qq2147058809-pixel/ProbablyAssembled in that mod's description or acknowledgements.** The credit must be player-visible in at least one place: the public description, documentation distributed with the mod, or in-game acknowledgements. Source comments alone do not satisfy this condition. Attribution alone does not grant permission. See [COPYRIGHT.md](COPYRIGHT.md).

Suggested credit, only after permission has been granted:

> This mod uses code from PC expansion by 阿铭, with the author's permission. Source: https://github.com/qq2147058809-pixel/ProbablyAssembled

The previous permission restrictions remain; this publication does not introduce an MIT, GPL, or other general open-source license.

The snapshot includes the workroom and shared storage, modular hardware and motherboard/case assembly, material/cash repair, batch recycling/discard, inspection and trading, dedicated suppliers including the latest bonus CPU, and the independent English patch with 489 catalog entries and eight manual pages under contract v12. It includes the recent workroom field-access workaround for the identified Brewing Expansion v3.2 shared-getter crash path; in-game coexistence verification remains pending. Only saves started after installing this version are supported. “1.0” identifies the gameplay release baseline; the current source assembly metadata still reads `0.1.0` and is unchanged by this publication.

**This is a source-only repository, not an installable or complete build package.** Artwork, portraits, icons, fonts, artwork workflows, third-party/game DLLs, binaries, release archives, local settings, saves, logs, and workspace history are excluded. The official build validates manual PNGs, so it stops when those private resources are absent. You need appropriately named and arranged resources to build. Code permission does not include unpublished artwork. Get the complete playable mod from [Nexus Mods](https://www.nexusmods.com/probablystolen/mods/385).

Build on Windows with PowerShell 7, the .NET 6 runtime, and a local Probably Stolen Demo installation with MelonLoader 0.7.3 Open-Beta (IL2CPP x64) and generated interop assemblies. Use the two `pwsh` commands above from the repository root, after supplying the required resources. Do not use direct `dotnet build` or MSBuild; the .NET SDK is not required. Dependencies are read from your own game installation. Outputs go to `bin/Release/net6.0`; deployment, packaging, and artwork tools are not included.

English runtime resources are current. The supply summary in `EnglishPatch/README.md` still awaits the extra guaranteed CPU note; its `..\pwsh\pwsh.exe` path belongs to the original workspace, so use this page's `pwsh` commands here. The current seller supplies 1–2 random intact T1–T3 parts, one additional intact T1–T3 CPU rolled independently, and four materials.

AI assisted development; 阿铭 is the author and maintainer. Source checks and successful compilation do not establish full in-game acceptance.
