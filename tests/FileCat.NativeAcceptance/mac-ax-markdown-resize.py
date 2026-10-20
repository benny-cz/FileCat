#!/usr/bin/env python3
"""Capture a native Markdown Page viewer before and after a window resize.

Usage: python3 mac-ax-markdown-resize.py SOURCE CAPTURE.app REPORT.json
SOURCE is an isolated FileCat panel containing a.txt and b.txt as described in
mac-ax-markdown-find.py. Requires an unlocked desktop, Accessibility for the
terminal runner, and the owner's approved FileCatCapture helper.
"""

import json
from pathlib import Path
import re
import runpy
import subprocess
import sys
import time

_find = runpy.run_path(str(Path(__file__).with_name("mac-ax-markdown-find.py")))
click_named = _find["click_named"]
close_viewer = _find["close_viewer"]
snapshot = _find["snapshot"]
viewer_open = _find["viewer_open"]


NAME = "native-page-resize.md"
CONTENT = ("""# Responsive Markdown

This long introduction explains how a viewer allocates available horizontal space to ordinary prose. It contains enough words to wrap into several lines in a narrow window and fewer lines when that window expands across the desktop.

## Width comparison

| Audit finding | Implemented behavior | Regression coverage | Remaining acceptance |
| --- | --- | --- | --- |
| FCUX-001 interrupted transfer | Retained destination and source mappings are described using a long but readable sentence that can wrap inside its table cell. | RecoverableTransferIntentTests and RecoveryTransferConflictTests | Native filesystem and process interruption acceptance |
| UXBUG-004 Markdown prose | Ordinary paragraphs and table columns should both respond to a wider Page viewer. | MarkdownTests and native desktop capture | Scale and assistive technology |

The concluding paragraph repeats the available-width check after the table, so both sides of the table can be inspected at narrow and wide sizes. Its line length should respond to the viewer's window width.
""").encode("utf-8")


def osa(script):
    result = subprocess.run(["/usr/bin/osascript", "-e", script], text=True,
                            capture_output=True, timeout=60, check=False)
    if result.returncode:
        raise AssertionError(result.stderr.strip() or "osascript failed")
    return result.stdout.strip()


def resize(width, height):
    result = osa(f'''
tell application "System Events"
    tell process "FileCat"
        repeat with w in windows
            if (name of w as text) contains "FileCat Viewer" then
                set size of w to {{{width}, {height}}}
                set position of w to {{120, 90}}
                return size of w
            end if
        end repeat
        error "Viewer absent during resize"
    end tell
end tell
''')
    dimensions = [int(value) for value in re.findall(r"\d+", result)]
    if len(dimensions) != 2 or any(abs(actual - expected) > 2 for actual, expected in
                                   zip(dimensions, (width, height))):
        raise AssertionError(f"Viewer resize did not settle at {width}x{height}: {result!r}")
    return dimensions


def capture(app, folder):
    before = set(folder.glob("native-screen-*.png"))
    launched = subprocess.run(["/usr/bin/open", "-a", str(app)],
                              capture_output=True, text=True, timeout=30, check=False)
    if launched.returncode:
        raise AssertionError(launched.stderr.strip() or "Capture helper did not launch")
    for _ in range(50):
        added = sorted(set(folder.glob("native-screen-*.png")) - before,
                       key=lambda p: p.stat().st_mtime_ns)
        if added:
            path = added[-1]
            if path.stat().st_size < 100_000:
                raise AssertionError("Captured PNG is unexpectedly small")
            return str(path)
        time.sleep(0.2)
    raise AssertionError("Capture helper produced no PNG")


def run(source, app):
    source = source.resolve(strict=True)
    app = app.resolve(strict=True)
    if not app.is_dir():
        raise AssertionError("Capture helper must be a macOS app")
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
        narrow_size = resize(760, 620)
        time.sleep(1)
        narrow_png = capture(app, source.parent)
        wide_size = resize(1220, 700)
        time.sleep(1)
        wide_png = capture(app, source.parent)
        if fixture.read_bytes() != CONTENT:
            raise AssertionError("Resize changed Markdown source bytes")
        return {"passed": True, "platform": "macOS", "mode": "Page",
                "narrowSize": narrow_size, "wideSize": wide_size,
                "narrowCapture": narrow_png, "wideCapture": wide_png,
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
    if len(sys.argv) != 4:
        raise SystemExit("Usage: mac-ax-markdown-resize.py SOURCE CAPTURE.app REPORT.json")
    try:
        result = run(Path(sys.argv[1]), Path(sys.argv[2]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[3]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
