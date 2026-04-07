# BaoMiHuaPatch

## 项目简介

`BaoMiHuaPatch` 是一个面向 `BaoMiHua.dll` 的补丁工具集，用于为 BaoMiHua 增加外部播放器支持，并在设置页面中注入对应的配置入口。

当前版本的 external patch 不再只依赖单一播放入口，而是会同时覆盖播放分发、播放消息接收，以及内置播放器窗口创建链，减少“外部播放器已启动，但 BaoMiHua 内置播放器仍被额外唤起”的情况。

当前仓库以“可直接使用的补丁发布包 + 补丁源码”的形式组织，既可以直接用于补丁分发，也便于后续审阅和继续维护。

## 功能特性

### 外部播放器补丁

- 支持为 BaoMiHua 增加外部播放器调用能力
- 在已配置外部播放器时优先尝试外部播放
- 支持同时改写播放分发入口、`PlayVLCMediaMessage` 接收端，以及播放器窗口创建入口
- 对缺少旧版 open hook 的版本支持 `play-only patch mode`
- 支持将媒体地址传递给外部播放器
- 针对 PotPlayer 补充起播进度与部分 HTTP 头处理
- 支持解析 `.strm` 间接资源
- 在适配版本上，已配置外部播放器时会额外抑制 BaoMiHua 内置播放器顶层窗口创建

### 设置页 UI 补丁

- 在设置页面中注入“外部播放器”配置区域
- 提供外部播放器可执行文件路径输入入口
- 显示当前配置状态信息

## 仓库结构

```text
.
├─ .gitignore
├─ Apply-BaoMiHua-Patches.bat
├─ Apply-BaoMiHua-Patches.ps1
├─ PatchBaoMiHuaExternalPlayer.csproj
├─ PatchBaoMiHuaExternalPlayer.exe
├─ PatchBaoMiHuaExternalPlayer.dll
├─ PatchBaoMiHuaExternalPlayer.deps.json
├─ PatchBaoMiHuaExternalPlayer.runtimeconfig.json
├─ ExternalPlayerPatchHelper.dll
├─ PatchBaoMiHuaSettingsPageUi.exe
├─ SettingsPageUiPatchHelper.dll
├─ Mono.Cecil.dll
├─ README.txt
├─ README.md
├─ tools/
│  ├─ AssemblySearch/
│  │  ├─ AssemblySearch.csproj
│  │  └─ Program.cs
│  └─ DispatcherInspector/
│     ├─ DispatcherInspector.csproj
│     └─ Program.cs
└─ src/
   ├─ ExternalPlayerPatchHelper.cs
   ├─ PatchBaoMiHuaExternalPlayer.cs
   ├─ PatchBaoMiHuaSettingsPageUi.cs
   └─ SettingsPageUiPatchHelper.cs
```

各文件用途如下：

- `Apply-BaoMiHua-Patches.bat`：图形化使用场景的一键入口
- `Apply-BaoMiHua-Patches.ps1`：PowerShell 一键执行脚本
- `PatchBaoMiHuaExternalPlayer.csproj`：external patcher 的构建入口
- `PatchBaoMiHuaExternalPlayer.exe`：外部播放器补丁器
- `PatchBaoMiHuaExternalPlayer.dll` / `.deps.json` / `.runtimeconfig.json`：external patcher 的运行时产物
- `ExternalPlayerPatchHelper.dll`：外部播放器运行时辅助程序集
- `PatchBaoMiHuaSettingsPageUi.exe`：设置页 UI 补丁器
- `SettingsPageUiPatchHelper.dll`：设置页 UI 运行时辅助程序集
- `Mono.Cecil.dll`：用于程序集读取、修改和回写的依赖库
- `tools/AssemblySearch/`：只读程序集检索工具源码
- `tools/DispatcherInspector/`：只读方法/IL 检视工具源码
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

脚本默认带有以下自动处理逻辑：

- 如果目标 DLL 已经包含外部播放器补丁，会自动跳过 external patch，避免重复注入失败
- 如果目标 DLL 已经包含设置页 UI 补丁，会优先尝试使用 `backups/BaoMiHua.dll.pre-settings-ui.bak` 自动恢复，再重打最新 UI 补丁
- 如果缺少可用的 UI 基底备份，脚本会停止并给出更明确的原因和恢复建议

### 方式二：使用 PowerShell

```powershell
powershell -ExecutionPolicy Bypass -File .\Apply-BaoMiHua-Patches.ps1 -TargetDll "D:\Program Files\NetEase\BaoMiHua\BaoMiHua.dll"
```

如果不传入 `-TargetDll`，脚本会尝试从当前目录和补丁包目录自动定位 `BaoMiHua.dll`。

如果要显式指定备份目录，也可以这样执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Apply-BaoMiHua-Patches.ps1 `
  -TargetDll "D:\Program Files\NetEase\BaoMiHua\BaoMiHua.dll" `
  -BackupDir ".\backups"
