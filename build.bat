@echo off
setlocal

REM ==============================================================
REM  VisionSystem - single solution build script
REM
REM  Projects (all under VisionSystem.sln):
REM    VisionAlgo.Native      C++ / OpenCV   algorithm DLL   (x64)
REM    VisionSystem.Controls  C#  image viewer control library
REM    VisionPlatform         C#  industrial vision software
REM    VisionSystem.Demo      C#  demo for the control library
REM
REM  Usage:
REM    build.bat            Debug   | x64
REM    build.bat Release    Release | x64
REM ==============================================================

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

set "VSROOT=D:\Program Files\Microsoft Visual Studio\2022\Community"
set "MSBUILD=%VSROOT%\MSBuild\Current\Bin\MSBuild.exe"

if not exist "%MSBUILD%" (
    echo [ERROR] MSBuild not found: %MSBUILD%
    echo         Edit VSROOT in build.bat to match your VS install path.
    exit /b 1
)

echo [INFO] Configuration: %CONFIG% ^| x64

REM The host shell may inject proxy vars in two cases (https_proxy / HTTPS_PROXY).
REM MSBuild's CL task builds the child-process environment with a case-insensitive
REM dictionary and then throws "item already added". Clear them first.
set http_proxy=
set https_proxy=
set HTTP_PROXY=
set HTTPS_PROXY=
set ftp_proxy=
set FTP_PROXY=
set all_proxy=
set ALL_PROXY=
set no_proxy=
set NO_PROXY=

pushd "%~dp0"
"%MSBUILD%" VisionSystem.sln -t:Build -p:Configuration=%CONFIG% -v:minimal -nologo
set "RC=%ERRORLEVEL%"
popd

echo.
if "%RC%"=="0" (
    echo [OK] BUILD SUCCEEDED
) else (
    echo [FAIL] BUILD FAILED, code %RC%
)

exit /b %RC%
