#!/usr/bin/env bash
# Build a directly signed validation app from an exact Git revision. No archive
# is produced or consumed. The output is confined to the Mac validation folder.
# Usage: mac-publish-git-validation.sh CHECKOUT EXPECTED_FULL_REVISION
set -euo pipefail

checkout="${1:?Git checkout required}"
expected="${2:?Full Git revision required}"
validation="$HOME/FileCatValidation"
checkout="$(cd "$checkout" && pwd -P)"
case "$checkout" in
    "$validation"/*) ;;
    *) echo 'Checkout must be inside FileCatValidation' >&2; exit 2 ;;
esac
[[ "$expected" =~ ^[0-9a-f]{40}$ ]] || { echo 'Expected revision must be a full Git ID' >&2; exit 2; }
actual="$(git -C "$checkout" rev-parse HEAD)"
[[ "$actual" == "$expected" ]] || { echo 'Checkout revision differs from requested Git ID' >&2; exit 2; }
[[ -z "$(git -C "$checkout" status --porcelain --untracked-files=no)" ]] || {
    echo 'Tracked sources are modified' >&2; exit 2;
}

bundle="$validation/FileCatValidation-${expected:0:8}.app"
[[ ! -e "$bundle" ]] || { echo "Validation bundle already exists: $bundle" >&2; exit 2; }
sdk="$validation/dotnet-sdk/dotnet"
[[ -x "$sdk" ]] || { echo 'Isolated .NET SDK is unavailable' >&2; exit 2; }
publish="$checkout/validation-publish"
"$sdk" publish "$checkout/src/FileCat.App/FileCat.App.csproj" -c Release \
    -r osx-arm64 --self-contained true -p:PublishReadyToRun=true \
    -p:DebugType=embedded -p:RestoreLockedMode=true -m:16 -o "$publish"

mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
cp -a "$publish/." "$bundle/Contents/MacOS/"
rm -f "$bundle/Contents/MacOS"/FileCat.ShellHost* \
      "$bundle/Contents/MacOS"/FileCat.PrivilegedHost*
cp "$checkout/LICENSE" "$checkout/THIRD-PARTY-NOTICES.md" "$bundle/Contents/Resources/"
cat > "$bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>FileCat</string>
  <key>CFBundleDisplayName</key><string>FileCat</string>
  <key>CFBundleIdentifier</key><string>io.github.benny-cz.filecat.validation</string>
  <key>CFBundleVersion</key><string>1.0.0</string>
  <key>CFBundleShortVersionString</key><string>1.0.0</string>
  <key>CFBundleExecutable</key><string>FileCat</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>26.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
codesign --force --deep --sign - --timestamp=none "$bundle"
codesign --verify --deep --strict "$bundle"
printf 'revision=%s\nbundle=%s\napp_sha256=' "$actual" "$bundle"
shasum -a 256 "$bundle/Contents/MacOS/FileCat.dll"
