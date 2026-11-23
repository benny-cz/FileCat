#!/bin/bash
# Upgrades the disposable, already enabled File Provider fixture app (/Applications/FileCatProviderFixture.app) on a test
# Mac from version 2 to version 3 in place, then adds its writable domain fixture-permissions-w. Domain fixture-12760a2e-d
# keeps version 2's items and behavior, and its listing is compared before and after. A byte-exact backup comes first;
# the new provider and manager are ad hoc signed. Run on that Mac only, after copying FCFixtureProvider.m and
# FCFixtureManager.m to ~/FileCatValidation.
set -Eeuo pipefail
base=$HOME/FileCatValidation
dir=$base/filecat-mac-provider-12760a2e-v3
app=/Applications/FileCatProviderFixture.app
appex=$app/Contents/PlugIns/FileCatProviderFixture.appex
extID=org.filecat.validation.nativeprovider.ready.extension
ready=$HOME/Library/CloudStorage/FileCatProviderFixture-FileCatNativeFixture12760a2eD
mkdir "$dir"
exec > "$dir/results.log" 2>&1
if ps -axo pid=,comm= | grep -Ei '(^|[ /])(dotnet|testhost)$'; then echo BUSY; exit 75; fi
test "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$app/Contents/Info.plist")" = 2
mv "$base/FCFixtureProvider.m" "$dir/extension.m"
mv "$base/FCFixtureManager.m" "$dir/app.m"
echo "== ready domain before"
ls -lO "$ready" | tee "$dir/ready-before.txt"
echo "== backup of the installed version 2"
ditto "$app" "$dir/backup/FileCatProviderFixture.app"
codesign -dv "$dir/backup/FileCatProviderFixture.app" 2>&1 | grep -E "CDHash|Identifier"
echo "== stage version 3"
ditto "$app" "$dir/stage/FileCatProviderFixture.app"
s=$dir/stage/FileCatProviderFixture.app
xcrun --sdk macosx clang -fobjc-arc -mmacosx-version-min=14.0 "$dir/extension.m" -framework Foundation -framework FileProvider -framework UniformTypeIdentifiers -o "$s/Contents/PlugIns/FileCatProviderFixture.appex/Contents/MacOS/FCFixtureProvider"
xcrun --sdk macosx clang -fobjc-arc -mmacosx-version-min=14.0 "$dir/app.m" -framework Foundation -framework FileProvider -o "$s/Contents/MacOS/FCFixtureManager"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion 3" "$s/Contents/PlugIns/FileCatProviderFixture.appex/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion 3" "$s/Contents/Info.plist"
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
echo "== install version 3"
pkill -f "[F]CFixtureProvider" || true
rm -rf "$app"
ditto "$s" "$app"
/usr/bin/pluginkit -a "$appex"
/usr/bin/pluginkit -e use -i "$extID"
/usr/bin/pluginkit -m -v -i "$extID"
sleep 3
echo "== ready domain after"
ls -lO "$ready" | tee "$dir/ready-after.txt"
if diff "$dir/ready-before.txt" "$dir/ready-after.txt"; then echo "ready domain listing unchanged"; else echo "READY DOMAIN LISTING CHANGED"; fi
echo "== add the writable domain"
"$app/Contents/MacOS/FCFixtureManager" add writable
sleep 5
root=$("$app/Contents/MacOS/FCFixtureManager" root writable | head -1)
echo "writable root: $root"
ls -lO "$root"
echo "== provider log"
log show --last 3m --style compact --predicate 'subsystem == "org.filecat.validation.nativeprovider"' | tail -30
echo "FINAL OK"
