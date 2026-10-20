#!/usr/bin/env python3
"""Check a Mac Copy review's decisive text and resizable geometry.

Usage: python3 mac-ax-copy-review-resize.py SOURCE DESTINATION CAPTURE.app REPORT.json
The isolated source contains a.txt and b.txt; b.txt must be absent from the
destination. This test never approves the Copy operation.
"""

import json
from pathlib import Path
import re
import runpy
import sys
import time


_find = runpy.run_path(str(Path(__file__).with_name("mac-ax-markdown-find.py")))
_resize = runpy.run_path(str(Path(__file__).with_name("mac-ax-markdown-resize.py")))
osa = _find["osa"]
capture = _resize["capture"]


def review_state(destination, summary):
    script = '''
tell application "System Events"
    tell process "FileCat"
        set allElements to entire contents of window 1
        set summaryFound to false
        set targetFound to false
        set destinationFound to false
        set cancelFound to false
        set approveFound to false
        repeat with e in allElements
            try
                set roleText to role of e as text
                set labelText to name of e as text
                if roleText is "AXStaticText" and labelText is SUMMARY then set summaryFound to true
                if roleText is "AXStaticText" and labelText is TARGET then set targetFound to true
                if roleText is "AXTextField" and labelText is "Destination" then
                    if (value of e as text) contains DESTINATION then set destinationFound to true
                end if
                if roleText is "AXButton" and labelText is "Cancel" then set cancelFound to true
                if roleText is "AXButton" and labelText is "Copy" then set approveFound to true
            end try
        end repeat
        return (summaryFound as text) & "|" & (targetFound as text) & "|" & (destinationFound as text) & "|" & (cancelFound as text) & "|" & (approveFound as text)
    end tell
end tell
'''.replace("SUMMARY", json.dumps(summary, ensure_ascii=False)).replace(
        "TARGET", json.dumps("→ into the folder " + str(destination), ensure_ascii=False)).replace(
        "DESTINATION", json.dumps(str(destination), ensure_ascii=False))
    result = osa(script)
    if result != "true|true|true|true|true":
        raise AssertionError(f"Review lost its decision text or actions: {result!r}")


def dialog_size():
    raw = osa('''
tell application "System Events"
    tell process "FileCat"
        return size of window 1
    end tell
end tell
''')
    size = [int(value) for value in re.findall(r"\d+", raw)]
    if len(size) != 2:
        raise AssertionError(f"Unexpected review size: {raw!r}")
    return size


def set_dialog_size(width, height):
    osa(f'''
tell application "System Events"
    tell process "FileCat"
        set size of window 1 to {{{width}, {height}}}
    end tell
end tell
''')
    actual = dialog_size()
    if abs(actual[0] - width) > 2 or abs(actual[1] - height) > 2:
        raise AssertionError(f"Copy review refused {width}x{height}: {actual}")
    return actual


def open_review():
    osa('''
tell application "System Events"
    tell process "FileCat"
        set frontmost to true
        set sourceRow to missing value
        set copyButton to missing value
        set allElements to entire contents of window 1
        repeat with e in allElements
            try
                if (role of e as text) is "AXRow" and (name of e as text) is "b.txt" then set sourceRow to e
                if (role of e as text) is "AXButton" and (name of e as text) is "Copy…" then set copyButton to e
            end try
        end repeat
        if sourceRow is missing value or copyButton is missing value then error "Copy fixture controls absent"
        set focused of sourceRow to true
        if not (focused of sourceRow) then error "b.txt did not receive focus"
        click copyButton
    end tell
end tell
''')


def cancel_review():
    osa('''
tell application "System Events"
    tell process "FileCat"
        set allElements to entire contents of window 1
        repeat with e in allElements
            try
                if (role of e as text) is "AXButton" and (name of e as text) is "Cancel" then
                    click e
                    return "canceled"
                end if
            end try
        end repeat
        error "Copy review Cancel action absent"
    end tell
end tell
''')


def run(source, destination, app):
    source = source.resolve(strict=True)
    destination = destination.resolve(strict=True)
    app = app.resolve(strict=True)
    if (source / "a.txt").read_bytes() != b"alpha\n" or (source / "b.txt").read_bytes() != b"beta\n":
        raise AssertionError("Unexpected isolated source bytes")
    if (destination / "b.txt").exists():
        raise AssertionError("The b.txt target must be absent before review")
    before_names = {item.name for item in destination.iterdir()}
    before = {item.name: item.read_bytes() for item in destination.iterdir() if item.is_file()}
    summary = 'Copy "b.txt" (5 bytes)'
    open_review()
    original_size = None
    try:
        review_state(destination, summary)
        original_size = dialog_size()
        compact = set_dialog_size(640, 480)
        review_state(destination, summary)
        time.sleep(0.3)
        compact_capture = capture(app, source.parent)
        expanded = set_dialog_size(980, 680)
        review_state(destination, summary)
        time.sleep(0.3)
        expanded_capture = capture(app, source.parent)
        if (source / "a.txt").read_bytes() != b"alpha\n" or (source / "b.txt").read_bytes() != b"beta\n":
            raise AssertionError("Review changed source bytes")
        return {"passed": True, "platform": "macOS", "dialog": "Copy review",
                "compactSize": compact, "expandedSize": expanded,
                "compactCapture": compact_capture, "expandedCapture": expanded_capture,
                "decisiveTextAndActionsPresent": True, "sourceBytesUnchanged": True}
    finally:
        primary_error = sys.exc_info()[1]
        cleanup_issues = []
        if original_size is not None:
            try:
                set_dialog_size(*original_size)
            except Exception as exc:
                cleanup_issues.append(f"review size restoration: {exc}")
        try:
            cancel_review()
        except Exception as exc:
            cleanup_issues.append(f"review Cancel: {exc}")
        if (destination / "b.txt").exists():
            cleanup_issues.append("Cancel created the reviewed destination file")
        if {item.name for item in destination.iterdir()} != before_names:
            cleanup_issues.append("Cancel changed the destination entries")
        after = {item.name: item.read_bytes() for item in destination.iterdir() if item.is_file()}
        if after != before:
            cleanup_issues.append("Cancel changed existing destination bytes")
        if cleanup_issues:
            prefix = f"{primary_error}; cleanup: " if primary_error else ""
            raise AssertionError(prefix + "; ".join(cleanup_issues)) from primary_error


def main():
    if len(sys.argv) != 5:
        raise SystemExit("Usage: mac-ax-copy-review-resize.py SOURCE DESTINATION CAPTURE.app REPORT.json")
    try:
        result = run(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[4]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
