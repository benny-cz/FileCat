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
rm -f "$TARBALL"
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
Recommends: xdg-utils, libsecret-1-0, libwebkit2gtk-4.1-0 | libwebkit2gtk-4.0-37
Description: Keyboard-first two-panel file manager
 FileCat is a two-panel file manager in the tradition of Altap Salamander, Total
 Commander, and FAR Manager: keyboard-first work with folders, archives, SFTP and
 FTP servers, and phones, with previews and guarded operations.
EOF
DEB_FILE="$OUT/filecat_${DEB_VERSION}_${DEB_ARCH}.deb"
rm -f "$DEB_FILE"
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
# The runtime, the start-up part every AppImage carries, pinned by checksum too: without --runtime-file appimagetool
# takes whatever type2-runtime's "continuous" release holds when the package is built. This is the build of commit
# 8f39b89 (2026-09-28, extraction directories made with mode 0700). When the release moves on, the check fails: review
# the new runtime, then update the commit, checksum and reviewed notice snapshot together.
RUNTIME="${APPIMAGE_RUNTIME:-}"
if [ -z "$RUNTIME" ]; then
  RUNTIME="$OUT/runtime-$APPIMAGE_ARCH"
  if [ "$APPIMAGE_ARCH" = "x86_64" ]; then
    RUNTIME_SUM=156f4bdbde9c52d01814600013e0a273f0118dc2de98975f3c8c63427ec79074
    curl -fsSL -o "$RUNTIME" "https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-x86_64"
    echo "$RUNTIME_SUM  $RUNTIME" | sha256sum -c - || { echo "type2-runtime's continuous build is no longer the pinned one: review it, then update the pin." >&2; exit 2; }
  else
    echo "Set APPIMAGE_RUNTIME to a type2-runtime for $APPIMAGE_ARCH." >&2
    exit 2
  fi
fi
dotnet run --project "$ROOT/eng/DependencyNotices" -c Release -- --appimage-runtime \
  "$ROOT/licenses/appimage-runtime" "$RUNTIME" "$APPDIR/usr/lib/filecat/licenses/appimage-runtime"
APPIMAGE="$OUT/FileCat-$VERSION-$APPIMAGE_ARCH.AppImage"
rm -f "$APPIMAGE"
# Runs without FUSE (CI containers).
APPIMAGE_EXTRACT_AND_RUN=1 ARCH="$APPIMAGE_ARCH" "$TOOL" --no-appstream --runtime-file "$RUNTIME" "$APPDIR" "$APPIMAGE"
rm -rf "$APPDIR" "$DEB"

echo "Done:"
ls -la "$TARBALL" "$DEB_FILE" "$APPIMAGE"
