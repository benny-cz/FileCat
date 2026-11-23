#!/bin/bash
# Upgrades the disposable, already enabled File Provider fixture app (/Applications/FileCatProviderFixture.app, domain
# fixture-12760a2e-d) on a test Mac to version 2 in place: a byte-exact backup first, then the new provider and manager,
# ad hoc signed, and a working-set signal so the system learns about the two unvisited containers. Run on that Mac only,
# after copying FCFixtureProvider.m and FCFixtureManager.m to ~/FileCatValidation.
set -Eeuo pipefail
base=$HOME/FileCatValidation
dir=$base/filecat-mac-provider-12760a2e-v2
app=/Applications/FileCatProviderFixture.app
appex=$app/Contents/PlugIns/FileCatProviderFixture.appex
extID=org.filecat.validation.nativeprovider.ready.extension
mkdir "$dir"
exec > "$dir/results.log" 2>&1
if ps -axo pid=,comm= | grep -Ei '(^|[ /])(dotnet|testhost)$'; then echo BUSY; exit 75; fi
mv "$base/FCFixtureProvider.m" "$dir/extension.m"
mv "$base/FCFixtureManager.m" "$dir/app.m"
echo "== backup of the installed version 1"
ditto "$app" "$dir/backup/FileCatProviderFixture.app"
codesign -dv "$dir/backup/FileCatProviderFixture.app" 2>&1 | grep -E "CDHash|Identifier"
echo "== stage version 2"
ditto "$app" "$dir/stage/FileCatProviderFixture.app"
s=$dir/stage/FileCatProviderFixture.app
xcrun --sdk macosx clang -fobjc-arc -mmacosx-version-min=14.0 "$dir/extension.m" -framework Foundation -framework FileProvider -framework UniformTypeIdentifiers -o "$s/Contents/PlugIns/FileCatProviderFixture.appex/Contents/MacOS/FCFixtureProvider"
xcrun --sdk macosx clang -fobjc-arc -mmacosx-version-min=14.0 "$dir/app.m" -framework Foundation -framework FileProvider -o "$s/Contents/MacOS/FCFixtureManager"
/usr/libexec/PlistBuddy -c "Set :NSExtension:NSExtensionFileProviderWantsFlattenedPackages false" "$s/Contents/PlugIns/FileCatProviderFixture.appex/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion 2" "$s/Contents/PlugIns/FileCatProviderFixture.appex/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion 2" "$s/Contents/Info.plist"
cat > "$dir/entitlements.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>com.apple.security.app-sandbox</key><true/>
</dict></plist>
PLIST
codesign --force --sign - --entitlements "$dir/entitlements.plist" "$s/Contents/PlugIns/FileCatProviderFixture.appex"
codesign --force --sign - --entitlements "$dir/entitlements.plist" "$s"
codesign --verify --deep --strict "$s"
echo "== install version 2"
pkill -f "[F]CFixtureProvider" || true
rm -rf "$app"
ditto "$s" "$app"
/usr/bin/pluginkit -a "$appex"
/usr/bin/pluginkit -e use -i "$extID"
/usr/bin/pluginkit -m -v -i "$extID"
sleep 3
root=$("$app/Contents/MacOS/FCFixtureManager" root | head -1) || true
echo "root: $root"
"$app/Contents/MacOS/FCFixtureManager" signal || echo "signal failed: $?"
sleep 10
ls -lO "$root" || true
echo "== provider log"
log show --last 3m --style compact --predicate 'subsystem == "org.filecat.validation.nativeprovider"' | tail -30
echo "FINAL OK"
