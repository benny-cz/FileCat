#!/usr/bin/env bash
# Builds FileCat's Linux release payloads (P9, approved 2026-09-28) under artifacts/:
#   FileCat-<ver>-<rid>.tar.gz         self-contained build + filecat.desktop + icon + install-desktop-entry.sh
#   filecat_<debver>_<arch>.deb        /opt/filecat, /usr/bin/filecat, desktop entry, icon
#   FileCat-<ver>-<appimage-arch>.AppImage
# Usage: eng/package-linux.sh [version] [rid]   (rid: linux-x64 or linux-arm64)
set -euo pipefail

VERSION="${1:-0.1.0-preview}"
RID="${2:-linux-x64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/artifacts"
PUB="$OUT/publish/$RID"
PREFIX="${VERSION%%-*}"
SOURCE_COMMIT="$(git -C "$ROOT" rev-parse HEAD)"
CREATED_UTC="$(git -C "$ROOT" show -s --format=%cI "$SOURCE_COMMIT")"
if [[ ! "$SOURCE_COMMIT" =~ ^[0-9a-f]{40}$ || -z "$CREATED_UTC" ]]; then
  echo "Cannot establish exact source identity for artifact SBOMs." >&2
  exit 2
fi
# The SBOMs name their source. Uncommitted build inputs are refused in CI and named by a state hash elsewhere,
# so an SBOM never claims a clean commit it was not built from.
BUILD_INPUTS=(src eng licenses Directory.Build.props Directory.Build.targets Directory.Packages.props global.json THIRD-PARTY-NOTICES.md LICENSE FileCat.slnx)
SOURCE_IDENTITY="$SOURCE_COMMIT"
if [ -n "$(git -C "$ROOT" status --porcelain=v1 --untracked-files=all -- "${BUILD_INPUTS[@]}")" ]; then
  if [ "${GITHUB_ACTIONS:-}" = "true" ]; then echo "Release packaging needs a clean checkout of every build input." >&2; exit 1; fi
  STATE_HASH="$( { git -C "$ROOT" diff HEAD --binary -- "${BUILD_INPUTS[@]}"; git -C "$ROOT" ls-files --others --exclude-standard -- "${BUILD_INPUTS[@]}" | LC_ALL=C sort | while IFS= read -r p; do printf '%s\n' "$p"; cat "$ROOT/$p"; done; } | { if command -v sha256sum >/dev/null 2>&1; then sha256sum; else shasum -a 256; fi; } | cut -d" " -f1)"
  SOURCE_IDENTITY="$SOURCE_COMMIT+dirty.$STATE_HASH"
  echo "warning: packaging uncommitted changes; the SBOMs record source $SOURCE_IDENTITY" >&2
fi
case "$RID" in
  linux-x64) DEB_ARCH=amd64; APPIMAGE_ARCH=x86_64 ;;
  linux-arm64) DEB_ARCH=arm64; APPIMAGE_ARCH=aarch64 ;;
  *) echo "Unsupported runtime: $RID" >&2; exit 2 ;;
esac

rm -rf "$PUB"
mkdir -p "$OUT"
dotnet publish "$ROOT/src/FileCat.App/FileCat.App.csproj" -c Release -r "$RID" --self-contained true \
  -p:PublishReadyToRun=true -p:Version="$VERSION" -p:VersionPrefix="$PREFIX" -p:DebugType=embedded -o "$PUB"
# Windows-only helpers (Shell host, administrator broker) do not ship on Linux.
rm -f "$PUB"/FileCat.ShellHost* "$PUB"/FileCat.PrivilegedHost*
cp "$ROOT/LICENSE" "$ROOT/THIRD-PARTY-NOTICES.md" "$PUB/"
dotnet run --project "$ROOT/eng/DependencyNotices" -c Release -- \
  "$ROOT/licenses/dependencies" "$ROOT/src/FileCat.App/packages.lock.json" "$PUB" "$PUB/licenses/dependencies"
# FileCat.Archives.Rar15.dll is LGPL-2.1-or-later (ported XADMaster code): its full license text ships with it.
mkdir -p "$PUB/licenses/xadmaster"
cp "$ROOT/licenses/xadmaster/LICENSE" "$ROOT/licenses/xadmaster/README.md" "$PUB/licenses/xadmaster/"
echo "9b872a8a070b8ad329c4bd380fb1bf0000f564c75023ec8e1e6803f15364b9e9  $PUB/licenses/xadmaster/LICENSE" | sha256sum -c - >/dev/null \
  || { echo "licenses/xadmaster/LICENSE differs from XADMaster's LGPL text." >&2; exit 2; }
install -m 644 "$ROOT/src/FileCat.App/Assets/filecat.png" "$PUB/filecat.png"
chmod +x "$PUB/FileCat"
# The icon at every size it is drawn for (the 16- and 24-pixel frames by hand), so that menus and panels need not
# shrink the large artwork, which reads poorly small.
ICONS="$OUT/icons-$RID"
rm -rf "$ICONS"
dotnet run --project "$ROOT/eng/IconFrames" -- "$ROOT/src/FileCat.App/Assets/filecat.ico" "$ICONS" >/dev/null
ICON_SIZES="16 24 32 48 64 128 256"

