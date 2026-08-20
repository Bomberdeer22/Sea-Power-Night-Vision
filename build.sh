#!/usr/bin/env bash
# Builds SeaPowerNightVision.dll and (optionally) copies it into the game's plugin folder.
#
#   ./build.sh                                   # uses the default Steam path
#   ./build.sh "/mnt/d/SteamLibrary/steamapps/common/Sea Power"
#
# Requires the .NET SDK (8.0 or newer) and a BepInEx 5.4.x install in the game folder.
set -euo pipefail

GAME_DIR="${1:-${SEA_POWER_DIR:-C:\\Program Files (x86)\\Steam\\steamapps\\common\\Sea Power}}"
CONFIG="${2:-Release}"

echo "Game directory: $GAME_DIR"
dotnet build src/SeaPowerNightVision/SeaPowerNightVision.csproj \
    -c "$CONFIG" \
    /p:GameDir="$GAME_DIR"

echo
echo "Output: src/SeaPowerNightVision/bin/$CONFIG/net472/SeaPowerNightVision.dll"
