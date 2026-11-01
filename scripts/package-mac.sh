#!/usr/bin/env bash
# Builds PinList.app for Apple Silicon and zips it. Runs on a Mac (the release workflow uses macos-latest).
# Usage: scripts/package-mac.sh <version> [output folder]
set -euo pipefail
VERSION="$1"
OUT="${2:-release}"
WORK="$(mktemp -d)"

# Restore runs for every target framework, including net10.0-windows, even though only net10.0 is built.
# EnableWindowsTargeting lets that restore work on macOS (NETSDK1100 otherwise).
dotnet publish src/PinList/PinList.csproj -c Release -f net10.0 -r osx-arm64 --self-contained true -o "$WORK/publish" \
  -p:EnableWindowsTargeting=true

APP="$WORK/PinList.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$WORK/publish/." "$APP/Contents/MacOS/"
sed "s/__VERSION__/$VERSION/g" src/PinList/macOS/Info.plist > "$APP/Contents/Info.plist"

# App icon from the 1024 px PNG, with the Retina (@2x) sizes
ICONSET="$WORK/PinList.iconset"
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s src/PinList/macOS/icon-1024.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  sips -z $((s * 2)) $((s * 2)) src/PinList/macOS/icon-1024.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/PinList.icns"

# Ad-hoc signature: free, no Apple account. macOS still asks once (System Settings → Privacy & Security → Open Anyway).
codesign --force --deep --sign - "$APP"

mkdir -p "$OUT"
(cd "$WORK" && ditto -c -k --keepParent PinList.app PinList-mac-arm64.zip)
mv "$WORK/PinList-mac-arm64.zip" "$OUT/"
echo "Built $OUT/PinList-mac-arm64.zip"
