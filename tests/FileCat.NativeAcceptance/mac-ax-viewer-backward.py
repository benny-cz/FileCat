#!/usr/bin/env python3
"""Exercise distant Previous/Next matches in a native macOS FileCat viewer.

Usage: python3 mac-ax-viewer-backward.py SOURCE REPORT.json
Run in an unlocked macOS desktop session with Accessibility access for osascript.
SOURCE is an isolated FileCat panel containing a.txt = b"alpha\n" and
b.txt = b"beta\n". The test creates and removes only its own search fixture.
"""

import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys


FIXTURE_NAME = "native-backward-search.txt"
FILLER = b"x" * 70 + b"\n"
CONTENT = FILLER * 15000 + b"NEEDLE first\n" + FILLER * 60000 + b"NEEDLE second\n" + FILLER * 1000
FIRST_OFFSET = CONTENT.find(b"NEEDLE")
SECOND_OFFSET = CONTENT.find(b"NEEDLE", FIRST_OFFSET + 1)
assert SECOND_OFFSET - FIRST_OFFSET > 4 * 1024 * 1024


def osa(script):
    result = subprocess.run(
        ["/usr/bin/osascript", "-e", script],
        text=True, capture_output=True, timeout=60, check=False,
    )
    if result.returncode:
        raise AssertionError(result.stderr.strip() or "osascript failed")
    return result.stdout.strip()


def find_click(role, name, viewer=False):
    target = "true" if viewer else "false"
    return osa(f'''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        repeat with w in windows
            set isViewer to (name of w as text) contains "FileCat Viewer"
            if isViewer is {target} then
                set allElements to entire contents of w
                repeat with e in allElements
                    try
                        if (role of e as text) is {json.dumps(role)} and (name of e as text) is {json.dumps(name)} then
                            click e
                            return "clicked"
                        end if
                    end try
                end repeat
            end if
        end repeat
        error "Missing {role}: {name}"
    end tell
end tell
''')


def viewer_open():
    return osa('''
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Viewer" then return "yes"
        end repeat
    end tell
end tell
return "no"
''') == "yes"


def close_viewer():
    if not viewer_open():
        return
    osa('''
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Viewer" then
                repeat with e in UI elements of w
                    try
                        if (description of e as text) is "close button" then
                            click e
                            return "closed"
                        end if
                    end try
                end repeat
            end if
        end repeat
        error "Viewer close button absent"
    end tell
end tell
''')


def enter_query(query):
    osa(f'''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        repeat with w in windows
            if (name of w as text) contains "FileCat Viewer" then
                set allElements to entire contents of w
                repeat with e in allElements
                    try
                        if (role of e as text) is "AXTextField" and (name of e as text) is "Find in file" then
                            click e
                            keystroke "a" using {{command down}}
                            key code 51
                            keystroke {json.dumps(query)}
                            if (value of e as text) is not {json.dumps(query)} then error "Query input differed"
                            return "typed"
                        end if
                    end try
                end repeat
            end if
        end repeat
        error "Find field absent"
    end tell
end tell
''')


def snapshot():
    result = osa('''
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Viewer" then
                set offsetText to ""
                set countText to ""
                set searchResult to ""
                set allElements to entire contents of w
                repeat with e in allElements
                    try
                        if (role of e as text) is "AXStaticText" then
                            set labelText to name of e as text
                            if labelText contains " · offset 0x" then set offsetText to labelText
                            if labelText is "1 match" or labelText is "2 matches" then set countText to labelText
                            if labelText contains "was not found before this position" then set searchResult to labelText
                        end if
                    end try
                end repeat
                return offsetText & "|" & countText & "|" & searchResult
            end if
        end repeat
        error "Viewer absent during snapshot"
    end tell
end tell
''')
    parts = result.split("|", 2)
    if len(parts) != 3:
        raise AssertionError(f"Malformed viewer snapshot: {result!r}")
    offset = re.search(r"offset 0x([0-9A-Fa-f]+)", parts[0])
    return {"offset": int(offset.group(1), 16) if offset else None,
            "count": parts[1], "searchResult": parts[2], "status": parts[0]}


