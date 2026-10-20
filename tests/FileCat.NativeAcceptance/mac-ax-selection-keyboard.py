#!/usr/bin/env python3
"""Check native macOS pointer anchoring and Shift range keys in FileCat.

Usage: python3 mac-ax-selection-keyboard.py SOURCE REPORT.json
Run as the unlocked desktop user with Accessibility access for osascript.
The source contains a.txt = b"alpha\n" and b.txt = b"beta\n".
"""

import json
from pathlib import Path
import subprocess
import sys
import time


CLICK_FIRST = '''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        set targetRow to missing value
        set elementsList to entire contents of window 1
        repeat with e in elementsList
            try
                if (role of e as text) is "AXRow" and (name of e as text) is "a.txt" then
                    set targetRow to e
                    exit repeat
                end if
            end try
        end repeat
        if targetRow is missing value then error "a.txt row absent"
        click targetRow
        if not (focused of targetRow) then error "a.txt did not receive pointer focus"
        return "clicked a.txt"
    end tell
end tell
'''

SHIFT_DOWN = '''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        key code 125 using {shift down}
    end tell
end tell
'''

UP = '''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        key code 126
    end tell
end tell
'''

SNAPSHOT = '''
tell application "System Events"
    tell process "FileCat"
        set summaryText to ""
        set focusedName to ""
        set elementsList to entire contents of window 1
        repeat with e in elementsList
            try
                set elementRole to role of e as text
                set elementName to name of e as text
                if elementRole is "AXStaticText" and (elementName starts with "Marked " or elementName is "0 folders, 2 files · 11 bytes") then
                    set summaryText to elementName
                end if
                if elementRole is "AXStaticText" and elementName is "a.txt · 6 bytes" then
                    set focusedName to "a.txt"
                end if
                if elementRole is "AXStaticText" and elementName is "b.txt · 5 bytes" then
                    set focusedName to "b.txt"
                end if
            end try
        end repeat
        return summaryText & "|" & focusedName
    end tell
end tell
'''


def osa(script):
    completed = subprocess.run(
        ["/usr/bin/osascript", "-e", script],
        text=True, capture_output=True, timeout=45, check=False,
    )
    if completed.returncode:
        raise AssertionError(completed.stderr.strip() or "osascript failed")
    return completed.stdout.strip()


def snapshot(expected_summary, expected_focus):
    result = osa(SNAPSHOT)
    if result != f"{expected_summary}|{expected_focus}":
        raise AssertionError(f"Expected {expected_summary!r}, focused {expected_focus!r}; got {result!r}")


def run(source):
    source = source.resolve(strict=True)
    assert (source / "a.txt").read_bytes() == b"alpha\n"
    assert (source / "b.txt").read_bytes() == b"beta\n"
    clear = "0 folders, 2 files · 11 bytes"
    marked = "Marked 1 of 2 files · 6 bytes"
    initial = osa(SNAPSHOT)
    if not initial.startswith(clear + "|"):
        raise AssertionError(f"Start with no marked files: {initial!r}")

    osa(CLICK_FIRST)
    time.sleep(0.2)
    snapshot(clear, "a.txt")
    osa(SHIFT_DOWN)
    snapshot(marked, "b.txt")
    osa(UP)
    snapshot(marked, "a.txt")
    osa(SHIFT_DOWN)
    snapshot(clear, "b.txt")

    assert (source / "a.txt").read_bytes() == b"alpha\n"
    assert (source / "b.txt").read_bytes() == b"beta\n"
    return {
        "passed": True,
        "platform": "macOS",
        "pointerAnchor": "a.txt",
        "firstShiftMarked": "a.txt",
        "ordinaryUpPreservedMark": True,
        "secondShiftClearedMark": True,
        "finalFocus": "b.txt",
        "sourceBytesUnchanged": True,
    }


def main():
    if len(sys.argv) != 3:
        raise SystemExit("Usage: mac-ax-selection-keyboard.py SOURCE REPORT.json")
    report = Path(sys.argv[2])
    try:
        result = run(Path(sys.argv[1]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
