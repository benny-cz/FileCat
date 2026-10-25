#!/usr/bin/env python3
"""Capture Text, Hex and Info at narrow and wide sizes on an isolated Mac desktop.

Usage: python3 mac-ax-viewer-mode-layout.py SOURCE FileCatCapture.app REPORT.json
The running signed FileCat app must show SOURCE in its left panel. Only this
runner's temporary file is created and removed.
"""

import hashlib
import json
from pathlib import Path
import runpy
import subprocess
import sys
import time


base = runpy.run_path(str(Path(__file__).with_name("mac-ax-markdown-resize.py")))
click_named = base["click_named"]
close_viewer = base["close_viewer"]
viewer_open = base["viewer_open"]
resize = base["resize"]
capture = base["capture"]
osa = base["osa"]
NAME = "native-viewer-mode-layout.txt"
CONTENT = ("FileCat viewer layout sample with a long but readable line that should remain usable in narrow and wide windows.\n" * 80).encode()


def viewer_title():
    return osa('''
tell application "System Events" to tell process "FileCat"
    repeat with w in windows
        if (name of w as text) contains "FileCat Viewer" then return name of w as text
    end repeat
    error "Viewer absent"
end tell
''')


def open_exact(name):
    click_named("AXButton", "Reread")
    time.sleep(0.5)
    # Newly added rows can be present in AX before they have a usable on-screen
    # hit target. Anchor the real list focus, then use FileCat's type-to-find.
    click_named("AXRow", "a.txt")
    osa(f'''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        keystroke {json.dumps(name)}
    end tell
end tell
''')
    # AXPress invokes Open, which can launch an external handler. View opens
    # FileCat's viewer for the exact focused item on every supported mode.
    click_named("AXButton", "View")
    for _ in range(25):
        if viewer_open():
            break
        time.sleep(0.2)
    else:
        raise AssertionError(f"View did not open {name}")
    if name not in viewer_title():
        raise AssertionError(f"View opened the wrong file: {viewer_title()}")


def run(source, helper):
    source = source.resolve(strict=True)
    helper = helper.resolve(strict=True)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    if helper != validation / "FileCatCapture.app" or not helper.is_dir():
        raise AssertionError("Use the approved validation capture helper")
    if source != validation / "source":
        raise AssertionError("Use only the isolated FileCatValidation source fixture")
    pids = subprocess.check_output(["/usr/bin/pgrep", "-x", "FileCat"], text=True).split()
    if len(pids) != 1:
        raise AssertionError("Exactly one validation FileCat process must be running")
    command = subprocess.check_output(["/bin/ps", "-p", pids[0], "-o", "command="], text=True).strip()
    executable = Path(command.split(" --new-instance", 1)[0]).resolve(strict=True)
    if validation not in executable.parents or executable.name != "FileCat" or executable.parent.name != "MacOS":
        raise AssertionError("The running FileCat is not a validation app")
    app_hash = hashlib.sha256((executable.parent / "FileCat.dll").read_bytes()).hexdigest()
    if (source / "a.txt").read_bytes() != b"alpha\n" or (source / "b.txt").read_bytes() != b"beta\n":
        raise AssertionError("The isolated source fixture differs")
    if viewer_open():
        raise AssertionError("Close the existing viewer first")
    fixture = source / NAME
    if fixture.exists():
        raise AssertionError("The test-owned viewer fixture already exists")
    fixture.write_bytes(CONTENT)
    digest = hashlib.sha256(CONTENT).digest()
    result = {"passed": False, "platform": "macOS", "appDllSha256": app_hash,
              "modes": {}}
    try:
        open_exact(NAME)
        for mode in ("Text", "Hex", "Info"):
            click_named("AXButton", mode, viewer=True)
            states = {}
            for label, dimensions in (("narrow", (720, 500)), ("wide", (1220, 700))):
                last_error = None
                for _ in range(5):
                    try:
                        actual_size = resize(*dimensions)
                        last_error = None
                        break
                    except AssertionError as error:
                        last_error = error
                        time.sleep(0.3)
                if last_error:
                    raise AssertionError(f"{mode} {label}: {last_error}; capture={capture(helper, validation)}") from last_error
                time.sleep(0.6)
                states[label] = {"size": actual_size, "capture": capture(helper, validation)}
            result["modes"][mode] = states
        if hashlib.sha256(fixture.read_bytes()).digest() != digest:
            raise AssertionError("Viewer layout checks changed their source bytes")
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
        if fixture.exists() and hashlib.sha256(fixture.read_bytes()).digest() == digest:
            fixture.unlink()
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
