@echo off
setlocal enabledelayedexpansion

REM ==============================================================
REM  VisionSystem - make a clean, portable release folder
REM
REM    publish.bat                 -> dist\VisionPlatform
REM    publish.bat D:\Pkg          -> custom target folder
REM    publish.bat D:\Pkg --with-models   (also copy models\, ~137 MB)
REM
REM  What goes in:  exe + own DLLs + ThirdParty\ (opencv / openvino /
REM  sqlite / vcruntime) + empty data folders.
REM  What stays out: *.pdb *.exp *.lib, Config\ (appconfig.json), Logs\*,
REM  Images\* (dev data), Recipes\* (dev recipes), thresholds_new.pnm,
REM  detect_diag.txt, models\ (unless --with-models).
REM ==============================================================

set "CFG=Release"
set "SRC=%~dp0bin\x64\%CFG%"
set "DST=%~1"
set "WITHMODELS=%~2"
if "%DST%"=="" set "DST=%~dp0dist\VisionPlatform"
if "%WITHMODELS%"=="--with-models" set "WITHMODELS=1" else set "WITHMODELS="
if "%~1"=="--with-models" (set "DST=%~dp0dist\VisionPlatform" & set "WITHMODELS=1")

if not exist "%SRC%\VisionPlatform.exe" (
    echo [ERROR] Not built: %SRC%\VisionPlatform.exe
    echo         Run build.bat Release first.
    exit /b 1
)

echo [INFO] Source : %SRC%
echo [INFO] Target : %DST%

if exist "%DST%" (
    echo [INFO] Target exists - refreshing binaries, keeping data folders
) else (
    mkdir "%DST%" 2>nul
)

REM ---- 1. host binaries (dll wildcard = our managed DLLs + VisionAlgo.Native; no pdb) ----
xcopy /Y /D /Q "%SRC%\VisionPlatform.exe" "%DST%\" >nul || exit /b 1
xcopy /Y /D /Q "%SRC%\*.dll" "%DST%\" >nul || exit /b 1

REM ---- 2. third-party native DLLs (opencv / openvino / sqlite) ----
if not exist "%DST%\ThirdParty" mkdir "%DST%\ThirdParty"
xcopy /Y /D /Q /E /I "%SRC%\ThirdParty" "%DST%\ThirdParty" >nul || exit /b 1

REM ---- 3. VC++ 2015-2022 runtime (app-local, so the target PC needs no Redist) ----
if not exist "%DST%\ThirdParty\vcruntime" mkdir "%DST%\ThirdParty\vcruntime"
for %%F in (msvcp140.dll vcruntime140.dll vcruntime140_1.dll vcomp140.dll concrt140.dll) do (
    if exist "%SystemRoot%\System32\%%F" (
        xcopy /Y /D /Q "%SystemRoot%\System32\%%F" "%DST%\ThirdParty\vcruntime\" >nul
    ) else (
        echo [WARN] Missing runtime on this PC: %%F
    )
)

REM ---- 4. data folders (created empty on first run; keep existing content) ----
for %%D in (Config Recipes Templates Images Logs Calibrations License Models\OCR Models\DL) do (
    if not exist "%DST%\%%D" mkdir "%DST%\%%D"
)

REM ---- 5. optional: deep-learning models ----
if defined WITHMODELS (
    if exist "%SRC%\Models\DL" (
        echo [INFO] Copying models\ ...
        xcopy /Y /D /Q /E /I "%SRC%\Models\DL" "%DST%\Models\DL" >nul
    )
) else (
    echo [INFO] models\ skipped - pass --with-models if you need YOLO / OCR-DL
)

REM ---- 5b. trial licence template (deploy\License -> <pkg>\License) ----
if exist "%~dp0deploy\License\*.lic" (
    echo [INFO] Copying trial licence template ...
    xcopy /Y /D /Q "%~dp0deploy\License\*.lic" "%DST%\License\" >nul
)

REM ---- 6. deployment notes ----
> "%DST%\DEPLOY.txt" (
    echo VisionSystem - portable deployment
    echo.
    echo Requirements on the target PC:
    echo   - Windows 10 x64 or later  ^(.NET Framework 4.8 is built in^)
    echo   - Write permission to this folder ^(do NOT put it in Program Files^)
    echo   - VC++ runtime is bundled under ThirdParty\vcruntime - no install needed
    echo.
    echo First start:
    echo   - Config\appconfig.json is generated automatically
    echo   - License\ holds license.lic - drop the file from your vendor here
    echo   - licence_30days.lic is a TRIAL template: first run binds it to this PC
    echo   - Recipes / Templates / Images are empty: import a project bundle
    echo     or create a recipe in the UI
    echo   - Cameras are NOT configured: bind them in Camera management
    echo.
    echo Upgrading an existing install:
    echo   - replace exe + DLLs + thirdparty\, keep Recipes / Templates
)

echo.
echo [OK] Portable package ready: %DST%
echo      Size:
dir /s "%DST%" 2>nul | findstr /C:"File(s)"
exit /b 0
