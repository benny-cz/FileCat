#!/usr/bin/env python3
"""Prove that a Finder Copy supersedes FileCat's earlier Cut on macOS.

Usage: python3 mac-ax-clipboard-external-copy.py SOURCE DESTINATION PASTEBOARD_HELPER KEY_HELPER REPORT.json
Run only against an isolated, logged-in FileCat desktop fixture with c.txt
already present when FileCat starts. The test removes c.txt on success and
replaces the guest clipboard.
"""

import json
from pathlib import Path
import runpy
import subprocess
import sys
import time
from uuid import uuid4


osa = runpy.run_path(str(Path(__file__).with_name("mac-ax-selection-keyboard.py")))["osa"]

FIND_ACCESSIBLE = '''
on seekPanelRow(itemRef, panelPrefix, panelPath, rowName, actionName, insidePanel, depth)
    if depth > 25 then return false
    tell application "System Events"
        try
            if (role of itemRef as text) is "AXList" then
                set labelText to name of itemRef as text
                if labelText starts with panelPrefix and labelText ends with (", " & panelPath) then set insidePanel to true
            end if
            if insidePanel and (role of itemRef as text) is "AXRow" and (name of itemRef as text) is rowName then
                if actionName is "focus" then set focused of itemRef to true
                if actionName is "click" then click itemRef
                return true
            end if
        end try
        try
            repeat with childRef in (UI elements of itemRef)
                if my seekPanelRow(childRef, panelPrefix, panelPath, rowName, actionName, insidePanel, depth + 1) then return true
            end repeat
        end try
    end tell
    return false
end seekPanelRow

on hasMarkedItems(itemRef, depth)
    if depth > 25 then return false
    tell application "System Events"
        try
            if (role of itemRef as text) is "AXStaticText" and (name of itemRef as text) starts with "Marked " then return true
        end try
        try
            repeat with childRef in (UI elements of itemRef)
                if my hasMarkedItems(childRef, depth + 1) then return true
            end repeat
        end try
    end tell
    return false
end hasMarkedItems
'''


def apple_string(value):
    return json.dumps(str(value), ensure_ascii=False)


def filecat_action(script):
    return osa(FIND_ACCESSIBLE + '''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        ACTION
    end tell
end tell
'''.replace("ACTION", script))


def require_no_marks():
    if filecat_action('return (my hasMarkedItems(window 1, 0) as text)') != "false":
        raise AssertionError("The isolated FileCat source has marked items")


def focus_source(source):
    script = '''
    if not (my seekPanelRow(window 1, PANEL_PREFIX, PANEL_PATH, "b.txt", "focus", false, 0)) then error "b.txt source row absent or unfocusable"
'''.replace("PANEL_PREFIX", apple_string("Panel 1, ")).replace("PANEL_PATH", apple_string(source))
    filecat_action(script)


def send_key(helper, operation):
    process = subprocess.run([str(helper), operation], capture_output=True, text=True,
                             timeout=15, check=False)
    if process.returncode or process.stdout.strip() != operation:
        raise AssertionError(f"Native {operation} input failed: {process.stderr.strip()}")


def finder_copy(path, key_helper):
    script = '''
tell application "Finder"
    activate
    reveal (POSIX file PATH as alias)
    set selectedItems to selection
    if (count of selectedItems) is not 1 then error "Finder selected more than the test file"
    if (POSIX path of (item 1 of selectedItems as alias)) is not PATH then error "Finder selected another file"
end tell
tell application "System Events"
    tell process "Finder"
        set frontmost to true
    end tell
end tell
return "selected test file"
'''.replace("PATH", apple_string(path))
    if osa(script) != "selected test file":
        raise AssertionError("Finder selection did not complete")
    send_key(key_helper, "copy")


def paste_into_target(destination, key_helper):
    script = '''
if not (my seekPanelRow(window 1, PANEL_PREFIX, PANEL_PATH, "a.txt", "focus", false, 0)) then error "Destination a.txt row absent or unfocusable"
'''.replace("PANEL_PREFIX", apple_string("Panel 2, ")).replace("PANEL_PATH", apple_string(destination))
    filecat_action(script)
    time.sleep(0.5)
    send_key(key_helper, "paste")


