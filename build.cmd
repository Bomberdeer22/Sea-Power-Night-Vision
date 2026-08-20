@echo off
REM Convenience wrapper so you don't have to fight PowerShell's execution policy.
REM
REM Usage (double-click, or from a terminal):
REM   build.cmd
REM   build.cmd -GameDir "D:\SteamLibrary\steamapps\common\Sea Power"
REM   build.cmd -AnchorChain
REM
REM Everything printed here is also saved to build-log.txt next to this file.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
set EXITCODE=%ERRORLEVEL%

echo.
if "%EXITCODE%"=="0" (
    echo ===== BUILD SUCCEEDED =====
) else (
    echo ===== BUILD FAILED ^(exit code %EXITCODE%^) =====
    echo A full log was written to "%~dp0build-log.txt"
)
echo.
pause
endlocal & exit /b %EXITCODE%
