@echo off
REM Build a distributable Windows release and then check it is actually one.
REM
REM Thin wrapper so this can be double-clicked, matching run_factoryforge.bat.
REM All the real work lives in tools/build_release.py and, crucially, in
REM tools/packaging/check_release.py -- because an export that produced a
REM binary is not the same as an export that produced a working one. Godot
REM exits 0 on an export "completed with warnings", and a missing .NET solution
REM is one of those warnings: you get an .exe whose every C# script fails at
REM runtime, silently. So the build alone is never the pass condition here.
REM
REM   build_windows.bat                 engine + frozen sidecar, zipped, checked
REM   build_windows.bat --no-sidecar    engine only; much faster, no PLC drivers
REM   build_windows.bat --skip-archive  leave dist\windows\ unzipped
REM
REM Anything you pass is handed to build_release.py unchanged.
REM
REM It pauses at the end so a double-clicked window does not vanish before you
REM can read the result. Set FF_NO_PAUSE=1 to suppress that -- do this in CI,
REM where a runner with a real console would otherwise wait forever for a key
REM that is never coming. (Detecting a double-click instead was tried: it
REM cannot be done reliably, because `cmd /c script.bat` from a shell and an
REM Explorer double-click produce the same %cmdcmdline%.)

SETLOCAL
TITLE FactoryForge - build Windows release

where python >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Could not find `python` on PATH. Install Python 3.11+ from
    echo         https://www.python.org/downloads/ and try again.
    pause
    exit /b 1
)

echo.
echo === 1/2  Exporting engine and freezing sidecar =====================
echo.
python "%~dp0tools\build_release.py" --target windows %*
if errorlevel 1 (
    echo.
    echo [FAILED] The build did not complete. Nothing was published.
    if not "%FF_NO_PAUSE%"=="1" pause
    exit /b 1
)

echo.
echo === 2/2  Checking the built release, not the checkout ==============
echo.
REM Runs every headless self-test against the exported binary. A checkout can
REM pass all of them while the release fails -- two of them read fixtures that
REM only Godot's own FileAccess can reach once packed into the .pck -- and it
REM asks the frozen sidecar which drivers it can really run, which is the one
REM question `connect --help` cannot answer.
python "%~dp0tools\packaging\check_release.py" --target windows
if errorlevel 1 (
    echo.
    echo [FAILED] The binary was produced but did not pass the release gate.
    echo          Do not ship dist\FactoryForge-windows.zip from this run.
    if not "%FF_NO_PAUSE%"=="1" pause
    exit /b 1
)

echo.
echo [OK] dist\windows\FactoryForge.exe  and  dist\FactoryForge-windows.zip
echo.
if not "%FF_NO_PAUSE%"=="1" pause
