#!/usr/bin/env bash
# Rebuilds the AppImage type2-runtime of FileCat's x86_64 AppImage from the sources in this archive, in the Alpine
# container the upstream build used, and relinks a FileCat AppImage with a rebuilt runtime. See README.
#
#   ./rebuild.sh [--out DIR] [--expect-shipped]   build DIR/runtime-x86_64 (default DIR: ./out, new or empty)
#   LIBFUSE_DIR=TREE ./rebuild.sh [--out DIR]      the same with a modified libfuse source tree
#   ./rebuild.sh --unpack-libfuse TREE             unpack libfuse 3.15.0 with the type2-runtime patch, for editing
#   ./rebuild.sh --relink APPIMAGE RUNTIME OUTPUT  write OUTPUT: RUNTIME followed by the payload of APPIMAGE
#
# CONTAINER_ENGINE selects the container command (default: docker, else podman), for example "podman" or "sudo docker".
set -euo pipefail

BUNDLE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
IMAGE=docker.io/library/alpine:3.21@sha256:ce64758a109eb420d874a118f87920e625e12d3634e03b4a5573fd9f6e5d3507
SHIPPED_SHA256=617aa77314d57c8b8e4fae9c3f0b35af7b30d5529cb69e98f64f92510ed98a46
TYPE2_COMMIT=fedfad51acc2860b2916a1eef32ba4a500ff31ef
TYPE2_ARCHIVE="sources/type2-runtime-$TYPE2_COMMIT.tar.gz"
# The package list of the upstream scripts/docker/Dockerfile at that commit.
PACKAGES="bash alpine-sdk util-linux strace file autoconf automake libtool xz eudev-dev gettext-dev linux-headers meson
  zstd-dev zstd-static zlib-dev zlib-static clang musl-dev mimalloc-dev"

die() { echo "rebuild.sh: $*" >&2; exit 1; }
usage() { sed -n '5,10p' "$BUNDLE/rebuild.sh" | sed 's/^# \{0,1\}//' >&2; exit "${1:-2}"; }

verify_sources() {
  (cd "$BUNDLE" && grep '  sources/' SHA256SUMS | sha256sum --check --strict --quiet -) ||
    die "the archives in sources/ differ from SHA256SUMS"
}

# Size of an ELF64 little-endian image from its header: section header table offset + entry size * entry count.
# The runtime finds its payload at this offset in its own file.
elf_size() {
  local shoff shentsize shnum
  [ "$(od -An -c -N 4 "$1" | tr -d ' ')" = '177ELF' ] || die "$1 is not an ELF file"
  shoff="$(od -An -t u8 -j 40 -N 8 "$1" | tr -d ' ')"
  shentsize="$(od -An -t u2 -j 58 -N 2 "$1" | tr -d ' ')"
  shnum="$(od -An -t u2 -j 60 -N 2 "$1" | tr -d ' ')"
  echo $((shoff + shentsize * shnum))
}

unpack_libfuse() {
  local tree="$1" stage
  [ ! -e "$tree" ] || die "$tree already exists"
  command -v patch >/dev/null || die "--unpack-libfuse needs patch"
  verify_sources
  mkdir -p "$(dirname "$tree")"
  stage="$(mktemp -d "$(dirname "$tree")/.libfuse.XXXXXX")"
  tar -xJf "$BUNDLE/sources/fuse-3.15.0.tar.xz" -C "$stage"
  tar -xzf "$BUNDLE/$TYPE2_ARCHIVE" -C "$stage" "type2-runtime-$TYPE2_COMMIT/patches/libfuse/mount.c.diff"
  (cd "$stage/fuse-3.15.0" && patch -p1 < "$stage/type2-runtime-$TYPE2_COMMIT/patches/libfuse/mount.c.diff")
  mv "$stage/fuse-3.15.0" "$tree"
  rm -rf "$stage"
  echo "libfuse 3.15.0 with the type2-runtime patch is in $tree."
  echo "Change it, then run: LIBFUSE_DIR=$tree $0 --out <new directory>"
}

relink() {
  local appimage="$1" runtime="$2" output="$3" size offset
  [ ! -e "$output" ] || die "$output already exists"
  size="$(elf_size "$runtime")"
  [ "$size" = "$(wc -c < "$runtime" | tr -d ' ')" ] ||
    die "$runtime has bytes after its ELF image; the runtime would not find the payload"
  offset="$(elf_size "$appimage")"
  [ "$(od -An -c -j "$offset" -N 4 "$appimage" | tr -d ' ')" = hsqs ] ||
    die "$appimage has no squashfs payload at offset $offset"
  { cat "$runtime"; tail -c "+$((offset + 1))" "$appimage"; } > "$output"
  chmod 755 "$output"
  echo "Wrote $output: $runtime, then the payload of $appimage from offset $offset."
}

