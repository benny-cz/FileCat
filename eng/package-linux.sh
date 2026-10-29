#!/usr/bin/env bash
# Builds FileCat's Linux release payloads (P9, approved 2026-09-28) under artifacts/:
#   FileCat-<ver>-<rid>.tar.gz         self-contained build + filecat.desktop + icon + install-desktop-entry.sh
#   filecat_<ver>_<arch>.deb           /opt/filecat, /usr/bin/filecat, desktop entry, icon (package version <debver>)
#   FileCat-<ver>-<appimage-arch>.AppImage
#   FileCat-<ver>-<rid>-appimage-runtime-source.tar.gz   corresponding source of the AppImage runtime (EXT-04)
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
# The package's Debian version keeps '~' so that a preview sorts before its release. GitHub renames release assets
# whose names contain '~', so the file is named with FileCat's own version.
DEB_FILE="$OUT/filecat_${VERSION}_${DEB_ARCH}.deb"
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
# The runtime, the start-up part every AppImage carries, is the reviewed x86_64 build of type2-runtime commit fedfad51
# (2026-10-19), vendored in eng/linux/appimage-runtime with its manifest: upstream's GitHub asset 657374641 of the
# mutable "continuous" release, which a later upstream build deletes. Packaging reads no runtime from the network.
# That commit is the previously reviewed 0700-extraction source plus one reproducibility-only file-prefix-map change.
# APPIMAGE_RUNTIME may name another copy, but every runtime must have the reviewed bytes. Update the vendored runtime,
# checksum, digest offset, source bundle, signature review and reviewed notice snapshot together.
VENDOR="$ROOT/eng/linux/appimage-runtime"
RUNTIME_SUM=617aa77314d57c8b8e4fae9c3f0b35af7b30d5529cb69e98f64f92510ed98a46
# appimagetool writes the AppImage's MD5 into the runtime's 16-byte .digest_md5 section, at this offset of these bytes.
RUNTIME_DIGEST_OFFSET=932096
if [ "$APPIMAGE_ARCH" != "x86_64" ]; then
  echo "No reviewed type2-runtime and source bundle exist for $APPIMAGE_ARCH." >&2
  exit 2
fi
RUNTIME="${APPIMAGE_RUNTIME:-$VENDOR/runtime-$APPIMAGE_ARCH}"
echo "$RUNTIME_SUM  $RUNTIME" | sha256sum -c - || { echo "The selected type2-runtime differs from the reviewed bytes." >&2; exit 2; }
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
# The AppImage must start with the reviewed runtime, which its source bundle describes: apart from the digest section,
# every runtime byte is unchanged.
PREFIX="$OUT/runtime-prefix-$RID"
head -c "$(wc -c < "$RUNTIME")" "$APPIMAGE" > "$PREFIX"
dd if=/dev/zero of="$PREFIX" bs=1 seek="$RUNTIME_DIGEST_OFFSET" count=16 conv=notrunc status=none
echo "$RUNTIME_SUM  $PREFIX" | sha256sum -c --quiet - || { echo "The AppImage does not start with the reviewed runtime." >&2; exit 2; }
rm -f "$PREFIX"

# 4) The runtime's corresponding source (EXT-04: LGPL-2.1 section 6 for its static libfuse), attached beside the
# AppImage: the libfuse, squashfuse and type2-runtime sources exactly as published, the upstream producer's package
# list, a README and rebuild.sh. The archive depends only on those vendored files, so its bytes are pinned as well:
# sorted entries, fixed time and owner, normalized modes and gzip -n. Update the pin with any of those files.
SOURCE_BUNDLE="$OUT/FileCat-$VERSION-$RID-appimage-runtime-source.tar.gz"
SOURCE_BUNDLE_SUM=8b92ecaff637f201fecb0a51dd383cdf42ff54eb891f9eba9eb8a05c0ae6530a
SOURCE_EPOCH=1792442122 # 2026-10-19T20:35:22Z, the time of type2-runtime commit fedfad51
STAGE="$OUT/appimage-runtime-source-$RID"
rm -rf "$STAGE" "$SOURCE_BUNDLE"
install -d -m 755 "$STAGE/appimage-runtime-source/sources"
for f in README SHA256SUMS producer-packages.txt sources/fuse-3.15.0.tar.xz sources/squashfuse-0.5.2.tar.gz \
  sources/type2-runtime-fedfad51acc2860b2916a1eef32ba4a500ff31ef.tar.gz; do
  install -m 644 "$VENDOR/source-bundle/$f" "$STAGE/appimage-runtime-source/$f"
done
install -m 755 "$VENDOR/source-bundle/rebuild.sh" "$STAGE/appimage-runtime-source/rebuild.sh"
(cd "$STAGE/appimage-runtime-source" && sha256sum -c --strict --quiet SHA256SUMS) \
  || { echo "The AppImage runtime sources differ from their reviewed checksums." >&2; exit 2; }
tar -C "$STAGE" --sort=name --format=gnu --mtime="@$SOURCE_EPOCH" --owner=0 --group=0 --numeric-owner \
  --mode='u=rwX,go=rX' -cf - appimage-runtime-source | gzip -9n > "$SOURCE_BUNDLE"
SOURCE_BUNDLE_ACTUAL="$(sha256sum "$SOURCE_BUNDLE" | cut -d ' ' -f 1)"
if [ "$SOURCE_BUNDLE_ACTUAL" != "$SOURCE_BUNDLE_SUM" ]; then
  echo "The runtime source bundle is $SOURCE_BUNDLE_ACTUAL, not the reviewed reproducible $SOURCE_BUNDLE_SUM." >&2
  exit 2
fi
rm -rf "$STAGE"

echo "Done:"
ls -la "$TARBALL" "$DEB_FILE" "$APPIMAGE" "$SOURCE_BUNDLE" "$SBOM_TAR" "$SBOM_DEB" "$SBOM_APPIMAGE"
