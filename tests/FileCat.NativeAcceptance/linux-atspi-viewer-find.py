#!/usr/bin/env python3
"""Check a native Ubuntu FileCat Text viewer through its AT-SPI UI.

Usage: linux-atspi-viewer-find.py SOURCE REPORT.json [UINPUT_HELPER]
   or: linux-atspi-viewer-find.py --launch APP REPORT.json UINPUT_HELPER [--narrow]
Run in the logged-in desktop session with its DBUS_SESSION_BUS_ADDRESS. The
isolated SOURCE contains a.txt = b"alpha\n" and b.txt = b"beta\n". Requires
gi.repository.Atspi 2.0 and wmctrl for graceful viewer-window closure.
"""

import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
from uuid import uuid4

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


def send_key(helper, chord):
    password = os.environ.get("FILECAT_NATIVE_SUDO_PASSWORD")
    command = ["sudo", "-S" if password else "-n", sys.executable, str(helper), chord]
    result = subprocess.run(command, input=(password + "\n") if password else None,
                            capture_output=True, text=True, timeout=15)
    if result.returncode:
        raise AssertionError(f"Native {chord} input failed with exit {result.returncode}")


def rect(node):
    bounds = node.get_component().get_extents(Atspi.CoordType.SCREEN)
    return [bounds.x, bounds.y, bounds.width, bounds.height]


def inside(frame, control, name):
    area = rect(control)
    if (area[2] <= 0 or area[3] <= 0 or area[0] < frame[0] or area[1] < frame[1] or
            area[0] + area[2] > frame[0] + frame[2] or
            area[1] + area[3] > frame[1] + frame[3]):
        raise AssertionError(f"{name} escapes the viewer frame: {area} outside {frame}")
    return area


def run(source, helper=None, narrow=False):
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
    row = wait_for(lambda: next((item for item in descendants(source_list)
                   if item.get_role_name() == "list item" and
                   (item.get_name() or "").startswith("a.txt, file,")), None),
                   "the exact a.txt source row")
    wait_for(lambda: row.grab_focus() and
             row.get_state_set().contains(Atspi.StateType.FOCUSED),
             "a.txt to own keyboard focus")
    checked_action(find(app, "button", "View"), "View command")
    viewer = wait_for(lambda: find(app, "frame", title), "Text viewer")
    try:
        wait_for(lambda: find(viewer, "label", "Mode: Text"), "Text mode")
        geometry = None
        if narrow:
            window_id = exact_viewer_window_id(title, expected_pid)
            subprocess.run(["/usr/bin/wmctrl", "-i", "-r", window_id, "-e",
                            "0,150,100,720,500"], check=True, timeout=5)
            subprocess.run(["/usr/bin/wmctrl", "-ia", window_id], check=True, timeout=5)
            time.sleep(.4)
            geometry = {"requestedPhysicalSize": [720, 500]}
        checked_action(find(viewer, "button", "Show or hide viewer Find bar"),
                       "viewer Find toggle")
        entry = wait_for(lambda: find(viewer, "entry", "Find in file"), "Find field")
        if not entry.set_text_contents("alpha"):
            raise AssertionError("AT-SPI could not set the Find query")
        checked_action(find(viewer, "button", "Find next match"), "Find Next")
        wait_for(lambda: find(viewer, "label", "1 match"), "visible exact match count")
        if narrow:
            frame = rect(viewer)
            geometry["frame"] = frame
            geometry["controls"] = {
                name: inside(frame, find(viewer, role, accessible), name)
                for name, role, accessible in (
                    ("Find", "entry", "Find in file"),
                    ("Previous", "button", "Find previous match"),
                    ("Next", "button", "Find next match"),
                    ("Count", "label", "1 match"))
            }
        if find(viewer, "button", "Find previous match") is None:
            raise AssertionError("The accessible Previous button is missing")
        checked_action(find(viewer, "button", "Find previous match"), "Find Previous")
        wait_for(lambda: find(viewer, "label", '"alpha" was not found before this position.'),
                 "the no-earlier-match result")
        if find(viewer, "label", "1 match") is None:
            raise AssertionError("The exact count vanished after directional search")
        if helper is not None:
            helper = helper.resolve(strict=True)
            if not str(helper).startswith(str(Path.home() / "FileCatValidation") + os.sep):
                raise AssertionError("The keyboard helper is outside the validation folder")
            send_key(helper, "f3")
            wait_for(lambda: find(viewer, "label", '"alpha" was not found after this position.'),
                     "the physical F3 no-later-match result")
            send_key(helper, "shift+f3")
            wait_for(lambda: find(viewer, "label", '"alpha" was not found before this position.'),
                     "the physical Shift+F3 no-earlier-match result")
            if find(viewer, "label", "1 match") is None:
                raise AssertionError("The exact count vanished after physical-key search")
        if not entry.set_text_contents("beta"):
            raise AssertionError("AT-SPI could not replace the Find query")
        wait_for(lambda: find(viewer, "label", "1 match") is None,
                 "the old query count to retire")
        checked_action(find(viewer, "button", "Find next match"), "Find Next for changed query")
        wait_for(lambda: find(viewer, "label", '"beta" was not found after this position.'),
                 "the new query's no-match result")
        wait_for(lambda: find(viewer, "label", "0 matches"),
                 "the new query's exact zero count")
    finally:
        close_viewer(app, title, expected_pid)

    if a_path.read_bytes() != b"alpha\n" or b_path.read_bytes() != b"beta\n":
        raise AssertionError("Searching changed the fixture bytes")
    return {"passed": True, "platform": "Ubuntu", "mode": "Text",
            "query": "alpha", "visibleMatchStatus": "1 match",
            "previousReportedNoEarlierMatch": True, "sourceBytesUnchanged": True,
            "viewerClosed": True, "physicalF3AndShiftF3Verified": helper is not None,
            "changedQueryRetiredOldCount": True, "changedQueryVisibleMatchStatus": "0 matches",
            "geometry": geometry}


