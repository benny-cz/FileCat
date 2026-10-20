#!/usr/bin/env python3
"""Verify Ubuntu partial owned Move and refusal to replay the same Cut.

Usage: linux-atspi-clipboard-partial-move.py SOURCE DESTINATION UINPUT_HELPER REPORT.json [--cancel-conflict]
Use the logged-in isolated FileCatValidation desktop and the same guest-only
uinput sudo setup as linux-atspi-clipboard-owned-move.py. On success this
runner restores both fixture folders and clears its Commander marks.
"""

import json
from pathlib import Path
import runpy
import sys
import time

import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


common = runpy.run_path(str(Path(__file__).with_name("linux-atspi-clipboard-owned-move.py")))
descendants = common["descendants"]
find = common["find"]
find_panel = common["find_panel"]
wait_for = common["wait_for"]
command = common["command"]
manifest = common["manifest"]


def button(app, prefix):
    return next((node for node in descendants(app)
                 if node.get_role_name() == "button"
                 and (node.get_name() or "").startswith(prefix)), None)


def press(node, description):
    if node is None or node.get_n_actions() != 1 or not node.do_action(0):
        raise AssertionError(f"The {description} accessibility action was unavailable")


def notice(app, phrase):
    return next((node.get_name() for node in descendants(app)
                 if phrase in (node.get_name() or "")), None)


def run(source, destination, helper, cancel_conflict=False):
    source = source.resolve(strict=True)
    destination = destination.resolve(strict=True)
    helper = helper.resolve(strict=True)
    fixture_root = (Path.home() / "FileCatValidation").resolve(strict=True)
    if source != fixture_root / "source" or destination != fixture_root / "destination":
        raise AssertionError("Use only the dedicated FileCatValidation source and destination")
    original_source = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    original_target = {"a.txt": b"alpha\n"}
    partial_source = {"a.txt": b"alpha\n"}
    partial_target = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    if manifest(source) != original_source or manifest(destination) != original_target:
        raise AssertionError("The isolated fixture is not exact")

    desktop = Atspi.get_desktop(0)
    apps = [desktop.get_child_at_index(i) for i in range(desktop.get_child_count())]
    matches = [app for app in apps if app.get_name() == "FileCat"
               and find_panel(app, 1, source) and find_panel(app, 2, destination)]
    if len(matches) != 1:
        raise AssertionError("Expected exactly one FileCat instance on the isolated fixture")
    app = matches[0]
    source_row = wait_for(lambda: find(find_panel(app, 1, source), "list item", "b.txt"),
                          "source b.txt row")
    if not source_row.grab_focus():
        raise AssertionError("Could not focus source b.txt")
    command(app, "Select all", helper)
    wait_for(lambda: notice(app, "Marked 2 of 2 files"), "both source files to be marked")
    command(app, "Cut items to clipboard", helper)
    cut_notice = wait_for(lambda: notice(app, "Cut 2 items"), "two-item Cut announcement")
    if manifest(source) != original_source or manifest(destination) != original_target:
        raise AssertionError("Cut changed files before Paste")

    target_row = wait_for(lambda: find(find_panel(app, 2, destination), "list item", "a.txt"),
                          "target a.txt row")
    if not target_row.grab_focus():
        raise AssertionError("Could not focus the destination panel")
    command(app, "Paste items or go to pasted path", helper)
    review = wait_for(lambda: button(app, "Review decision"), "the existing a.txt review action")
    press(review, "conflict review")
    choice = "Cancel operation" if cancel_conflict else "Skip"
    decision = wait_for(lambda: button(app, choice), f"the {choice} conflict decision")
    press(decision, choice)
    expected_source = original_source if cancel_conflict else partial_source
    expected_target = original_target if cancel_conflict else partial_target
    wait_for(lambda: manifest(source) == expected_source and manifest(destination) == expected_target,
             f"exact files after {choice}", seconds=25)

    # The same owned clipboard publication must retain its partial lifecycle.
    target_row = wait_for(lambda: find(find_panel(app, 2, destination), "list item", "a.txt"),
                          "target a.txt row after the partial move")
    if not target_row.grab_focus():
        raise AssertionError("Could not focus destination for a second Paste")
    command(app, "Paste items or go to pasted path", helper)
    completed = 0 if cancel_conflict else 1
    refusal = wait_for(lambda: notice(app, f"previous move fully completed {completed} of 2 items"),
                       "partial Move replay refusal")
    if manifest(source) != expected_source or manifest(destination) != expected_target:
        raise AssertionError("Second Paste changed the earlier Move result")

    # Retire only this test's file movement; a failing run leaves its fixture
    # intact for inspection instead of hiding an unexpected effect.
    if not cancel_conflict:
        (destination / "b.txt").rename(source / "b.txt")
        wait_for(lambda: find(find_panel(app, 1, source), "list item", "b.txt"),
                 "restored source b.txt row")
    restored_row = find(find_panel(app, 1, source), "list item", "b.txt")
    if not restored_row.grab_focus():
        raise AssertionError("Could not return to source marks")
    command(app, "Unselect all", helper)
    if manifest(source) != original_source or manifest(destination) != original_target:
        raise AssertionError("The exact fixture was not restored")
    if notice(app, "Marked "):
        raise AssertionError("Test marks remained after cleanup")
    return {"passed": True, "platform": "Ubuntu",
            "scenario": f"Cut a.txt and b.txt; {choice} for existing a.txt; refuse replay",
            "cutNotice": cut_notice, "replayRefusal": refusal,
            "conflictReviewChoice": choice, "completedRoots": completed,
            "resultBytesExact": True, "moveReplayRefused": True,
            "fixtureRestored": True}


def main():
    if len(sys.argv) not in (5, 6) or len(sys.argv) == 6 and sys.argv[5] != "--cancel-conflict":
        raise SystemExit(__doc__)
    report = Path(sys.argv[4])
    try:
        result = run(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]),
                     len(sys.argv) == 6)
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
