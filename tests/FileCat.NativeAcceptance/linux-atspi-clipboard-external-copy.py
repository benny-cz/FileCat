#!/usr/bin/env python3
"""Verify Ubuntu Files replaces FileCat Cut with Copy or Cut of the same file path.

Usage: linux-atspi-clipboard-external-copy.py SOURCE DESTINATION UINPUT_HELPER REPORT.json [--files-cut]
Both panels must show the exact a.txt/b.txt test fixture. Requires
linux-atspi-clipboard-owned-move.py beside this script, GNOME Files, and the
same guest-only uinput sudo setup described in its README entry.
"""

import json
import os
from pathlib import Path
import runpy
import subprocess
import sys

import gi
gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


common = runpy.run_path(str(Path(__file__).with_name("linux-atspi-clipboard-owned-move.py")))
descendants = common["descendants"]
find = common["find"]
find_panel = common["find_panel"]
wait_for = common["wait_for"]
send_key = common["send_key"]
command = common["command"]
manifest = common["manifest"]


def app_named(name):
    desktop = Atspi.get_desktop(0)
    return [desktop.get_child_at_index(i) for i in range(desktop.get_child_count())
            if desktop.get_child_at_index(i).get_name() == name]


def files_frame(source):
    return next((node for app in app_named("org.gnome.Nautilus")
                 for node in descendants(app)
                 if node.get_role_name() == "frame" and node.get_name() == source.name), None)


def selected_file(frame, name):
    return next((node for node in descendants(frame)
                 if node.get_role_name() == "table cell" and node.get_name() == f"{name}. File"), None)


def close_frame(frame):
    close = next((i for i in range(frame.get_n_actions())
                  if frame.get_action_name(i) == "window.close"), None)
    if close is None or not frame.do_action(close):
        raise AssertionError("Could not close the exact Ubuntu Files test window")


def run(source, destination, helper, external_chord="ctrl+c"):
    assert external_chord in ("ctrl+c", "ctrl+x")
    source = source.resolve(strict=True)
    destination = destination.resolve(strict=True)
    helper = helper.resolve(strict=True)
    expected_source = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    expected_target = {"a.txt": b"alpha\n"}
    assert manifest(source) == expected_source and manifest(destination) == expected_target, "Fixture is not exact"
    assert files_frame(source) is None, "An Ubuntu Files window already owns the test source"
    matches = [app for app in app_named("FileCat") if find_panel(app, 1, source)]
    assert len(matches) == 1, "Expected exactly one FileCat window with the test panels"
    app = matches[0]
    source_row = wait_for(lambda: find(find_panel(app, 1, source), "list item", "b.txt"), "source b.txt row")
    assert source_row.grab_focus(), "Could not focus only source b.txt"
    command(app, "Cut items to clipboard", helper)
    cut_notice = wait_for(lambda: next((node.get_name() for node in descendants(app)
                                        if (node.get_name() or "").startswith("Cut 1 item")), None),
                          "one-item Cut announcement")
    assert manifest(source) == expected_source and manifest(destination) == expected_target, "Cut changed files before Paste"

    frame = None
    try:
        desktop_env = {key: value for key, value in os.environ.items()
                       if key != "FILECAT_NATIVE_SUDO_PASSWORD"}
        subprocess.Popen(["nautilus", str(source)], stdin=subprocess.DEVNULL,
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                         env=desktop_env)
        frame = wait_for(lambda: files_frame(source), "Ubuntu Files source window")
        assert wait_for(lambda: selected_file(frame, "a.txt") and selected_file(frame, "b.txt"),
                        "the two Files items")
        send_key(helper, "home")
        wait_for(lambda: selected_file(frame, "a.txt").get_state_set().contains(Atspi.StateType.SELECTED),
                 "the first Files item selected")
        send_key(helper, "right")
        b_row = selected_file(frame, "b.txt")
        a_row = selected_file(frame, "a.txt")
        assert wait_for(lambda: b_row.get_state_set().contains(Atspi.StateType.SELECTED)
                        and not a_row.get_state_set().contains(Atspi.StateType.SELECTED),
                        "only b.txt selected in Ubuntu Files")
        send_key(helper, external_chord)
        assert manifest(source) == expected_source and manifest(destination) == expected_target, "Files clipboard publication changed fixture bytes"
        send_key(helper, "alt+tab")
        target_list = find_panel(app, 2, destination)
        assert target_list, "The FileCat target panel disappeared"
        target_row = wait_for(lambda: find(target_list, "list item", "a.txt"), "target a.txt row")
        assert target_row.grab_focus(), "Could not focus the FileCat target panel"
        command(app, "Paste items or go to pasted path", helper)
        wait_for(lambda: manifest(source) == expected_source
                 and manifest(destination) == {"a.txt": b"alpha\n", "b.txt": b"beta\n"},
                 "external same-path Copy to finish", seconds=25)
        copy_notice = wait_for(lambda: next((node.get_name() for node in descendants(app)
                                            if (node.get_name() or "").startswith('Copy "b.txt" to ')), None),
                               "Copy rather than Move evidence")
        assert manifest(source) == expected_source
        assert manifest(destination) == {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
        (destination / "b.txt").unlink()
        assert manifest(source) == expected_source and manifest(destination) == expected_target
        publication = "Cut" if external_chord == "ctrl+x" else "Copy"
        return {"passed": True, "platform": "Ubuntu", "scenario": f"FileCat Cut b.txt; Ubuntu Files {publication} b.txt; FileCat Paste",
                "externalPublication": publication,
                "cutNotice": cut_notice, "copyNotice": copy_notice, "externalSamePathImportedAsCopy": True,
                "sourceSurvived": True, "targetBytesMatch": True, "unselectedBytesPreserved": True,
                "fixtureRestored": True}
    finally:
        if frame is not None:
            close_frame(frame)


def main():
    if len(sys.argv) not in (5, 6) or (len(sys.argv) == 6 and sys.argv[5] != "--files-cut"):
        raise SystemExit("Usage: linux-atspi-clipboard-external-copy.py SOURCE DESTINATION UINPUT_HELPER REPORT.json [--files-cut]")
    report = Path(sys.argv[4])
    try:
        result = run(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3]),
                     "ctrl+x" if len(sys.argv) == 6 else "ctrl+c")
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
