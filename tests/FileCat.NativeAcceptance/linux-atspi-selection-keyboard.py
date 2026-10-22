#!/usr/bin/env python3
"""Exercise FileCat's range anchor with native Ubuntu touch and keyboard input.

Usage: linux-atspi-selection-keyboard.py APP_PATH REPORT.json KEY_HELPER TOUCH_HELPER [--narrow]
Run in the logged-in desktop session with an isolated Git-built app. The
caller passes a one-time sudo password through FILECAT_NATIVE_SUDO_PASSWORD;
the runner removes it before starting FileCat.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


shared = runpy.run_path(str(Path(__file__).with_name("linux-atspi-dialog-pointer-dismiss.py")))
descendants = shared["descendants"]
wait_for = shared["wait_for"]
named = shared["named"]
activate_window = shared["activate_window"]
native_click = shared["native_click"]


def main():
    if len(sys.argv) not in (5, 6) or (len(sys.argv) == 6 and sys.argv[5] != "--narrow"):
        raise SystemExit(__doc__)
    narrow = len(sys.argv) == 6
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, key_helper, touch_helper = (Path(value).resolve(strict=True)
                                          for value in (sys.argv[1], sys.argv[3], sys.argv[4]))
    report_path = Path(sys.argv[2]).resolve()
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            report_path.parent != validation or
            key_helper.name != "linux-uinput-key.py" or
            touch_helper.name != "linux-uinput-touch.py" or
            any(validation not in helper.parents for helper in (key_helper, touch_helper))):
        raise SystemExit("Use the isolated FileCat build, owned helpers and report under ~/FileCatValidation")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise SystemExit("A transient guest sudo credential is required for physical test input")

    fixture = validation / "SelectionKeyboardFixture" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    expected = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    for name, content in expected.items():
        (source / name).write_bytes(content)
    source_sha = {name: hashlib.sha256(content).hexdigest() for name, content in expected.items()}
    with app_path.open("rb") as binary:
        app_sha = hashlib.file_digest(binary, "sha256").hexdigest()
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    report = {"passed": False, "appSha256": app_sha, "input": "uinput touch + keyboard",
              "narrow": narrow,
              "sourceSha256": source_sha, "steps": []}
    process = None
    try:
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log,
                                       stderr=subprocess.STDOUT)
            desktop = Atspi.get_desktop(0)
            app = wait_for(lambda: next((candidate for candidate in
                (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                if candidate.get_name() == "FileCat" and
                named(candidate, "list", f"Panel 1, source, {source}")), None),
                "the isolated FileCat source panel")
            if narrow:
                windows = subprocess.run(["/usr/bin/wmctrl", "-lp"], check=True,
                                         capture_output=True, text=True, timeout=5)
                ids = [line.split()[0] for line in windows.stdout.splitlines()
                       if len(line.split()) > 2 and line.split()[2] == str(process.pid)]
                if len(ids) != 1:
                    raise AssertionError(f"Expected one owned window to resize, got {ids}")
                subprocess.run(["/usr/bin/wmctrl", "-ir", ids[0], "-e", "0,100,80,720,540"],
                               check=True, timeout=5)

                def geometry():
                    raw = subprocess.run(["/usr/bin/xdotool", "getwindowgeometry", "--shell", ids[0]],
                                         check=True, capture_output=True, text=True, timeout=5).stdout
                    values = dict(line.split("=", 1) for line in raw.splitlines() if "=" in line)
                    return int(values["WIDTH"]), int(values["HEIGHT"])

                def narrow_geometry():
                    width, height = geometry()
                    return (width, height) if 700 <= width <= 800 and 500 <= height <= 600 else None

                settled = wait_for(narrow_geometry, "the narrow 720 by 540 window")
                report["windowSize"] = settled

            def status():
                names = [node.get_name() for node in descendants(app)]
                return next((name for name in names if name.startswith("Marked ") or
                             name == "0 folders, 2 files · 11 bytes"), None)

            def focused():
                return [name for name in expected if any(
                    item.get_state_set().contains(Atspi.StateType.FOCUSED)
                    for item in named(app, "list item", name))]

            def snapshot(label, summary, focus):
                wait_for(lambda: status() == summary and focused() == [focus],
                         f"{label}: {summary}, focus {focus}")
                report["steps"].append({"action": label, "status": status(), "focused": focused()})

            def key(chord):
                activate_window(process.pid)
                sent = subprocess.run(["sudo", "-S", sys.executable, str(key_helper), chord],
                                      input=sudo_password + "\n", capture_output=True,
                                      text=True, timeout=15)
                if sent.returncode:
                    raise AssertionError(f"Physical {chord} input failed: {sent.stderr[-300:]}")

            clear = "0 folders, 2 files · 11 bytes"
            marked = "Marked 1 of 2 files · 6 bytes"
            wait_for(lambda: status() == clear, "the complete two-file listing")
            row = wait_for(lambda: next(iter(named(app, "list item", "a.txt")), None),
                           "the owned a.txt row")
            bounds = row.get_component().get_extents(Atspi.CoordType.SCREEN)
            if bounds.width < 35 or bounds.height < 15:
                raise AssertionError(f"Source row has unusable bounds: {bounds}")
            activate_window(process.pid)
            native_click(round(bounds.x + min(40, bounds.width / 2)),
                         round(bounds.y + bounds.height / 2), touch_helper, sudo_password)
            snapshot("touch a.txt", clear, "a.txt")
            key("shift+down")
            snapshot("Shift+Down marks a.txt", marked, "b.txt")
            key("up")
            snapshot("Up retains mark", marked, "a.txt")
            key("shift+down")
            snapshot("Shift+Down clears mark", clear, "b.txt")
            if {name: hashlib.sha256((source / name).read_bytes()).hexdigest()
                    for name in expected} != source_sha or list(target.iterdir()):
                raise AssertionError("Native selection changed the source or destination fixture")
            report.update({"passed": True, "sourceRowBounds": [bounds.x, bounds.y,
                           bounds.width, bounds.height], "fixtureUnchanged": True,
                           "targetEmpty": True})
    except Exception as exc:
        report["error"] = f"{type(exc).__name__}: {exc}"
        log_file = fixture / "app.log"
        if log_file.exists():
            report["appLogTail"] = log_file.read_text(errors="replace")[-1500:]
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        shutil.rmtree(fixture)
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
