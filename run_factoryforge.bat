@echo off
REM Thin wrapper so Windows users can double-click a .bat file instead of
REM typing `python run.py`. All the actual launcher logic lives in run.py.
TITLE FactoryForge 3D Simulator Launcher
where python >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Could not find `python` on PATH. Install Python 3.11+ from
    echo         https://www.python.org/downloads/ and try again.
    pause
    exit /b 1
)
python "%~dp0run.py" %*
if errorlevel 1 pause
