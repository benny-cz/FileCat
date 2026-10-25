#!/usr/bin/env python3
"""Short-lived physical mouse for an isolated Ubuntu acceptance journey.

Commands on stdin: MOVE signed-dx signed-dy, DOWN, UP, QUIT. Every command
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
    try:
        fcntl.ioctl(fd, 0x40045564, 1)  # UI_SET_EVBIT EV_KEY
        fcntl.ioctl(fd, 0x40045564, 2)  # UI_SET_EVBIT EV_REL
        fcntl.ioctl(fd, 0x40045565, 272)  # UI_SET_KEYBIT BTN_LEFT
        fcntl.ioctl(fd, 0x40045566, 0)  # UI_SET_RELBIT REL_X
        fcntl.ioctl(fd, 0x40045566, 1)  # UI_SET_RELBIT REL_Y
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
            elif len(parts) == 3 and parts[0] == "MOVE":
                dx, dy = int(parts[1]), int(parts[2])
                if max(abs(dx), abs(dy)) > 500:
                    raise ValueError("Pointer move exceeds the bounded test step")
                emit(fd, 2, 0, dx)
                emit(fd, 2, 1, dy)
            else:
                raise ValueError("Invalid pointer command")
            sync(fd)
            time.sleep(0.04)
            print("OK", flush=True)
    finally:
        if pressed:
            emit(fd, 1, 272, 0)
            sync(fd)
        if created:
            time.sleep(0.1)
            fcntl.ioctl(fd, 0x5502)  # UI_DEV_DESTROY
        os.close(fd)


if __name__ == "__main__":
    main()
