param(
    [string]$TargetDll = "",

    [string]$BackupDir = ""
)

$ErrorActionPreference = "Stop"

$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($BackupDir)) {
    $BackupDir = Join-Path $packageRoot "backups"
}

$monoCecil = Join-Path $packageRoot "Mono.Cecil.dll"
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

foreach ($path in @($monoCecil, $externalPatcher, $externalHelper, $settingsPatcher, $settingsHelper)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "补丁包文件缺失：$path"
    }
}

New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null

$targetDllFullPath = (Resolve-Path -LiteralPath $TargetDll).Path
$externalBackup = Join-Path $BackupDir "BaoMiHua.dll.pre-external-player.bak"
$settingsBackup = Join-Path $BackupDir "BaoMiHua.dll.pre-settings-ui.bak"

function Ensure-MonoCecilLoaded {
    if (-not ("Mono.Cecil.AssemblyDefinition" -as [type])) {
        Add-Type -Path $monoCecil
    }
}

function Get-PatchState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AssemblyPath
    )

    Ensure-MonoCecilLoaded

    $assembly = $null
    try {
        $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($AssemblyPath)
        return [PSCustomObject]@{
            HasExternalPatch   = $assembly.MainModule.GetType("BaoMiHuaPatch.ExternalPlayerPatch") -ne $null
            HasSettingsUiPatch = $assembly.MainModule.GetType("BaoMiHuaPatch.SettingsPageUiPatch") -ne $null
        }
    }
    finally {
        if ($assembly -ne $null) {
            $assembly.Dispose()
        }
    }
}

function Get-SettingsRecoveryHint {
    if (Test-Path -LiteralPath $settingsBackup) {
        return "脚本会优先尝试用这份备份自动恢复后再重打 UI 补丁：$settingsBackup"
    }

    return "如果要更新 UI 补丁，脚本需要一份未带 settings UI 补丁的基底 DLL（通常是 BaoMiHua.dll.pre-settings-ui.bak）。"
}

function Restore-SettingsPatchBase {
    if (-not (Test-Path -LiteralPath $settingsBackup)) {
        throw "检测到当前 DLL 已包含设置页 UI 补丁，但自动恢复失败：没有找到备份 $settingsBackup"
    }

    $backupState = Get-PatchState -AssemblyPath $settingsBackup
    if ($backupState.HasSettingsUiPatch) {
        throw "自动恢复失败：$settingsBackup 这份备份也已经包含 settings UI 补丁，不能作为重打 UI 的基底。"
    }

    Write-Host "[restore] Settings UI patch already exists, restoring target DLL from backup before reapplying..."
    Copy-Item -LiteralPath $settingsBackup -Destination $targetDllFullPath -Force
}

function Invoke-PatcherStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StepLabel,

        [Parameter(Mandatory = $true)]
        [string]$PatcherPath,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$FailureHint
    )

    & $PatcherPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$StepLabel失败。$FailureHint`n补丁器的原始报错已经在上面输出。"
    }
}

try {
    $targetState = Get-PatchState -AssemblyPath $targetDllFullPath
    if ($targetState.HasSettingsUiPatch) {
        Restore-SettingsPatchBase
        $targetState = Get-PatchState -AssemblyPath $targetDllFullPath
    }

    if ($targetState.HasExternalPatch) {
        Write-Host "[1/2] External player patch already exists, skipping repeated patching."
    }
    else {
        Write-Host "[1/2] Applying external player patch..."
        Invoke-PatcherStep `
            -StepLabel "外部播放器补丁" `
            -PatcherPath $externalPatcher `
            -Arguments @($targetDllFullPath, $externalHelper, $externalBackup) `
            -FailureHint "常见原因：BaoMiHua 还没完全退出，或者当前 DLL 不是整套重打所需的干净基底。"
    }

    $targetState = Get-PatchState -AssemblyPath $targetDllFullPath
    if ($targetState.HasSettingsUiPatch) {
        throw "当前 DLL 仍然包含设置页 UI 补丁，无法继续重打。$(Get-SettingsRecoveryHint)"
    }

    Write-Host "[2/2] Applying settings page UI patch..."
    Invoke-PatcherStep `
        -StepLabel "设置页 UI 补丁" `
        -PatcherPath $settingsPatcher `
        -Arguments @($targetDllFullPath, $settingsHelper, $settingsBackup) `
        -FailureHint "$(Get-SettingsRecoveryHint)"

    Write-Host ""
    Write-Host "补丁完成：$targetDllFullPath"
    Write-Host "备份目录：$BackupDir"
}
catch {
    Write-Host ""
    Write-Host "[ERROR] $($_.Exception.Message)"
    Write-Host ""
    Write-Host "建议先检查："
    Write-Host "1. BaoMiHua 是否已经彻底退出。"
    Write-Host "2. 如果当前 DLL 已带 settings UI 补丁，$settingsBackup 是否存在且可用。"
    Write-Host "3. 如果要从零重打整套补丁，是否先恢复了：$externalBackup"
    Write-Host "4. 如果备份目录里没有可恢复的基底 DLL，请先手动准备正确的基底文件。"
    exit 1
}
