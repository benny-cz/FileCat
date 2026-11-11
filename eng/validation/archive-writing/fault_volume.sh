#!/bin/bash
# AT08 device faults on a real Linux volume (test machines only): an ext4 file system on a device-mapper target over a loop
# image, whose writes a controller can make fail with EIO (dm-flakey error_writes) while reads keep working, for
# UnixArchiveStorageTests.A_drive_that_fails_writes_refuses_with_the_drive_text_and_keeps_the_original.
# Usage, as root:
#   fault_volume.sh setup IMAGE MOUNT OWNER CONTROL   a 512 MiB image, device "fc-aw-fault", ext4 at MOUNT; MOUNT/data and
#                                                     CONTROL (outside the volume) belong to OWNER, mode 0700
#   fault_volume.sh serve MOUNT CONTROL               answers CONTROL/request ("fail", "restore" or "stop") in CONTROL/done
#                                                     with the same word once the change is in effect; anything else stops
#   fault_volume.sh teardown IMAGE MOUNT              unmounts, removes the device and the loop, deletes the image
# "restore" makes writes work again, then unmounts, checks (e2fsck -fy) and mounts the file system, so one that turned
# read-only after the errors can be compared and cleaned. The test then sets FILECAT_ARCHIVE_FAULT_VOLUME=MOUNT/data and
# FILECAT_ARCHIVE_FAULT_CONTROL=CONTROL.
set -euo pipefail
NAME=fc-aw-fault
DEV=/dev/mapper/$NAME
[ "$(id -u)" = 0 ] || { echo "run as root" >&2; exit 2; }
loop_of() { dmsetup deps -o devname "$NAME" | sed -n 's/.*(\(loop[0-9]*\)).*/\/dev\/\1/p'; }
table() { local loop sectors; loop=$(loop_of); sectors=$(blockdev --getsz "$loop")
  if [ "$1" = fail ]; then echo "0 $sectors flakey $loop 0 0 3600 1 error_writes"; else echo "0 $sectors linear $loop 0"; fi; }
case "${1:-}" in
  setup)
    IMAGE=$2; MOUNT=$3; OWNER=$4; CONTROL=$5
    [ ! -e "$DEV" ] || { echo "$NAME exists" >&2; exit 2; }
    truncate -s 512M "$IMAGE"; LOOP=$(losetup --find --show "$IMAGE")
    dmsetup create "$NAME" --table "0 $(blockdev --getsz "$LOOP") linear $LOOP 0"
    mkfs.ext4 -q -F "$DEV"; mkdir -p "$MOUNT"; mount "$DEV" "$MOUNT"
    mkdir -p "$MOUNT/data" "$CONTROL"; chown "$OWNER" "$MOUNT/data" "$CONTROL"; chmod 700 "$MOUNT/data" "$CONTROL"
    echo "ready $DEV over $LOOP at $MOUNT" ;;
  serve)
    MOUNT=$2; CONTROL=$3
    while true; do
      if [ -f "$CONTROL/request" ]; then
        WORD=$(head -c 16 "$CONTROL/request" | tr -cd 'a-z'); rm -f "$CONTROL/request"
        ANSWER=error
        case "$WORD" in
          fail)
            # Suspending first flushes and freezes the file system, so everything written before is on the image.
            if dmsetup suspend "$NAME" && dmsetup reload "$NAME" --table "$(table fail)" && dmsetup resume "$NAME"; then ANSWER=fail; fi ;;
          restore)
            if dmsetup suspend --nolockfs "$NAME" && dmsetup reload "$NAME" --table "$(table linear)" && dmsetup resume "$NAME" &&
               umount "$MOUNT" && { e2fsck -fy "$DEV" >/dev/null; [ $? -le 2 ]; } && mount "$DEV" "$MOUNT"; then ANSWER=restore; fi ;;
          stop) ANSWER=stop ;;
          *) ANSWER=refused ;;
        esac
        printf '%s' "$ANSWER" >"$CONTROL/done.tmp"; chmod 644 "$CONTROL/done.tmp"; mv "$CONTROL/done.tmp" "$CONTROL/done"
        [ "$ANSWER" = stop ] && exit 0
        [ "$ANSWER" = refused ] && exit 3
      fi
      sleep 0.2
    done ;;
  teardown)
    IMAGE=$2; MOUNT=$3
    LOOP=$(loop_of || true)
    if [ -e "$DEV" ]; then dmsetup suspend --nolockfs "$NAME" && dmsetup reload "$NAME" --table "$(table linear)" && dmsetup resume "$NAME" || true; fi
    umount "$MOUNT" 2>/dev/null || true
    dmsetup remove "$NAME" 2>/dev/null || true
    [ -n "$LOOP" ] && losetup -d "$LOOP" 2>/dev/null || true
    rm -f "$IMAGE"; echo "removed" ;;
  *) echo "usage: fault_volume.sh setup IMAGE MOUNT OWNER CONTROL | serve MOUNT CONTROL | teardown IMAGE MOUNT" >&2; exit 2 ;;
esac
