@echo off
setlocal
title One-Click EU Overlord - Restore
rem Self-elevate, then revert region + policy file and remove persistence.
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0'"
    exit /b
)
set "ENGINE=%~dp0..\src\EUOverlord.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -File "%ENGINE%" -Action Restore
echo.
pause
