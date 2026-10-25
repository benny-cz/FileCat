#!/usr/bin/env python3
"""Capture a test-owned Mac Hex editor at narrow and wide native window sizes.

Usage: python3 mac-ax-hex-editor-resize.py SOURCE FileCatCapture.app REPORT.json
"""

import hashlib
import json
from pathlib import Path
import re
import runpy
import subprocess
import sys
import time

directory = Path(__file__).parent
editor = runpy.run_path(str(directory / "mac-ax-hex-editor-backward.py"))
viewer = runpy.run_path(str(directory / "mac-ax-viewer-mode-layout.py"))
NAME = editor["FIXTURE_NAME"]
CONTENT = bytes(range(256)) * 32


def hex_semantic():
    return editor["osa"]('''
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
            if (name of w as text) contains "FileCat Hex Editor" then
                return my hexValue(w, 0)
            end if
        end repeat
    end tell
end tell
error "Owned Hex editor absent"
''')


def hex_row():
    value = hex_semantic()
    match = re.search(r"row 00000000: ((?:[0-9A-F]{2}(?:[ .]|$))+)", value)
    if match is None:
        raise AssertionError(f"The Mac Hex row is not exposed: {value[:350]!r}")
    row = re.findall(r"\b[0-9A-F]{2}\b", match.group(1))
    if row != [f"{byte:02X}" for byte in CONTENT[:len(row)]]:
        raise AssertionError(f"Mac Hex row bytes differ: {row!r}")
    return {"bytesPerRow": len(row), "firstRow": row}


def scrollbar_geometry():
    raw = editor["osa"]('''
on barFrame(itemRef, depth)
    if depth > 25 then return ""
    tell application "System Events"
        set itemName to ""
        try
            set itemName to name of itemRef as text
        end try
        if itemName is "Hex vertical position" then
            set p to position of itemRef
            set s to size of itemRef
            return (item 1 of p as text) & "|" & (item 2 of p as text) & "|" & ¬
                   (item 1 of s as text) & "|" & (item 2 of s as text)
        end if
        try
            repeat with childRef in (UI elements of itemRef)
                set found to my barFrame(childRef, depth + 1)
                if found is not "" then return found
            end repeat
        end try
    end tell
    return ""
end barFrame
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Hex Editor" then
                return my barFrame(w, 0)
            end if
        end repeat
    end tell
end tell
error "Owned Hex editor absent"
''')
    values = [int(value) for value in raw.split("|")]
    if len(values) != 4 or values[2] < 8 or values[3] < 150:
        raise AssertionError(f"Implausible Mac Hex vertical scrollbar: {raw!r}")
    return values


def document_geometry():
    raw = editor["osa"]('''
on documentFrame(itemRef, depth)
    if depth > 25 then return ""
    tell application "System Events"
        set itemName to ""
        try
            set itemName to name of itemRef as text
        end try
        if itemName contains "viewport begins at byte" then
            set p to position of itemRef
            set s to size of itemRef
            return (item 1 of p as text) & "|" & (item 2 of p as text) & "|" & ¬
                   (item 1 of s as text) & "|" & (item 2 of s as text)
        end if
        try
            repeat with childRef in (UI elements of itemRef)
                set found to my documentFrame(childRef, depth + 1)
                if found is not "" then return found
            end repeat
        end try
    end tell
    return ""
end documentFrame
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Hex Editor" then
                return my documentFrame(w, 0)
            end if
        end repeat
    end tell
end tell
error "Owned Hex editor absent"
''')
    values = [int(value) for value in raw.split("|")]
    if len(values) != 4:
        raise AssertionError(f"Mac Hex document has no native frame: {raw!r}")
    return values


def viewport_byte(value):
    match = re.search(r"viewport begins at byte ([0-9][0-9,\s\u00a0\u202f]*)", value)
    if match is None:
        raise AssertionError(f"Mac Hex viewport is absent: {value[:350]!r}")
    return int(re.sub(r"\D", "", match.group(1)))


