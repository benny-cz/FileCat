#!/usr/bin/env python3
"""Short-lived physical mouse for an isolated Ubuntu acceptance journey.

Commands on stdin: MOVE signed-dx signed-dy, HWHEEL signed-steps,
WHEEL signed-steps, SHIFT_DOWN, SHIFT_UP, DOWN, UP, QUIT. Every command
returns OK on stdout. The button and device are released on normal EOF too.
The caller keeps moves inside the disposable test VM's display bounds.
"""

import fcntl
import os
import struct
import sys
import time


def emit(fd, kind, code, value):
    os.write(fd, struct.pack("llHHi", 0, 0, kind, code, value))


def sync(fd):
    emit(fd, 0, 0, 0)


def main():
    fd = os.open("/dev/uinput", os.O_WRONLY | os.O_NONBLOCK)
    created = False
    pressed = False
    shift_held = False
    try:
        fcntl.ioctl(fd, 0x40045564, 1)  # UI_SET_EVBIT EV_KEY
        fcntl.ioctl(fd, 0x40045564, 2)  # UI_SET_EVBIT EV_REL
        fcntl.ioctl(fd, 0x40045565, 272)  # UI_SET_KEYBIT BTN_LEFT
        fcntl.ioctl(fd, 0x40045565, 42)  # UI_SET_KEYBIT KEY_LEFTSHIFT
        fcntl.ioctl(fd, 0x40045566, 0)  # UI_SET_RELBIT REL_X
        fcntl.ioctl(fd, 0x40045566, 1)  # UI_SET_RELBIT REL_Y
        fcntl.ioctl(fd, 0x40045566, 6)  # UI_SET_RELBIT REL_HWHEEL
        fcntl.ioctl(fd, 0x40045566, 8)  # UI_SET_RELBIT REL_WHEEL
        device = struct.pack("80sHHHHI256i", b"FileCat isolated acceptance pointer",
                             3, 0x1234, 0x567C, 1, 0, *([0] * 256))
        os.write(fd, device)
        fcntl.ioctl(fd, 0x5501)  # UI_DEV_CREATE
        created = True
        time.sleep(0.5)
        print("READY", flush=True)
        for line in sys.stdin:
            parts = line.strip().split()
            if parts == ["QUIT"]:
                print("OK", flush=True)
                break
            if parts == ["DOWN"] and not pressed:
                emit(fd, 1, 272, 1)
                pressed = True
            elif parts == ["UP"] and pressed:
                emit(fd, 1, 272, 0)
                pressed = False
            elif parts == ["SHIFT_DOWN"] and not shift_held:
                emit(fd, 1, 42, 1)
                shift_held = True
            elif parts == ["SHIFT_UP"] and shift_held:
                emit(fd, 1, 42, 0)
                shift_held = False
            elif len(parts) == 3 and parts[0] == "MOVE":
                dx, dy = int(parts[1]), int(parts[2])
                if max(abs(dx), abs(dy)) > 500:
                    raise ValueError("Pointer move exceeds the bounded test step")
                emit(fd, 2, 0, dx)
                emit(fd, 2, 1, dy)
            elif len(parts) == 2 and parts[0] == "HWHEEL":
                steps = int(parts[1])
                if steps == 0 or abs(steps) > 8:
                    raise ValueError("Horizontal wheel step exceeds the bounded test")
                emit(fd, 2, 6, steps)
            elif len(parts) == 2 and parts[0] == "WHEEL":
                steps = int(parts[1])
                if steps == 0 or abs(steps) > 8:
                    raise ValueError("Wheel step exceeds the bounded test")
                emit(fd, 2, 8, steps)
            else:
                raise ValueError("Invalid pointer command")
            sync(fd)
            time.sleep(0.04)
            print("OK", flush=True)
    finally:
        if shift_held:
            emit(fd, 1, 42, 0)
            sync(fd)
        if pressed:
            emit(fd, 1, 272, 0)
            sync(fd)
        if created:
            time.sleep(0.1)
            fcntl.ioctl(fd, 0x5502)  # UI_DEV_DESTROY
        os.close(fd)


if __name__ == "__main__":
    main()
