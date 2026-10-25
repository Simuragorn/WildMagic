@echo off
setlocal
cd /d "%~dp0"

if not exist "WindowsHost\WildMagicHost.exe" (
    echo WildMagicHost.exe was not found. Build the multiplayer test in Unity first.
    pause
    exit /b 1
)

if not exist "WebGL\index.html" (
    echo The WebGL build was not found. Build the multiplayer test in Unity first.
    pause
    exit /b 1
)

start "WildMagic Host" "WindowsHost\WildMagicHost.exe"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Serve-WildMagicWebGL.ps1" -Root "%~dp0WebGL" -Port 8080
