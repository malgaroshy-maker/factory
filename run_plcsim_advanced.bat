@echo off
TITLE FactoryForge — Siemens PLCSIM Advanced Launcher
echo ============================================================
echo   FactoryForge Siemens PLCSIM Advanced Direct Driver
echo ============================================================
echo.
echo This attaches to an ALREADY RUNNING 3D engine (launch it first with
echo run.py or run_factoryforge.bat) via the PLCSIM Advanced shared-memory API.
echo.

if "%~1"=="" (
    echo Usage: run_plcsim_advanced.bat ^<PLCSIM instance name^>
    echo.
    echo   The instance name is the one shown in the S7-PLCSIM Advanced Control
    echo   Panel, which is not necessarily the CPU's name in TIA Portal.
    pause
    exit /b 1
)

cd %~dp0sidecar
python -m factoryforge_sidecar connect --driver plcsim-advanced -o instance %~1
pause
