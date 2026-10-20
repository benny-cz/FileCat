#!/usr/bin/env python3
"""Interrupt an owned Move at a conflict, then inspect its desktop recovery.

Usage: linux-atspi-clipboard-interrupted-move.py prepare|inspect|review|cancel-review SOURCE DESTINATION UINPUT_HELPER REPORT.json
Run prepare only on the dedicated FileCatValidation guest. Relaunch that same
FileCat binary with the same profile and panels before running inspect.
"""

import json
import os
from pathlib import Path
import runpy
import signal
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

ORIGINAL_SOURCE = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
ORIGINAL_TARGET = {"a.txt": b"alpha\n"}


def fixture(source, destination, helper):
    root = (Path.home() / "FileCatValidation").resolve(strict=True)
    source = source.resolve(strict=True)
    destination = destination.resolve(strict=True)
    helper = helper.resolve(strict=True)
    if source != root / "source" or destination != root / "destination" or helper != root / "linux-uinput-key.py":
        raise AssertionError("Use only the dedicated FileCatValidation fixture")
    if manifest(source) != ORIGINAL_SOURCE or manifest(destination) != ORIGINAL_TARGET:
        raise AssertionError("The dedicated source or destination bytes are not exact")
    return root, source, destination, helper


def app_for(source, destination):
    desktop = Atspi.get_desktop(0)
    apps = [desktop.get_child_at_index(index) for index in range(desktop.get_child_count())]
    matches = [app for app in apps if app.get_name() == "FileCat"
               and find_panel(app, 1, source) and find_panel(app, 2, destination)]
    if len(matches) != 1:
        raise AssertionError(f"Expected one exact FileCat fixture window; found {len(matches)}")
    return matches[0]


def exact_app_pid(root, source, destination):
    executable = root / "app" / "FileCat"
    expected_args = [str(executable), "--new-instance", "--data", str(root / "data"),
                     "--left", str(source), "--right", str(destination)]
    matches = []
    for proc in Path("/proc").iterdir():
        if not proc.name.isdecimal():
            continue
        try:
            args = (proc / "cmdline").read_bytes().rstrip(b"\0").decode().split("\0")
            actual_executable = (proc / "exe").resolve(strict=True)
        except (OSError, UnicodeError):
            continue
        if args == expected_args and actual_executable == executable:
            matches.append(int(proc.name))
    if len(matches) != 1:
        raise AssertionError(f"Expected one exact validation FileCat process; found {len(matches)}")
    return matches[0]


def prepare(root, source, destination, helper):
    app = app_for(source, destination)
    pid = exact_app_pid(root, source, destination)
    source_row = wait_for(lambda: find(find_panel(app, 1, source), "list item", "b.txt"),
                          "source b.txt row")
    if not source_row.grab_focus():
        raise AssertionError("Could not focus source b.txt")
    command(app, "Select all", helper)
    wait_for(lambda: any("Marked 2 of 2 files" in (node.get_name() or "")
                         for node in descendants(app)), "both source marks")
    command(app, "Cut items to clipboard", helper)
    wait_for(lambda: any("Cut 2 items" in (node.get_name() or "")
                         for node in descendants(app)), "owned Cut")
    if manifest(source) != ORIGINAL_SOURCE or manifest(destination) != ORIGINAL_TARGET:
        raise AssertionError("Cut changed the fixture before Paste")
    target_row = wait_for(lambda: find(find_panel(app, 2, destination), "list item", "a.txt"),
                          "target a.txt row")
    if not target_row.grab_focus():
        raise AssertionError("Could not focus target a.txt")
    command(app, "Paste items or go to pasted path", helper)
    wait_for(lambda: any(node.get_role_name() == "button" and
                         (node.get_name() or "").startswith("Review decision")
                         for node in descendants(app)), "Move conflict review")
    if manifest(source) != ORIGINAL_SOURCE or manifest(destination) != ORIGINAL_TARGET:
        raise AssertionError("Move changed files before conflict approval")
    if exact_app_pid(root, source, destination) != pid:
        raise AssertionError("The validation FileCat process changed before interruption")
    os.kill(pid, signal.SIGKILL)
    wait_for(lambda: not Path(f"/proc/{pid}").exists(), "only the validation process to exit")
    if manifest(source) != ORIGINAL_SOURCE or manifest(destination) != ORIGINAL_TARGET:
        raise AssertionError("Interrupting the held Move changed file bytes")
    journal = root / "data" / "local" / "journal"
    return {"passed": True, "platform": "Ubuntu", "phase": "prepare",
            "scenario": "Cut two files; hold Move at existing target conflict; kill exact FileCat process",
            "pid": pid, "conflictWasVisible": True, "sourceAndTargetBytesExact": True,
            "journalExists": journal.is_dir(),
            "journalEntryCount": len(list(journal.iterdir())) if journal.is_dir() else 0}


