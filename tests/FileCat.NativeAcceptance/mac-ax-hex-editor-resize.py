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
CONTENT = bytearray(bytes(range(256)) * 32)
CONTENT[3000:3006] = b"NEEDLE"
CONTENT[6000:6006] = b"NEEDLE"
CONTENT = bytes(CONTENT)


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


def visible_row_bytes(value):
    match = re.search(r"row [0-9A-F]{8}: ((?:[0-9A-F]{2}(?:[ .]|$))+)", value)
    if match is None:
        raise AssertionError(f"The visible Mac Hex row is not exposed: {value[:350]!r}")
    return len(re.findall(r"\b[0-9A-F]{2}\b", match.group(1)))


def selected_search_hit(offset):
    expected = f"Selected bytes {offset} through {offset + 5}, 6 bytes"
    value = ""
    for _ in range(12):
        value = hex_semantic()
        normalized = re.sub(r"(?<=\d)[\u00a0\u202f,](?=\d{3}\b)", "", value)
        if expected in normalized:
            return {"sourceOffset": offset, "selection": expected,
                    "viewportFirstByte": viewport_byte(value),
                    "visibleRowBytes": visible_row_bytes(value)}
        time.sleep(0.2)
    raise AssertionError(f"Mac Hex search did not select its exact bytes: {value[:500]!r}")


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


