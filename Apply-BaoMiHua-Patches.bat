@echo off
setlocal EnableExtensions DisableDelayedExpansion
title BaoMiHua Patch Launcher

set "PACKAGE_DIR=%~dp0"
set "EXTERNAL_PATCHER=%PACKAGE_DIR%PatchBaoMiHuaExternalPlayer.exe"
set "EXTERNAL_HELPER=%PACKAGE_DIR%ExternalPlayerPatchHelper.dll"
set "SETTINGS_PATCHER=%PACKAGE_DIR%PatchBaoMiHuaSettingsPageUi.exe"
set "SETTINGS_HELPER=%PACKAGE_DIR%SettingsPageUiPatchHelper.dll"
set "BACKUP_DIR=%PACKAGE_DIR%backups"

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
echo [1/2] Applying external player patch...
"%EXTERNAL_PATCHER%" "%TARGET_DLL%" "%EXTERNAL_HELPER%" "%BACKUP_DIR%\BaoMiHua.dll.pre-external-player.bak"
if errorlevel 1 goto :external_patch_failed

echo.
echo [2/2] Applying settings page UI patch...
"%SETTINGS_PATCHER%" "%TARGET_DLL%" "%SETTINGS_HELPER%" "%BACKUP_DIR%\BaoMiHua.dll.pre-settings-ui.bak"
if errorlevel 1 goto :settings_patch_failed

echo.
echo [DONE] Patch applied successfully:
echo %TARGET_DLL%
echo.
echo Backups:
echo %BACKUP_DIR%
echo.
pause
exit /b 0

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

:external_patch_failed
echo.
echo [ERROR] External player patch failed.
goto :fail

:settings_patch_failed
echo.
echo [ERROR] Settings page UI patch failed.
goto :fail

:fail
echo.
echo Make sure BaoMiHua is fully closed and the target DLL is a clean base file.
echo.
pause
exit /b 1
