#!/usr/bin/env python3
"""Verify a native FileCat Cut and Paste on an isolated Ubuntu desktop.

Usage: linux-atspi-clipboard-owned-move.py SOURCE DESTINATION UINPUT_HELPER REPORT.json
The signed-in FileCat instance must show SOURCE in panel 1 and DESTINATION in
panel 2. The caller must provide sudo access to /dev/uinput, either through a
noninteractive sudo rule or FILECAT_NATIVE_SUDO_PASSWORD in its environment.
The script restores the two exact test folders after a successful move.
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


def find_panel(app, number, path):
    return next((child for child in descendants(app) if child.get_role_name() == "list"
                 and (child.get_name() or "").startswith(f"Panel {number}, ")
                 and (child.get_name() or "").endswith(f", {path}")), None)


def wait_for(check, description, seconds=15):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        result = check()
        if result:
            return result
        time.sleep(0.15)
    raise AssertionError(f"Timed out waiting for {description}")


def send_key(helper, chord):
    password = os.environ.get("FILECAT_NATIVE_SUDO_PASSWORD")
    command = ["sudo", "-S" if password else "-n", sys.executable, str(helper), chord]
    result = subprocess.run(command, input=(password + "\n") if password else None,
                            capture_output=True, text=True, timeout=15)
    if result.returncode:
        raise AssertionError(f"Native {chord} input failed with exit {result.returncode}")


def command(app, title, helper):
    entry = find(app, "entry", "Search commands")
    assert entry and entry.grab_focus(), "Command search did not take focus"
    assert entry.get_editable_text().set_text_contents(title), "Could not set command query"
    wait_for(lambda: any(title.lower() in (child.get_name() or "").lower()
                         for child in descendants(app) if child.get_role_name() == "list item"),
             f"the {title} command result")
    send_key(helper, "enter")


def manifest(folder):
    return {item.name: item.read_bytes() for item in folder.iterdir()}


def run(source, destination, helper):
    source = source.resolve(strict=True)
    destination = destination.resolve(strict=True)
    helper = helper.resolve(strict=True)
    original_source = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    original_target = {"a.txt": b"alpha\n"}
    assert manifest(source) == original_source, "Source is not the exact isolated fixture"
    assert manifest(destination) == original_target, "Target is not the exact isolated fixture"

    desktop = Atspi.get_desktop(0)
    apps = [desktop.get_child_at_index(i) for i in range(desktop.get_child_count())]
    matches = [app for app in apps if app.get_name() == "FileCat"
               and find_panel(app, 1, source)]
    assert len(matches) == 1, (f"Expected one FileCat instance on this fixture; found {len(matches)}. "
                               f"FileCat lists: {[[child.get_name() for child in descendants(app) if child.get_role_name() == 'list'] for app in apps if app.get_name() == 'FileCat']}")
    app = matches[0]
    source_list = find_panel(app, 1, source)
    target_list = find_panel(app, 2, destination)
    assert target_list, "Target panel is not exposed"
    source_row = wait_for(lambda: find(source_list, "list item", "b.txt"), "b.txt row")
    assert source_row.grab_focus(), "Could not focus only b.txt"
    command(app, "Cut items to clipboard", helper)
    notice = wait_for(lambda: next((child.get_name() for child in descendants(app)
                                    if (child.get_name() or "").startswith("Cut 1 item")), None),
                      "Cut confirmation")
    assert manifest(source) == original_source and manifest(destination) == original_target, "Cut changed files before Paste"

    target_row = wait_for(lambda: find(target_list, "list item", "a.txt"), "target a.txt row")
    assert target_row.grab_focus(), "Could not focus the target panel"
    command(app, "Paste items or go to pasted path", helper)
    wait_for(lambda: manifest(source) == {"a.txt": b"alpha\n"}
             and manifest(destination) == {"a.txt": b"alpha\n", "b.txt": b"beta\n"},
             "owned Cut to move exactly b.txt", seconds=25)
    time.sleep(0.5)
    assert manifest(source) == {"a.txt": b"alpha\n"}
    assert manifest(destination) == {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    (destination / "b.txt").rename(source / "b.txt")
    assert manifest(source) == original_source and manifest(destination) == original_target
    return {"passed": True, "platform": "Ubuntu", "scenario": "FileCat Cut b.txt; FileCat Paste into target",
            "cutNotice": notice, "ownedMoveCompleted": True, "movedBytesMatch": True,
            "unselectedSourceAndTargetBytesPreserved": True, "fixtureRestored": True}


def main():
    if len(sys.argv) != 5:
        raise SystemExit("Usage: linux-atspi-clipboard-owned-move.py SOURCE DESTINATION UINPUT_HELPER REPORT.json")
    report = Path(sys.argv[4])
    try:
        result = run(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
