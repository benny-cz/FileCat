#!/usr/bin/env python3
"""Click, right-click or only move the pointer to one screen point from a disposable Linux uinput device.

Usage: sudo python3 linux-uinput-click.py X Y SCREEN_WIDTH SCREEN_HEIGHT
       [left | right | move [HOLD_SECONDS] | drag X2 Y2 [HOLD_SECONDS]]
"move" leaves the pointer there for HOLD_SECONDS (default 2) before the device goes, so hover effects can show;
"drag" presses the left button at X,Y, moves in steps to X2,Y2 and holds there HOLD_SECONDS (default 0.3) before it
lets go, so whatever a held drag does there (a strip scrolling) can happen.
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


def click(x, y, width, height, mode="left", hold=2.0, to=None):
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
        if mode == "move":
            time.sleep(hold)
        elif mode == "drag":
            event(fd, EV_KEY, BTN_LEFT, 1)
            sync(fd)
            time.sleep(0.3)
            steps = 24
            for step in range(1, steps + 1):
                event(fd, EV_ABS, ABS_X, x + (to[0] - x) * step // steps)
                event(fd, EV_ABS, ABS_Y, y + (to[1] - y) * step // steps)
                sync(fd)
                time.sleep(0.04)
            time.sleep(hold)
            event(fd, EV_KEY, BTN_LEFT, 0)
            sync(fd)
            time.sleep(0.3)
        else:
            button = BTN_RIGHT if mode == "right" else BTN_LEFT
            event(fd, EV_KEY, button, 1)
            sync(fd)
            time.sleep(0.08)
            event(fd, EV_KEY, button, 0)
            sync(fd)
            time.sleep(0.2)
    finally:
        if created:
            time.sleep(0.15)
            fcntl.ioctl(fd, 0x5502)  # UI_DEV_DESTROY
        os.close(fd)


if __name__ == "__main__":
    args = sys.argv[1:]
    mode = args[4] if len(args) > 4 else "left"
    extra = args[5:]
    if (len(args) < 4 or not all(value.isdigit() for value in args[:4]) or
            mode not in ("left", "right", "move", "drag") or mode in ("left", "right") and extra or
            mode == "move" and len(extra) > 1 or
            mode == "drag" and (len(extra) not in (2, 3) or not all(value.isdigit() for value in extra[:2]))):
        raise SystemExit(__doc__)
    x, y, width, height = (int(value) for value in args[:4])
    to = (int(extra[0]), int(extra[1])) if mode == "drag" else None
    hold_text = extra[0] if mode == "move" and extra else extra[2] if mode == "drag" and len(extra) == 3 else None
    try:
        hold = float(hold_text) if hold_text is not None else 2.0 if mode == "move" else 0.3
    except ValueError:
        raise SystemExit(__doc__)
    if not (0 < width <= 16384 and 0 < height <= 16384 and x < width and y < height):
        raise SystemExit("The point must lie on the screen")
    if to is not None and not (to[0] < width and to[1] < height):
        raise SystemExit("The drag must end on the screen")
    if not 0 <= hold <= 10:
        raise SystemExit("Hold between 0 and 10 seconds")
    click(x, y, width, height, mode, hold, to)