```

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
- 在适配版本上，外部播放器成功接管后，不再额外弹出 BaoMiHua 内置播放器顶层窗口
- 在适配场景下支持 PotPlayer 播放进度和部分请求头传递

## external patch 行为说明

当前 external patch 主要覆盖三层行为：

1. 播放分发入口：优先在 `a.ae` 的播放方法里调用 `TryPlay(...)`
2. 播放消息接收：在 `Filmly.ViewModels.MediaPlayerListViewModel.Receive(PlayVLCMediaMessage)` 中优先调用 `TryPlay(...)`
3. 播放器窗口创建：在 `a.aF.C()` 这条“确保播放器窗口存在”的路径上，若检测到已配置外部播放器，则直接跳过内置播放器窗口创建

这样做的目标是把“外部播放器已经起来了，但 BaoMiHua 自己又额外打开一个播放窗口”的情况压下去。

需要注意的是，第 3 层窗口抑制是明显偏“外部播放器优先”的策略：

- 如果外部播放器路径已配置，`a.aF.C()` 会直接跳过内置播放器窗口创建
- 如果外部播放器路径虽然已配置，但路径错误、播放器本身异常，或者外部播放器启动失败，当前版本不保证还能自动拉起完整的内置播放器窗口兜底

如果你更需要“外部播放器失败后一定恢复到内置窗口”的语义，需要按目标版本继续定制窗口链逻辑。

## 兼容性说明

本项目属于定向二进制补丁，不是通用插件机制。补丁是否能够生效，取决于目标 BaoMiHua 版本的程序集结构是否与当前实现保持一致。

当前实现显式依赖的结构包括：

- `a.ae`
- `a.aF`
- `Filmly.ViewModels.MediaPlayerListViewModel`
- `Filmly.Messages.PlayVLCMediaMessage`
- `Filmly.ViewModels.MediaViewModel`
- `Filmly.Views.SettingsPage`

如果上游版本变更了类型名、方法签名或页面结构，补丁可能无法应用，或者应用后行为不符合预期。

其中 external patch 对 `a.ae` 的方法定位已经从“硬编码旧方法名”调整为“优先精确匹配，失败时再按方法形态与调用特征收敛”的策略，因此比早期版本更能适配方法名漂移。但它依然是定向二进制补丁，不应当被视为通用版本无关方案。

## 备份说明

补丁工具会在写入目标 DLL 之前创建备份：

- `Apply-BaoMiHua-Patches.bat` 与 `Apply-BaoMiHua-Patches.ps1` 默认将备份写入 `backups/`
- 单独运行补丁 EXE 时，可通过第三个参数指定备份路径

常见备份文件含义如下：

- `BaoMiHua.dll.pre-external-player.bak`：整套补丁开始前的干净基底，适合“从零重打 external + settings”
- `BaoMiHua.dll.pre-settings-ui.bak`：已经带 external、但尚未带 settings UI 的基底，适合“只更新 UI 补丁”

一键脚本在检测到“目标 DLL 已经带 settings UI 补丁”时，会优先尝试自动用 `BaoMiHua.dll.pre-settings-ui.bak` 恢复，再继续重打 UI 补丁。

即使工具支持自动备份，仍建议额外保留一份原始 `BaoMiHua.dll`，便于回滚和重复验证。

## 排障说明

如果 external patch 执行失败，优先检查以下几类问题：

1. BaoMiHua 是否已经彻底退出，目标 DLL 是否仍被占用
2. 当前 DLL 是否是可用于重打的正确基底
3. 备份目录里是否存在可恢复的 `pre-external-player` / `pre-settings-ui` 基底
4. 当前 BaoMiHua 版本的程序集结构是否已经发生明显漂移

运行期排障时，可以优先看：

- `%LOCALAPPDATA%\\BaoMiHua\\log\\external-player.log`

这个日志会记录 external patch 的关键判断，例如：

- 是否命中了 `TryPlay(...)`
- 外部播放器路径是否解析成功
- 传给 PotPlayer 的参数
- 是否触发了“跳过内置播放器窗口创建”

## 注意事项

- 请在目标程序完全退出后再执行补丁
- 如果只是更新设置页 UI 补丁，优先保留 external patch，并使用 `BaoMiHua.dll.pre-settings-ui.bak` 作为恢复基底
- 如果要从零重打整套补丁，优先恢复 `BaoMiHua.dll.pre-external-player.bak`
- 当脚本提示“自动恢复失败”时，通常说明备份缺失，或备份本身也已经被打过 UI 补丁
- 不建议在来源不明或已被其他工具修改过的 DLL 上直接叠加执行
- external patch 当前偏“外部播放器优先”，如果你依赖“外部播放器失败后自动恢复到完整内置播放器窗口”，请先在目标版本上单独验证

## 开发说明

当前仓库已经补齐了 external patcher 与两套只读分析工具的 `.csproj`：

- `PatchBaoMiHuaExternalPlayer.csproj`
- `tools/AssemblySearch/AssemblySearch.csproj`
- `tools/DispatcherInspector/DispatcherInspector.csproj`

external patcher 可直接使用 `dotnet build -c Release` 重建；根目录保留的是可直接分发的发布产物。

仓库中的 `.gitignore` 默认忽略：

- `bin/`
- `obj/`
- `artifacts/`

这样可以把临时验证目录和构建缓存留在本地，不污染补丁发布包本身。

## 免责声明

本项目会对第三方程序集进行修改，请在充分了解风险的前提下使用。

使用者应自行确认目标版本、备份原始文件，并自行承担因补丁使用、版本不兼容或重复修改导致的结果。