def pasteboard_files(helper):
    process = subprocess.run([str(helper)], capture_output=True, text=True,
                             timeout=15, check=False)
    if process.returncode:
        raise AssertionError(f"Pasteboard file inspection failed: {process.stderr.strip()}")
    state = json.loads(process.stdout)
    if not isinstance(state.get("changeCount"), int) or not isinstance(state.get("filePaths"), list):
        raise AssertionError("Pasteboard inspector returned an invalid state")
    return state


def await_file_publication(helper, expected, newer_than):
    state = None
    for _ in range(25):
        state = pasteboard_files(helper)
        if state["filePaths"] == [str(expected)] and state["changeCount"] > newer_than:
            return state["changeCount"]
        time.sleep(0.2)
    raise AssertionError(f"Expected the exact test file publication {expected}; got {state!r}")


def run(source, destination, helper, key_helper):
    source = source.resolve(strict=True)
    destination = destination.resolve(strict=True)
    helper = helper.resolve(strict=True)
    key_helper = key_helper.resolve(strict=True)
    expected = {"a.txt": b"alpha\n", "b.txt": b"beta\n", "c.txt": b"gamma\n"}
    if {p.name: p.read_bytes() for p in source.iterdir()} != expected:
        raise AssertionError("Source must contain only the isolated a.txt, b.txt and c.txt fixtures")
    if {p.name: p.read_bytes() for p in destination.iterdir()} != {"a.txt": b"alpha\n"}:
        raise AssertionError("Destination must contain only the previous isolated a.txt copy")
    added_source = source / "c.txt"
    added_target = destination / "c.txt"
    primary_error = None
    try:
        require_no_marks()
        osa('set the clipboard to ' + apple_string("FileCat clipboard test " + str(uuid4())))
        initial = pasteboard_files(helper)
        if initial["filePaths"]:
            raise AssertionError("The test sentinel still exposes a file URL")
        focus_source(source)
        time.sleep(1)
        require_no_marks()
        send_key(key_helper, "cut")
        cut_generation = await_file_publication(helper, source / "b.txt", initial["changeCount"])
        finder_copy(str(added_source), key_helper)
        finder_generation = await_file_publication(helper, added_source, cut_generation)
        paste_into_target(destination, key_helper)
        for _ in range(80):
            if added_target.exists():
                break
            time.sleep(0.25)
        if not added_target.exists() or added_target.read_bytes() != b"gamma\n":
            raise AssertionError("Finder's copied file was not pasted with exact bytes")
        if (source / "b.txt").read_bytes() != b"beta\n":
            raise AssertionError("The obsolete FileCat Cut moved or altered b.txt")
        if added_source.read_bytes() != b"gamma\n" or (source / "a.txt").read_bytes() != b"alpha\n":
            raise AssertionError("Finder's copied source or a.txt changed")
        if (destination / "a.txt").read_bytes() != b"alpha\n":
            raise AssertionError("The pre-existing destination file changed")
        return {"passed": True, "platform": "macOS", "scenario": "Cut b.txt; Finder Copy c.txt; FileCat Paste",
                "cutPublishedExactFile": True, "finderReplacedPublication": True,
                "publicationGenerationAdvanced": finder_generation > cut_generation > initial["changeCount"],
                "externalCopyBytesMatch": True, "obsoleteCutSourcePreserved": True,
                "otherSourceAndTargetBytesPreserved": True}
    except Exception as exc:
        primary_error = exc
        raise
    finally:
        # Retire the test-owned file URL before removing that file. Never read or
        # reveal clipboard content belonging to a different application.
        try:
            osa('set the clipboard to "FileCat native clipboard test complete"')
        except Exception:
            if primary_error is None:
                raise
        if primary_error is None:
            if added_target.exists():
                added_target.unlink()
            added_source.unlink()


def main():
    if len(sys.argv) != 6:
        raise SystemExit("Usage: mac-ax-clipboard-external-copy.py SOURCE DESTINATION PASTEBOARD_HELPER KEY_HELPER REPORT.json")
    try:
        result = run(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]), Path(sys.argv[4]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[5]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