def launch_and_run(app_path, helper, narrow=False):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    if app_path.name != "FileCat" or validation not in app_path.parents:
        raise AssertionError("Use an isolated FileCat binary below FileCatValidation")
    root = validation / "TextViewerKeyboard" / uuid4().hex
    source, target, data = (root / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    (source / "a.txt").write_bytes(b"alpha\n")
    (source / "b.txt").write_bytes(b"beta\n")
    with app_path.open("rb") as binary:
        app_hash = hashlib.file_digest(binary, "sha256").hexdigest()
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    os.environ.setdefault("DISPLAY", ":0")
    if (validation / "dotnet-sdk" / "dotnet").is_file():
        os.environ.setdefault("DOTNET_ROOT", str(validation / "dotnet-sdk"))
    with (root / "app.log").open("wb") as log:
        process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                    "--left", str(source), "--right", str(target)],
                                   cwd=root, env=os.environ.copy(), stdout=log,
                                   stderr=subprocess.STDOUT)
        try:
            desktop = Atspi.get_desktop(0)
            wait_for(lambda: next((app for app in
                     (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                     if app.get_name() == "FileCat"
                     and find(app, "list", f"Panel 1, source, {source}")), None),
                     "isolated Text viewer fixture")
            result = run(source, helper, narrow=narrow)
            result.update({"app": str(app_path), "appSha256": app_hash,
                           "fixture": str(root)})
            return result
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)


def main():
    if len(sys.argv) in (5, 6) and sys.argv[1] == "--launch" and \
            (len(sys.argv) == 5 or sys.argv[5] == "--narrow"):
        report = Path(sys.argv[3])
        check = lambda: launch_and_run(Path(sys.argv[2]), Path(sys.argv[4]),
                                       narrow=len(sys.argv) == 6)
    elif len(sys.argv) in (3, 4):
        report = Path(sys.argv[2])
        check = lambda: run(Path(sys.argv[1]), Path(sys.argv[3]) if len(sys.argv) == 4 else None)
    else:
        raise SystemExit("Usage: linux-atspi-viewer-find.py SOURCE REPORT.json [UINPUT_HELPER] | --launch APP REPORT.json UINPUT_HELPER")
    try:
        result = check()
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
