<#
.SYNOPSIS
    Builds the Sea Power Night Vision plugin and copies it into the game's BepInEx plugins folder.

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

$ErrorActionPreference = "Stop"

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
    $vdf = "C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        foreach ($match in (Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' -AllMatches).Matches) {
            $library = $match.Groups[1].Value -replace '\\\\', '\'
            $candidate = Join-Path $library "steamapps\common\Sea Power"
            if (Test-Path (Join-Path $candidate "Sea Power.exe")) { return $candidate }
        }
    }

    return $null
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    $GameDir = Find-SeaPower
    if (-not $GameDir) {
        throw "Could not auto-detect Sea Power. Re-run with -GameDir '<path to Sea Power>'."
    }
}

Write-Host "Game directory : $GameDir"
Write-Host "Configuration  : $Configuration"

$args = @(
    "build", "src/SeaPowerNightVision/SeaPowerNightVision.csproj",
    "-c", $Configuration,
    "/p:GameDir=$GameDir"
)

if ($AnchorChain) { $args += "/p:AnchorChain=true" }
if ($NoCopy)      { $args += "/p:CopyToPlugins=false" }

& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Write-Host ""
Write-Host "Done. Launch the game and press Ctrl+N in a mission." -ForegroundColor Green
