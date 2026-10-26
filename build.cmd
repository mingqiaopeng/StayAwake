@echo off
rem ============================================================================
rem  StayAwake - build script
rem
rem  Usage:  build.cmd  [/clean]
rem
rem  Requires the .NET Framework 4.x compiler that ships with Windows itself.
rem  No Visual Studio, no NuGet, no SDK download.
rem
rem  NOTE: This file must stay pure ASCII. cmd.exe reads .cmd using the OEM
rem        code page, so non-ASCII comments here get mangled into garbage
rem        commands. Keep it English-only.
rem ============================================================================
setlocal

rem Prefer the 64-bit compiler, fall back to the 32-bit one on x86-only
rem systems where Framework64 does not exist. /platform:anycpu output is
rem identical either way.
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. Install .NET Framework 4.x, or edit the
    echo         CSC path at the top of this script.
    exit /b 1
)

pushd "%~dp0"

if /i "%~1"=="/clean" (
    if exist StayAwake.exe del /q StayAwake.exe
    if exist StayAwake.pdb del /q StayAwake.pdb
    echo Cleaned.
)

rem  /win32manifest  declares PerMonitorV2 DPI awareness. Without it the tray
rem                  icon is blurry on 150%% displays.
rem  /warnaserror+   zero warnings expected.
echo Compiling...
"%CSC%" ^
    /nologo ^
    /target:winexe ^
    /platform:anycpu ^
    /optimize+ ^
    /warnaserror+ ^
    /win32manifest:app.manifest ^
    /out:StayAwake.exe ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /resource:SA_ICON_GREY.png,StayAwake.SA_ICON_GREY.png ^
    /resource:SA_ICON_LIGHT.png,StayAwake.SA_ICON_LIGHT.png ^
    /resource:SA_ICON_RED.png,StayAwake.SA_ICON_RED.png ^
    /resource:SA_ICON_NAVY.png,StayAwake.SA_ICON_NAVY.png ^
    StayAwake.cs Log.cs UiKit.cs AutoStart.cs TrayIconFactory.cs ^
    TrayController.cs PanelForm.cs MainForm.cs Program.cs

if errorlevel 1 (
    echo.
    echo [FAILED] Build did not succeed.
    popd
    exit /b 1
)

echo [OK] StayAwake.exe
for %%F in (StayAwake.exe) do echo         %%~zF bytes

popd
endlocal
exit /b 0
