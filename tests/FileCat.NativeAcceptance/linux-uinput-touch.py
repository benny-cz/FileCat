#!/usr/bin/env python3
"""Tap one screen point with a disposable Linux uinput touchscreen.

Usage: sudo python3 linux-uinput-touch.py X Y SCREEN_WIDTH SCREEN_HEIGHT
The calling acceptance runner supplies sudo authentication separately. No
credentials, device nodes or persistent privileges are stored by this helper.
"""

import fcntl
import os
import struct
import sys
import time


EV_SYN = 0
EV_KEY = 1
EV_ABS = 3
ABS_X = 0
ABS_Y = 1
BTN_TOUCH = 330
INPUT_PROP_DIRECT = 0


def emit(fd, kind, code, value):
    os.write(fd, struct.pack("llHHi", 0, 0, kind, code, value))


def tap(x, y, width, height):
    if not (100 <= width <= 16384 and 100 <= height <= 16384
            and 0 <= x < width and 0 <= y < height):
        raise SystemExit("The tap point must be within a bounded display")
    fd = os.open("/dev/uinput", os.O_WRONLY | os.O_NONBLOCK)
    created = False
    try:
        fcntl.ioctl(fd, 0x40045564, EV_KEY)  # UI_SET_EVBIT
        fcntl.ioctl(fd, 0x40045564, EV_ABS)
        fcntl.ioctl(fd, 0x40045565, BTN_TOUCH)  # UI_SET_KEYBIT
        fcntl.ioctl(fd, 0x40045567, ABS_X)  # UI_SET_ABSBIT
        fcntl.ioctl(fd, 0x40045567, ABS_Y)
        fcntl.ioctl(fd, 0x4004556E, INPUT_PROP_DIRECT)  # UI_SET_PROPBIT
        axes = [0] * 256
        axes[ABS_X] = width - 1  # absmax[ABS_X]
        axes[ABS_Y] = height - 1  # absmax[ABS_Y]
        device = struct.pack("80sHHHHI256i", b"FileCat native acceptance touch",
                             3, 0x1234, 0x5679, 1, 0, *axes)
        os.write(fd, device)
        fcntl.ioctl(fd, 0x5501)  # UI_DEV_CREATE
        created = True
        time.sleep(0.7)  # Allow the compositor to register the device.
        emit(fd, EV_ABS, ABS_X, x)
        emit(fd, EV_ABS, ABS_Y, y)
        emit(fd, EV_KEY, BTN_TOUCH, 1)
        emit(fd, EV_SYN, 0, 0)
        time.sleep(0.08)
        emit(fd, EV_KEY, BTN_TOUCH, 0)
        emit(fd, EV_SYN, 0, 0)
    finally:
        if created:
            time.sleep(0.15)
            fcntl.ioctl(fd, 0x5502)  # UI_DEV_DESTROY
        os.close(fd)


if __name__ == "__main__":
    if len(sys.argv) != 5:
        raise SystemExit("Usage: linux-uinput-touch.py X Y SCREEN_WIDTH SCREEN_HEIGHT")
    tap(*(int(arg) for arg in sys.argv[1:]))
