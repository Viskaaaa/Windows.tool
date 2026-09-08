@echo off
setlocal
cd /d "%~dp0"

rem Double-click to launch the app for testing, without building the standalone exe.
rem Keep this window open while the app runs - closing it closes the app.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo  The .NET SDK is not installed. Get it from:
    echo    https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

if not exist "src\RigBooster\licenses.dat" (
    echo  No license table yet - run build-exe.bat once first.
    pause
    exit /b 1
)

echo  Starting Rig Booster. The window opens in a few seconds...
dotnet run --project src/RigBooster
if errorlevel 1 pause
