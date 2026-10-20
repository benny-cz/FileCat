#!/usr/bin/env python3
"""Check native macOS Markdown Page keyboard selection and rendered-text Copy.

Usage: python3 mac-ax-page-copy.py SOURCE REPORT.json
SOURCE is an isolated FileCat panel containing the a.txt and b.txt fixtures
described in mac-ax-markdown-find.py. Requires an unlocked desktop and
Accessibility access for the terminal runner.
"""

import json
from pathlib import Path
import runpy
import subprocess
import sys
import time
import uuid


_find = runpy.run_path(str(Path(__file__).with_name("mac-ax-markdown-find.py")))
_resize = runpy.run_path(str(Path(__file__).with_name("mac-ax-markdown-resize.py")))
click_named = _find["click_named"]
close_viewer = _find["close_viewer"]
snapshot = _find["snapshot"]
viewer_open = _find["viewer_open"]
capture = _resize["capture"]

NAME = "native-page-copy.md"
CONTENT = ("""# Clipboard Page Fixture

The rendered paragraph carries a unique marker: basil comet.

| Item | Value |
| --- | --- |
| Selected table row | copper lantern |
""").encode("utf-8")


def osa(script):
    result = subprocess.run(["/usr/bin/osascript", "-e", script], text=True,
                            capture_output=True, timeout=60, check=False)
    if result.returncode:
        raise AssertionError(result.stderr.strip() or "osascript failed")
    return result.stdout.strip()


def keyboard_select_and_copy():
    return osa('''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        repeat with w in windows
            if (name of w as text) contains "FileCat Viewer" then
                set pageArea to missing value
                set rolesFound to ""
                set allElements to entire contents of w
                repeat with candidate in allElements
                    try
                        set candidateRole to role of candidate as text
                        set candidateName to ""
                        try
                            set candidateName to name of candidate as text
                        end try
                        set rolesFound to rolesFound & candidateRole & ":" & candidateName & " | "
                        if candidateRole is "AXWebArea" then set pageArea to candidate
                    end try
                end repeat
                if pageArea is missing value then error "Page AXWebArea absent; " & rolesFound
                set pageLocation to position of pageArea
                set pageX to item 1 of pageLocation
                set pageY to item 2 of pageLocation
                click at {pageX + 80, pageY + 80}
                delay 0.2
                keystroke "a" using {command down}
                delay 0.3
                keystroke "c" using {command down}
                delay 0.4
                return "clicked Page AXWebArea and invoked Command+A / Command+C"
            end if
        end repeat
        error "Viewer absent during Page copy"
    end tell
end tell
''')


def clipboard_text():
    result = subprocess.run(["/usr/bin/pbpaste"], capture_output=True,
                            timeout=15, check=False)
    if result.returncode:
        raise AssertionError("macOS pasteboard read failed")
    return result.stdout.decode("utf-8", errors="replace")


def install_clipboard_sentinel():
    sentinel = "FileCat native Page Copy sentinel " + uuid.uuid4().hex
    result = subprocess.run(["/usr/bin/pbcopy"], input=sentinel.encode("utf-8"),
                            capture_output=True, timeout=15, check=False)
    if result.returncode or clipboard_text() != sentinel:
        raise AssertionError("Could not establish an isolated pasteboard sentinel")
    return sentinel


def run(source):
    source = source.resolve(strict=True)
    if (source / "a.txt").read_bytes() != b"alpha\n":
        raise AssertionError("Unexpected a.txt fixture")
    if (source / "b.txt").read_bytes() != b"beta\n":
        raise AssertionError("Unexpected b.txt fixture")
    if viewer_open():
        raise AssertionError("Close an existing viewer before this test")
    fixture = source / NAME
    if fixture.exists():
        raise AssertionError(f"Fixture already exists: {fixture}")
    with fixture.open("xb") as output:
        output.write(CONTENT)
    try:
        click_named("AXButton", "Reread")
        click_named("AXRow", NAME)
        click_named("AXButton", "View")
        if snapshot()["mode"] != "Mode: Page":
            raise AssertionError("Markdown did not open in Page mode")
        time.sleep(0.5)
        sentinel = install_clipboard_sentinel()
        key_route = keyboard_select_and_copy()
        time.sleep(0.3)
        copied = clipboard_text()
        screenshot = capture(source.parent / "FileCatCapture.app", source.parent)
        if copied == sentinel:
            raise AssertionError(f"Page keyboard Copy left the isolated pasteboard sentinel unchanged; {key_route}; capture={screenshot}")
        required = ("Clipboard Page Fixture", "basil comet", "Selected table row",
                    "copper lantern")
        missing = [item for item in required if item not in copied]
        if missing:
            raise AssertionError(f"Page keyboard Copy omitted expected fixture markers: {missing}; {key_route}")
        if "| --- |" in copied or "# Clipboard Page Fixture" in copied:
            raise AssertionError("Page Copy returned Markdown source instead of rendered text")
        if fixture.read_bytes() != CONTENT:
            raise AssertionError("Page keyboard interaction changed source bytes")
        return {"passed": True, "platform": "macOS", "mode": "Page",
                "keyboardRoute": key_route, "renderedTextMarkers": list(required),
                "copiedCharacters": len(copied), "selectionCapture": screenshot,
                "sourceBytesUnchanged": True}
    finally:
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
    if len(sys.argv) != 3:
        raise SystemExit("Usage: mac-ax-page-copy.py SOURCE REPORT.json")
    try:
        result = run(Path(sys.argv[1]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[2]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
