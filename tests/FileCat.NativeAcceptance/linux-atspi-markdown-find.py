#!/usr/bin/env python3
"""Check native Ubuntu Markdown Page Find through AT-SPI and WebKitGTK.

Usage: linux-atspi-markdown-find.py SOURCE REPORT.json
SOURCE is an isolated FileCat panel with a.txt = b"alpha\n" and b.txt =
b"beta\n". Run in its logged-in desktop session. Requires gi/Atspi 2.0,
wmctrl, and linux-atspi-viewer-find.py beside this script.
"""

import json
from pathlib import Path
import runpy
import sys

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


helpers = runpy.run_path(str(Path(__file__).with_name("linux-atspi-viewer-find.py")))
descendants = helpers["descendants"]
find = helpers["find"]
wait_for = helpers["wait_for"]
checked_action = helpers["checked_action"]
close_viewer = helpers["close_viewer"]

NAME = "native-page-search.md"
CONTENT = b"# Native Page Search\n\nThis marker is first.\n\n## Next\n\nThis marker is second.\n"


def label_containing(node, text):
    return next((child for child in descendants(node)
                 if child.get_role_name() == "label" and text in child.get_name()), None)


def run(source):
    source = source.resolve(strict=True)
    assert (source / "a.txt").read_bytes() == b"alpha\n"
    assert (source / "b.txt").read_bytes() == b"beta\n"
    fixture = source / NAME
    if fixture.exists():
        raise AssertionError(f"Test fixture already exists: {fixture}")
    desktop = Atspi.get_desktop(0)
    apps = [desktop.get_child_at_index(i) for i in range(desktop.get_child_count())]
    matches = [app for app in apps if app.get_name() == "FileCat"
               and find(app, "list", f"Panel 1, source, {source}")]
    if len(matches) != 1:
        raise AssertionError(f"Expected one FileCat instance on {source}")
    app = matches[0]
    pid = app.get_process_id()
    title = f"{NAME} — FileCat Viewer"
    if find(app, "frame", title):
        raise AssertionError("Close the prior Markdown viewer before this test")

    with fixture.open("xb") as output:
        output.write(CONTENT)
    try:
        checked_action(find(app, "button", "Reread"), "source refresh")
        source_list = find(app, "list", f"Panel 1, source, {source}")
        row = wait_for(lambda: find(source_list, "list item", NAME), "Markdown source row")
        if not row.grab_focus():
            raise AssertionError("Could not focus the Markdown row")
        wait_for(lambda: row.get_state_set().contains(Atspi.StateType.FOCUSED),
                 "Markdown row focus")
        checked_action(find(app, "button", "View"), "Markdown View command")
        viewer = wait_for(lambda: find(app, "frame", title), "Markdown Page viewer")
        wait_for(lambda: find(viewer, "label", "Mode: Page"), "Page mode")
        checked_action(find(viewer, "button", "Show or hide viewer Find bar"),
                       "Page Find toggle")
        entry = wait_for(lambda: find(viewer, "entry", "Find in file"), "Page Find field")
        if not entry.set_text_contents("marker"):
            raise AssertionError("AT-SPI could not set the Page Find query")
        checked_action(find(viewer, "button", "Find next match"), "Page Next")
        wait_for(lambda: label_containing(viewer, 'Found "marker" in the page.'),
                 "rendered Page result")
        count = wait_for(lambda: find(viewer, "label", "2 matches"),
                         "exact rendered Page match count")
        checked_action(find(viewer, "button", "Find next match"), "second Page Next")
        wait_for(lambda: label_containing(viewer, 'Found "marker" in the page.'),
                 "second rendered Page result")
        checked_action(find(viewer, "button", "Find previous match"), "Page Previous")
        wait_for(lambda: label_containing(viewer, 'Found "marker" in the page.'),
                 "backward rendered Page result")
        if count.get_name() != "2 matches":
            raise AssertionError("The exact Page match count changed during navigation")
        if fixture.read_bytes() != CONTENT:
            raise AssertionError("Page search changed the Markdown bytes")
        return {"passed": True, "platform": "Ubuntu", "mode": "Page",
                "query": "marker", "visibleMatchStatus": "2 matches",
                "nextTwiceAndPreviousFound": True, "sourceBytesUnchanged": True}
    finally:
        try:
            if find(app, "frame", title):
                close_viewer(app, title, pid)
        finally:
            if fixture.exists() and fixture.read_bytes() == CONTENT:
                fixture.unlink()
                checked_action(find(app, "button", "Reread"), "post-test refresh")


def main():
    if len(sys.argv) != 3:
        raise SystemExit("Usage: linux-atspi-markdown-find.py SOURCE REPORT.json")
    try:
        result = run(Path(sys.argv[1]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[2]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
