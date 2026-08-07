@echo off
TITLE FactoryForge 3D Simulator Launcher
echo ============================================================
echo   FactoryForge 3D Factory Simulator v1.0
echo   Author: Mahamed Algaroshy (محمد الجروشي)
echo ============================================================
echo.

echo [1/3] Building C# Engine...
cd /d "%~dp0engine"
dotnet build
if %errorlevel% neq 0 (
    echo [ERROR] C# Engine Build Failed!
    pause
    exit /b %errorlevel%
)
echo [OK] Engine build succeeded.
echo.

echo [2/3] Starting Godot 3D Engine...
cd /d "%~dp0engine"
start "" "D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path .
echo [OK] Godot 3D Engine launched.
echo.

echo [3/3] Waiting 4 seconds for Tag Bus server to initialize...
timeout /t 4 /nobreak >nul

echo [4/4] Launching Python Live Driver...
cd /d "%~dp0"
python "%~dp0scratch\live_driver.py"

pause
