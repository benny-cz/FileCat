#!/usr/bin/env bash
# Prepares the disposable fixtures for File-system details' native Linux tests on loop-mounted images: ext4 restrictions
# (fs-verity, immutable, append-only, no-dump), ext4 case folding, historical ext2/ext3/ext4 inode flags, Btrfs properties
# and subvolumes, and Btrfs and XFS extent records. Needs passwordless sudo, e2fsprogs, btrfs-progs, xfsprogs and python3.
# Prints the test variables and appends them to $GITHUB_ENV when that is set.
#
#   prepare-linux-details-fixtures.sh <new or empty work directory>            prepare
#   prepare-linux-details-fixtures.sh <work directory> --remove                unmount everything it mounted
set -Eeuo pipefail

work=$(realpath -m "${1:?usage: prepare-linux-details-fixtures.sh <work directory> [--remove]}")
mounts=(restrictions casefold ext-flags/ext2 ext-flags/ext3 ext-flags/ext4 btrfs btrfs-extents xfs)
if [[ "${2:-}" == --remove ]]; then
    for name in "${mounts[@]}"; do
        if findmnt -rn -M "$work/$name" > /dev/null; then sudo umount "$work/$name"; fi
    done
    exit 0
fi
mkdir -p "$work"
if [[ -n "$(ls -A "$work")" ]]; then echo "The work directory must be empty: $work" >&2; exit 2; fi
owner="$(id -u):$(id -g)"

emit() {
    echo "$1=$2"
    if [[ -n "${GITHUB_ENV:-}" ]]; then echo "$1=$2" >> "$GITHUB_ENV"; fi
}

# Formats an image with the given command, mounts it with the given options and hands the root to this user.
volume() {
    local name=$1 size=$2 options=$3
    shift 3
    truncate -s "$size" "$work/${name//\//-}.img"
    "$@" "$work/${name//\//-}.img"
    mkdir -p "$work/$name"
    sudo mount -o "loop,nodev,nosuid,$options" "$work/${name//\//-}.img" "$work/$name"
    sudo chown "$owner" "$work/$name"
}

# ext4 restrictions: fs-verity, immutable, append-only, no-dump and an ordinary file, plus a link to the verity file.
volume restrictions 128M noexec mkfs.ext4 -q -F -O verity
r="$work/restrictions"
python3 - "$r" <<'PY'
import fcntl, os, struct, sys
from pathlib import Path
root = Path(sys.argv[1])
for name in ("verity", "immutable", "append", "no-dump", "ordinary"):
    with (root / name).open("xb") as stream:
        stream.write(b"ext4-restriction-fixture-payload\n" * 192)
