@echo off
setlocal
cd /d "%~dp0"

rem Double-click this to produce the shareable RigBooster.exe.
rem Keep the secret below identical to BuildSecret in
rem src\RigBooster\Services\LicenseService.cs, or the app cannot read its own key table.
set SECRET=PsHiRveTG2Bq4EXx6GiwJliYBTvlxgMbfj7xVxuu

echo.
echo  ==============================
echo   Rig Booster - building
echo  ==============================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo  The .NET SDK is not installed, or this window was opened before installing it.
    echo.
    echo  Install the SDK ^(not the Runtime^) from:
    echo    https://dotnet.microsoft.com/download/dotnet/8.0
    echo  then close this window and run the file again.
    echo.
    pause
    exit /b 1
)

if not exist "users.txt" (
    echo  No users.txt found - creating one with the default accounts.
    > "users.txt" echo viska,viska9
    >> "users.txt" echo giorgakis,giorgakis
    echo  Edit users.txt to add people, then run this again.
    echo.
)

echo  [1/2] Packing license keys...
dotnet run --project tools/LicenseGen -- pack %SECRET% users.txt src/RigBooster/licenses.dat
if errorlevel 1 (
    echo.
    echo  Packing the keys failed. Check that users.txt has one "name,key" per line.
    pause
    exit /b 1
)

echo.
echo  [2/2] Building the app. First run takes a few minutes - this is normal.
dotnet publish src/RigBooster/RigBooster.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true
if errorlevel 1 (
    echo.
    echo  The build failed. Copy the red lines above and send them over.
    pause
    exit /b 1
)

set OUT=src\RigBooster\bin\Release\net8.0-windows\win-x64\publish

echo.
echo  ==============================
echo   Done. RigBooster.exe is in:
echo   %OUT%
echo  ==============================
echo.
echo  That single file is the whole app - send it to anyone.
echo  On their first run Windows shows a blue SmartScreen box:
echo  click "More info" then "Run anyway".
echo.

if exist "%OUT%\RigBooster.exe" explorer "%OUT%"
pause
