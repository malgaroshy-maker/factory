@echo off
TITLE FactoryForge — Siemens PLCSIM Advanced Launcher
echo ============================================================
echo   FactoryForge Siemens PLCSIM Advanced Direct Driver
echo ============================================================
echo.

cd %~dp0sidecar
python -m factoryforge_sidecar demo --driver plcsim-advanced -o instance Sorting_PLC
pause
