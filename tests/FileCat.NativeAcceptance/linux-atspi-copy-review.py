#!/usr/bin/env python3
"""Native Ubuntu Copy review check against an isolated, running FileCat fixture.

Usage: linux-atspi-copy-review.py SOURCE DESTINATION REPORT.json
Run as the logged-in desktop user with DBUS_SESSION_BUS_ADDRESS set to that
user's session bus. Requires the system's gi/Atspi 2.0 binding.
"""

import json
from pathlib import Path
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
    return next(
        (child for child in descendants(node)
         if child.get_role_name() == role and child.get_name() == name),
        None,
    )


def find_last(node, role, name):
    matches = [child for child in descendants(node)
               if child.get_role_name() == role and child.get_name() == name]
    return matches[-1] if matches else None


def wait_for(check, description, seconds=12):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        result = check()
        if result:
            return result
        time.sleep(0.15)
    raise AssertionError(f"Timed out waiting for {description}")


def checked_action(node, description):
    if node is None or node.get_n_actions() != 1 or not node.do_action(0):
        raise AssertionError(f"The {description} accessibility action was unavailable")


def run(source, destination):
    source = source.resolve(strict=True)
    destination = destination.resolve(strict=True)
    a_path = source / "a.txt"
    b_path = source / "b.txt"
    original_a = a_path.read_bytes()
    original_b = b_path.read_bytes()
    assert original_a == b"alpha\n" and original_b == b"beta\n"
    assert not tuple(destination.iterdir()), "The target fixture is not empty"

    desktop = Atspi.get_desktop(0)
    apps = [desktop.get_child_at_index(index)
            for index in range(desktop.get_child_count())]
    matches = [app for app in apps if app.get_name() == "FileCat"
               and find(app, "list", f"Panel 1, source, {source}")]
    assert len(matches) == 1, "Expected one FileCat instance on the fixture"
    app = matches[0]
    source_list = find(app, "list", f"Panel 1, source, {source}")
    target_list = find(app, "list", f"Panel 2, target, {destination}")
    assert source_list and target_list, "The source and target locations are not exposed"
    row = find(source_list, "list item", "a.txt")
    assert row and row.grab_focus(), "Could not focus the exact source row"
    assert wait_for(lambda: row.get_state_set().contains(Atspi.StateType.FOCUSED),
                    "the source row to own focus")

    copy_command = find(app, "button", "Copy…")
    checked_action(copy_command, "Copy command")
    summary = f'Copy "a.txt" ({len(original_a)} bytes)'
    target_summary = f"→ into the folder {destination}"

    def reviewed():
        return (find(app, "label", summary) is not None
                and find(app, "label", target_summary) is not None
                and find(app, "entry", "Destination") is not None
                and find(app, "button", "Cancel") is not None)

    wait_for(reviewed, "the complete Copy review")
    checked_action(find(app, "button", "Cancel"), "Cancel")
    wait_for(lambda: find(app, "label", summary) is None, "Copy review to close")
    assert a_path.read_bytes() == original_a and b_path.read_bytes() == original_b
    assert not tuple(destination.iterdir()), "Cancel changed the destination"

    checked_action(find(app, "button", "Copy…"), "Copy command after Cancel")
    wait_for(reviewed, "the reopened Copy review")
    checked_action(find_last(app, "button", "Copy"), "Copy approval")
    target_a = destination / "a.txt"
    wait_for(lambda: target_a.exists() and target_a.read_bytes() == original_a,
             "the copied target bytes", seconds=20)
    assert a_path.read_bytes() == original_a and b_path.read_bytes() == original_b
    assert not (destination / "b.txt").exists(), "An unselected file was copied"
    return {
        "passed": True,
        "source": str(source),
        "destination": str(destination),
        "focusedItem": "a.txt",
        "reviewSummary": summary,
        "reviewTarget": target_summary,
        "cancelPreservedFixture": True,
        "approvedTargetBytes": target_a.read_bytes().hex(),
        "unselectedSourcePreserved": True,
    }


def main():
    if len(sys.argv) != 4:
        raise SystemExit("Usage: linux-atspi-copy-review.py SOURCE DESTINATION REPORT.json")
    report = Path(sys.argv[3])
    try:
        result = run(Path(sys.argv[1]), Path(sys.argv[2]))
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
