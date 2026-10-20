#!/usr/bin/env python3
"""Check a partial owned Move and refusal to replay its original Cut on macOS.

Usage: python3 mac-ax-clipboard-partial-move.py SOURCE DESTINATION PASTEBOARD_HELPER KEY_HELPER REPORT.json [--cancel-conflict]
Run only against the isolated FileCatValidation desktop fixture. On success the
test restores the exact fixture bytes and retires its clipboard publication.
"""

import json
from pathlib import Path
import runpy
import sys
import time
from uuid import uuid4


clipboard = runpy.run_path(str(Path(__file__).with_name("mac-ax-clipboard-external-copy.py")))

OPEN_DECISION = '''
on openReview(itemRef, depth)
    if depth > 25 then return false
    tell application "System Events"
        try
            if (role of itemRef as text) is "AXButton" and (name of itemRef as text) contains "Review decision" then
                click itemRef
                return true
            end if
        end try
        try
            repeat with childRef in (UI elements of itemRef)
                if my openReview(childRef, depth + 1) then return true
            end repeat
        end try
    end tell
    return false
end openReview
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        return (my openReview(window 1, 0) as text)
    end tell
end tell
'''

CHOOSE_CONFLICT = '''
on chooseDecision(itemRef, depth)
    if depth > 25 then return false
    tell application "System Events"
        try
            if (role of itemRef as text) is "AXButton" and (name of itemRef as text) is "DECISION_NAME" then
                click itemRef
                return true
            end if
        end try
        try
            repeat with childRef in (UI elements of itemRef)
                if my chooseDecision(childRef, depth + 1) then return true
            end repeat
        end try
    end tell
    return false
end chooseDecision
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        return (my chooseDecision(window 1, 0) as text)
    end tell
end tell
'''

PARTIAL_NOTICE = '''
on hasPartialNotice(itemRef, depth)
    if depth > 25 then return false
    tell application "System Events"
        try
            set itemName to name of itemRef as text
            if itemName contains "previous move fully completed COMPLETED_ROOTS of 2 items" then return true
        end try
        try
            set itemValue to value of itemRef as text
            if itemValue contains "previous move fully completed COMPLETED_ROOTS of 2 items" then return true
        end try
        try
            repeat with childRef in (UI elements of itemRef)
                if my hasPartialNotice(childRef, depth + 1) then return true
            end repeat
        end try
    end tell
    return false
end hasPartialNotice
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        return (my hasPartialNotice(window 1, 0) as text)
    end tell
end tell
'''


def manifest(folder):
    return {path.name: path.read_bytes() for path in folder.iterdir()}


def until(check, description, seconds=35):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if check():
            return
        time.sleep(0.25)
    raise AssertionError(description)


