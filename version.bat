@echo off
setlocal
REM ==============================================================
REM  VisionSystem version tool
REM
REM    version.bat                  show current version
REM    version.bat 1.0.0.1          set version (all C# projects)
REM
REM  Single source of truth: src\SharedVersion.cs
REM  Wrapper only; real logic lives in tools\version\Set-Version.ps1
REM ==============================================================

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\version\Set-Version.ps1" %*
exit /b %ERRORLEVEL%
