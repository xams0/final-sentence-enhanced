#!/bin/bash
# Dev: builds the native DLL and installs the mod into the developer's runtime folder
# (%APPDATA%\Lanppart3\runtime\dev), with the LanppartLab REPL. Start a game on it with an injector
# (LANPPART3_DEV=1 runs the mod without the launcher; LAB_ID / LAB_DIR drive the REPL, see mod/dev).
# Usage: mod/deploy.sh
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UE4SS="${UE4SS_DIR:-$ROOT/_work/dl/ue4ss/std}"
ZIG="${ZIG:-$ROOT/_work/dl/zig-x86_64-windows-0.17.0/zig.exe}"
DEST="$APPDATA/Lanppart3/runtime/dev"

mkdir -p "$ROOT/build"
"$ZIG" cc -shared -target x86_64-windows-gnu -O2 -Wall -Wextra \
  -o "$ROOT/build/lanppart_native.dll" "$ROOT/native/lanppart_native.c"

mkdir -p "$DEST/Mods/Lanppart/Scripts" "$DEST/Mods/Lanppart/Assets" "$DEST/Mods/LanppartLab/Scripts"
[ -f "$DEST/UE4SS.dll" ] || cp "$UE4SS/ue4ss/UE4SS.dll" "$DEST/"
sed -e 's/^HookLoadMap = 1/HookLoadMap = 0/' -e 's/^HookInitGameState = 1/HookInitGameState = 0/' \
  "$UE4SS/ue4ss/UE4SS-settings.ini" > "$DEST/UE4SS-settings.ini"
printf 'Lanppart : 1\nLanppartLab : 1\n' > "$DEST/Mods/mods.txt"
cp "$ROOT"/mod/Lanppart/Scripts/*.lua "$DEST/Mods/Lanppart/Scripts/"
cp "$ROOT"/mod/Lanppart/Assets/*.png "$DEST/Mods/Lanppart/Assets/"
cp "$ROOT/build/lanppart_native.dll" "$DEST/Mods/Lanppart/"
cp "$ROOT"/mod/dev/LanppartLab/Scripts/*.lua "$DEST/Mods/LanppartLab/Scripts/"
touch "$DEST/Mods/Lanppart/enabled.txt" "$DEST/Mods/LanppartLab/enabled.txt"
echo "Lanppart deployed to $DEST"
