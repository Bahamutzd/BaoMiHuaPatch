param(
    [string]$TargetDll = "",

    [string]$BackupDir = ""
)

$ErrorActionPreference = "Stop"

$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($BackupDir)) {
    $BackupDir = Join-Path $packageRoot "backups"
}

$externalPatcher = Join-Path $packageRoot "PatchBaoMiHuaExternalPlayer.exe"
$externalHelper = Join-Path $packageRoot "ExternalPlayerPatchHelper.dll"
$settingsPatcher = Join-Path $packageRoot "PatchBaoMiHuaSettingsPageUi.exe"
$settingsHelper = Join-Path $packageRoot "SettingsPageUiPatchHelper.dll"

if ([string]::IsNullOrWhiteSpace($TargetDll)) {
    $cwdDll = Join-Path (Get-Location) "BaoMiHua.dll"
    if (Test-Path -LiteralPath $cwdDll) {
        $TargetDll = $cwdDll
    }
}

if ([string]::IsNullOrWhiteSpace($TargetDll)) {
    $packageDll = Join-Path $packageRoot "BaoMiHua.dll"
    if (Test-Path -LiteralPath $packageDll) {
        $TargetDll = $packageDll
    }
}

if ([string]::IsNullOrWhiteSpace($TargetDll)) {
    throw "未提供 TargetDll，且当前目录与补丁包目录下都没有找到 BaoMiHua.dll"
}

if (-not (Test-Path -LiteralPath $TargetDll)) {
    throw "目标 DLL 不存在：$TargetDll"
}

foreach ($path in @($externalPatcher, $externalHelper, $settingsPatcher, $settingsHelper)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "补丁包文件缺失：$path"
    }
}

New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null

$targetDllFullPath = (Resolve-Path -LiteralPath $TargetDll).Path
$externalBackup = Join-Path $BackupDir "BaoMiHua.dll.pre-external-player.bak"
$settingsBackup = Join-Path $BackupDir "BaoMiHua.dll.pre-settings-ui.bak"

Write-Host "开始打外部播放器补丁..."
& $externalPatcher $targetDllFullPath $externalHelper $externalBackup
if ($LASTEXITCODE -ne 0) {
    throw "外部播放器补丁失败，退出码：$LASTEXITCODE"
}

Write-Host "开始打设置页 UI 补丁..."
& $settingsPatcher $targetDllFullPath $settingsHelper $settingsBackup
if ($LASTEXITCODE -ne 0) {
    throw "设置页 UI 补丁失败，退出码：$LASTEXITCODE"
}

Write-Host ""
Write-Host "补丁完成：$targetDllFullPath"
Write-Host "备份目录：$BackupDir"