def run(source, destination, pasteboard_helper, key_helper, cancel_conflict=False):
    source = source.resolve(strict=True)
    destination = destination.resolve(strict=True)
    pasteboard_helper = pasteboard_helper.resolve(strict=True)
    key_helper = key_helper.resolve(strict=True)
    fixture_root = (Path.home() / "FileCatValidation").resolve(strict=True)
    if source != fixture_root / "source" or destination != fixture_root / "destination":
        raise AssertionError("Use only the dedicated FileCatValidation source and destination")
    original_source = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    original_target = {"a.txt": b"alpha\n"}
    partial_source = {"a.txt": b"alpha\n"}
    partial_target = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    if manifest(source) != original_source or manifest(destination) != original_target:
        raise AssertionError("The isolated source or destination fixture has changed")

    clipboard["require_no_marks"]()
    clipboard["osa"]('set the clipboard to ' + clipboard["apple_string"]("FileCat partial Move " + str(uuid4())))
    decided = False
    moved = False
    primary_error = None
    try:
        before = clipboard["pasteboard_files"](pasteboard_helper)
        if before["filePaths"]:
            raise AssertionError("The test sentinel still exposes file URLs")
        clipboard["focus_source"](str(source))
        clipboard["filecat_action"]('key code 35 using {command down, shift down}')
        clipboard["filecat_action"]('key code 0 using {command down}')
        clipboard["filecat_action"]('key code 51')
        clipboard["filecat_action"]('keystroke "Select all"')
        clipboard["filecat_action"]('key code 36')
        until(lambda: clipboard["filecat_action"]('return (my hasMarkedItems(window 1, 0) as text)') == "true",
              "Select all did not mark the two test files")
        clipboard["send_key"](key_helper, "cut")
        expected_paths = {str(source / "a.txt"), str(source / "b.txt")}
        until(lambda: set(clipboard["pasteboard_files"](pasteboard_helper)["filePaths"]) == expected_paths,
              "Cut did not publish both exact test file URLs")
        cut = clipboard["pasteboard_files"](pasteboard_helper)
        if cut["changeCount"] <= before["changeCount"] or set(cut["filePaths"]) != expected_paths:
            raise AssertionError("The owned Cut publication was replaced")

        clipboard["paste_into_target"](str(destination), key_helper)
        until(lambda: clipboard["osa"](OPEN_DECISION) == "true",
              "The existing a.txt conflict did not expose a review action")
        choice = "Cancel operation" if cancel_conflict else "Skip"
        if clipboard["osa"](CHOOSE_CONFLICT.replace("DECISION_NAME", choice)) != "true":
            raise AssertionError(f"The conflict review did not offer {choice}")
        decided = True
        if cancel_conflict:
            time.sleep(2)
            if manifest(source) != original_source or manifest(destination) != original_target:
                raise AssertionError("Cancel changed the original source or destination")
        else:
            until(lambda: manifest(source) == partial_source and manifest(destination) == partial_target,
                  "Skip did not preserve a.txt and move only b.txt with exact bytes")
            moved = True

        clipboard["paste_into_target"](str(destination), key_helper)
        completed = 0 if cancel_conflict else 1
        until(lambda: clipboard["osa"](PARTIAL_NOTICE.replace("COMPLETED_ROOTS", str(completed))) == "true",
              f"Replay did not explain that {completed} of two roots completed")
        expected_source = original_source if cancel_conflict else partial_source
        expected_target = original_target if cancel_conflict else partial_target
        if manifest(source) != expected_source or manifest(destination) != expected_target:
            raise AssertionError("A second Paste replayed or changed the earlier Move")
        return {"passed": True, "platform": "macOS",
                "scenario": f"Cut a.txt and b.txt; {choice} for existing a.txt; refuse replay",
                "cutPublishedBothExactFiles": True,
                "conflictReviewChoice": choice,
                "completedRoots": completed,
                "resultBytesExact": True,
                "moveReplayRefused": True}
    except Exception as exc:
        primary_error = exc
        raise
    finally:
        clipboard_error = None
        try:
            clipboard["osa"]('set the clipboard to "FileCat partial Move test complete"')
        except Exception as exc:
            clipboard_error = exc
        if moved:
            if manifest(source) == partial_source and manifest(destination) == partial_target:
                (destination / "b.txt").unlink()
                (source / "b.txt").write_bytes(b"beta\n")
            else:
                raise AssertionError("Fixture changed unexpectedly; left it for inspection")
        if decided:
            clipboard["focus_source"](str(source))
            clipboard["filecat_action"]('key code 35 using {command down, shift down}')
            clipboard["filecat_action"]('keystroke "Unselect all"')
            clipboard["filecat_action"]('key code 36')
            clipboard["require_no_marks"]()
        if clipboard_error is not None and primary_error is None:
            raise clipboard_error


def main():
    if len(sys.argv) not in (6, 7) or len(sys.argv) == 7 and sys.argv[6] != "--cancel-conflict":
        raise SystemExit(__doc__)
    try:
        result = run(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]), Path(sys.argv[4]),
                     len(sys.argv) == 7)
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[5]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
