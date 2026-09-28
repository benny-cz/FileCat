#!/usr/bin/env bash
# Builds large disk images for the recovery benchmark (P10: "benchmark scan/preview"): NTFS and FAT32, 512 MiB each,
# 20,000 small files in 200 folders plus 200 files of 256 KiB, half of everything deleted. Not committed: the workflow
# "Recovery fixtures" builds them and runs the benchmark (RecoveryBenchmark in the Core tests) right away.
# Usage: sudo eng/make-recovery-bench.sh OUTDIR     (Ubuntu with dosfstools, ntfs-3g, python3)
set -euo pipefail

OUT="$(realpath "${1:?output directory}")"
mkdir -p "$OUT"
WORK="$(mktemp -d)"
MNT="$WORK/mnt"
mkdir -p "$MNT"
cleanup() { umount "$MNT" 2>/dev/null || true; rm -rf "$WORK"; }
trap cleanup EXIT

populate() {
  python3 - "$MNT" <<'PY'
import os, sys
root = sys.argv[1]
for folder in range(200):
    d = os.path.join(root, f"folder-{folder:03d}")
    os.makedirs(d, exist_ok=True)
    for i in range(100):
        with open(os.path.join(d, f"note-{i:03d}.txt"), "w") as f:
            f.write(f"folder {folder} note {i}\n" * 40)
    with open(os.path.join(d, "photo.jpg"), "wb") as f:
        f.write(bytes((folder * 7 + b) % 251 for b in range(256 * 1024)))
PY
  sync
  # Half of everything goes: every second note, and every second folder's photo.
  find "$MNT" -name 'note-*[02468].txt' -delete
  for folder in $(seq -f '%03g' 0 2 199); do rm -f "$MNT/folder-$folder/photo.jpg"; done
  sync
}

truncate -s 512M "$WORK/bench-ntfs.img"
mkntfs -F -Q -q -L BENCH -c 4096 "$WORK/bench-ntfs.img"
ntfs-3g "$WORK/bench-ntfs.img" "$MNT"
populate
umount "$MNT"
mv "$WORK/bench-ntfs.img" "$OUT/"

truncate -s 512M "$WORK/bench-fat32.img"
mkfs.vfat -F 32 -n BENCH "$WORK/bench-fat32.img" >/dev/null
mount -o loop "$WORK/bench-fat32.img" "$MNT"
populate
umount "$MNT"
mv "$WORK/bench-fat32.img" "$OUT/"
ls -la "$OUT"
