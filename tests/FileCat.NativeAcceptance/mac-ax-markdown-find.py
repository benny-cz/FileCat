#!/usr/bin/env python3
"""Check native macOS Markdown Page Find controls and truthful match status.

Usage: python3 mac-ax-markdown-find.py SOURCE REPORT.json
SOURCE is an isolated FileCat panel containing a.txt = b"alpha\n" and
b.txt = b"beta\n". Requires an unlocked desktop and osascript Accessibility.
"""

import json
from pathlib import Path
import subprocess
import sys


NAME = "native-page-search.md"
CONTENT = b"# Native Page Search\n\nThis marker is first.\n\n## Next\n\nThis marker is second.\n"


def osa(script):
    completed = subprocess.run(["/usr/bin/osascript", "-e", script],
                               text=True, capture_output=True, timeout=60, check=False)
    if completed.returncode:
        raise AssertionError(completed.stderr.strip() or "osascript failed")
    return completed.stdout.strip()


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


def click_named(role, name, viewer=False):
    return osa(f'''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        repeat with w in windows
            set isViewer to (name of w as text) contains "FileCat Viewer"
            if isViewer is {str(viewer).lower()} then
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


def set_query():
    click_named("AXTextField", "Find in file", viewer=True)
    current_value = osa('''
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Viewer" then
                set allElements to entire contents of w
                repeat with e in allElements
                    try
                        if (role of e as text) is "AXTextField" and (name of e as text) is "Find in file" then
                            set currentValue to value of e
                            if currentValue is missing value then return ""
                            return currentValue as text
                        end if
                    end try
                end repeat
            end if
        end repeat
        error "Find field absent"
    end tell
end tell
''')
    if current_value:
        raise AssertionError(f"Find query was unexpectedly prefilled: {current_value!r}")
    osa('''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        keystroke "a" using {command down}
        key code 51
        keystroke "marker"
    end tell
end tell
''')


def snapshot():
    result = osa('''
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Viewer" then
                set modeText to ""
                set countText to ""
                set statusText to ""
                set previousFound to false
                set nextFound to false
                set allElements to entire contents of w
                repeat with e in allElements
                    try
                        set roleText to role of e as text
                        set labelText to name of e as text
                        if roleText is "AXStaticText" then
                            if labelText is "Mode: Page" then set modeText to labelText
                            if labelText contains "total unavailable in Page mode" then set countText to labelText
                            if labelText contains "in the page." then set statusText to labelText
                        end if
                        if roleText is "AXButton" then
                            if labelText is "Find previous match" then set previousFound to true
                            if labelText is "Find next match" then set nextFound to true
                        end if
                    end try
                end repeat
                return modeText & "|" & countText & "|" & statusText & "|" & previousFound & "|" & nextFound
            end if
        end repeat
        error "Viewer absent during snapshot"
    end tell
end tell
''')
    parts = result.split("|", 4)
    if len(parts) != 5:
        raise AssertionError(f"Malformed Page snapshot: {result!r}")
    return {"mode": parts[0], "count": parts[1], "status": parts[2],
            "previous": parts[3] == "true", "next": parts[4] == "true"}


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
    fixture = source / NAME
    if fixture.exists():
        raise AssertionError(f"Test fixture already exists: {fixture}")
    with fixture.open("xb") as output:
        output.write(CONTENT)
    try:
        click_named("AXButton", "Reread")
        click_named("AXRow", NAME)
        click_named("AXButton", "View")
        opened = snapshot()
        if opened["mode"] != "Mode: Page":
            raise AssertionError(f"Markdown did not open in Page mode: {opened}")
        click_named("AXButton", "Show or hide viewer Find bar", viewer=True)
        set_query()
        click_named("AXButton", "Find next match", viewer=True)
        forward = snapshot()
        if 'Found "marker" in the page.' not in forward["status"]:
            raise AssertionError(f"Page Next did not find the marker: {forward}")
        if forward["count"] != "At least 1 match · total unavailable in Page mode":
            raise AssertionError(f"Page match status overclaims or is absent: {forward}")
        if not (forward["previous"] and forward["next"]):
            raise AssertionError(f"Page navigation controls absent: {forward}")
        click_named("AXButton", "Find next match", viewer=True)
        second = snapshot()
        if 'Found "marker" in the page.' not in second["status"]:
            raise AssertionError(f"Second Page match missing: {second}")
        click_named("AXButton", "Find previous match", viewer=True)
        backward = snapshot()
        if 'Found "marker" in the page.' not in backward["status"]:
            raise AssertionError(f"Page Previous did not find a match: {backward}")
        send_f3(backward=False)
        keyboard_next = snapshot()
        if 'Found "marker" in the page.' not in keyboard_next["status"]:
            raise AssertionError(f"Page F3 did not find a match: {keyboard_next}")
        send_f3(backward=True)
        keyboard_previous = snapshot()
        if 'Found "marker" in the page.' not in keyboard_previous["status"]:
            raise AssertionError(f"Page Shift+F3 did not find a match: {keyboard_previous}")
        close_viewer()
        if fixture.read_bytes() != CONTENT:
            raise AssertionError("Page search changed the Markdown bytes")
        return {"passed": True, "platform": "macOS", "mode": "Page",
                "query": "marker", "visibleMatchStatus": forward["count"],
                "nextTwiceAndPreviousFound": True,
                "keyboardF3AndShiftF3Found": True, "sourceBytesUnchanged": True}
    finally:
        # A native engine crash can remove the FileCat process before this
        # check returns. Still remove only our exact, isolated fixture.
        try:
            close_viewer()
        except AssertionError:
            pass
        if fixture.exists() and fixture.read_bytes() == CONTENT:
            fixture.unlink()
            try:
                click_named("AXButton", "Reread")
            except AssertionError:
                pass


def main():
    if len(sys.argv) == 4 and sys.argv[3] == "--cleanup":
        fixture = Path(sys.argv[1]).resolve(strict=True) / NAME
        if not fixture.exists() or fixture.read_bytes() != CONTENT:
            raise SystemExit("No exact test-owned Markdown fixture to clean")
        fixture.unlink()
        print(json.dumps({"cleaned": str(fixture)}))
        return 0
    if len(sys.argv) != 3:
        raise SystemExit("Usage: mac-ax-markdown-find.py SOURCE REPORT.json")
    try:
        result = run(Path(sys.argv[1]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[2]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
