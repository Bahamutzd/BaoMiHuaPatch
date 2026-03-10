# BaoMiHuaPatch

## 项目简介

`BaoMiHuaPatch` 是一个面向 `BaoMiHua.dll` 的补丁工具集，用于为 BaoMiHua 增加外部播放器支持，并在设置页面中注入对应的配置入口。

当前仓库以“可直接使用的补丁发布包 + 补丁源码”的形式组织，既可以直接用于补丁分发，也便于后续审阅和继续维护。

## 功能特性

### 外部播放器补丁

- 支持为 BaoMiHua 增加外部播放器调用能力
- 在已配置外部播放器时优先尝试外部播放
- 外部播放器启动失败时保留内置播放器回退逻辑
- 支持将媒体地址传递给外部播放器
- 针对 PotPlayer 补充起播进度与部分 HTTP 头处理
- 支持解析 `.strm` 间接资源

### 设置页 UI 补丁

- 在设置页面中注入“外部播放器”配置区域
- 提供外部播放器可执行文件路径输入入口
- 显示当前配置状态信息

## 仓库结构

```text
.
├─ Apply-BaoMiHua-Patches.bat
├─ Apply-BaoMiHua-Patches.ps1
├─ PatchBaoMiHuaExternalPlayer.exe
├─ ExternalPlayerPatchHelper.dll
├─ PatchBaoMiHuaSettingsPageUi.exe
├─ SettingsPageUiPatchHelper.dll
├─ Mono.Cecil.dll
├─ README.txt
├─ README.md
└─ src/
   ├─ ExternalPlayerPatchHelper.cs
   ├─ PatchBaoMiHuaExternalPlayer.cs
   ├─ PatchBaoMiHuaSettingsPageUi.cs
   └─ SettingsPageUiPatchHelper.cs
```

各文件用途如下：

- `Apply-BaoMiHua-Patches.bat`：图形化使用场景的一键入口
- `Apply-BaoMiHua-Patches.ps1`：PowerShell 一键执行脚本
- `PatchBaoMiHuaExternalPlayer.exe`：外部播放器补丁器
- `ExternalPlayerPatchHelper.dll`：外部播放器运行时辅助程序集
- `PatchBaoMiHuaSettingsPageUi.exe`：设置页 UI 补丁器
- `SettingsPageUiPatchHelper.dll`：设置页 UI 运行时辅助程序集
- `Mono.Cecil.dll`：用于程序集读取、修改和回写的依赖库
- `src/`：补丁器与辅助逻辑的源码

## 运行要求

- Windows 环境
- 可访问目标 `BaoMiHua.dll`
- 执行补丁前需彻底关闭 BaoMiHua

## 快速开始

### 方式一：使用一键脚本

1. 关闭 BaoMiHua。
2. 双击运行 `Apply-BaoMiHua-Patches.bat`。
3. 根据提示提供以下任一目标：
   - `BaoMiHua.dll` 完整路径
   - BaoMiHua 安装目录
   - 将 `BaoMiHua.dll` 直接拖放到 BAT 文件上
   - 将安装目录直接拖放到 BAT 文件上

脚本会优先按以下顺序自动探测目标 DLL：

1. 当前工作目录下的 `BaoMiHua.dll`
2. 补丁包所在目录下的 `BaoMiHua.dll`

### 方式二：使用 PowerShell

```powershell
powershell -ExecutionPolicy Bypass -File .\Apply-BaoMiHua-Patches.ps1 -TargetDll "D:\Program Files\NetEase\BaoMiHua\BaoMiHua.dll"
```

如果不传入 `-TargetDll`，脚本会尝试从当前目录和补丁包目录自动定位 `BaoMiHua.dll`。

### 方式三：分别执行补丁器

先执行外部播放器补丁：

```powershell
.\PatchBaoMiHuaExternalPlayer.exe `
  "D:\Program Files\NetEase\BaoMiHua\BaoMiHua.dll" `
  ".\ExternalPlayerPatchHelper.dll" `
  ".\backups\BaoMiHua.dll.pre-external-player.bak"
```

再执行设置页 UI 补丁：

```powershell
.\PatchBaoMiHuaSettingsPageUi.exe `
  "D:\Program Files\NetEase\BaoMiHua\BaoMiHua.dll" `
  ".\SettingsPageUiPatchHelper.dll" `
  ".\backups\BaoMiHua.dll.pre-settings-ui.bak"
```

## 使用结果

补丁应用成功后，通常应具备以下效果：

- 设置页面出现外部播放器配置区域
- 配置有效播放器路径后，播放时优先调用外部播放器
- 外部播放器启动失败时仍可回退到内置播放器流程
- 在适配场景下支持 PotPlayer 播放进度和部分请求头传递

## 兼容性说明

本项目属于定向二进制补丁，不是通用插件机制。补丁是否能够生效，取决于目标 BaoMiHua 版本的程序集结构是否与当前实现保持一致。

当前实现显式依赖的结构包括：

- `a.ae`
- `Filmly.ViewModels.MediaViewModel`
- `Filmly.Views.SettingsPage`

如果上游版本变更了类型名、方法签名或页面结构，补丁可能无法应用，或者应用后行为不符合预期。

## 备份说明

补丁工具会在写入目标 DLL 之前创建备份：

- `Apply-BaoMiHua-Patches.bat` 与 `Apply-BaoMiHua-Patches.ps1` 默认将备份写入 `backups/`
- 单独运行补丁 EXE 时，可通过第三个参数指定备份路径

即使工具支持自动备份，仍建议额外保留一份原始 `BaoMiHua.dll`，便于回滚和重复验证。

## 注意事项

- 请在目标程序完全退出后再执行补丁
- 建议仅对未打过同类补丁的原始 DLL 使用本工具
- 如需重新打补丁，建议先恢复原始 DLL，再执行新的补丁版本
- 不建议在来源不明或已被其他工具修改过的 DLL 上直接叠加执行

## 开发说明

当前仓库保留了补丁相关二进制文件与源码，但尚未补充完整的 Visual Studio 工程文件或 `.csproj` 结构。

如果后续需要将仓库演进为可持续维护的开发仓库，建议优先补齐以下内容：

- `.csproj` 与解决方案文件
- 适配版本记录或兼容性矩阵
- 自动化构建与基础验证流程

## 免责声明

本项目会对第三方程序集进行修改，请在充分了解风险的前提下使用。

使用者应自行确认目标版本、备份原始文件，并自行承担因补丁使用、版本不兼容或重复修改导致的结果。
