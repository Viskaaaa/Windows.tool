@echo off
setlocal
cd /d "%~dp0"

rem Installs an already-built ViskaTweak.exe onto this PC for the current user.
rem Run build-exe.bat first if the exe does not exist yet.

set "SRC=src\ViskaTweak\bin\Release\net8.0-windows\win-x64\publish\ViskaTweak.exe"
set "DEST=%LOCALAPPDATA%\Viska Tweak"

if not exist "%SRC%" (
    rem Also allow running this next to a copy of the exe.
    if exist "ViskaTweak.exe" (
        set "SRC=ViskaTweak.exe"
    ) else (
        echo  Could not find ViskaTweak.exe.
        echo  Run build-exe.bat first, or put this file next to the exe.
        echo.
        pause
        exit /b 1
    )
)

echo.
echo  Installing Viska Tweak to:
echo    %DEST%
echo.

if not exist "%DEST%" mkdir "%DEST%"
copy /y "%SRC%" "%DEST%\ViskaTweak.exe" >nul
if errorlevel 1 (
    echo  Copy failed. Close Viska Tweak if it is running, then try again.
    pause
    exit /b 1
)

rem Shortcuts, via the Windows Script Host - no extra tools needed.
powershell -NoProfile -Command ^
  "$s=New-Object -ComObject WScript.Shell;" ^
  "foreach($p in @([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('Programs'))){" ^
  "$l=$s.CreateShortcut((Join-Path $p 'Viska Tweak.lnk'));" ^
  "$l.TargetPath='%DEST%\ViskaTweak.exe';$l.WorkingDirectory='%DEST%';" ^
  "$l.IconLocation='%DEST%\ViskaTweak.exe,0';$l.Description='Game and FiveM optimizer';$l.Save()}"

rem Listing in Add or Remove Programs, per-user so no admin rights are needed.
set "KEY=HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\ViskaTweak"
reg add "%KEY%" /v DisplayName     /t REG_SZ /d "Viska Tweak" /f >nul
reg add "%KEY%" /v DisplayIcon     /t REG_SZ /d "%DEST%\ViskaTweak.exe" /f >nul
reg add "%KEY%" /v DisplayVersion  /t REG_SZ /d "1.0.0" /f >nul
reg add "%KEY%" /v Publisher       /t REG_SZ /d "Viska Tweak" /f >nul
reg add "%KEY%" /v InstallLocation /t REG_SZ /d "%DEST%" /f >nul
reg add "%KEY%" /v UninstallString /t REG_SZ /d "\"%DEST%\ViskaTweak.exe\" --uninstall" /f >nul
reg add "%KEY%" /v NoModify        /t REG_DWORD /d 1 /f >nul
reg add "%KEY%" /v NoRepair        /t REG_DWORD /d 1 /f >nul

echo  Done. Look for "Viska Tweak" on your desktop and in the Start menu.
echo  To remove it later: Settings ^> Apps, or the Uninstall button inside the app.
echo.

choice /c YN /m "Launch it now"
if errorlevel 2 goto :eof
start "" "%DEST%\ViskaTweak.exe"
