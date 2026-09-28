#!/usr/bin/env bash
# Builds FileCat for macOS on Apple silicon (P9, approved 2026-09-28) under artifacts/:
#   FileCat-<ver>-osx-arm64.zip   FileCat.app, ad-hoc signed only (not notarized: Gatekeeper asks on first launch;
#                                 right-click → Open, or remove the quarantine attribute, to run it)
# Usage: eng/package-macos.sh [version]      (needs macOS: sips, iconutil, codesign, ditto)
set -euo pipefail

VERSION="${1:-0.1.0-preview}"
RID="osx-arm64"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/artifacts"
PUB="$OUT/publish/$RID"
PREFIX="${VERSION%%-*}"

rm -rf "$PUB"
mkdir -p "$OUT"
dotnet publish "$ROOT/src/FileCat.App/FileCat.App.csproj" -c Release -r "$RID" --self-contained true \
  -p:PublishReadyToRun=true -p:Version="$VERSION" -p:VersionPrefix="$PREFIX" -p:DebugType=embedded -o "$PUB"
rm -f "$PUB"/FileCat.ShellHost* "$PUB"/FileCat.PrivilegedHost*

APP="$OUT/FileCat.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -a "$PUB/." "$APP/Contents/MacOS/"
cp "$ROOT/LICENSE" "$ROOT/THIRD-PARTY-NOTICES.md" "$APP/Contents/Resources/"

# Icon: every size macOS asks for, from the 256-pixel artwork.
ICONSET="$OUT/FileCat.iconset"
rm -rf "$ICONSET"
mkdir -p "$ICONSET"
SRC="$ROOT/src/FileCat.App/Assets/filecat.png"
for s in 16 32 64 128 256; do
  sips -z "$s" "$s" "$SRC" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
done
cp "$ICONSET/icon_32x32.png" "$ICONSET/icon_16x16@2x.png"
cp "$ICONSET/icon_64x64.png" "$ICONSET/icon_32x32@2x.png"
cp "$ICONSET/icon_256x256.png" "$ICONSET/icon_128x128@2x.png"
rm "$ICONSET/icon_64x64.png"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/FileCat.icns"
rm -rf "$ICONSET"

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>FileCat</string>
  <key>CFBundleDisplayName</key><string>FileCat</string>
  <key>CFBundleIdentifier</key><string>io.github.benny-cz.filecat</string>
  <key>CFBundleVersion</key><string>$PREFIX</string>
  <key>CFBundleShortVersionString</key><string>$PREFIX</string>
  <key>CFBundleExecutable</key><string>FileCat</string>
  <key>CFBundleIconFile</key><string>FileCat</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>Copyright (c) 2026 Benny and FileCat contributors. MIT license.</string>
</dict>
</plist>
EOF

# Apple silicon runs only signed code: an ad-hoc signature, no Developer ID and no notarization (not approved yet).
codesign --force --deep --sign - --timestamp=none "$APP"
codesign --verify --deep --strict "$APP"

ZIP="$OUT/FileCat-$VERSION-$RID.zip"
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"
rm -rf "$APP"
echo "Done:"
ls -la "$ZIP"
