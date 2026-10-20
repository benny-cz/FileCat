#!/usr/bin/env python3
"""Repeat Mac pointer/Shift range selection in a narrow FileCat window.

Usage: python3 mac-ax-selection-narrow.py SOURCE CAPTURE.app REPORT.json
The source contains the isolated a.txt and b.txt fixtures. The original main
window size and position are restored even when a test assertion fails.
"""

import json
from pathlib import Path
import re
import runpy
import sys
import time


_selection = runpy.run_path(str(Path(__file__).with_name("mac-ax-selection-keyboard.py")))
_resize = runpy.run_path(str(Path(__file__).with_name("mac-ax-markdown-resize.py")))
osa = _selection["osa"]
select_range = _selection["run"]
capture = _resize["capture"]


def geometry(property_name):
    raw = osa(f'''
tell application "System Events"
    tell process "FileCat"
        return {property_name} of window 1
    end tell
end tell
''')
    values = [int(value) for value in re.findall(r"-?\d+", raw)]
    if len(values) != 2:
        raise AssertionError(f"Unexpected main-window {property_name}: {raw!r}")
    return values


def set_geometry(width, height, x, y):
    osa(f'''
tell application "System Events"
    tell process "FileCat"
        set position of window 1 to {{{x}, {y}}}
        set size of window 1 to {{{width}, {height}}}
    end tell
end tell
''')


def run(source, app):
    source = source.resolve(strict=True)
    app = app.resolve(strict=True)
    original_size = geometry("size")
    original_position = geometry("position")
    if original_size[0] < 900:
        raise AssertionError("The main window is already narrow; start from an expanded state")
    primary_error = None
    try:
        set_geometry(760, 600, 120, 90)
        actual_size = geometry("size")
        if abs(actual_size[0] - 760) > 2 or abs(actual_size[1] - 600) > 2:
            raise AssertionError(f"Could not settle at the narrow test size: {actual_size}")
        result = select_range(source)
        time.sleep(0.3)
        screenshot = capture(app, source.parent)
        return {**result, "narrowWindow": actual_size, "capture": screenshot,
                "originalWindow": original_size, "geometryRestored": True}
    except Exception as exc:
        primary_error = exc
        raise
    finally:
        try:
            set_geometry(*original_size, *original_position)
            restored = geometry("size")
            if any(abs(actual - expected) > 2 for actual, expected in zip(restored, original_size)):
                raise AssertionError(f"Main window size was not restored: {restored}")
        except Exception as restoration_error:
            if primary_error is not None:
                raise AssertionError(f"{primary_error}; restoration: {restoration_error}") from primary_error
            raise


def main():
    if len(sys.argv) != 4:
        raise SystemExit("Usage: mac-ax-selection-narrow.py SOURCE CAPTURE.app REPORT.json")
    try:
        result = run(Path(sys.argv[1]), Path(sys.argv[2]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[3]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