def inspect(root, source, destination):
    app = app_for(source, destination)
    pid = exact_app_pid(root, source, destination)
    if manifest(source) != ORIGINAL_SOURCE or manifest(destination) != ORIGINAL_TARGET:
        raise AssertionError("The restarted app changed source or target bytes")
    visible = sorted({name for node in descendants(app)
                      if (name := node.get_name()) and any(term in name.lower() for term in
                      ("interrupt", "recover", "continue", "move", "clipboard", "cut"))})
    if not any("interrupted operation needs attention" in name for name in visible):
        raise AssertionError("Restarted app did not announce the interrupted operation")
    if not any(name.startswith("Continue move") for name in visible):
        raise AssertionError("Restarted app did not expose explicit Move continuation")
    if not any(name.startswith("Move 2 items to ") and str(destination) in name
               for name in visible):
        raise AssertionError("Recovery did not identify the exact interrupted destination")
    journal = root / "data" / "local" / "journal"
    return {"passed": True, "platform": "Ubuntu", "phase": "inspect",
            "scenario": "Restart exact FileCat profile after an interrupted held Move",
            "pid": pid, "sourceAndTargetBytesExact": True,
            "journalExists": journal.is_dir(),
            "journalEntryCount": len(list(journal.iterdir())) if journal.is_dir() else 0,
            "relevantAccessibleNames": visible[:24]}


def review(root, source, destination):
    app = app_for(source, destination)
    action = wait_for(lambda: next((node for node in descendants(app)
                                   if node.get_role_name() == "button" and
                                   (node.get_name() or "").startswith("Continue move")), None),
                      "explicit interrupted Move action")
    if action.get_n_actions() != 1 or not action.do_action(0):
        raise AssertionError("Could not open interrupted Move review")
    time.sleep(0.6)
    if manifest(source) != ORIGINAL_SOURCE or manifest(destination) != ORIGINAL_TARGET:
        raise AssertionError("Opening interrupted Move review changed file bytes")
    desktop = Atspi.get_desktop(0)
    apps = [desktop.get_child_at_index(index) for index in range(desktop.get_child_count())]
    filecat = [node for node in apps if node.get_name() == "FileCat"]
    names = sorted({name for owner in filecat for node in descendants(owner)
                    if (name := node.get_name()) and any(term in name.lower() for term in
                    ("recover", "continue", "source", "destination", "move", "cancel", "review"))})
    buttons = sorted({node.get_name() for owner in filecat for node in descendants(owner)
                      if node.get_role_name() == "button" and node.get_name()})
    if not any("2 source items · 0 steps had finished" in name for name in names):
        raise AssertionError("Recovery review did not retain its two source roots and zero completed steps")
    if f"Destination: {destination}" not in names or "Cancel" not in buttons or "Continue move" not in buttons:
        raise AssertionError("Recovery review did not expose its destination and explicit actions")
    return {"passed": True, "platform": "Ubuntu", "phase": "review",
            "sourceAndTargetBytesExact": True, "relevantAccessibleNames": names[:36],
            "buttons": buttons[:36]}


def cancel_review(root, source, destination):
    app = app_for(source, destination)
    choices = [node for node in descendants(app)
               if node.get_role_name() == "button" and node.get_name() == "Cancel"]
    if len(choices) != 1 or choices[0].get_n_actions() != 1 or not choices[0].do_action(0):
        raise AssertionError("The recovery review's Cancel action was unavailable or ambiguous")
    wait_for(lambda: not any(node.get_name() == "Awaiting approval of the reviewed recovery plan…"
                             for node in descendants(app)), "recovery review to close")
    if manifest(source) != ORIGINAL_SOURCE or manifest(destination) != ORIGINAL_TARGET:
        raise AssertionError("Closing recovery review changed source or target bytes")
    return {"passed": True, "platform": "Ubuntu", "phase": "cancel-review",
            "sourceAndTargetBytesExact": True, "reviewClosedWithoutApproval": True}


def main():
    if len(sys.argv) != 6 or sys.argv[1] not in ("prepare", "inspect", "review", "cancel-review"):
        raise SystemExit(__doc__)
    report = Path(sys.argv[5])
    try:
        root, source, destination, helper = fixture(Path(sys.argv[2]), Path(sys.argv[3]), Path(sys.argv[4]))
        result = (prepare(root, source, destination, helper) if sys.argv[1] == "prepare"
                  else inspect(root, source, destination) if sys.argv[1] == "inspect"
                  else review(root, source, destination) if sys.argv[1] == "review"
                  else cancel_review(root, source, destination))
    except Exception as exc:
        result = {"passed": False, "phase": sys.argv[1], "error": f"{type(exc).__name__}: {exc}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