def scrollbar_drag_check(frame, helper, validation):
    x, y, width, height = frame
    x += width // 2
    before = hex_semantic()
    if viewport_byte(before) != 0 or "No byte selection" not in before or not before.startswith("Offset 0 of"):
        raise AssertionError(f"Mac Hex editor did not start unselected at byte zero: {before!r}")
    down_attempts = []
    for offset in (20, 26, 32):
        start_y = y + offset
        end_y = min(start_y + 120, y + height - 30)
        pointer_drag(x, start_y, x, end_y)
        down = hex_semantic()
        down_attempts.append({"start": start_y, "viewport": viewport_byte(down)})
        if "No byte selection" not in down or not down.startswith("Offset 0 of"):
            raise AssertionError(f"Mac thumb drag changed byte selection or cursor: {down!r}")
        if viewport_byte(down) > 0:
            break
    else:
        screenshot = viewer["capture"](helper, validation)
        pointer = subprocess.check_output(["/opt/homebrew/bin/cliclick", "p"], text=True).strip()
        raise AssertionError(f"Mac scrollbar drag failed to scroll without selecting: {frame!r}, "
                             f"attempts={down_attempts!r}, pointer={pointer!r}, "
                             f"screen={screenshot!r}, {down!r}")
    up_attempts = []
    for shift in (0, -6, 6, -12, 12):
        pointer_drag(x, end_y + shift, x, start_y + shift)
        up = hex_semantic()
        up_attempts.append({"start": end_y + shift, "viewport": viewport_byte(up)})
        if "No byte selection" not in up or not up.startswith("Offset 0 of"):
            raise AssertionError(f"Mac reverse thumb drag changed byte selection or cursor: {up!r}")
        if viewport_byte(up) == 0:
            break
    else:
        screenshot = viewer["capture"](helper, validation)
        raise AssertionError(f"Mac reverse thumb drag changed selection/cursor: {frame!r}, "
                             f"down={viewport_byte(down)}, up={viewport_byte(up)}, "
                             f"attempts={up_attempts!r}, screen={screenshot!r}, {up!r}")
    return {"thumbStart": [x, start_y], "thumbEnd": [x, end_y],
            "downViewportByte": viewport_byte(down), "upViewportByte": viewport_byte(up),
            "selectionUnchanged": True, "downAttempts": down_attempts,
            "upAttempts": up_attempts}


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
    attempts = []
    for offset in (20, 26, 32):
        thumb_start = bar_y + offset
        thumb_end = min(thumb_start + 120, bar_y + bar_height - 30)
        pointer_drag(thumb_x, thumb_start, thumb_x, thumb_end)
        preserved = hex_semantic()
        attempts.append({"start": thumb_start, "viewport": viewport_byte(preserved)})
        if "Selected bytes 0 through 3, 4 bytes" not in preserved:
            raise AssertionError(f"Mac thumb drag changed an explicit byte selection: {preserved!r}")
        if viewport_byte(preserved) > 0:
            break
    else:
        raise AssertionError(f"Mac thumb drag did not scroll its explicit selection: {attempts!r}")
    return {"dragStart": [start_x, row_y], "dragEnd": [end_x, row_y],
            "selectedBytes": [0, 3], "thumbPreservedViewportByte": viewport_byte(preserved),
            "thumbAttempts": attempts}


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
        states["wide"]["thumbDrag"] = scrollbar_drag_check(states["wide"]["scrollbar"],
                                                               helper, validation)
        states["wide"]["contentSelection"] = content_selection_check(
            states["wide"]["document"], states["wide"]["scrollbar"])
        # Keep the same editor open across scroll, search, resize and zoom. The
        # needle positions are far from the first displayed row, so a stale
        # viewport or a lost search selection cannot satisfy these checks.
        editor["enter_query"]("NEEDLE")
        search_status = editor["editor_status"]("Found at 0xBB8")
        first_hit = selected_search_hit(3000)
        narrow = editor["editor_size"](720, 500)
        for _ in range(3):
            if narrow[0] <= 724 and narrow[1] <= 504:
                break
            narrow = editor["pointer_resize"](720, 500)["after"][2:]
        # At native 1x the Cocoa window manager can stop a second shrink with
        # Find open at about 864x560. It is still a substantially narrower
        # usable window; the following row-capacity assertion tests reflow.
        if narrow[0] > 900 or narrow[1] > 600:
            raise AssertionError(f"Search selection did not fit the narrow editor: {narrow!r}")
        raise_editor()
        narrow_hit = selected_search_hit(3000)
        wide = editor["editor_size"](1200, 700)
        for _ in range(4):
            time.sleep(0.5)
            wide = editor["editor_frame"]()[2:]
            if wide[0] >= narrow[0] + 200 and wide[1] >= narrow[1] + 100:
                break
            wide = editor["editor_size"](1200, 700)
        if wide[0] < narrow[0] + 200 or wide[1] < narrow[1] + 100:
            raise AssertionError(f"Search selection did not fit the expanded editor: {wide!r}")
        raise_editor()
        wide_hit = selected_search_hit(3000)
        if wide_hit["visibleRowBytes"] <= narrow_hit["visibleRowBytes"]:
            raise AssertionError("The searched Hex selection did not reflow with the editor")
        zoom_steps = 0
        zoomed_hit = wide_hit
        try:
            while zoom_steps < 3 and zoomed_hit["visibleRowBytes"] >= wide_hit["visibleRowBytes"]:
                editor["editor_click"]("AXButton", "Increase editor content size")
                zoom_steps += 1
                time.sleep(0.3)
                zoomed_hit = selected_search_hit(3000)
            if zoomed_hit["visibleRowBytes"] >= wide_hit["visibleRowBytes"]:
                raise AssertionError("Mac Hex content size did not reduce the fitted row")
        finally:
            for _ in range(zoom_steps):
                editor["editor_click"]("AXButton", "Decrease editor content size")
        restored_hit = selected_search_hit(3000)
        if restored_hit["visibleRowBytes"] != wide_hit["visibleRowBytes"]:
            raise AssertionError("Mac Hex content size did not restore the original row width")
        states["combinedSearchResizeZoom"] = {
            "query": "NEEDLE", "status": search_status, "first": first_hit,
            "narrowSize": narrow, "narrow": narrow_hit,
            "wideSize": wide, "wide": wide_hit,
            "zoomSteps": zoom_steps, "zoomed": zoomed_hit,
            "restored": restored_hit}
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
