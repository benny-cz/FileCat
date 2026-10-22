#!/usr/bin/env python3
"""Exercise FileCat's range anchor with native Ubuntu touch and keyboard input.

Usage: linux-atspi-selection-keyboard.py APP_PATH REPORT.json KEY_HELPER TOUCH_HELPER [--narrow | --sort MOUSE_HELPER | --filter MOUSE_HELPER]
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
    if (len(sys.argv) not in (5, 6, 7) or
            (len(sys.argv) == 6 and sys.argv[5] != "--narrow") or
            (len(sys.argv) == 7 and sys.argv[5] not in ("--sort", "--filter"))):
        raise SystemExit(__doc__)
    narrow = len(sys.argv) == 6
    sort = len(sys.argv) == 7 and sys.argv[5] == "--sort"
    filter_case = len(sys.argv) == 7 and sys.argv[5] == "--filter"
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
    mouse_helper = Path(sys.argv[6]).resolve(strict=True) if sort or filter_case else None
    if mouse_helper is not None and (mouse_helper.name != "linux-uinput-mouse.py" or
                                     validation not in mouse_helper.parents):
        raise SystemExit("Use the owned transient uinput mouse helper")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise SystemExit("A transient guest sudo credential is required for physical test input")

    fixture = validation / "SelectionKeyboardFixture" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    expected = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    if sort or filter_case:
        expected.update({"c.txt": b"charlie\n", "d.txt": b"delta\n"})
    for name, content in expected.items():
        (source / name).write_bytes(content)
    source_sha = {name: hashlib.sha256(content).hexdigest() for name, content in expected.items()}
    with app_path.open("rb") as binary:
        app_sha = hashlib.file_digest(binary, "sha256").hexdigest()
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    report = {"passed": False, "appSha256": app_sha, "input": "uinput touch + keyboard",
              "narrow": narrow, "sort": sort, "filter": filter_case,
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
                             name.startswith(f"0 folders, {len(expected)} files · ")), None)

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

            clear = ("0 folders, 4 files · 25 bytes" if sort or filter_case else
                     "0 folders, 2 files · 11 bytes")
            marked = "Marked 1 of 2 files · 6 bytes"
            wait_for(lambda: status() == clear, "the complete owned listing")
            anchor = "b.txt" if sort or filter_case else "a.txt"
            row = wait_for(lambda: next(iter(named(app, "list item", anchor)), None),
                           f"the owned {anchor} row")
            bounds = row.get_component().get_extents(Atspi.CoordType.SCREEN)
            if bounds.width < 35 or bounds.height < 15:
                raise AssertionError(f"Source row has unusable bounds: {bounds}")
            activate_window(process.pid)
            native_click(round(bounds.x + min(40, bounds.width / 2)),
                         round(bounds.y + bounds.height / 2), touch_helper, sudo_password)
            snapshot(f"touch {anchor}", clear, anchor)
            if sort or filter_case:
                def order():
                    rows = []
                    for name in expected:
                        items = named(app, "list item", name)
                        if len(items) > 1:
                            return []
                        if items:
                            rows.append((items[0].get_component().get_extents(Atspi.CoordType.SCREEN).y,
                                         name))
                    return [name for _, name in sorted(rows)]

                if filter_case:
                    source_list = named(app, "list", f"Panel 1, source, {source}")[0]
                    list_bounds = source_list.get_component().get_extents(Atspi.CoordType.SCREEN)
                    candidates = []
                    for node in descendants(app):
                        if node.get_name() != "Filter":
                            continue
                        component = node.get_component()
                        if component is None:
                            continue
                        extent = component.get_extents(Atspi.CoordType.SCREEN)
                        if (extent.width >= 30 and extent.height >= 15 and
                                list_bounds.x <= extent.x < list_bounds.x + list_bounds.width and
                                extent.y < bounds.y):
                            candidates.append((node, extent))
                    if len(candidates) != 1:
                        raise AssertionError(f"Expected one source Filter entry, got {len(candidates)}")
                    filter_bounds = candidates[0][1]
                    filter_point = (round(filter_bounds.x + filter_bounds.width / 2),
                                    round(filter_bounds.y + filter_bounds.height / 2))

                    def set_filter(text_chord):
                        activate_window(process.pid)
                        native_click(*filter_point, touch_helper, sudo_password)
                        key("ctrl+a")
                        key(text_chord)
                        key("enter")

                    set_filter("filter-hide")
                    wait_for(lambda: order() == ["a.txt", "c.txt", "d.txt"] and
                             not named(app, "list item", "b.txt"),
                             "the filtered listing without b.txt")
                    report["steps"].append({"action": "hide b.txt with *|b*",
                                            "order": order()})
                    set_filter("filter-reset")
                    wait_for(lambda: order() == ["a.txt", "b.txt", "c.txt", "d.txt"],
                             "the restored four-file listing")
                    report["steps"].append({"action": "restore all files with *",
                                            "order": order()})
                    target_name = "d.txt"
                    target_summary = "Marked 2 of 4 files · 14 bytes"
                    gesture = "Shift+click d.txt after filter"
                else:
                    target_name = "c.txt"
                    target_summary = "Marked 2 of 4 files · 13 bytes"
                    gesture = "Shift+click c.txt after sort"

                def header_candidate(node):
                    if not node.get_name().startswith("Name") or node.get_role_name() == "list item":
                        return False
                    component = node.get_component()
                    if component is None:
                        return False
                    extent = component.get_extents(Atspi.CoordType.SCREEN)
                    return (extent.width >= 30 and extent.height >= 12 and
                            bounds.y - 80 <= extent.y < bounds.y)

                header = next((node for node in descendants(app) if header_candidate(node)), None) if sort else None
                if sort and header is None:
                    # Avalonia's Linux bridge does not expose this painted header as a
                    # separate AT-SPI node. Locate its bounded first row directly
                    # above the parent and file rows, then prove the clicked column
                    # by observing an actual name-order reversal.
                    source_list = named(app, "list", f"Panel 1, source, {source}")[0]
                    list_bounds = source_list.get_component().get_extents(Atspi.CoordType.SCREEN)
                    if not 40 <= bounds.y - list_bounds.y <= 70 or list_bounds.width < 120:
                        raise AssertionError("The Name header cannot be bounded from the source list")
                    header_point = (list_bounds.x + 60, list_bounds.y + 11)
                    report["headerInput"] = "bounded first column above the source rows"
                elif sort:
                    header_bounds = header.get_component().get_extents(Atspi.CoordType.SCREEN)
                    header_point = (round(header_bounds.x + header_bounds.width / 2),
                                    round(header_bounds.y + header_bounds.height / 2))
                    report["headerInput"] = "AT-SPI named Name header"
                if sort:
                    report["headerPoint"] = header_point
                    for attempt in range(2):
                        activate_window(process.pid)
                        native_click(*header_point,
                                     touch_helper, sudo_password)
                        try:
                            wait_for(lambda: order() == ["d.txt", "c.txt", "b.txt", "a.txt"],
                                     "descending name order", seconds=3)
                            break
                        except AssertionError:
                            if attempt == 1:
                                raise
                    report["steps"].append({"action": "sort Name descending", "order": order()})
                target_row = named(app, "list item", target_name)[0]
                target_bounds = target_row.get_component().get_extents(Atspi.CoordType.SCREEN)
                geometry = subprocess.run(["/usr/bin/xdotool", "getdisplaygeometry"],
                                          check=True, capture_output=True, text=True, timeout=5)
                width, height = (int(value) for value in geometry.stdout.split())
                click_point = (round(target_bounds.x + min(40, target_bounds.width / 2)),
                               round(target_bounds.y + target_bounds.height / 2))
                report["shiftClickTarget"] = {"rowBounds": [target_bounds.x, target_bounds.y,
                                               target_bounds.width, target_bounds.height],
                                               "point": click_point}
                activate_window(process.pid)

                def mouse(mode, point):
                    sent = subprocess.run(["sudo", "-S", sys.executable, str(mouse_helper),
                                           str(point[0]), str(point[1]),
                                           str(width), str(height), mode],
                                          input=sudo_password + "\n", capture_output=True,
                                          text=True, timeout=15)
                    if sent.returncode:
                        raise AssertionError(f"Physical {mode} input failed: {sent.stderr[-300:]}")

                def pointer_position():
                    pointer = subprocess.run(["/usr/bin/xdotool", "getmouselocation", "--shell"],
                                             check=True, capture_output=True, text=True, timeout=5)
                    fields = dict(line.split("=", 1) for line in pointer.stdout.splitlines()
                                  if "=" in line)
                    return (int(fields["X"]), int(fields["Y"]))

                # This VM's compositor accelerates even one-pixel uinput steps.
                # Measure the real pointer position before sending a button event;
                # a missed row must fail the test without changing selection.
                requested = list(click_point)
                report["pointerCalibration"] = []
                for _ in range(6):
                    mouse("move", requested)
                    actual = pointer_position()
                    report["pointerCalibration"].append({"requested": requested[:],
                                                         "actual": actual})
                    if (target_bounds.x + 5 <= actual[0] < target_bounds.x + min(80, target_bounds.width - 5)
                            and target_bounds.y + 3 <= actual[1] < target_bounds.y + target_bounds.height - 3):
                        break
                    requested = [max(0, min(limit - 1, requested[axis] + click_point[axis] - actual[axis]))
                                 for axis, limit in enumerate((width, height))]
                else:
                    raise AssertionError(f"Physical pointer could not be calibrated inside {target_name}")
                mouse("shift-current", (0, 0))
                report["pointerAfterShiftClick"] = pointer_position()
                report["postShiftClick"] = {"status": status(), "focused": focused(),
                                            "order": order()}
                snapshot(gesture, target_summary, target_name)
            else:
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
