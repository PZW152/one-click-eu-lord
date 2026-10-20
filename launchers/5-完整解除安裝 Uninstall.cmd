@echo off
setlocal
title One-Click EU Grandpa - Uninstall
rem Self-elevate, revert everything, then offer to delete app data.
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0'"
    exit /b
)
set "ENGINE=%~dp0..\src\EUGrandpa.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -File "%ENGINE%" -Action Uninstall -FullClean
echo.
pause
