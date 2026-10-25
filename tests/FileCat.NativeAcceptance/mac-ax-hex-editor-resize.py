#!/usr/bin/env python3
"""Capture a test-owned Mac Hex editor at narrow and wide native window sizes.

Usage: python3 mac-ax-hex-editor-resize.py SOURCE FileCatCapture.app REPORT.json
"""

import hashlib
import json
from pathlib import Path
import runpy
import subprocess
import sys
import time

directory = Path(__file__).parent
editor = runpy.run_path(str(directory / "mac-ax-hex-editor-backward.py"))
viewer = runpy.run_path(str(directory / "mac-ax-viewer-mode-layout.py"))
NAME = editor["FIXTURE_NAME"]
CONTENT = bytes(range(256)) * 32


def raise_editor():
    for _ in range(5):
        first = editor["osa"]('''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        repeat with w in windows
            if (name of w as text) contains "FileCat Hex Editor" then
                perform action "AXRaise" of w
                delay 0.2
                return name of window 1 as text
            end if
        end repeat
        error "Owned Hex editor absent"
    end tell
end tell
''')
        if "FileCat Hex Editor" in first:
            return
        time.sleep(0.2)
    raise AssertionError(f"Hex editor did not come to the foreground: {first}")


def run(source, helper):
    source = source.resolve(strict=True)
    helper = helper.resolve(strict=True)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    if source != validation / "source" or helper != validation / "FileCatCapture.app":
        raise AssertionError("Use only the isolated Mac validation source and approved capture helper")
    if (source / "a.txt").read_bytes() != b"alpha\n" or (source / "b.txt").read_bytes() != b"beta\n":
        raise AssertionError("The isolated source fixture differs")
    pids = subprocess.check_output(["/usr/bin/pgrep", "-x", "FileCat"], text=True).split()
    if len(pids) != 1 or editor["editor_open"]():
        raise AssertionError("Require one validation FileCat and no open Hex editor")
    command = subprocess.check_output(["/bin/ps", "-p", pids[0], "-o", "command="], text=True).strip()
    executable = Path(command.split(" --new-instance", 1)[0]).resolve(strict=True)
    if validation not in executable.parents or executable.name != "FileCat" or executable.parent.name != "MacOS":
        raise AssertionError("The running FileCat is not an isolated validation app")
    app_hash = hashlib.sha256((executable.parent / "FileCat.dll").read_bytes()).hexdigest()
    fixture = source / NAME
    if fixture.exists():
        raise AssertionError("The test-owned editor fixture already exists")
    fixture.write_bytes(CONTENT)
    digest = hashlib.sha256(CONTENT).digest()
    try:
        editor["ensure_source_panel"]()
        editor["find_click"]("AXButton", "Reread")
        editor["find_click"]("AXRow", NAME)
        editor["open_editor"]()
        states = {}
        for label, dimensions in (("narrow", (720, 500)), ("wide", (1200, 700))):
            actual = editor["editor_size"](*dimensions)
            drags = []
            if label == "narrow":
                for _ in range(3):
                    if actual[0] <= dimensions[0] + 4 and actual[1] <= dimensions[1] + 4:
                        break
                    drag = editor["pointer_resize"](*dimensions)
                    drags.append(drag)
                    actual = drag["after"][2:]
            if label == "narrow" and (actual[0] > 724 or actual[1] > 504):
                raise AssertionError(f"Hex editor did not fit its narrow frame: {actual}")
            if label == "wide" and (actual[0] < states["narrow"]["size"][0] + 200
                                    or actual[1] < states["narrow"]["size"][1] + 100):
                raise AssertionError(f"Hex editor did not expand from its narrow frame: {actual}")
            raise_editor()
            time.sleep(0.5)
            states[label] = {"size": actual, "nativeDrags": drags,
                             "capture": viewer["capture"](helper, validation)}
        if hashlib.sha256(fixture.read_bytes()).digest() != digest:
            raise AssertionError("Hex editor resize changed its source bytes")
        return {"passed": True, "platform": "macOS", "mode": "Hex editor",
                "appDllSha256": app_hash, "states": states, "sourceBytesUnchanged": True}
    finally:
        editor["close_editor"]()
        if fixture.exists() and hashlib.sha256(fixture.read_bytes()).digest() == digest:
            fixture.unlink()
            editor["find_click"]("AXButton", "Reread")


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    try:
        report = run(Path(sys.argv[1]), Path(sys.argv[2]))
    except Exception as error:
        report = {"passed": False, "error": f"{type(error).__name__}: {error}"}
    Path(sys.argv[3]).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