def pointer_drag(start_x, start_y, end_x, end_y):
    done = subprocess.run(["/opt/homebrew/bin/cliclick", "-r", "-e", "4", "-w", "100",
                           f"dd:{start_x},{start_y}", f"dm:{end_x},{end_y}",
                           f"du:{end_x},{end_y}"], capture_output=True,
                          text=True, timeout=15)
    if done.returncode:
        raise AssertionError(done.stderr.strip() or "Native Mac Hex pointer drag failed")
    time.sleep(0.3)


def scrollbar_drag_check(frame):
    x, y, width, height = frame
    start_y = y + 30
    end_y = min(start_y + 120, y + height - 30)
    x += width // 2
    before = hex_semantic()
    if viewport_byte(before) != 0 or "No byte selection" not in before or not before.startswith("Offset 0 of"):
        raise AssertionError(f"Mac Hex editor did not start unselected at byte zero: {before!r}")
    pointer_drag(x, start_y, x, end_y)
    down = hex_semantic()
    if viewport_byte(down) <= 0 or "No byte selection" not in down or not down.startswith("Offset 0 of"):
        raise AssertionError(f"Mac scrollbar drag failed to scroll without selecting: {frame!r}, {down!r}")
    pointer_drag(x, end_y, x, start_y)
    up = hex_semantic()
    if viewport_byte(up) != 0 or "No byte selection" not in up or not up.startswith("Offset 0 of"):
        raise AssertionError(f"Mac reverse thumb drag changed selection/cursor: {frame!r}, {up!r}")
    return {"thumbStart": [x, start_y], "thumbEnd": [x, end_y],
            "downViewportByte": viewport_byte(down), "upViewportByte": viewport_byte(up),
            "selectionUnchanged": True}


def content_selection_check(document, scrollbar):
    doc_x, doc_y, doc_width, doc_height = document
    if doc_width < 600 or doc_height < 300:
        raise AssertionError(f"Mac Hex document has implausible pointer bounds: {document!r}")
    start_x, row_y = doc_x + 94, doc_y + 10
    end_x = start_x + 84
    if end_x >= scrollbar[0]:
        raise AssertionError("Mac Hex content drag would intersect the scrollbar")
    pointer_drag(start_x, row_y, end_x, row_y)
    selected = hex_semantic()
    if "Selected bytes 0 through 3, 4 bytes" not in selected:
        raise AssertionError(f"Deliberate Mac Hex content drag did not select four bytes: {selected!r}")
    bar_x, bar_y, bar_width, bar_height = scrollbar
    thumb_x = bar_x + bar_width // 2
    thumb_start = bar_y + 30
    thumb_end = min(thumb_start + 120, bar_y + bar_height - 30)
    pointer_drag(thumb_x, thumb_start, thumb_x, thumb_end)
    preserved = hex_semantic()
    if viewport_byte(preserved) <= 0 or "Selected bytes 0 through 3, 4 bytes" not in preserved:
        raise AssertionError(f"Mac thumb drag changed an explicit byte selection: {preserved!r}")
    return {"dragStart": [start_x, row_y], "dragEnd": [end_x, row_y],
            "selectedBytes": [0, 3], "thumbPreservedViewportByte": viewport_byte(preserved)}


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
            if label == "wide":
                # Cocoa and the window manager can acknowledge an AX size before
                # the layout pass has applied it. Re-read the native frame, then
                # retry the requested size only if it is still too small.
                for _ in range(4):
                    time.sleep(0.5)
                    actual = editor["editor_frame"]()[2:]
                    if (actual[0] >= states["narrow"]["size"][0] + 200
                            and actual[1] >= states["narrow"]["size"][1] + 100):
                        break
                    actual = editor["editor_size"](*dimensions)
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
            states[label] = {"size": actual, "row": hex_row(), "nativeDrags": drags,
                             "capture": viewer["capture"](helper, validation)}
            if label == "wide":
                states[label]["scrollbar"] = scrollbar_geometry()
                states[label]["document"] = document_geometry()
        if states["wide"]["row"]["bytesPerRow"] <= states["narrow"]["row"]["bytesPerRow"]:
            raise AssertionError(f"Hex editor did not fit more complete bytes after widening: {states!r}")
        states["wide"]["thumbDrag"] = scrollbar_drag_check(states["wide"]["scrollbar"])
        states["wide"]["contentSelection"] = content_selection_check(
            states["wide"]["document"], states["wide"]["scrollbar"])
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
