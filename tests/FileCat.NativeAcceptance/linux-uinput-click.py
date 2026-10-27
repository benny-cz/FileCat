#!/usr/bin/env python3
"""Click the left mouse button at one screen point from a disposable Linux uinput device.

Usage: sudo python3 linux-uinput-click.py X Y SCREEN_WIDTH SCREEN_HEIGHT
X and Y are pixels on a single screen SCREEN_WIDTH by SCREEN_HEIGHT pixels (as xwininfo reports them). The device has
absolute axes and mouse buttons, which udev and libinput take for an absolute mouse (as a VM's tablet pointer), so the
compositor moves its own pointer there: XTEST clicks do not reach X11 windows under GNOME's Xwayland. The calling
native acceptance test supplies sudo authentication separately; no credentials are stored in this helper.
"""

import fcntl
import os
import struct
import sys
import time


EV_SYN, EV_KEY, EV_ABS = 0, 1, 3
ABS_X, ABS_Y = 0, 1
BTN_LEFT, BTN_RIGHT = 0x110, 0x111


def event(fd, kind, code, value):
    os.write(fd, struct.pack("llHHi", 0, 0, kind, code, value))


def sync(fd):
    event(fd, EV_SYN, 0, 0)


def click(x, y, width, height):
    fd = os.open("/dev/uinput", os.O_WRONLY | os.O_NONBLOCK)
    created = False
    try:
        fcntl.ioctl(fd, 0x40045564, EV_KEY)  # UI_SET_EVBIT
        fcntl.ioctl(fd, 0x40045564, EV_ABS)
        for button in (BTN_LEFT, BTN_RIGHT):
            fcntl.ioctl(fd, 0x40045565, button)  # UI_SET_KEYBIT
        for axis in (ABS_X, ABS_Y):
            fcntl.ioctl(fd, 0x40045567, axis)  # UI_SET_ABSBIT
        # Linux uinput_user_dev: 80-byte name, input_id, ff_effects_max, then absmax, absmin, absfuzz and absflat,
        # 64 entries each.
        absmax = [0] * 64
        absmax[ABS_X], absmax[ABS_Y] = width - 1, height - 1
        device = struct.pack("80sHHHHI256i", b"FileCat native acceptance absolute pointer",
                             3, 0x1234, 0x5679, 1, 0, *(absmax + [0] * 192))
        os.write(fd, device)
        fcntl.ioctl(fd, 0x5501)  # UI_DEV_CREATE
        created = True
        time.sleep(1.0)  # Let the compositor register the new pointer.
        # Arrive from a neighbouring point, so the move to the target is a real motion.
        for px, py in ((max(0, x - 3), y), (x, y)):
            event(fd, EV_ABS, ABS_X, px)
            event(fd, EV_ABS, ABS_Y, py)
            sync(fd)
            time.sleep(0.15)
        event(fd, EV_KEY, BTN_LEFT, 1)
        sync(fd)
        time.sleep(0.08)
        event(fd, EV_KEY, BTN_LEFT, 0)
        sync(fd)
        time.sleep(0.2)
    finally:
        if created:
            time.sleep(0.15)
            fcntl.ioctl(fd, 0x5502)  # UI_DEV_DESTROY
        os.close(fd)


if __name__ == "__main__":
    if len(sys.argv) != 5 or not all(value.isdigit() for value in sys.argv[1:]):
        raise SystemExit(__doc__)
    x, y, width, height = (int(value) for value in sys.argv[1:])
    if not (0 < width <= 16384 and 0 < height <= 16384 and x < width and y < height):
        raise SystemExit("The point must lie on the screen")
    click(x, y, width, height)