def send_f3(backward):
    modifier = " using {shift down}" if backward else ""
    osa(f'''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        key code 99{modifier}
    end tell
end tell
''')


def run(source):
    source = source.resolve(strict=True)
    assert (source / "a.txt").read_bytes() == b"alpha\n"
    assert (source / "b.txt").read_bytes() == b"beta\n"
    if viewer_open():
        raise AssertionError("Close an existing viewer before this test")
    fixture = source / FIXTURE_NAME
    if fixture.exists():
        raise AssertionError(f"Test fixture already exists: {fixture}")
    with fixture.open("xb") as output:
        output.write(CONTENT)
    content_digest = hashlib.sha256(CONTENT).digest()
    try:
        find_click("AXButton", "Reread")
        find_click("AXRow", FIXTURE_NAME)
        find_click("AXButton", "View")
        find_click("AXButton", "Show or hide viewer Find bar", viewer=True)
        enter_query("NEEDLE")

        find_click("AXButton", "Find next match", viewer=True)
        first = snapshot()
        if first["offset"] is None or abs(first["offset"] - FIRST_OFFSET) > 65536:
            raise AssertionError(f"First match offset differed: {first}")

        find_click("AXButton", "Find next match", viewer=True)
        second = snapshot()
        if second["offset"] is None or abs(second["offset"] - SECOND_OFFSET) > 65536:
            raise AssertionError(f"Second match offset differed: {second}")
        if second["offset"] - first["offset"] <= 4 * 1024 * 1024:
            raise AssertionError(f"Distant forward result missing: {first}, {second}")

        find_click("AXButton", "Find previous match", viewer=True)
        backward = snapshot()
        if backward["offset"] is None or abs(backward["offset"] - FIRST_OFFSET) > 65536:
            raise AssertionError(f"Previous did not return to first match: {backward}")

        send_f3(backward=False)
        keyboard_next = snapshot()
        if keyboard_next["offset"] is None or abs(keyboard_next["offset"] - SECOND_OFFSET) > 65536:
            raise AssertionError(f"F3 did not reach the second match: {keyboard_next}")
        send_f3(backward=True)
        keyboard_previous = snapshot()
        if keyboard_previous["offset"] is None or abs(keyboard_previous["offset"] - FIRST_OFFSET) > 65536:
            raise AssertionError(f"Shift+F3 did not return to the first match: {keyboard_previous}")

        find_click("AXButton", "Find previous match", viewer=True)
        before_first = snapshot()
        if "was not found before this position" not in before_first["searchResult"]:
            raise AssertionError(f"Missing no-earlier-match feedback: {before_first}")
        if before_first["count"] != "2 matches":
            raise AssertionError(f"Missing exact visible match count: {before_first}")

        enter_query("first")
        find_click("AXButton", "Find next match", viewer=True)
        changed = snapshot()
        if changed["offset"] is None or abs(changed["offset"] - (FIRST_OFFSET + len(b"NEEDLE "))) > 65536:
            raise AssertionError(f"Changed query returned a stale match: {changed}")
        if changed["count"] != "1 match":
            raise AssertionError(f"Changed query retained the old match count: {changed}")
        close_viewer()
        if hashlib.sha256(fixture.read_bytes()).digest() != content_digest:
            raise AssertionError("Search changed its source bytes")
        return {"passed": True, "platform": "macOS", "mode": "Text",
                "firstMatchByte": FIRST_OFFSET, "secondMatchByte": SECOND_OFFSET,
                "firstVisibleOffset": first["offset"], "secondVisibleOffset": second["offset"],
                "backwardVisibleOffset": backward["offset"], "visibleMatchStatus": before_first["count"],
                "keyboardF3AndShiftF3Found": True, "changedQueryVisibleMatchStatus": changed["count"],
                "noEarlierMatchFeedback": True,
                "sourceBytesUnchanged": True}
    finally:
        close_viewer()
        if fixture.exists() and hashlib.sha256(fixture.read_bytes()).digest() == content_digest:
            fixture.unlink()
            find_click("AXButton", "Reread")


def main():
    if len(sys.argv) != 3:
        raise SystemExit("Usage: mac-ax-viewer-backward.py SOURCE REPORT.json")
    try:
        result = run(Path(sys.argv[1]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[2]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
