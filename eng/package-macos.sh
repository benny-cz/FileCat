#!/usr/bin/env bash
# Builds FileCat for macOS on Apple silicon (P9, approved 2026-09-28) under artifacts/:
#   FileCat-<ver>-osx-arm64.zip   FileCat.app, ad-hoc signed only (not notarized: Gatekeeper asks on first launch;
#                                 right-click → Open, or remove the quarantine attribute, to run it)
# Usage: eng/package-macos.sh [version]      (needs macOS: iconutil, codesign, ditto; and the .NET SDK)
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

# Icon: every size macOS asks for, from the frames drawn for each size (the 16-pixel one by hand) rather than the
# 256-pixel artwork shrunk, which reads poorly small. 512 and up are left out: macOS scales the 256 for them.
FRAMES="$OUT/icon-frames"
rm -rf "$FRAMES"
dotnet run --project "$ROOT/eng/IconFrames" -- "$ROOT/src/FileCat.App/Assets/filecat.ico" "$FRAMES" >/dev/null
ICONSET="$OUT/FileCat.iconset"
rm -rf "$ICONSET"
mkdir -p "$ICONSET"
cp "$FRAMES/filecat-16.png" "$ICONSET/icon_16x16.png"
cp "$FRAMES/filecat-32.png" "$ICONSET/icon_16x16@2x.png"
cp "$FRAMES/filecat-32.png" "$ICONSET/icon_32x32.png"
cp "$FRAMES/filecat-64.png" "$ICONSET/icon_32x32@2x.png"
cp "$FRAMES/filecat-128.png" "$ICONSET/icon_128x128.png"
cp "$FRAMES/filecat-256.png" "$ICONSET/icon_128x128@2x.png"
cp "$FRAMES/filecat-256.png" "$ICONSET/icon_256x256.png"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/FileCat.icns"
rm -rf "$ICONSET" "$FRAMES"

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
