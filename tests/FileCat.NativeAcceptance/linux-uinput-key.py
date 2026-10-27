#!/usr/bin/env python3
"""Send one physical keyboard chord from a disposable Linux uinput device.

Usage: sudo python3 linux-uinput-key.py CHORD [absolute owned release signal]
The calling native acceptance test controls the logged-in desktop and supplies
sudo authentication separately. Controlled Shift and Ctrl holds wait for their
test-owned release signals for at most 30 seconds. No credentials are stored in
this helper.
"""

import fcntl
import os
from pathlib import Path
import struct
import sys
import time


KEYS = {"enter": (28,), "escape": (1,), "insert": (110,), "home": (102,), "up": (103,),
        "down": (108,), "shift+down": (42, 108),
        "shift+down-hold": (42, 108), "left": (105,),
        "shift+down-hold-controlled": (42, 108), "shift+left": (42, 105), "shift+right": (42, 106), "f2": (60,),
        "shift-hold-controlled": (42,), "ctrl-hold-controlled": (29,),
        "f3": (61,), "shift+f3": (42, 61), "f4": (62,), "f5": (63,), "f6": (64,), "f7": (65,), "f8": (66,),
        "end": (107,), "pageup": (104,), "pagedown": (109,), "space": (57,),
        "right": (106,), "tab": (15,), "alt+tab": (56, 15), "alt+left": (56, 105),
        "alt+up": (56, 103), "alt+f": (56, 33), "alt+n": (56, 49),
        "ctrl+l": (29, 38),
        "ctrl+a": (29, 30), "ctrl+f": (29, 33), "ctrl+i": (29, 23), "ctrl+t": (29, 20),
        "ctrl+tab": (29, 15), "ctrl+shift+l": (29, 42, 38),
        "ctrl+r": (29, 19), "ctrl+s": (29, 31), "ctrl+m": (29, 50), "ctrl+k": (29, 37), "ctrl+home": (29, 102), "slash": (53,), "a": (30,), "c": (46,), "h": (35,), "r": (19,), "backspace": (14,), "ctrl+shift+p": (29, 42, 25), "ctrl+shift+f": (29, 42, 33),
        "s": (31,), "p": (25,), "l": (38,), "i": (23,), "t": (20,), "e": (18,),
        "ctrl+c": (29, 46), "ctrl+x": (29, 45), "ctrl+v": (29, 47), "ctrl+q": (29, 16), "alt+f7": (56, 65),
        "shift+f10": (42, 68), "f1": (59,), "f9": (67,), "f10": (68,), "delete": (111,), "alt+2": (56, 3), "alt+3": (56, 4),
        "alt+shift+enter": (56, 42, 28)}
# "type:find files" types lowercase letters and spaces with one device (US layout).
LETTERS = dict(zip("abcdefghijklmnopqrstuvwxyz ", (30, 48, 46, 32, 18, 33, 34, 35, 23, 36, 37, 38, 50, 49, 24, 25, 16, 19,
                                                     31, 20, 22, 47, 17, 45, 21, 44, 57)))
SEQUENCES = {"filter-hide": ((42, 9), (42, 43), (48,), (42, 9)),
             "filter-reset": ((42, 9),),
             "window-move-left": ((56, 65),) + ((105,),) * 6 + ((28,),),
             "down-ten": ((108,),) * 10,
             "up-ten": ((103,),) * 10}  # *|b*, *, and bounded list focus runs
EV_KEY = 1
EV_SYN = 0


def event(fd, code, value):
    os.write(fd, struct.pack("llHHi", 0, 0, EV_KEY, code, value))
    os.write(fd, struct.pack("llHHi", 0, 0, EV_SYN, 0, 0))


def send(chord, release_path=None):
    strokes = (SEQUENCES[chord] if chord in SEQUENCES else
               tuple((LETTERS[c],) for c in chord[5:]) if chord.startswith("type:") else (KEYS[chord],))
    fd = os.open("/dev/uinput", os.O_WRONLY | os.O_NONBLOCK)
    created = False
    held_modifier = None
    try:
        fcntl.ioctl(fd, 0x40045564, EV_KEY)  # UI_SET_EVBIT
        for code in set(code for keys in strokes for code in keys):
            fcntl.ioctl(fd, 0x40045565, code)  # UI_SET_KEYBIT
        # Linux uinput_user_dev: 80-byte name, input_id, ff_effects_max,
        # then four 64-entry absolute-axis arrays. No absolute axes are used.
        device = struct.pack("80sHHHHI256i", b"FileCat native acceptance keyboard",
                             3, 0x1234, 0x5678, 1, 0, *([0] * 256))
        os.write(fd, device)
        fcntl.ioctl(fd, 0x5501)  # UI_DEV_CREATE
        created = True
        time.sleep(0.6)  # Let the compositor register the new keyboard.
        if chord in ("shift+down-hold", "shift+down-hold-controlled",
                     "shift-hold-controlled", "ctrl-hold-controlled"):
            held_modifier = 29 if chord == "ctrl-hold-controlled" else 42
            event(fd, held_modifier, 1)
            if chord.startswith("shift+down"):
                event(fd, 108, 1)
                time.sleep(0.05)
                event(fd, 108, 0)
            print("HELD", flush=True)
            if chord.endswith("-controlled"):
                deadline = time.monotonic() + 30
                while not release_path.is_file():
                    if time.monotonic() >= deadline:
                        raise TimeoutError("The owned runner did not release Shift within 30 seconds")
                    time.sleep(0.05)
            else:
                time.sleep(8)
            return
        for keys in strokes:
            for code in keys:
                event(fd, code, 1)
                time.sleep(0.03)
            for code in reversed(keys):
                event(fd, code, 0)
                time.sleep(0.03)
            time.sleep(0.05)
    finally:
        if held_modifier is not None:
            event(fd, held_modifier, 0)
        if created:
            time.sleep(0.15)
            fcntl.ioctl(fd, 0x5502)  # UI_DEV_DESTROY
        os.close(fd)


if __name__ == "__main__":
    if (len(sys.argv) not in (2, 3) or
            sys.argv[1] not in KEYS and sys.argv[1] not in SEQUENCES and not (
                sys.argv[1].startswith("type:") and 0 < len(sys.argv[1]) - 5 <= 40 and set(sys.argv[1][5:]) <= set(LETTERS)) or
            (len(sys.argv) == 3) != sys.argv[1].endswith("-controlled")):
        raise SystemExit("Usage: linux-uinput-key.py CHORD [absolute owned release signal]")
    release = Path(sys.argv[2]) if len(sys.argv) == 3 else None
    if release is not None and (not release.is_absolute() or release.name not in
                                ("shift-release.signal", "ctrl-release.signal") or
                                release.exists()):
        raise SystemExit("The controlled hold requires a fresh absolute test release signal")
    send(sys.argv[1], release)
