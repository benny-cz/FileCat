#!/usr/bin/env python3
"""Verify a completed Mac Copy shows its average in Operations details.

Usage: python3 mac-ax-finished-speed.py SOURCE DESTINATION REPORT.json

The signed validation app must show the exact isolated source and destination.
The source contains only a.txt and b.txt; the destination contains only a.txt.
This test copies b.txt and removes only its own resulting destination b.txt.
"""

import json
from pathlib import Path
import runpy
import subprocess
import sys
import time


osa = runpy.run_path(str(Path(__file__).with_name("mac-ax-markdown-find.py")))["osa"]
REVIEW = runpy.run_path(str(Path(__file__).with_name("mac-ax-copy-review-resize.py")))


def wait_for(probe, description, seconds=20):
    deadline = time.monotonic() + seconds
    last = None
    while time.monotonic() < deadline:
        last = probe()
        if last:
            return last
        time.sleep(0.2)
    raise AssertionError(f"Timed out waiting for {description}; last={last!r}")


def click_button(name):
    script = '''
on clickNamed(itemRef, soughtName, depth)
    if depth > 25 then return false
    tell application "System Events"
        try
            if (role of itemRef as text) is "AXButton" and (name of itemRef as text) is soughtName then
                click itemRef
                return true
            end if
            repeat with childRef in (UI elements of itemRef)
                if my clickNamed(childRef, soughtName, depth + 1) then return true
            end repeat
        end try
    end tell
    return false
end clickNamed
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        repeat with w in windows
            if (name of w as text) contains "FileCat" and (name of w as text) does not contain "Viewer" then
                if my clickNamed(w, SOUGHT, 0) then return "clicked"
            end if
        end repeat
        error "Missing button " & SOUGHT
    end tell
end tell
'''.replace("SOUGHT", json.dumps(name, ensure_ascii=False))
    return osa(script)


def visible_average():
    return osa('''
on findRate(itemRef, depth)
    if depth > 25 then return ""
    tell application "System Events"
        try
            if (role of itemRef as text) is "AXStaticText" then
                set labelText to name of itemRef as text
                if labelText starts with "Average transfer speed: " then return labelText
            end if
            repeat with childRef in (UI elements of itemRef)
                set rateText to my findRate(childRef, depth + 1)
                if rateText is not "" then return rateText
            end repeat
        end try
    end tell
    return ""
end findRate
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat" and (name of w as text) does not contain "Viewer" then
                return my findRate(w, 0)
            end if
        end repeat
    end tell
end tell
return ""
''')


def main_window_size():
    result = osa('''
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat" and (name of w as text) does not contain "Viewer" then
                return size of w
            end if
        end repeat
    end tell
end tell
''')
    values = [int(part.strip()) for part in result.split(",")]
    if len(values) != 2:
        raise AssertionError(f"Unexpected main window size: {result!r}")
    return values


def set_main_window_size(size):
    osa('''
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat" and (name of w as text) does not contain "Viewer" then
                set size of w to {WIDTH, HEIGHT}
                return "resized"
            end if
        end repeat
    end tell
end tell
'''.replace("WIDTH", str(size[0])).replace("HEIGHT", str(size[1])))


def run(source, destination):
    source = source.resolve(strict=True)
    destination = destination.resolve(strict=True)
    root = Path("/Users/benny/FileCatValidation")
    if source != root / "source" or destination != root / "destination":
        raise AssertionError("The Mac test must use only the isolated validation panels")
    expected_source = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    expected_destination = {"a.txt": b"alpha\n"}
    manifest = lambda folder: {item.name: item.read_bytes() for item in folder.iterdir()}
    if manifest(source) != expected_source or manifest(destination) != expected_destination:
        raise AssertionError("The validation panels are not at their exact starting state")
    process_lines = subprocess.check_output(["/bin/ps", "-axo", "command="], text=True)
    matches = [line for line in process_lines.splitlines()
               if "/FileCatValidation/FileCatValidation.app/Contents/MacOS/FileCat " in line
               and f"--left {source} --right {destination}" in line]
    if len(matches) != 1:
        raise AssertionError("Exactly one signed validation app must own these panels")
    marked = osa('''
tell application "System Events"
    tell process "FileCat"
        repeat with e in (entire contents of window 1)
            try
                if (role of e as text) is "AXStaticText" and (name of e as text) starts with "Marked " then return "marked"
            end try
        end repeat
    end tell
end tell
return "clear"
''')
    if marked != "clear":
        raise AssertionError("The isolated source still has marks; clear them before this one-file Copy")

    target = destination / "b.txt"
    original_size = None
    copied = False
    review_open = False
    try:
        REVIEW["open_review"]()
        review_open = True
        REVIEW["review_state"](destination, 'Copy "b.txt" (5 bytes)')
        osa('''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        key code 36
        return "approved by Enter"
    end tell
end tell
''')
        wait_for(lambda: target.exists() and target.read_bytes() == b"beta\n",
                 "the exact completed Copy")
        review_open = False
        copied = True
        if not visible_average():
            click_button("Details (Ctrl+J)")
        average = wait_for(visible_average, "the finished average in Operations details")
        if average == "Average transfer speed: not applicable":
            raise AssertionError("A completed data Copy showed no numeric average")
        original_size = main_window_size()
        set_main_window_size([760, 600])
        narrow_average = wait_for(visible_average, "the average after narrowing the window")
        if narrow_average != average:
            raise AssertionError("Narrowing the window changed the finished average")
        if manifest(source) != expected_source or manifest(destination) != {
                **expected_destination, "b.txt": b"beta\n"}:
            raise AssertionError("The Copy changed unselected or unexpected file bytes")
        return {"passed": True, "platform": "macOS", "operation": "Copy",
                "finishedDetailsAverage": average, "narrowAverage": narrow_average,
                "narrowWindowSize": main_window_size(), "sourceAndTargetBytesExact": True}
    finally:
        cleanup_issues = []
        if review_open:
            try:
                REVIEW["cancel_review"]()
            except Exception as exc:
                cleanup_issues.append(f"review cancellation: {exc}")
        if original_size is not None:
            try:
                set_main_window_size(original_size)
                if main_window_size() != original_size:
                    cleanup_issues.append("The Mac main window did not return to its original size")
            except Exception as exc:
                cleanup_issues.append(f"window size restoration: {exc}")
        if copied:
            try:
                if target.exists() and target.read_bytes() == b"beta\n":
                    target.unlink()
            except Exception as exc:
                cleanup_issues.append(f"test Copy cleanup: {exc}")
        if copied and (manifest(source) != expected_source or
                       manifest(destination) != expected_destination):
            cleanup_issues.append("The Mac finished-speed fixture was not restored exactly")
        if cleanup_issues:
            raise AssertionError("; ".join(cleanup_issues))


def main():
    if len(sys.argv) != 4:
        raise SystemExit("Usage: mac-ax-finished-speed.py SOURCE DESTINATION REPORT.json")
    try:
        result = run(Path(sys.argv[1]), Path(sys.argv[2]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[3]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
