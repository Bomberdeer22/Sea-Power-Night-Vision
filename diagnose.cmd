@echo off
REM Diagnoses why the night vision plugin isn't loading.
REM
REM   diagnose.cmd
REM   diagnose.cmd -EnableConsole                     turns on the BepInEx debug console
REM   diagnose.cmd -GameDir "D:\...\Sea Power"
REM
REM Writes a full report to diagnose-log.txt next to this file.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0diagnose.ps1" %*
echo.
pause
endlocal
