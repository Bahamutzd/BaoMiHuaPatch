@echo off
setlocal EnableExtensions DisableDelayedExpansion
title BaoMiHua Patch Launcher

set "PACKAGE_DIR=%~dp0"
set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
set "PATCH_SCRIPT=%PACKAGE_DIR%Apply-BaoMiHua-Patches.ps1"
set "EXTERNAL_PATCHER=%PACKAGE_DIR%PatchBaoMiHuaExternalPlayer.exe"
set "EXTERNAL_HELPER=%PACKAGE_DIR%ExternalPlayerPatchHelper.dll"
set "SETTINGS_PATCHER=%PACKAGE_DIR%PatchBaoMiHuaSettingsPageUi.exe"
set "SETTINGS_HELPER=%PACKAGE_DIR%SettingsPageUiPatchHelper.dll"
set "BACKUP_DIR=%PACKAGE_DIR%backups"

if not exist "%PATCH_SCRIPT%" goto :missing_patch_script
if not exist "%EXTERNAL_PATCHER%" goto :missing_external_patcher
if not exist "%EXTERNAL_HELPER%" goto :missing_external_helper
if not exist "%SETTINGS_PATCHER%" goto :missing_settings_patcher
if not exist "%SETTINGS_HELPER%" goto :missing_settings_helper

set "TARGET_INPUT=%~1"
if not defined TARGET_INPUT (
    if exist "%CD%\BaoMiHua.dll" (
        set "TARGET_INPUT=%CD%\BaoMiHua.dll"
    )
)

if not defined TARGET_INPUT (
    if exist "%PACKAGE_DIR%BaoMiHua.dll" (
        set "TARGET_INPUT=%PACKAGE_DIR%BaoMiHua.dll"
    )
)

if not defined TARGET_INPUT (
    echo.
    echo Enter the full path of BaoMiHua.dll,
    echo or enter the BaoMiHua install directory path.
    echo You can also drag BaoMiHua.dll or the install directory onto this BAT file.
    set /p "TARGET_INPUT=> "
)

set "TARGET_INPUT=%TARGET_INPUT:"=%"
if not defined TARGET_INPUT goto :missing_target

if exist "%TARGET_INPUT%\BaoMiHua.dll" (
    set "TARGET_DLL=%TARGET_INPUT%\BaoMiHua.dll"
) else (
    set "TARGET_DLL=%TARGET_INPUT%"
)

for %%I in ("%TARGET_DLL%") do (
    set "TARGET_DLL=%%~fI"
    set "TARGET_NAME=%%~nxI"
)

if not exist "%TARGET_DLL%" goto :target_not_found
if /I not "%TARGET_NAME%"=="BaoMiHua.dll" goto :target_wrong_name

mkdir "%BACKUP_DIR%" >nul 2>nul

echo.
echo Launching patch script...
"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%PATCH_SCRIPT%" -TargetDll "%TARGET_DLL%" -BackupDir "%BACKUP_DIR%"
if errorlevel 1 goto :script_failed
pause
exit /b 0

:missing_patch_script
echo.
echo [ERROR] Missing file:
echo %PATCH_SCRIPT%
goto :fail

:missing_external_patcher
echo.
echo [ERROR] Missing file:
echo %EXTERNAL_PATCHER%
goto :fail

:missing_external_helper
echo.
echo [ERROR] Missing file:
echo %EXTERNAL_HELPER%
goto :fail

:missing_settings_patcher
echo.
echo [ERROR] Missing file:
echo %SETTINGS_PATCHER%
goto :fail

:missing_settings_helper
echo.
echo [ERROR] Missing file:
echo %SETTINGS_HELPER%
goto :fail

:missing_target
echo.
echo [ERROR] No target path was provided.
goto :fail

:target_not_found
echo.
echo [ERROR] Target DLL was not found:
echo %TARGET_DLL%
goto :fail

:target_wrong_name
echo.
echo [ERROR] The target file name must be BaoMiHua.dll:
echo %TARGET_DLL%
goto :fail

:script_failed
echo.
echo [ERROR] Patch script failed.
echo 上面的 PowerShell 输出已经给了更具体的原因。
goto :fail

:fail
echo.
echo 常见处理：
echo 1. 先彻底关闭 BaoMiHua，再重新执行。
echo 2. 脚本检测到已包含 settings UI 补丁时，会优先尝试自动恢复：
echo    %BACKUP_DIR%\BaoMiHua.dll.pre-settings-ui.bak
echo    如果这份备份不存在或本身也已打过 UI 补丁，才需要手动处理。
echo 3. 如果要从零重打整套补丁，先恢复：
echo    %BACKUP_DIR%\BaoMiHua.dll.pre-external-player.bak
echo 4. 如果当前备份目录里没有对应备份，请先准备正确的基底 DLL，再执行脚本。
echo.
pause
exit /b 1
