@echo off
TITLE FactoryForge 3D Simulator Launcher
echo ============================================================
echo   FactoryForge 3D Factory Simulator v1.0
echo   Author: Mahamed Algaroshy (محمد الجروشي)
echo ============================================================
echo.

echo [1/4] Clearing any stale processes listening on port 7411...
powershell -NoProfile -Command "Get-NetTCPConnection -LocalPort 7411 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }" 2>nul
taskkill /F /IM Godot_v4.7.1-stable_mono_win64.exe /IM Godot_v4.7.1-stable_mono_win64_console.exe 2>nul
timeout /t 1 /nobreak >nul

echo [2/4] Building C# Engine...
cd /d "%~dp0engine"
dotnet build
if %errorlevel% neq 0 (
    echo [ERROR] C# Engine Build Failed!
    pause
    exit /b %errorlevel%
)
echo [OK] Engine build succeeded.
echo.

echo [3/4] Launching Godot 3D Engine Window...
cd /d "%~dp0engine"
if exist "D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe" (
    start "" "D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe" --path "%~dp0engine"
) else (
    start "" "D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path "%~dp0engine"
)
echo [OK] Godot 3D Engine launched.
echo.

echo [4/4] Waiting 4 seconds for Tag Bus server to initialize...
timeout /t 4 /nobreak >nul

echo [LIVE DEMO STARTING] Launching Python Live Driver...
cd /d "%~dp0"
python "%~dp0scratch\live_driver.py"

pause
