BaoMiHua 补丁包
生成日期：2026-03-10

这个补丁包包含两类改动：
1. 外部播放器补丁
   - 支持设置页配置外部播放器路径
   - 点击播放时优先交给外部播放器，不再因为进度同步初始化失败而回退内置播放器
   - PotPlayer 启动时会带上 BaoMiHua 内的历史播放进度
   - 支持 115 / OpenList / .strm 等播放链路的外部播放器参数处理
2. 设置页 UI 补丁
   - 在设置页中显示“外部播放器路径”输入框

目录说明：
- Apply-BaoMiHua-Patches.bat
  双击即可使用的启动器，也支持把 BaoMiHua.dll 或安装目录直接拖到这个 bat 上
- Apply-BaoMiHua-Patches.ps1
  一键应用两个补丁的脚本
- PatchBaoMiHuaExternalPlayer.exe
  外部播放器补丁工具
- ExternalPlayerPatchHelper.dll
  外部播放器 helper
- PatchBaoMiHuaSettingsPageUi.exe
  设置页 UI 补丁工具
- SettingsPageUiPatchHelper.dll
  设置页 UI helper
- Mono.Cecil.dll
  两个补丁工具运行时依赖
- src\
  本次补丁相关源码，方便后续复用或二次修改

使用前注意：
1. 先关闭 BaoMiHua
2. 脚本支持“已打 external patch 时自动跳过第 1 步”
3. 如果目标 DLL 已经带了 settings UI 补丁，脚本会优先尝试自动恢复 `backups\BaoMiHua.dll.pre-settings-ui.bak` 后再重打 UI
4. 如果目标 DLL 已经打过旧版补丁，但缺少可用备份，请先准备正确的基底 DLL 再重新应用

最简单的使用方式：
1. 把压缩包内容直接解压到 BaoMiHua 根目录
2. 直接双击 `Apply-BaoMiHua-Patches.bat`
3. 启动器会优先自动识别“当前目录”下的 `BaoMiHua.dll`
4. 如果当前目录没有，再按提示输入 `BaoMiHua.dll` 的完整路径，或者直接输入 BaoMiHua 安装目录路径
5. 也可以把 `BaoMiHua.dll` 文件或安装目录直接拖到这个 bat 上

命令行方式：
1. 打开 PowerShell
2. 先进入 BaoMiHua 根目录
3. 执行：
   powershell -ExecutionPolicy Bypass -File .\Apply-BaoMiHua-Patches.ps1
4. 如果补丁脚本不在当前目录，也可以显式传入路径：
   powershell -ExecutionPolicy Bypass -File .\Apply-BaoMiHua-Patches.ps1 -TargetDll "你的 BaoMiHua.dll 路径"

脚本会按正确顺序依次执行：
1. 外部播放器补丁
2. 设置页 UI 补丁

自动处理规则：
1. 如果目标 DLL 已经包含 external patch，脚本会自动跳过第 1 步
2. 如果目标 DLL 已经包含 settings UI patch，脚本会优先尝试用 `backups\BaoMiHua.dll.pre-settings-ui.bak` 自动恢复
3. 如果找不到可恢复的 UI 基底备份，脚本会停止，并明确提示缺少哪份备份

如果你只想手动执行，也可以分别运行：
1. .\PatchBaoMiHuaExternalPlayer.exe "D:\Program Files\NetEase\BaoMiHua\BaoMiHua.dll" ".\ExternalPlayerPatchHelper.dll" ".\backups\BaoMiHua.dll.pre-external-player.bak"
2. .\PatchBaoMiHuaSettingsPageUi.exe "D:\Program Files\NetEase\BaoMiHua\BaoMiHua.dll" ".\SettingsPageUiPatchHelper.dll" ".\backups\BaoMiHua.dll.pre-settings-ui.bak"

备份文件用途：
1. `BaoMiHua.dll.pre-external-player.bak`
   这是整套补丁开始前的干净基底，适合从零重打 external + settings
2. `BaoMiHua.dll.pre-settings-ui.bak`
   这是已经带 external、但还没带 settings UI 的基底，适合更新 UI 补丁

补充说明：
- 本补丁包内的外部播放器 helper 已经包含 PotPlayer 起播进度逻辑
- 本补丁包内的外部播放器 patcher 已经包含嵌套类型克隆修复
- 重新打补丁时，不再需要额外把 ExternalPlayerPatchHelper.new.dll 丢到程序根目录
- 双击 BAT 时，失败原因会优先显示在 PowerShell 输出里，不再只给一条笼统报错
