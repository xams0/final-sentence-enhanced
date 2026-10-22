#!/bin/bash
# Builds the Lanppart3 launcher: ONE executable with the mod inside (dist/Lanppart3-Final-Sentence.exe).
# The launcher unpacks the mod to %APPDATA%\Lanppart3\runtime and injects it into the games it starts:
# nothing is ever copied into the game's folder.
#
# Needs: zig (native DLL), the .NET SDK (launcher, .NET Framework 4.8), and UE4SS v3.0.1 experimental
# (https://github.com/UE4SS-RE/RE-UE4SS/releases, the "UE4SS_v3.0.1-..." zip) unpacked in $UE4SS_DIR.
# Usage: mod/package.sh
set -e
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UE4SS="${UE4SS_DIR:-$ROOT/_work/dl/ue4ss/std}"
ZIG="${ZIG:-$ROOT/_work/dl/zig-x86_64-windows-0.17.0/zig.exe}"
DOTNET="${DOTNET:-/c/Program Files/dotnet/dotnet.exe}"
BUILD="$ROOT/build"
PAYLOAD="$BUILD/payload"
OUT="$ROOT/dist"
EXE="Lanppart3-Final-Sentence.exe"

mkdir -p "$BUILD"
"$ZIG" cc -shared -target x86_64-windows-gnu -O2 -Wall -Wextra \
  -o "$BUILD/lanppart_native.dll" "$ROOT/native/lanppart_native.c"

# The mod as the launcher unpacks it: UE4SS.dll at the root, the Lanppart Lua mod under Mods.
rm -rf "$PAYLOAD"
mkdir -p "$PAYLOAD/Mods/Lanppart/Scripts" "$PAYLOAD/Mods/Lanppart/Assets"
cp "$UE4SS/ue4ss/UE4SS.dll" "$UE4SS/ue4ss/LICENSE" "$PAYLOAD/"
# UE4SS' LoadMap/InitGameState hooks crash this game at startup; the mod doesn't need them.
sed -e 's/^HookLoadMap = 1/HookLoadMap = 0/' -e 's/^HookInitGameState = 1/HookInitGameState = 0/' \
  "$UE4SS/ue4ss/UE4SS-settings.ini" > "$PAYLOAD/UE4SS-settings.ini"
printf 'Lanppart : 1\n' > "$PAYLOAD/Mods/mods.txt"
cp "$ROOT"/mod/Lanppart/Scripts/*.lua "$PAYLOAD/Mods/Lanppart/Scripts/"
cp "$ROOT"/mod/Lanppart/Assets/*.png "$PAYLOAD/Mods/Lanppart/Assets/"
cp "$BUILD/lanppart_native.dll" "$PAYLOAD/Mods/Lanppart/"
touch "$PAYLOAD/Mods/Lanppart/enabled.txt"

# Zipped with .NET (forward slashes in the entries, unlike PowerShell 5's Compress-Archive).
mkdir -p "$ROOT/launcher/Payload"
rm -f "$ROOT/launcher/Payload/mod.zip"
powershell -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; \
  [IO.Compression.ZipFile]::CreateFromDirectory('$(cygpath -w "$PAYLOAD")', '$(cygpath -w "$ROOT/launcher/Payload/mod.zip")')"

"$DOTNET" build "$ROOT/launcher/Lanppart3Launcher.csproj" -c Release -nologo -v quiet

rm -rf "$OUT"
mkdir -p "$OUT"
cp "$ROOT/launcher/bin/Release/net48/Lanppart3 Final Sentence.exe" "$OUT/$EXE"
cp "$ROOT/mod/LISEZMOI.txt" "$OUT/"
# Shortcut to the fresh launcher at the project's root (local only, not versioned).
powershell -NoProfile -Command "\$s = (New-Object -ComObject WScript.Shell).CreateShortcut('$(cygpath -w "$ROOT/Lanppart3 Final Sentence.lnk")'); \
  \$s.TargetPath = '$(cygpath -w "$OUT/$EXE")'; \$s.WorkingDirectory = '$(cygpath -w "$OUT")'; \
  \$s.IconLocation = '$(cygpath -w "$OUT/$EXE"),0'; \$s.Description = 'Lanppart3 Final Sentence (dernière version construite)'; \$s.Save()"
echo "Launcher: $OUT/$EXE"
