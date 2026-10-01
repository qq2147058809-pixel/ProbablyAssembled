# 深空装机 - Probably Assembled

作者：阿铭  
游戏：Probably Stolen Demo

此仓库仅发布 Mod 的 C# 源码及编译项目文件，包含本体和可选英文补丁源码。

## 有意不公开的内容

仓库不包含任何 Mod 美术资源、立绘/图标、原始美术文件、调色与美术制作流程、编译 DLL、打包文件或本机游戏配置。`assets` 目录留空；因此本仓库是源码留档，不是可直接安装的完整 Mod 包。缺少私有美术资源时编译出的 DLL 无法呈现完整贴图。

## 编译

Windows 上需安装 PowerShell 7、.NET 6 运行环境，并准备已安装 MelonLoader 的 Probably Stolen Demo 游戏目录。可从项目根目录运行：

```powershell
.\build.ps1 -GameDir "D:\Games\Probably Stolen Demo"
```

英文补丁是独立源码项目的一部分。构建脚本会同时编译本体和英文补丁。游戏自身程序集只从用户本机游戏目录读取，不随仓库发布。

## AI 辅助说明

本 Mod 的开发过程中使用了 AI 辅助编程。仓库作者和项目维护者为阿铭。

详见 [COPYRIGHT.md](COPYRIGHT.md)。