(root / "verity-link").symlink_to("verity")
fd = os.open(root / "verity", os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
try:
    # FS_IOC_ENABLE_VERITY, _IOW('f', 133, struct fsverity_enable_arg): version 1, SHA-256, 4096-byte blocks.
    fcntl.ioctl(fd, 0x40806685, struct.pack("=IIIIQIIQ11Q", 1, 1, 4096, 0, 0, 0, 0, 0, *([0] * 11)))
finally:
    os.close(fd)
PY
sudo chattr +i "$r/immutable"
sudo chattr +a "$r/append"
chattr +d "$r/no-dump"
lsattr "$r/verity" "$r/immutable" "$r/append" "$r/no-dump" "$r/ordinary"
emit FILECAT_TEST_EXT4_RESTRICTIONS_ROOT "$r"

# ext4 case folding: the same names in an ordinary and a folded directory, read-only afterwards.
volume casefold 64M noexec mkfs.ext4 -q -F -O casefold
c="$work/casefold"
mkdir "$c/ordinary" "$c/folded"
chattr +F "$c/folded"
for directory in ordinary folded; do
    printf 'casefold fixture\n' > "$c/$directory/MiXeD.txt"
    printf 'casefold fixture\n' > "$c/$directory/"$'caf\xc3\xa9.txt'
done
ln -s . "$c/parent-alias"
ln -s folded "$c/folded-link"
sudo mount -o remount,ro "$c"
lsattr -d "$c/ordinary" "$c/folded"
emit FILECAT_TEST_EXT4_CASEFOLD_ROOT "$c"

# Historical ext2, ext3 and ext4 inode flags (c, s and u) on a file and a folder, read-only afterwards.
for format in ext2 ext3 ext4; do
    volume "ext-flags/$format" 32M noexec "mkfs.$format" -q -F
    m="$work/ext-flags/$format"
    printf 'fixture payload canary\n' > "$m/marked.bin"
    printf 'fixture payload canary\n' > "$m/ordinary.bin"
    mkdir "$m/flagged-directory"
    ln -s marked.bin "$m/symlink.bin"
    chattr +csu "$m/marked.bin" "$m/flagged-directory"
    sudo mount -o remount,ro "$m"
    lsattr -d "$m/marked.bin" "$m/flagged-directory"
    findmnt -n -M "$m" -o FSTYPE,OPTIONS
done
emit FILECAT_TEST_EXT_FLAGS_ROOT "$work/ext-flags"

# Btrfs properties: a nested subvolume, inherited and per-file compression policies, a denied file, a link and a FIFO.
volume btrfs 256M compress=zlib mkfs.btrfs -q -f
b="$work/btrfs"
printf 'btrfs-payload-sentinel\n' > "$b/default.dat"
btrfs subvolume create "$b/nested" > /dev/null
mkdir "$b/nested/policy"
btrfs property set "$b/nested/policy" compression zstd
printf 'btrfs-payload-sentinel\n' > "$b/nested/policy/covered.dat"
for property in lzo zlib none; do
    printf 'btrfs-payload-sentinel\n' > "$b/nested/$property.dat"
    btrfs property set "$b/nested/$property.dat" compression "$property"
done
touch "$b/nested/denied.dat"
chmod 000 "$b/nested/denied.dat"
ln -s policy/covered.dat "$b/nested/link"
mkfifo "$b/nested/pipe"
emit FILECAT_TEST_LINUX_BTRFS_ROOT "$b"
emit FILECAT_TEST_LINUX_BTRFS_SUBVOLUME_ID "$(btrfs inspect-internal rootid "$b/nested")"

# Btrfs extents: a reflinked pair, an ordinary file and a compressed file, read-only afterwards.
volume btrfs-extents 256M noexec mkfs.btrfs -q -f
e="$work/btrfs-extents"
head -c 524288 /dev/urandom > "$e/source"
cp --reflink=always "$e/source" "$e/clone"
head -c 524288 /dev/urandom > "$e/ordinary"
touch "$e/compressed"
btrfs property set "$e/compressed" compression zstd
python3 -c 'import sys; sys.stdout.write("compressible btrfs extent fixture line\n" * 18500)' > "$e/compressed"
sync
sudo mount -o remount,ro "$e"
filefrag -v "$e/source" "$e/clone" "$e/ordinary" "$e/compressed" | grep -E "^ +[0-9]+:|extents? found"
emit FILECAT_TEST_LINUX_BTRFS_EXTENTS_ROOT "$e"

# XFS extents: reflinked files, one partly rewritten, a sparse file, a reserved (unwritten) range and an xattr.
volume xfs 512M noexec mkfs.xfs -q -f -m reflink=1
x="$work/xfs"
head -c 1048576 /dev/urandom > "$x/source"
python3 -c 'import os, sys; os.setxattr(sys.argv[1], "user.filecat.fixture", b"fixture-attr-value")' "$x/source"
cp --reflink=always "$x/source" "$x/clone"
cp --reflink=always "$x/source" "$x/partial"
dd if=/dev/urandom of="$x/partial" bs=65536 count=1 conv=notrunc status=none
head -c 1048576 /dev/urandom > "$x/ordinary"
truncate -s 1048577 "$x/sparse"
printf 'a' | dd of="$x/sparse" bs=1 seek=0 conv=notrunc status=none
printf 'b' | dd of="$x/sparse" bs=1 seek=1048576 conv=notrunc status=none
fallocate -l 65536 "$x/reserved"
sync
sudo mount -o remount,ro "$x"
filefrag -v "$x/partial" "$x/sparse" "$x/reserved" | grep -E "^ +[0-9]+:|extents? found"
emit FILECAT_TEST_LINUX_XFS_EXTENTS_ROOT "$x"

# FIEMAP on this machine's own disposable temporary folder.
emit FILECAT_TEST_LINUX_FIEMAP 1
uname -r
