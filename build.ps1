<#
.SYNOPSIS
    Builds the Sea Power Night Vision plugin and copies it into the game's BepInEx plugins folder.

.DESCRIPTION
    Everything printed to the console is also written to build-log.txt next to this script, so
    you can read it afterwards even if the window closed. Prefer running build.cmd, which keeps
    the window open and bypasses the PowerShell execution policy.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Sea Power"
    .\build.ps1 -AnchorChain      # also compiles the Anchor Chain / Workshop entry point
#>
[CmdletBinding()]
param(
    [string]$GameDir = "",
    [string]$Configuration = "Release",
    [switch]$AnchorChain,
    [switch]$NoCopy
)

$ErrorActionPreference = "Continue"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$logPath = Join-Path $scriptDir "build-log.txt"

try { Stop-Transcript | Out-Null } catch { }
try { Start-Transcript -Path $logPath -Force | Out-Null } catch { }

function Write-Section($text) {
    Write-Host ""
    Write-Host "--- $text " -ForegroundColor Cyan
}

function Fail($message) {
    Write-Host ""
    Write-Host "ERROR: $message" -ForegroundColor Red
    Write-Host "Log written to: $logPath"
    try { Stop-Transcript | Out-Null } catch { }
    exit 1
}

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

    # Walk any extra Steam libraries listed in libraryfolders.vdf.
    foreach ($vdf in @(
        "C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf",
        "C:\Program Files\Steam\steamapps\libraryfolders.vdf")) {

        if (-not (Test-Path $vdf)) { continue }

        foreach ($match in (Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' -AllMatches).Matches) {
            $library = $match.Groups[1].Value -replace '\\\\', '\'
            $candidate = Join-Path $library "steamapps\common\Sea Power"
            if (Test-Path (Join-Path $candidate "Sea Power.exe")) { return $candidate }
        }
    }

    return $null
}

Write-Section "Environment"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Fail @"
The .NET SDK is not installed (the 'dotnet' command was not found).

Install the .NET SDK 8.0 (or newer) from:
  https://dotnet.microsoft.com/download/dotnet/8.0
Choose the SDK installer for Windows x64, then re-run this script in a NEW terminal.
"@
}

$sdks = & dotnet --list-sdks 2>&1
Write-Host "dotnet   : $($dotnet.Source)"
Write-Host "SDKs     :"
$sdks | ForEach-Object { Write-Host "           $_" }

if (-not ($sdks -match '^\d+\.')) {
    Fail @"
'dotnet' is installed but no SDK was found (you may only have the runtime).
Install the .NET SDK 8.0 from https://dotnet.microsoft.com/download/dotnet/8.0
"@
}

Write-Section "Game location"

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $GameDir = Find-SeaPower
    if (-not $GameDir) {
        Fail @"
Could not auto-detect the Sea Power install folder.

Re-run and point at it yourself, e.g.:
  build.cmd -GameDir "D:\SteamLibrary\steamapps\common\Sea Power"

(The folder is the one containing 'Sea Power.exe'. In Steam:
 right-click Sea Power > Manage > Browse local files.)
"@
    }
}

Write-Host "Game dir : $GameDir"

$managed = Join-Path $GameDir "Sea Power_Data\Managed"
$bepinex = Join-Path $GameDir "BepInEx\core"

if (-not (Test-Path (Join-Path $GameDir "Sea Power.exe"))) {
    Fail "'Sea Power.exe' was not found in '$GameDir'. Pass the correct folder with -GameDir."
}

if (-not (Test-Path (Join-Path $managed "UnityEngine.CoreModule.dll"))) {
    Fail "Could not find the game's Managed folder at '$managed'. Is this really the Sea Power install?"
}

if (-not (Test-Path (Join-Path $bepinex "BepInEx.dll"))) {
    Fail @"
BepInEx is not installed at '$bepinex'.

1. Download BepInEx 5.4.x, x64, for Unity Mono:
     https://github.com/BepInEx/BepInEx/releases
2. Extract it so that the 'BepInEx' folder sits next to 'Sea Power.exe'.
3. Launch the game once, then close it (this generates BepInEx/core and the config).
4. Re-run this script.
"@
}

Write-Host "Managed  : OK"
Write-Host "BepInEx  : OK"

Write-Section "Building ($Configuration)"

$arguments = @(
    "build", (Join-Path $scriptDir "src\SeaPowerNightVision\SeaPowerNightVision.csproj"),
    "-c", $Configuration,
    "/p:GameDir=$GameDir",
    "-v", "minimal", "-nologo"
)

if ($AnchorChain) { $arguments += "/p:AnchorChain=true" }
if ($NoCopy)      { $arguments += "/p:CopyToPlugins=false" }

& dotnet @arguments
$buildExit = $LASTEXITCODE

if ($buildExit -ne 0) {
    Fail @"
The build failed (dotnet exit code $buildExit). The compiler output is above and in:
  $logPath

Please send that log if you want help fixing it.
"@
}

Write-Section "Result"

$dll = Join-Path $scriptDir "src\SeaPowerNightVision\bin\$Configuration\net472\SeaPowerNightVision.dll"
if (Test-Path $dll) {
    Write-Host "Built    : $dll"
}

if (-not $NoCopy) {
    $installed = Join-Path $GameDir "BepInEx\plugins\SeaPowerNightVision\SeaPowerNightVision.dll"
    if (Test-Path $installed) {
        Write-Host "Installed: $installed" -ForegroundColor Green
    } else {
        Write-Host "WARNING: the DLL was built but not found at '$installed'. Copy it there manually." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Done. Launch the game, load a mission and press Ctrl+N." -ForegroundColor Green
Write-Host "If nothing happens, check '$($GameDir)\BepInEx\LogOutput.log' for 'Sea Power Night Vision'."
Write-Host "Log written to: $logPath"

try { Stop-Transcript | Out-Null } catch { }
exit 0
