#!/usr/bin/env python3
"""Click a bounded screen point with a short-lived Linux uinput mouse.

Usage: sudo python3 linux-uinput-mouse.py X Y SCREEN_WIDTH SCREEN_HEIGHT [shift|move|shift-current|ctrl-current]
The pointer is first clamped to the upper-left corner, then moved in small
relative steps to limit acceleration. No device or privilege persists.
"""

import fcntl
import os
import struct
import sys
import time


EV_SYN = 0
EV_KEY = 1
EV_REL = 2
REL_X = 0
REL_Y = 1
BTN_LEFT = 272
KEY_LEFTSHIFT = 42
KEY_LEFTCTRL = 29


def emit(fd, kind, code, value):
    os.write(fd, struct.pack("llHHi", 0, 0, kind, code, value))


def move(fd, dx, dy):
    emit(fd, EV_REL, REL_X, dx)
    emit(fd, EV_REL, REL_Y, dy)
    emit(fd, EV_SYN, 0, 0)


def click(x, y, width, height, mode="click"):
    if not (100 <= width <= 16384 and 100 <= height <= 16384
            and 0 <= x < width and 0 <= y < height):
        raise SystemExit("The click point must be within a bounded display")
    fd = os.open("/dev/uinput", os.O_WRONLY | os.O_NONBLOCK)
    created = False
    modifier_held = None
    try:
        fcntl.ioctl(fd, 0x40045564, EV_KEY)  # UI_SET_EVBIT
        fcntl.ioctl(fd, 0x40045564, EV_REL)
        fcntl.ioctl(fd, 0x40045565, BTN_LEFT)  # UI_SET_KEYBIT
        if mode in ("shift", "shift-current"):
            fcntl.ioctl(fd, 0x40045565, KEY_LEFTSHIFT)
        if mode == "ctrl-current":
            fcntl.ioctl(fd, 0x40045565, KEY_LEFTCTRL)
        fcntl.ioctl(fd, 0x40045566, REL_X)  # UI_SET_RELBIT
        fcntl.ioctl(fd, 0x40045566, REL_Y)
        device = struct.pack("80sHHHHI256i", b"FileCat native acceptance mouse",
                             3, 0x1234, 0x567A, 1, 0, *([0] * 256))
        os.write(fd, device)
        fcntl.ioctl(fd, 0x5501)  # UI_DEV_CREATE
        created = True
        time.sleep(0.7)
        if mode not in ("shift-current", "ctrl-current"):
            move(fd, -32767, -32767)
            time.sleep(0.15)
            # Low-velocity motion limits acceleration; the caller can measure
            # the resulting pointer before asking for a current-position click.
            for axis, total in ((REL_X, x), (REL_Y, y)):
                for _ in range(total):
                    emit(fd, EV_REL, axis, 1)
                    emit(fd, EV_SYN, 0, 0)
                    time.sleep(0.001)
            time.sleep(0.15)
        if mode == "move":
            return
        if mode in ("shift", "shift-current", "ctrl-current"):
            modifier_held = KEY_LEFTCTRL if mode == "ctrl-current" else KEY_LEFTSHIFT
            emit(fd, EV_KEY, modifier_held, 1)
            emit(fd, EV_SYN, 0, 0)
            time.sleep(0.08)
        emit(fd, EV_KEY, BTN_LEFT, 1)
        emit(fd, EV_SYN, 0, 0)
        time.sleep(0.08)
        emit(fd, EV_KEY, BTN_LEFT, 0)
        emit(fd, EV_SYN, 0, 0)
    finally:
        if modifier_held is not None:
            emit(fd, EV_KEY, modifier_held, 0)
            emit(fd, EV_SYN, 0, 0)
        if created:
            time.sleep(0.15)
            fcntl.ioctl(fd, 0x5502)  # UI_DEV_DESTROY
        os.close(fd)


if __name__ == "__main__":
    if len(sys.argv) not in (5, 6) or (len(sys.argv) == 6 and sys.argv[5] not in ("shift", "move", "shift-current", "ctrl-current")):
        raise SystemExit("Usage: linux-uinput-mouse.py X Y SCREEN_WIDTH SCREEN_HEIGHT [shift|move|shift-current|ctrl-current]")
    click(*(int(arg) for arg in sys.argv[1:5]), mode=sys.argv[5] if len(sys.argv) == 6 else "click")
