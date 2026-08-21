<#
.SYNOPSIS
    Collects everything needed to work out why the night vision plugin isn't loading.

.DESCRIPTION
    Checks the BepInEx install, the doorstop injector, the plugin file itself and the logs,
    then writes the whole report to diagnose-log.txt next to this script.

.EXAMPLE
    diagnose.cmd
    diagnose.cmd -EnableConsole      # also switches the BepInEx debug console on
#>
[CmdletBinding()]
param(
    [string]$GameDir = "",
    [switch]$EnableConsole
)

$ErrorActionPreference = "Continue"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$logPath = Join-Path $scriptDir "diagnose-log.txt"

try { Stop-Transcript | Out-Null } catch { }
try { Start-Transcript -Path $logPath -Force | Out-Null } catch { }

function Section($text) {
    Write-Host ""
    Write-Host "=== $text ===" -ForegroundColor Cyan
}

function Good($text) { Write-Host "  [ OK ] $text" -ForegroundColor Green }
function Bad($text)  { Write-Host "  [FAIL] $text" -ForegroundColor Red }
function Info($text) { Write-Host "         $text" }

function Find-SeaPower {
    $candidates = @(
        "C:\Program Files (x86)\Steam\steamapps\common\Sea Power",
        "C:\Program Files\Steam\steamapps\common\Sea Power",
        "D:\SteamLibrary\steamapps\common\Sea Power",
        "E:\SteamLibrary\steamapps\common\Sea Power",
        "D:\Steam\steamapps\common\Sea Power",
        "E:\Steam\steamapps\common\Sea Power"
    )
    foreach ($path in $candidates) {
        if (Test-Path (Join-Path $path "Sea Power.exe")) { return $path }
    }
    foreach ($vdf in @(
        "C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf",
        "C:\Program Files\Steam\steamapps\libraryfolders.vdf")) {
        if (-not (Test-Path $vdf)) { continue }
        foreach ($m in (Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' -AllMatches).Matches) {
            $library = $m.Groups[1].Value -replace '\\\\', '\'
            $candidate = Join-Path $library "steamapps\common\Sea Power"
            if (Test-Path (Join-Path $candidate "Sea Power.exe")) { return $candidate }
        }
    }
    return $null
}

if ([string]::IsNullOrWhiteSpace($GameDir)) { $GameDir = Find-SeaPower }
if (-not $GameDir) {
    Bad "Could not find the Sea Power folder. Re-run as: diagnose.cmd -GameDir ""<path>"""
    try { Stop-Transcript | Out-Null } catch { }
    exit 1
}

Section "Game"
Info "Folder: $GameDir"
$exe = Join-Path $GameDir "Sea Power.exe"
if (Test-Path $exe) {
    Good "Sea Power.exe found"
    Info ("Version: " + (Get-Item $exe).VersionInfo.FileVersion)
} else {
    Bad "Sea Power.exe NOT found here"
}

Section "Doorstop (what actually injects BepInEx)"
foreach ($f in @("winhttp.dll", "doorstop_config.ini", ".doorstop_version", "version.dll")) {
    $p = Join-Path $GameDir $f
    if (Test-Path $p) { Good "$f present" } else { Info "$f missing" }
}

$doorstopCfg = Join-Path $GameDir "doorstop_config.ini"
if (Test-Path $doorstopCfg) {
    Info "--- doorstop_config.ini ---"
    Get-Content $doorstopCfg | ForEach-Object { Info $_ }
    $enabledLine = Select-String -Path $doorstopCfg -Pattern '^\s*enabled\s*=\s*(\S+)' -AllMatches
    if ($enabledLine) {
        $val = $enabledLine.Matches[0].Groups[1].Value
        if ($val -match 'true') { Good "doorstop enabled = $val" } else { Bad "doorstop enabled = $val  <-- BepInEx will NOT run" }
    }
} else {
    Bad "No doorstop_config.ini: BepInEx cannot inject, so no plugin will ever load."
}

$proxyDir = Join-Path $GameDir "BepInEx\proxy"
if (Test-Path $proxyDir) {
    Bad "Found BepInEx\proxy - the injector files are in the wrong place."
    Info "Move everything from '$proxyDir' into '$GameDir' and relaunch."
}

Section "BepInEx"
$bepinex = Join-Path $GameDir "BepInEx"
if (-not (Test-Path $bepinex)) {
    Bad "No BepInEx folder at all."
} else {
    Good "BepInEx folder present"
    $core = Join-Path $bepinex "core"
    if (Test-Path $core) {
        Get-ChildItem $core -Filter *.dll | ForEach-Object {
            Info ("core: {0}  v{1}" -f $_.Name, $_.VersionInfo.FileVersion)
        }
    } else {
        Bad "BepInEx\core is missing - run the game once after installing BepInEx."
    }
}

Section "Installed plugins"
$plugins = Join-Path $bepinex "plugins"
if (Test-Path $plugins) {
    Get-ChildItem $plugins -Recurse -Filter *.dll | ForEach-Object {
        Info ("{0}   ({1:N0} bytes, {2})" -f $_.FullName.Replace($plugins, "plugins"), $_.Length, $_.LastWriteTime)
    }
    $ours = Get-ChildItem $plugins -Recurse -Filter "SeaPowerNightVision.dll" -ErrorAction SilentlyContinue
    if ($ours) { Good "SeaPowerNightVision.dll is installed" } else { Bad "SeaPowerNightVision.dll is NOT under BepInEx\plugins" }
} else {
    Bad "No BepInEx\plugins folder."
}

Section "BepInEx config"
$bepCfg = Join-Path $bepinex "config\BepInEx.cfg"
if (Test-Path $bepCfg) {
    Good "BepInEx.cfg present"
    $consoleOn = (Select-String -Path $bepCfg -Pattern '^\s*Enabled\s*=\s*true' -Context 3,0 |
                  Where-Object { $_.Context.PreContext -match '\[Logging.Console\]' })
    if ($consoleOn) { Info "Debug console: enabled" } else { Info "Debug console: disabled (normal)" }

    if ($EnableConsole) {
        $content = Get-Content $bepCfg -Raw
        $updated = [regex]::Replace($content, '(?ms)(\[Logging\.Console\].*?^Enabled\s*=\s*)false', '${1}true')
        if ($updated -ne $content) {
            Set-Content -Path $bepCfg -Value $updated -Encoding UTF8
            Good "Enabled the BepInEx console. Relaunch the game to see it."
        } else {
            Info "Console already enabled (or the config layout was unexpected)."
        }
    }
} else {
    Bad "No BepInEx\config\BepInEx.cfg - BepInEx has never run successfully."
}

Section "LogOutput.log"
$logOut = Join-Path $bepinex "LogOutput.log"
if (Test-Path $logOut) {
    $item = Get-Item $logOut
    Good "LogOutput.log present"
    Info ("Last written: {0}  ({1:N0} bytes)" -f $item.LastWriteTime, $item.Length)
    $age = (Get-Date) - $item.LastWriteTime
    if ($age.TotalHours -gt 2) {
        Bad "This log is $([int]$age.TotalHours)h old - BepInEx did not run the last time you played."
    }

    Info "--- first 25 lines ---"
    Get-Content $logOut -TotalCount 25 | ForEach-Object { Info $_ }

    Info "--- lines mentioning the mod / errors ---"
    $hits = Select-String -Path $logOut -Pattern 'NightVision|Night Vision|Sea Power Night|error|exception|fail' -AllMatches
    if ($hits) { $hits | Select-Object -Last 40 | ForEach-Object { Info $_.Line } } else { Info "(none)" }
} else {
    Bad "No LogOutput.log - BepInEx is not being injected at all."
}

Section "Unity Player.log"
$playerLog = Join-Path $env:USERPROFILE "AppData\LocalLow\Triassic Games\Sea Power\Player.log"
if (Test-Path $playerLog) {
    $item = Get-Item $playerLog
    Good "Player.log present (last written $($item.LastWriteTime))"
    $hits = Select-String -Path $playerLog -Pattern 'NightVision|Night Vision|AnchorChain|BepInEx' -AllMatches
    if ($hits) { $hits | Select-Object -Last 30 | ForEach-Object { Info $_.Line } } else { Info "(no relevant lines)" }
} else {
    Info "No Player.log found at $playerLog"
}

Section "Summary"
Write-Host "Report saved to: $logPath" -ForegroundColor Yellow
Write-Host "Send that file (or its contents) back and the cause should be obvious."

try { Stop-Transcript | Out-Null } catch { }
exit 0
