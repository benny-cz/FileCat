#!/usr/bin/env python3
"""Native Ubuntu Hex editor Find check on a disposable Git-built FileCat fixture.

Run `seed ROOT`, launch FileCat with ROOT/source, ROOT/destination and ROOT/data,
then run `check ROOT REPORT.json UINPUT_HELPER` in the logged-in desktop session.
"""

import json
import os
from pathlib import Path
import subprocess
import sys
import time


SIZE = 7 * 1024 * 1024
FIRST = 1024 * 1024 + 20
SECOND = 6 * 1024 * 1024 + 20
NAME = "hex-editor-backward.bin"


def expected_bytes():
    data = bytearray(b"x" * SIZE)
    data[FIRST:FIRST + 12] = b"NEEDLE first"
    data[SECOND:SECOND + 13] = b"NEEDLE second"
    return bytes(data)


def seed(root):
    root = root.resolve()
    if root.exists():
        raise AssertionError("The isolated Hex editor fixture already exists")
    if not str(root).startswith("/home/benny/FileCatValidation/HexEditorFind/"):
        raise AssertionError("Fixture must stay below the VM validation folder")
    (root / "source").mkdir(parents=True)
    (root / "destination").mkdir()
    (root / "data").mkdir()
    (root / "source" / NAME).write_bytes(expected_bytes())
    return {"root": str(root), "source": str(root / "source"),
            "destination": str(root / "destination"), "data": str(root / "data")}


def descendants(node):
    yield node
    for index in range(node.get_child_count()):
        yield from descendants(node.get_child_at_index(index))


def find(node, role, name):
    return next((child for child in descendants(node)
                 if child.get_role_name() == role and child.get_name() == name), None)


def named(node, role, fragment):
    return next((child for child in descendants(node)
                 if child.get_role_name() == role and fragment in child.get_name()), None)


def wait_for(check, description, seconds=15):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        result = check()
        if result:
            return result
        time.sleep(0.15)
    raise AssertionError(f"Timed out waiting for {description}")


def activate(node, description):
    if node is None:
        raise AssertionError(f"The {description} control was unavailable")
    for index in range(node.get_n_actions()):
        if node.do_action(index):
            return
    actions = [node.get_action_name(index) for index in range(node.get_n_actions())]
    raise AssertionError(f"The {description} accessibility action was unavailable "
                         f"(role={node.get_role_name()}, name={node.get_name()!r}, "
                         f"actions={actions!r})")


def send_enter(helper):
    password = os.environ.get("FILECAT_NATIVE_SUDO_PASSWORD")
    command = ["sudo", "-S" if password else "-n", sys.executable, str(helper), "enter"]
    result = subprocess.run(command, input=(password + "\n") if password else None,
                            capture_output=True, text=True, timeout=15)
    if result.returncode:
        raise AssertionError(f"Native Enter input failed with exit {result.returncode}")


def open_with_command_search(app, helper):
    search = find(app, "entry", "Search commands")
    if search is None or not search.grab_focus():
        raise AssertionError("Command search could not take focus")
    if not search.get_editable_text().set_text_contents("Edit file bytes in hex"):
        raise AssertionError("Could not set the Hex editor command query")
    wait_for(lambda: named(app, "list item", "Id = hex.edit,"),
             "Hex editor command result")
    send_enter(helper)


def close_window(app, title, expected_pid):
    result = subprocess.run(["/usr/bin/wmctrl", "-lp"], capture_output=True,
                            text=True, timeout=5, check=True)
    windows = []
    for line in result.stdout.splitlines():
        parts = line.split(maxsplit=4)
        if len(parts) == 5 and parts[2] == str(expected_pid) and parts[4] == title:
            windows.append(parts[0])
    if len(windows) != 1:
        raise AssertionError(f"Expected one owned editor window; got {windows!r}")
    subprocess.run(["/usr/bin/wmctrl", "-ic", windows[0]], timeout=5, check=True)
    wait_for(lambda: find(app, "frame", title) is None, "Hex editor closure")


