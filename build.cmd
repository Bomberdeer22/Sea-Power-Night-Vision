@echo off
REM Convenience wrapper so you don't have to fight PowerShell's execution policy.
REM Usage:
REM   build.cmd
REM   build.cmd -GameDir "D:\SteamLibrary\steamapps\common\Sea Power"
REM   build.cmd -AnchorChain
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
endlocal
