#!/usr/bin/env bash
# Builds the disposable disk images the recovery tests read (P10). Real file systems, changed by the Linux kernel's own
# vfat and exfat drivers and by ntfs-3g, so deletions look exactly as they do on real media. Each image gets:
#   keep.txt                          existing (never deleted)
#   overwritten.txt                   deleted, then its space reused by filler.bin     -> must not be claimed recoverable
#   frag-a.bin                        written in two pieces around frag-b and frag-c, then deleted (fragmented)
#   docs/report.txt, docs/Long file name with spaces.txt, docs/Příliš žluťoučký kůň.txt   deleted last -> recoverable
#   photos/ (with a.jpg, b.jpg)       deleted as a whole folder last
#   tiny.txt (60 bytes)               deleted last (NTFS keeps it inside the MFT record)
# File contents are "<name>:<8-digit line number>\n" lines, so tests can regenerate every byte (RecoveryFixtures.cs).
# Usage: sudo eng/make-recovery-fixtures.sh OUTDIR     (Ubuntu with dosfstools, exfatprogs, ntfs-3g, python3)
set -euo pipefail

OUT="$(realpath "${1:?output directory}")"
mkdir -p "$OUT"
WORK="$(mktemp -d)"
MNT="$WORK/mnt"
mkdir -p "$MNT"
cleanup() { umount "$MNT" 2>/dev/null || true; losetup -D 2>/dev/null || true; rm -rf "$WORK"; }
trap cleanup EXIT

# write NAME-IN-IMAGE SIZE [append]: deterministic content keyed by the file's base name.
write() {
  python3 - "$MNT/$1" "$2" "${3:-}" <<'PY'
import os, sys
path, size, mode = sys.argv[1], int(sys.argv[2]), sys.argv[3]
os.makedirs(os.path.dirname(path), exist_ok=True)
name = os.path.basename(path)
start = os.path.getsize(path) if mode == "append" else 0
out = bytearray()
line = 0
while len(out) < start + size:
    out += f"{name}:{line:08d}\n".encode("utf-8")
    line += 1
with open(path, "ab" if mode == "append" else "wb") as f:
    f.write(bytes(out[start:start + size]))
    f.flush()
    os.fsync(f.fileno())
PY
  sync
}

scenario() {
  write keep.txt 1500
  write overwritten.txt 20000
  write frag-a.bin 16384
  write frag-b.bin 16384
  write frag-c.bin 8192
  write frag-a.bin 24576 append        # frag-a continues after frag-c: two pieces
  rm "$MNT/overwritten.txt"; sync
  write filler.bin 49152                # takes the space overwritten.txt had
  rm "$MNT/frag-a.bin"; sync
  write "docs/report.txt" 10000
  write "docs/Long file name with spaces.txt" 5000
  write "docs/Příliš žluťoučký kůň.txt" 3000
  write "photos/a.jpg" 70000
  write "photos/b.jpg" 12345
  write tiny.txt 60
  # Deleted last: nothing is written after these, so their content survives intact.
  rm "$MNT/docs/report.txt" "$MNT/docs/Long file name with spaces.txt" "$MNT/docs/Příliš žluťoučký kůň.txt" "$MNT/tiny.txt"
  rm -r "$MNT/photos"
  sync
}

fat() { # name fat-bits size-MiB sectors-per-cluster
  local img="$WORK/$1.img"
  truncate -s "$3M" "$img"
  mkfs.vfat -F "$2" -s "$4" -n FIXTURE -i 1234ABCD "$img" >/dev/null
  mount -o loop,utf8=1,shortname=mixed "$img" "$MNT"
  scenario
  umount "$MNT"
  gzip -9n < "$img" > "$OUT/$1.img.gz"
}

exfat() {
  local img="$WORK/exfat.img"
  truncate -s 16M "$img"
  mkfs.exfat -n FIXTURE -c 4K "$img" >/dev/null
  mount -o loop "$img" "$MNT" 2>/dev/null || mount.exfat-fuse "$img" "$MNT"
  scenario
  umount "$MNT"
  gzip -9n < "$img" > "$OUT/exfat.img.gz"
}

ntfs() {
  local img="$WORK/ntfs.img"
  truncate -s 16M "$img"
  mkntfs -F -Q -q -L FIXTURE -c 4096 "$img"
  ntfs-3g "$img" "$MNT"
  scenario
  umount "$MNT"
  gzip -9n < "$img" > "$OUT/ntfs.img.gz"
}

# A partitioned disk: an MBR (or GPT) table with a FAT16 and an exFAT partition, one deleted file in each.
disk() { # name label-type
  local img="$WORK/$1.img"
  truncate -s 48M "$img"
  printf 'label: %s\nstart=2048, size=40960, type=%s\nstart=43008, size=40960, type=%s\n' "$2" \
    "$([ "$2" = gpt ] && echo EBD0A0A2-B9E5-4433-87C0-68B6B72699C7 || echo 6)" \
    "$([ "$2" = gpt ] && echo EBD0A0A2-B9E5-4433-87C0-68B6B72699C7 || echo 7)" | sfdisk -q "$img"
  local dev
  dev="$(losetup -f --show -P "$img")"
  mkfs.vfat -F 16 -n FIRST "${dev}p1" >/dev/null
  mkfs.exfat -n SECOND "${dev}p2" >/dev/null
  mount "${dev}p1" "$MNT"; write "first.txt" 7000; rm "$MNT/first.txt"; sync; umount "$MNT"
  mount "${dev}p2" "$MNT" 2>/dev/null || mount.exfat-fuse "${dev}p2" "$MNT"; write "second.txt" 9000; rm "$MNT/second.txt"; sync; umount "$MNT"
  losetup -d "$dev"
  gzip -9n < "$img" > "$OUT/$1.img.gz"
}

fat fat12 12 4 2
fat fat16 16 24 2
fat fat32 32 40 1
exfat
ntfs
disk disk-mbr dos
disk disk-gpt gpt
ls -la "$OUT"