def check(root, helper):
    import gi
    gi.require_version("Atspi", "2.0")
    from gi.repository import Atspi

    root = root.resolve(strict=True)
    helper = helper.resolve(strict=True)
    if not str(helper).startswith("/home/benny/FileCatValidation/"):
        raise AssertionError("The input helper must stay in the VM validation folder")
    source = root / "source"
    owned = source / NAME
    expected = expected_bytes()
    if owned.read_bytes() != expected:
        raise AssertionError("The isolated Hex editor file is not pristine")
    if any((root / "destination").iterdir()):
        raise AssertionError("The isolated destination is not empty")
    desktop = Atspi.get_desktop(0)
    apps = [desktop.get_child_at_index(index) for index in range(desktop.get_child_count())]
    matches = [app for app in apps if app.get_name() == "FileCat"
               and find(app, "list", f"Panel 1, source, {source}")]
    if len(matches) != 1:
        raise AssertionError("Expected one FileCat instance on the exact test source")
    app = matches[0]
    pid = app.get_process_id()
    if pid <= 0 or os.fsencode(str(source)) not in Path(f"/proc/{pid}/cmdline").read_bytes():
        raise AssertionError("The app's process identity does not match the fixture")
    title = f"{NAME} — FileCat Hex Editor"
    editor = find(app, "frame", title)
    if editor is None:
        row = find(find(app, "list", f"Panel 1, source, {source}"), "list item", NAME)
        if row is None or not row.grab_focus():
            raise AssertionError("Could not focus the exact test file")
        wait_for(lambda: row.get_state_set().contains(Atspi.StateType.FOCUSED),
                 "test file focus")
        open_with_command_search(app, helper)
        editor = wait_for(lambda: find(app, "frame", title), "native Hex editor")
    try:
        activate(find(editor, "button", "Show or hide hex editor Find bar"), "Find toggle")
        query = wait_for(lambda: find(editor, "entry", "Find in file"), "Find entry")
        if not query.set_text_contents("NEEDLE"):
            raise AssertionError("AT-SPI could not set the Find query")
        activate(find(editor, "button", "Find next match"), "Next")
        wait_for(lambda: named(editor, "label", f"Found at 0x{FIRST:X}"), "first match")
        wait_for(lambda: find(editor, "label", "1 distinct match visited · total not counted"),
                 "first match status")
        activate(find(editor, "button", "Find next match"), "Next")
        wait_for(lambda: named(editor, "label", f"Found at 0x{SECOND:X}"), "distant match")
        wait_for(lambda: find(editor, "label", "2 distinct matches visited · total not counted"),
                 "two-match status")
        activate(find(editor, "button", "Find previous match"), "Previous")
        wait_for(lambda: named(editor, "label", f"Found at 0x{FIRST:X}"), "earlier match")
        activate(find(editor, "button", "Find previous match"), "Previous")
        wait_for(lambda: named(editor, "label", '"NEEDLE" was not found before the cursor.'),
                 "no-earlier-match feedback")
        if not query.set_text_contents("second"):
            raise AssertionError("AT-SPI could not change the query")
        wait_for(lambda: find(editor, "label", "0 matches visited · total not counted"),
                 "changed-query reset")
        activate(find(editor, "button", "Find next match"), "Next")
        wait_for(lambda: named(editor, "label", f"Found at 0x{SECOND + 7:X}"),
                 "changed-query match")
        wait_for(lambda: find(editor, "label", "1 distinct match visited · total not counted"),
                 "changed-query count")
    finally:
        close_window(app, title, pid)
    if owned.read_bytes() != expected or any((root / "destination").iterdir()):
        raise AssertionError("The editor search changed test fixture bytes")
    return {"passed": True, "platform": "Ubuntu", "firstHit": FIRST,
            "secondHit": SECOND, "visibleControls": ["Next", "Previous"],
            "countAfterSecond": 2, "changedQueryCount": 1,
            "sourceIntact": True, "editorClosed": True}


def main():
    if len(sys.argv) == 3 and sys.argv[1] == "seed":
        print(json.dumps(seed(Path(sys.argv[2]))))
        return 0
    if len(sys.argv) != 5 or sys.argv[1] != "check":
        raise SystemExit("Usage: linux-atspi-hex-editor-find.py seed ROOT | check ROOT REPORT.json UINPUT_HELPER")
    try:
        result = check(Path(sys.argv[2]), Path(sys.argv[4]))
    except Exception as error:
        result = {"passed": False, "error": f"{type(error).__name__}: {error}"}
    Path(sys.argv[3]).write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
