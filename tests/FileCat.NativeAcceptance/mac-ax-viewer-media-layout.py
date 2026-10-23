#!/usr/bin/env python3
"""Capture native Mac Picture and structured Info viewer layouts.

Usage: python3 mac-ax-viewer-media-layout.py SOURCE FileCatCapture.app REPORT.json
Run with one signed validation FileCat process showing the isolated source.
"""

import hashlib
import json
from pathlib import Path
import runpy
import struct
import subprocess
import sys
import time
import zlib


base = runpy.run_path(str(Path(__file__).with_name("mac-ax-viewer-mode-layout.py")))
click_named = base["click_named"]
viewer_open = base["viewer_open"]
close_viewer = base["close_viewer"]
open_exact = base["open_exact"]
resize = base["resize"]
capture = base["capture"]
PICTURE_NAME = "native-picture-layout.png"
STRUCTURE_NAME = "native-structure-layout.bin"


def picture_bytes():
    width, height = 640, 480
    scanlines = bytearray()
    for y in range(height):
        scanlines.append(0)
        for x in range(width):
            scanlines.extend((x * 255 // (width - 1), y * 255 // (height - 1),
                              240 if (x // 40 + y // 40) % 2 else 40))

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(scanlines)) + chunk(b"IEND", b""))


def capture_mode(mode, helper, validation):
    click_named("AXButton", mode, viewer=True)
    states = {}
    for label, dimensions in (("narrow", (720, 500)), ("wide", (1220, 700))):
        last_error = None
        for _ in range(5):
            try:
                size = resize(*dimensions)
                last_error = None
                break
            except AssertionError as error:
                last_error = error
                time.sleep(0.3)
        if last_error:
            raise AssertionError(f"{mode} {label}: {last_error}; capture={capture(helper, validation)}") from last_error
        time.sleep(0.8)
        states[label] = {"size": size, "capture": capture(helper, validation)}
    return states


def run(source, helper):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    source = source.resolve(strict=True)
    helper = helper.resolve(strict=True)
    if source != validation / "source" or helper != validation / "FileCatCapture.app":
        raise AssertionError("Use only the isolated source and approved capture helper")
    if (source / "a.txt").read_bytes() != b"alpha\n" or (source / "b.txt").read_bytes() != b"beta\n":
        raise AssertionError("The isolated source fixture differs")
    pids = subprocess.check_output(["/usr/bin/pgrep", "-x", "FileCat"], text=True).split()
    if len(pids) != 1:
        raise AssertionError("Exactly one validation FileCat process must be running")
    command = subprocess.check_output(["/bin/ps", "-p", pids[0], "-o", "command="], text=True).strip()
    executable = Path(command.split(" --new-instance", 1)[0]).resolve(strict=True)
    if validation not in executable.parents or executable.name != "FileCat" or executable.parent.name != "MacOS":
        raise AssertionError("The running FileCat is not a validation app")
    if viewer_open():
        raise AssertionError("Close the existing viewer first")
    image = source / PICTURE_NAME
    structure = source / STRUCTURE_NAME
    if image.exists() or structure.exists():
        raise AssertionError("A test-owned media fixture already exists")
    contents = {image: picture_bytes(), structure: executable.read_bytes()}
    digests = {path: hashlib.sha256(data).digest() for path, data in contents.items()}
    result = {"passed": False, "platform": "macOS", "appDllSha256":
              hashlib.sha256((executable.parent / "FileCat.dll").read_bytes()).hexdigest(), "modes": {}}
    try:
        for path, data in contents.items():
            path.write_bytes(data)
        open_exact(PICTURE_NAME)
        result["modes"]["Picture"] = capture_mode("Picture", helper, validation)
        close_viewer()
        open_exact(STRUCTURE_NAME)
        result["modes"]["Structured Info"] = capture_mode("Info", helper, validation)
        close_viewer()
        if any(hashlib.sha256(path.read_bytes()).digest() != digest for path, digest in digests.items()):
            raise AssertionError("A media viewer changed its source bytes")
        result["sourceBytesUnchanged"] = True
        result["passed"] = True
        return result
    finally:
        for _ in range(3):
            try:
                if not viewer_open():
                    break
                close_viewer()
            except AssertionError:
                break
        for path, digest in digests.items():
            if path.exists() and hashlib.sha256(path.read_bytes()).digest() == digest:
                path.unlink()
        try:
            click_named("AXButton", "Reread")
        except AssertionError:
            pass


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    try:
        report = run(Path(sys.argv[1]), Path(sys.argv[2]))
    except Exception as exc:
        report = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[3]).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