desktop_entry() { # $1: Exec, $2: Icon
  cat <<EOF
[Desktop Entry]
Type=Application
Name=FileCat
GenericName=File Manager
Comment=Keyboard-first two-panel file manager
Exec=$1 %F
Icon=$2
Terminal=false
Categories=System;FileTools;FileManager;Utility;
MimeType=inode/directory;
StartupWMClass=FileCat
EOF
}

# 1) Tarball: runs from wherever it is unpacked; the script adds a menu entry for the current user.
desktop_entry filecat filecat > "$PUB/filecat.desktop"
install -m 755 "$ROOT/eng/install-linux-desktop-entry.sh" "$PUB/install-desktop-entry.sh"
TARBALL="$OUT/FileCat-$VERSION-$RID.tar.gz"
SBOM_TAR="$OUT/sbom-$VERSION-$RID-tar.spdx.json"
rm -f "$TARBALL"
rm -f "$SBOM_TAR"
dotnet run --project "$ROOT/eng/ArtifactSbom" -c Release -- \
  "$PUB" "$ROOT/licenses/dependencies/index.json" "$VERSION" "$RID" tar "$SOURCE_IDENTITY" "$CREATED_UTC" "$SBOM_TAR"
tar -C "$OUT/publish" -czf "$TARBALL" --transform "s,^$RID,FileCat-$VERSION," "$RID"

# 2) Debian package (Ubuntu, Debian): the runtime libraries a self-contained .NET and Avalonia app loads.
DEB_VERSION="${VERSION/-/\~}"
DEB="$OUT/deb-$RID"
rm -rf "$DEB"
mkdir -p "$DEB/DEBIAN" "$DEB/opt/filecat" "$DEB/usr/bin" "$DEB/usr/share/applications"
cp -a "$PUB/." "$DEB/opt/filecat/"
rm -f "$DEB/opt/filecat/install-desktop-entry.sh"
ln -s /opt/filecat/FileCat "$DEB/usr/bin/filecat"
desktop_entry filecat filecat > "$DEB/usr/share/applications/filecat.desktop"
for s in $ICON_SIZES; do
  install -Dm 644 "$ICONS/filecat-$s.png" "$DEB/usr/share/icons/hicolor/${s}x${s}/apps/filecat.png"
done
# Ubuntu 24.04+ restricts unprivileged user namespaces. Keep the AppArmor 4
# profile as package data, and activate it only on systems which expose that
# restriction; older Debian/Ubuntu parsers never see unsupported userns syntax.
install -Dm 644 "$ROOT/eng/linux/filecat.apparmor" "$DEB/usr/share/filecat/apparmor/filecat"
cat > "$DEB/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
restriction=/proc/sys/kernel/apparmor_restrict_unprivileged_userns
source_profile=/usr/share/filecat/apparmor/filecat
active_profile=/etc/apparmor.d/filecat
if [ -r "$restriction" ] && [ "$(cat "$restriction")" = 1 ]; then
  if command -v apparmor_parser >/dev/null 2>&1; then
    install -m 644 "$source_profile" "$active_profile"
    if ! apparmor_parser -r "$active_profile"; then
      rm -f "$active_profile"
      echo "FileCat: AppArmor could not load the WebKit sandbox profile; page preview will fail closed." >&2
    fi
  else
    echo "FileCat: AppArmor tools are unavailable; WebKit page preview will fail closed." >&2
  fi
fi
exit 0
EOF
cat > "$DEB/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
active_profile=/etc/apparmor.d/filecat
if [ "$1" = remove ] || [ "$1" = purge ]; then
  if [ -f "$active_profile" ] && command -v apparmor_parser >/dev/null 2>&1; then
    apparmor_parser -R "$active_profile" >/dev/null 2>&1 || true
  fi
  rm -f "$active_profile"
fi
exit 0
EOF
chmod 755 "$DEB/DEBIAN/postinst" "$DEB/DEBIAN/postrm"
SIZE_KB="$(du -sk "$DEB/opt" | cut -f1)"
cat > "$DEB/DEBIAN/control" <<EOF
Package: filecat
Version: $DEB_VERSION
Section: utils
Priority: optional
Architecture: $DEB_ARCH
Installed-Size: $SIZE_KB
Maintainer: FileCat contributors <filecat@users.noreply.github.com>
Homepage: https://github.com/benny-cz/FileCat
Depends: libc6, libgcc-s1, libstdc++6, libssl3 | libssl1.1, libicu78 | libicu76 | libicu74 | libicu72 | libicu71 | libicu70 | libicu67, libfontconfig1, libx11-6, libice6, libsm6
Recommends: xdg-utils, libsecret-1-0, apparmor, libwebkit2gtk-4.1-0 | libwebkit2gtk-4.0-37
Description: Keyboard-first two-panel file manager
 FileCat is a two-panel file manager in the tradition of Altap Salamander, Total
 Commander, and FAR Manager: keyboard-first work with folders, archives, SFTP and
 FTP servers, and phones, with previews and guarded operations.
