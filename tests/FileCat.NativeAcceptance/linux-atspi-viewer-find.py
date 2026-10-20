#!/usr/bin/env python3
"""Check a native Ubuntu FileCat Text viewer through its AT-SPI UI.

Usage: linux-atspi-viewer-find.py SOURCE REPORT.json
Run in the logged-in desktop session with its DBUS_SESSION_BUS_ADDRESS. The
isolated SOURCE contains a.txt = b"alpha\n" and b.txt = b"beta\n". Requires
gi.repository.Atspi 2.0 and wmctrl for graceful viewer-window closure.
"""

import json
import os
from pathlib import Path
import subprocess
import sys
import time

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


def descendants(node):
    yield node
    for index in range(node.get_child_count()):
        yield from descendants(node.get_child_at_index(index))


def find(node, role, name):
    return next((child for child in descendants(node)
                 if child.get_role_name() == role and child.get_name() == name), None)


def wait_for(check, description, seconds=12):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        found = check()
        if found:
            return found
        time.sleep(0.15)
    raise AssertionError(f"Timed out waiting for {description}")


def checked_action(node, description):
    if node is None or node.get_n_actions() != 1 or not node.do_action(0):
        raise AssertionError(f"The {description} accessibility action was unavailable")


def exact_viewer_window_id(title, expected_pid):
    completed = subprocess.run(["/usr/bin/wmctrl", "-lp"], text=True,
                               capture_output=True, timeout=5, check=True)
    windows = []
    for line in completed.stdout.splitlines():
        parts = line.split(maxsplit=4)
        if len(parts) == 5 and parts[4] == title and parts[2] == str(expected_pid):
            windows.append(parts[0])
    if len(windows) != 1:
        raise AssertionError(f"Expected one owned viewer window: {title!r}; got {windows!r}")
    return windows[0]


def close_viewer(app, title, expected_pid):
    window_id = exact_viewer_window_id(title, expected_pid)
    subprocess.run(["/usr/bin/wmctrl", "-ic", window_id],
                   capture_output=True, text=True, timeout=5, check=True)
    wait_for(lambda: find(app, "frame", title) is None, "the viewer to close")


def run(source):
    source = source.resolve(strict=True)
    a_path = source / "a.txt"
    b_path = source / "b.txt"
    assert a_path.read_bytes() == b"alpha\n"
    assert b_path.read_bytes() == b"beta\n"
    desktop = Atspi.get_desktop(0)
    apps = [desktop.get_child_at_index(i) for i in range(desktop.get_child_count())]
    matches = [app for app in apps if app.get_name() == "FileCat"
               and find(app, "list", f"Panel 1, source, {source}")]
    if len(matches) != 1:
        raise AssertionError(f"Expected one FileCat instance on {source}")
    app = matches[0]
    title = "a.txt — FileCat Viewer"
    if find(app, "frame", title):
        raise AssertionError("Close the prior viewer before this test")
    expected_pid = app.get_process_id()
    if expected_pid <= 0:
        raise AssertionError("FileCat's AT-SPI process identity is unavailable")
    original_command = Path(f"/proc/{expected_pid}/cmdline").read_bytes()
    if os.fsencode(str(source)) not in original_command:
        raise AssertionError("The viewer owner was not launched on this fixture")

    source_list = find(app, "list", f"Panel 1, source, {source}")
    row = find(source_list, "list item", "a.txt")
    if row is None or not row.grab_focus():
        raise AssertionError("Could not focus the exact a.txt row")
    wait_for(lambda: row.get_state_set().contains(Atspi.StateType.FOCUSED),
             "a.txt to own keyboard focus")
    checked_action(find(app, "button", "View"), "View command")
    viewer = wait_for(lambda: find(app, "frame", title), "Text viewer")
    try:
        wait_for(lambda: find(viewer, "label", "Mode: Text"), "Text mode")
        checked_action(find(viewer, "button", "Show or hide viewer Find bar"),
                       "viewer Find toggle")
        entry = wait_for(lambda: find(viewer, "entry", "Find in file"), "Find field")
        if not entry.set_text_contents("alpha"):
            raise AssertionError("AT-SPI could not set the Find query")
        checked_action(find(viewer, "button", "Find next match"), "Find Next")
        wait_for(lambda: find(viewer, "label", "1 match"), "visible exact match count")
        if find(viewer, "button", "Find previous match") is None:
            raise AssertionError("The accessible Previous button is missing")
        checked_action(find(viewer, "button", "Find previous match"), "Find Previous")
        wait_for(lambda: find(viewer, "label", '"alpha" was not found before this position.'),
                 "the no-earlier-match result")
        if find(viewer, "label", "1 match") is None:
            raise AssertionError("The exact count vanished after directional search")
    finally:
        close_viewer(app, title, expected_pid)

    if a_path.read_bytes() != b"alpha\n" or b_path.read_bytes() != b"beta\n":
        raise AssertionError("Searching changed the fixture bytes")
    return {"passed": True, "platform": "Ubuntu", "mode": "Text",
            "query": "alpha", "visibleMatchStatus": "1 match",
            "previousReportedNoEarlierMatch": True, "sourceBytesUnchanged": True,
            "viewerClosed": True}


def main():
    if len(sys.argv) != 3:
        raise SystemExit("Usage: linux-atspi-viewer-find.py SOURCE REPORT.json")
    try:
        result = run(Path(sys.argv[1]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    Path(sys.argv[2]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
