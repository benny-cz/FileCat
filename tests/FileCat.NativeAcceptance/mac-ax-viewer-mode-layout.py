#!/usr/bin/env python3
"""Capture Text, Hex and Info at narrow and wide sizes on an isolated Mac desktop.

Usage: python3 mac-ax-viewer-mode-layout.py SOURCE FileCatCapture.app REPORT.json
The running signed FileCat app must show SOURCE in its left panel. Only this
runner's temporary file is created and removed.
"""

import hashlib
import json
from pathlib import Path
import re
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


def hex_row():
    value = hex_semantic()
    match = re.search(r"row 00000000: ((?:[0-9A-F]{2}(?:[ .]|$))+)", value)
    if match is None:
        raise AssertionError(f"The Mac viewer Hex row is not exposed: {value[:350]!r}")
    row = re.findall(r"\b[0-9A-F]{2}\b", match.group(1))
    if row != [f"{byte:02X}" for byte in CONTENT[:len(row)]]:
        raise AssertionError(f"Mac viewer Hex row bytes differ: {row!r}")
    return {"bytesPerRow": len(row), "firstRow": row}


def hex_semantic():
    return osa('''
on hexValue(itemRef, depth)
    if depth > 25 then return ""
    tell application "System Events"
        set itemName to ""
        try
            set itemName to name of itemRef as text
        end try
        if itemName contains "viewport begins at byte" then return itemName
        try
            repeat with childRef in (UI elements of itemRef)
                set found to my hexValue(childRef, depth + 1)
                if found is not "" then return found
            end repeat
        end try
    end tell
    return ""
end hexValue
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Viewer" then
                return my hexValue(w, 0)
            end if
        end repeat
    end tell
end tell
error "Owned viewer absent"
''')


def hex_element_frame(needle):
    raw = osa(f'''
on elementFrame(itemRef, depth)
    if depth > 25 then return ""
    tell application "System Events"
        set itemName to ""
        try
            set itemName to name of itemRef as text
        end try
        if itemName contains {json.dumps(needle)} then
            set p to position of itemRef
            set s to size of itemRef
            return (item 1 of p as text) & "|" & (item 2 of p as text) & "|" & ¬
                   (item 1 of s as text) & "|" & (item 2 of s as text)
        end if
        try
            repeat with childRef in (UI elements of itemRef)
                set found to my elementFrame(childRef, depth + 1)
                if found is not "" then return found
            end repeat
        end try
    end tell
    return ""
end elementFrame
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Viewer" then
                return my elementFrame(w, 0)
            end if
        end repeat
    end tell
end tell
error "Owned viewer absent"
''')
    frame = [int(value) for value in raw.split("|")]
    if len(frame) != 4:
        raise AssertionError(f"Mac viewer Hex element has no frame: {needle}: {raw!r}")
    return frame


def hex_viewport(value):
    match = re.search(r"viewport begins at byte ([0-9][0-9,\s\u00a0\u202f]*)", value)
    if match is None:
        raise AssertionError(f"Mac viewer Hex viewport is unavailable: {value[:350]!r}")
    return int(re.sub(r"\D", "", match.group(1)))


def hex_drag(start_x, start_y, end_x, end_y):
    done = subprocess.run(["/opt/homebrew/bin/cliclick", "-r", "-e", "4", "-w", "100",
                           f"dd:{start_x},{start_y}", f"dm:{end_x},{end_y}",
                           f"du:{end_x},{end_y}"], capture_output=True,
                          text=True, timeout=15)
    if done.returncode:
        raise AssertionError(done.stderr.strip() or "Native Mac viewer Hex drag failed")
    time.sleep(0.3)


def hex_pointer_check():
    bar = hex_element_frame("Hex vertical position")
    document = hex_element_frame("viewport begins at byte")
    bx, by, bw, bh = bar
    dx, dy, dw, dh = document
    if bw < 8 or bh < 150 or dw < 600 or dh < 300:
        raise AssertionError(f"Mac viewer Hex pointer bounds are unusable: {bar!r}, {document!r}")
    tx, ty, end_y = bx + bw // 2, by + 30, min(by + 150, by + bh - 30)
    before = hex_semantic()
    if hex_viewport(before) != 0 or "No byte selection" not in before or not before.startswith("Offset 0 of"):
        raise AssertionError(f"Mac viewer Hex did not start at byte zero: {before!r}")
    hex_drag(tx, ty, tx, end_y)
    down = hex_semantic()
    if hex_viewport(down) <= 0 or "No byte selection" not in down or not down.startswith("Offset 0 of"):
        raise AssertionError(f"Mac viewer thumb drag selected bytes or did not scroll: {down!r}")
    hex_drag(tx, end_y, tx, ty)
    up = hex_semantic()
    if hex_viewport(up) != 0 or "No byte selection" not in up or not up.startswith("Offset 0 of"):
        raise AssertionError(f"Mac viewer reverse thumb drag changed selection/cursor: {up!r}")
    start_x, row_y, end_x = dx + 94, dy + 10, dx + 178
    if end_x >= bx:
        raise AssertionError("Mac viewer Hex content drag would intersect the scrollbar")
    hex_drag(start_x, row_y, end_x, row_y)
    selected = hex_semantic()
    if "Selected bytes 0 through 3, 4 bytes" not in selected:
        raise AssertionError(f"Deliberate Mac viewer Hex drag selected the wrong bytes: {selected!r}")
    hex_drag(tx, ty, tx, end_y)
    preserved = hex_semantic()
    if hex_viewport(preserved) <= 0 or "Selected bytes 0 through 3, 4 bytes" not in preserved:
        raise AssertionError(f"Mac viewer thumb drag changed the explicit selection: {preserved!r}")
    return {"scrollbar": bar, "document": document,
            "downViewportByte": hex_viewport(down), "upViewportByte": hex_viewport(up),
            "selectedBytes": [0, 3], "selectionPreservedViewportByte": hex_viewport(preserved)}


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
        keystroke "nativev"
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
                if mode == "Hex":
                    states[label]["row"] = hex_row()
            if mode == "Hex" and states["wide"]["row"]["bytesPerRow"] <= states["narrow"]["row"]["bytesPerRow"]:
                raise AssertionError(f"Viewer Hex row did not expand with the window: {states!r}")
            if mode == "Hex":
                states["wide"]["pointer"] = hex_pointer_check()
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
                osa('tell application "System Events" to tell process "FileCat" to key code 53')
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
