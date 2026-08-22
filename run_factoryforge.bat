@echo off
setlocal enabledelayedexpansion
TITLE FactoryForge 3D Simulator Launcher
echo ============================================================
echo   FactoryForge 3D Factory Simulator v1.0
echo   Author: Mahamed Algaroshy (محمد الجروشي)
echo ============================================================
echo.

echo [1/5] Clearing any stale processes listening on port 7411...
powershell -NoProfile -Command "Get-NetTCPConnection -LocalPort 7411 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }" 2>nul
taskkill /F /IM Godot_v4.7.1-stable_mono_win64.exe /IM Godot_v4.7.1-stable_mono_win64_console.exe 2>nul
timeout /t 1 /nobreak >nul

echo [2/5] Locating Godot...
set "GODOT_EXE="
if defined GODOT if exist "%GODOT%" set "GODOT_EXE=%GODOT%"

if not defined GODOT_EXE (
    for %%G in (Godot_v4.7.1-stable_mono_win64_console.exe godot.exe godot-mono.exe) do (
        if not defined GODOT_EXE (
            where %%G >nul 2>nul && for /f "delims=" %%P in ('where %%G') do set "GODOT_EXE=%%P"
        )
    )
)

if not defined GODOT_EXE (
    if exist "D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" (
        set "GODOT_EXE=D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe"
    )
)

if not defined GODOT_EXE (
    echo [ERROR] Could not find a Godot 4.7.1 .NET build.
    echo         Set the GODOT environment variable to its .exe path, or put it on PATH.
    pause
    exit /b 1
)
echo [OK] Using Godot at "%GODOT_EXE%"
echo.

echo [3/5] Building C# Engine...
cd /d "%~dp0engine"
dotnet build
if %errorlevel% neq 0 (
    echo [ERROR] C# Engine Build Failed!
    pause
    exit /b %errorlevel%
)
echo [OK] Engine build succeeded.
echo.

echo [4/5] Launching Godot 3D Engine Window...
start "" "%GODOT_EXE%" --path "%~dp0engine"
echo [OK] Godot 3D Engine launched.
echo.

if /i "%~1"=="--no-driver" (
    echo Launched with --no-driver: skipping the live Python driver.
    exit /b 0
)

echo [5/5] Waiting 4 seconds for Tag Bus server to initialize...
timeout /t 4 /nobreak >nul

echo [LIVE DEMO STARTING] Launching Python Live Driver...
cd /d "%~dp0"
python "%~dp0tools\live_driver.py"

pause