# Runs as root inside $IMAGE after the packages are installed; started by build below. It follows the upstream
# recipe at $TYPE2_COMMIT: the workflow's version file, scripts/common/install-dependencies.sh with its two downloads
# replaced by the bundled archives (or LIBFUSE_DIR's tree), then scripts/build-runtime.sh unchanged.
container_build() {
  local owner src
  [ -f /etc/alpine-release ] && [ -d /out ] || die "--in-container runs only inside the container rebuild.sh starts"
  # Hand the results to the owner of the output directory: the caller with Docker, root with rootless Podman.
  owner="$(stat -c %u:%g /out)"
  trap "chown -R $owner /out" EXIT
  apk info -v 2>/dev/null | LC_ALL=C sort > /out/installed-packages.txt
  # libfuse's install helper creates the /dev/fuse device node, which rootless containers refuse. Nothing that is
  # built depends on that node; with a placeholder the helper skips it.
  [ -e /dev/fuse ] || touch /dev/fuse

  (
    tmpdir="$(mktemp -d)"
    cd "$tmpdir"
    # keep the random tmpdir path out of the debug info, it would end up in the runtime's build ID
    prefix_map_flags="-ffile-prefix-map=$tmpdir=."
    if [ -d /libfuse ]; then
      cp -a /libfuse "$tmpdir/fuse-3.15.0"
      pushd fuse-3*/
    else
      cp "/bundle/sources/fuse-3.15.0.tar.xz" .
      echo "70589cfd5e1cff7ccd6ac91c86c01be340b227285c5e200baa284e401eea2ca0  fuse-3.15.0.tar.xz" | sha256sum -c -
      tar xf fuse-3.*.tar.xz
      pushd fuse-3*/
      patch -p1 < "/build/type2-runtime-$TYPE2_COMMIT/patches/libfuse/mount.c.diff"
    fi
    mkdir build
    cd build
    CFLAGS="$prefix_map_flags" meson setup --prefix=/usr ..
    meson configure --default-library static
    ninja -v install
    popd

    # Minimize binary size
    export CFLAGS="-ffunction-sections -fdata-sections -Os $prefix_map_flags"

    cp "/bundle/sources/squashfuse-0.5.2.tar.gz" ./0.5.2.tar.gz
    echo "db0238c5981dabbd80ee09ae15387f390091668ca060a7bc38047912491443d3  0.5.2.tar.gz" | sha256sum -c -
    tar xf 0.5.2.tar.gz
    pushd squashfuse-*/
    ./autogen.sh
    ./configure LDFLAGS="-static"
    make -j"$(nproc)"
    make install
    /usr/bin/install -c -m 644 ./*.h '/usr/local/include/squashfuse'
    popd
    rm -rf "$tmpdir"
  )

  src="/build/type2-runtime-$TYPE2_COMMIT"
  # The upstream workflow writes this file before it builds.
  printf 'https://github.com/AppImage/type2-runtime/commit/%s\n' "${TYPE2_COMMIT:0:7}" > "$src/src/runtime/version"
  (cd "$src" && bash scripts/build-runtime.sh)
  cp "$src/out/runtime-x86_64" "$src/out/runtime-x86_64.debug" /out/
}

build() {
  local out="$1" expect="$2" engine labels="" sha
  local -a engine_command mounts
  verify_sources
  if [ -e "$out" ] && [ -n "$(ls -A "$out")" ]; then die "$out must be new or empty"; fi
  mkdir -p "$out"
  out="$(cd "$out" && pwd)"
  engine="${CONTAINER_ENGINE:-}"
  if [ -z "$engine" ]; then
    if command -v docker >/dev/null; then engine=docker
    elif command -v podman >/dev/null; then engine=podman
    else die "needs Docker or Podman; set CONTAINER_ENGINE"
    fi
  fi
  read -r -a engine_command <<< "$engine"
  if command -v selinuxenabled >/dev/null && selinuxenabled; then labels=",z"; fi
  mounts=(-v "$BUNDLE:/bundle:ro$labels" -v "$out:/out:rw$labels")
  if [ -n "${LIBFUSE_DIR:-}" ]; then
    [ -d "$LIBFUSE_DIR" ] || die "LIBFUSE_DIR is not a directory: $LIBFUSE_DIR"
    [ ! -e "$LIBFUSE_DIR/build" ] || die "LIBFUSE_DIR must not contain a build directory"
    mounts+=(-v "$(cd "$LIBFUSE_DIR" && pwd):/libfuse:ro$labels")
  fi
  "${engine_command[@]}" run --rm --platform linux/amd64 "${mounts[@]}" "$IMAGE" /bin/sh -c "set -e
    apk add --no-cache $(echo $PACKAGES)
    mkdir /build
    tar -xzf /bundle/$TYPE2_ARCHIVE -C /build
    exec bash /bundle/rebuild.sh --in-container"

  LC_ALL=C comm -23 <(LC_ALL=C sort "$BUNDLE/producer-packages.txt") <(LC_ALL=C sort "$out/installed-packages.txt") \
    > "$out/package-differences.txt"
  sha="$(sha256sum "$out/runtime-x86_64" | cut -d ' ' -f 1)"
  echo
  echo "Rebuilt $out/runtime-x86_64"
  echo "  SHA-256 $sha"
  if [ "$sha" = "$SHIPPED_SHA256" ]; then
    echo "  identical to the runtime in FileCat's AppImage"
  else
    echo "  differs from the runtime in FileCat's AppImage ($SHIPPED_SHA256)"
  fi
  if [ -s "$out/package-differences.txt" ]; then
    echo "  producer packages not installed at their recorded versions (package-differences.txt):"
    sed 's/^/    /' "$out/package-differences.txt"
  fi
  if [ "$expect" = 1 ] && [ "$sha" != "$SHIPPED_SHA256" ]; then exit 3; fi
}

out=./out
expect=0
while [ $# -gt 0 ]; do
  case "$1" in
    --out) [ $# -ge 2 ] || usage; out="$2"; shift 2 ;;
    --expect-shipped) expect=1; shift ;;
    --unpack-libfuse) [ $# -eq 2 ] || usage; unpack_libfuse "$2"; exit 0 ;;
    --relink) [ $# -eq 4 ] || usage; relink "$2" "$3" "$4"; exit 0 ;;
    --in-container) [ $# -eq 1 ] || usage; container_build; exit 0 ;;
    -h|--help) usage 0 ;;
    *) usage ;;
  esac
done
build "$out" "$expect"
