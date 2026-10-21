#!/usr/bin/env python3
"""Check native Ubuntu Markdown Page Find through AT-SPI and WebKitGTK.

Usage: linux-atspi-markdown-find.py SOURCE REPORT.json [UINPUT_HELPER]
   or: linux-atspi-markdown-find.py --launch APP REPORT.json UINPUT_HELPER
SOURCE is an isolated FileCat panel with a.txt = b"alpha\n" and b.txt =
b"beta\n". Run in its logged-in desktop session. Requires gi/Atspi 2.0,
wmctrl, and linux-atspi-viewer-find.py beside this script.
"""

import json
import hashlib
import os
from pathlib import Path
import runpy
import subprocess
import sys
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


helpers = runpy.run_path(str(Path(__file__).with_name("linux-atspi-viewer-find.py")))
descendants = helpers["descendants"]
find = helpers["find"]
wait_for = helpers["wait_for"]
checked_action = helpers["checked_action"]
close_viewer = helpers["close_viewer"]
send_key = helpers["send_key"]

NAME = "native-page-search.md"
CONTENT = b"# Native Page Search\n\nThis marker is first.\n\n## Next\n\nThis marker is second.\n"


def label_containing(node, text):
    return next((child for child in descendants(node)
                 if child.get_role_name() == "label" and text in child.get_name()), None)


def run(source, helper=None):
    source = source.resolve(strict=True)
    if (source / "a.txt").read_bytes() != b"alpha\n" or (source / "b.txt").read_bytes() != b"beta\n":
        raise AssertionError("The isolated Markdown fixture's source bytes changed")
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
        if helper is not None:
            helper = helper.resolve(strict=True)
            if not str(helper).startswith(str(Path.home() / "FileCatValidation") + os.sep):
                raise AssertionError("The keyboard helper is outside the validation folder")
            # Reset the count first. A physical shortcut that never reaches the
            # viewer cannot pass merely because an earlier button search did.
            for chord in ("f3", "shift+f3"):
                if not entry.set_text_contents(""):
                    raise AssertionError("Could not clear the Page Find query")
                wait_for(lambda: find(viewer, "label", "2 matches") is None,
                         "the previous Page count to retire")
                if not entry.set_text_contents("marker"):
                    raise AssertionError("Could not restore the Page Find query")
                send_key(helper, chord)
                wait_for(lambda: find(viewer, "label", "2 matches"),
                         f"the physical {chord} Page match count")
                wait_for(lambda: label_containing(viewer, 'Found "marker" in the page.'),
                         f"the physical {chord} Page result")
        if fixture.read_bytes() != CONTENT:
            raise AssertionError("Page search changed the Markdown bytes")
        if (source / "a.txt").read_bytes() != b"alpha\n" or (source / "b.txt").read_bytes() != b"beta\n":
            raise AssertionError("Page search changed the adjacent fixture bytes")
        return {"passed": True, "platform": "Ubuntu", "mode": "Page",
                "query": "marker", "visibleMatchStatus": "2 matches",
                "nextTwiceAndPreviousFound": True, "sourceBytesUnchanged": True,
                "physicalF3AndShiftF3Verified": helper is not None}
    finally:
        try:
            if find(app, "frame", title):
                close_viewer(app, title, pid)
        finally:
            if fixture.exists() and fixture.read_bytes() == CONTENT:
                fixture.unlink()
                checked_action(find(app, "button", "Reread"), "post-test refresh")


def launch_and_run(app_path, helper):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    if app_path.name != "FileCat" or validation not in app_path.parents:
        raise AssertionError("Use an isolated FileCat binary below FileCatValidation")
    root = validation / "MarkdownViewerKeyboard" / uuid4().hex
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
                     "isolated Markdown viewer fixture")
            result = run(source, helper)
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
    if len(sys.argv) == 5 and sys.argv[1] == "--launch":
        report = Path(sys.argv[3])
        check = lambda: launch_and_run(Path(sys.argv[2]), Path(sys.argv[4]))
    elif len(sys.argv) in (3, 4):
        report = Path(sys.argv[2])
        check = lambda: run(Path(sys.argv[1]), Path(sys.argv[3]) if len(sys.argv) == 4 else None)
    else:
        raise SystemExit("Usage: linux-atspi-markdown-find.py SOURCE REPORT.json [UINPUT_HELPER] | --launch APP REPORT.json UINPUT_HELPER")
    try:
        result = check()
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
