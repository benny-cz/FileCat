#!/usr/bin/env python3
"""Send one physical keyboard chord from a disposable Linux uinput device.

Usage: sudo python3 linux-uinput-key.py enter|escape|home|left|right|tab|alt+tab|ctrl+c|ctrl+v
The calling native acceptance test controls the logged-in desktop and supplies
sudo authentication separately. No credentials are stored in this helper.
"""

import fcntl
import os
import struct
import sys
import time


KEYS = {"enter": (28,), "escape": (1,), "home": (102,), "left": (105,),
        "right": (106,), "tab": (15,), "alt+tab": (56, 15),
        "ctrl+c": (29, 46), "ctrl+v": (29, 47)}
EV_KEY = 1
EV_SYN = 0


def event(fd, code, value):
    os.write(fd, struct.pack("llHHi", 0, 0, EV_KEY, code, value))
    os.write(fd, struct.pack("llHHi", 0, 0, EV_SYN, 0, 0))


def send(chord):
    keys = KEYS[chord]
    fd = os.open("/dev/uinput", os.O_WRONLY | os.O_NONBLOCK)
    created = False
    try:
        fcntl.ioctl(fd, 0x40045564, EV_KEY)  # UI_SET_EVBIT
        for code in keys:
            fcntl.ioctl(fd, 0x40045565, code)  # UI_SET_KEYBIT
        # Linux uinput_user_dev: 80-byte name, input_id, ff_effects_max,
        # then four 64-entry absolute-axis arrays. No absolute axes are used.
        device = struct.pack("80sHHHHI256i", b"FileCat native acceptance keyboard",
                             3, 0x1234, 0x5678, 1, 0, *([0] * 256))
        os.write(fd, device)
        fcntl.ioctl(fd, 0x5501)  # UI_DEV_CREATE
        created = True
        time.sleep(0.6)  # Let the compositor register the new keyboard.
        for code in keys:
            event(fd, code, 1)
            time.sleep(0.03)
        for code in reversed(keys):
            event(fd, code, 0)
            time.sleep(0.03)
    finally:
        if created:
            time.sleep(0.15)
            fcntl.ioctl(fd, 0x5502)  # UI_DEV_DESTROY
        os.close(fd)


if __name__ == "__main__":
    if len(sys.argv) != 2 or sys.argv[1] not in KEYS:
        raise SystemExit("Usage: linux-uinput-key.py enter|escape|home|left|right|tab|alt+tab|ctrl+c|ctrl+v")
    send(sys.argv[1])