EOF
DEB_FILE="$OUT/filecat_${DEB_VERSION}_${DEB_ARCH}.deb"
SBOM_DEB="$OUT/sbom-$VERSION-$RID-deb.spdx.json"
rm -f "$DEB_FILE"
rm -f "$SBOM_DEB"
dotnet run --project "$ROOT/eng/ArtifactSbom" -c Release -- \
  "$DEB" "$ROOT/licenses/dependencies/index.json" "$VERSION" "$RID" deb "$SOURCE_IDENTITY" "$CREATED_UTC" "$SBOM_DEB"
dpkg-deb --root-owner-group --build "$DEB" "$DEB_FILE"

# 3) AppImage: one file that runs on most distributions (appimagetool 1.9.1, pinned by checksum; build tool only).
APPDIR="$OUT/FileCat-$RID.AppDir"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/lib/filecat"
cp -a "$PUB/." "$APPDIR/usr/lib/filecat/"
rm -f "$APPDIR/usr/lib/filecat/install-desktop-entry.sh"
desktop_entry FileCat filecat > "$APPDIR/filecat.desktop"
install -m 644 "$PUB/filecat.png" "$APPDIR/filecat.png"
for s in $ICON_SIZES; do
  install -Dm 644 "$ICONS/filecat-$s.png" "$APPDIR/usr/share/icons/hicolor/${s}x${s}/apps/filecat.png"
done
cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/lib/filecat/FileCat" "$@"
EOF
chmod +x "$APPDIR/AppRun"
TOOL="${APPIMAGETOOL:-}"
if [ -z "$TOOL" ]; then
  TOOL="$OUT/appimagetool"
  if [ "$(uname -m)" = "x86_64" ]; then
    SUM=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0
    curl -fsSL -o "$TOOL" "https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage"
    echo "$SUM  $TOOL" | sha256sum -c -
  else
    echo "Set APPIMAGETOOL to an appimagetool for $(uname -m)." >&2
    exit 2
  fi
  chmod +x "$TOOL"
fi
# The runtime, the start-up part every AppImage carries, is selected by GitHub's immutable asset ID and pinned by
# checksum too. Asset 657374641 is the x86_64 build of type2-runtime commit fedfad51 (2026-10-19). That commit is the
# previously reviewed 0700-extraction source plus one reproducibility-only file-prefix-map change. A replacement asset
# receives another ID; deletion, changed bytes or a wrong API response therefore fail closed. Update the asset ID,
# source commit, checksum, signature review and reviewed notice snapshot together.
RUNTIME="${APPIMAGE_RUNTIME:-}"
if [ -z "$RUNTIME" ]; then
  RUNTIME="$OUT/runtime-$APPIMAGE_ARCH"
  if [ "$APPIMAGE_ARCH" = "x86_64" ]; then
    RUNTIME_SUM=617aa77314d57c8b8e4fae9c3f0b35af7b30d5529cb69e98f64f92510ed98a46
    curl -fsSL --retry 3 -H 'Accept: application/octet-stream' -H 'X-GitHub-Api-Version: 2022-11-28' \
      -o "$RUNTIME" "https://api.github.com/repos/AppImage/type2-runtime/releases/assets/657374641"
    echo "$RUNTIME_SUM  $RUNTIME" | sha256sum -c - || { echo "The selected type2-runtime asset differs from the reviewed bytes." >&2; exit 2; }
  else
    echo "Set APPIMAGE_RUNTIME to a type2-runtime for $APPIMAGE_ARCH." >&2
    exit 2
  fi
fi
dotnet run --project "$ROOT/eng/DependencyNotices" -c Release -- --appimage-runtime \
  "$ROOT/licenses/appimage-runtime" "$RUNTIME" "$APPDIR/usr/lib/filecat/licenses/appimage-runtime"
APPIMAGE="$OUT/FileCat-$VERSION-$APPIMAGE_ARCH.AppImage"
SBOM_APPIMAGE="$OUT/sbom-$VERSION-$RID-appimage.spdx.json"
rm -f "$APPIMAGE"
rm -f "$SBOM_APPIMAGE"
dotnet run --project "$ROOT/eng/ArtifactSbom" -c Release -- \
  "$APPDIR" "$ROOT/licenses/dependencies/index.json" "$VERSION" "$RID" appimage "$SOURCE_IDENTITY" "$CREATED_UTC" "$SBOM_APPIMAGE" \
  ".appimage/runtime=$RUNTIME"
# Runs without FUSE (CI containers).
APPIMAGE_EXTRACT_AND_RUN=1 ARCH="$APPIMAGE_ARCH" "$TOOL" --no-appstream --runtime-file "$RUNTIME" "$APPDIR" "$APPIMAGE"
rm -rf "$APPDIR" "$DEB"

echo "Done:"
ls -la "$TARBALL" "$DEB_FILE" "$APPIMAGE" "$SBOM_TAR" "$SBOM_DEB" "$SBOM_APPIMAGE"
