#!/usr/bin/env python3
"""Exercise FileCat's range anchor with native Ubuntu touch and keyboard input.

Usage: linux-atspi-selection-keyboard.py APP_PATH REPORT.json KEY_HELPER TOUCH_HELPER [--narrow | --deactivate | --deactivate-narrow | --command-line | --command-line-narrow | --sort MOUSE_HELPER | --sort-control MOUSE_HELPER | --filter MOUSE_HELPER | --tab MOUSE_HELPER | --sort-narrow MOUSE_HELPER | --sort-control-narrow MOUSE_HELPER | --filter-narrow MOUSE_HELPER | --tab-narrow MOUSE_HELPER]
Run in the logged-in desktop session with an isolated Git-built app. The
caller passes a one-time sudo password through FILECAT_NATIVE_SUDO_PASSWORD;
the runner removes it before starting FileCat.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import select
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
            (len(sys.argv) == 6 and sys.argv[5] not in
             ("--narrow", "--deactivate", "--deactivate-narrow",
              "--command-line", "--command-line-narrow")) or
            (len(sys.argv) == 7 and sys.argv[5] not in
             ("--sort", "--sort-control", "--filter", "--tab", "--sort-narrow",
              "--sort-control-narrow", "--filter-narrow", "--tab-narrow"))):
        raise SystemExit(__doc__)
    narrow = len(sys.argv) >= 6 and sys.argv[5].endswith("narrow")
    deactivate_case = len(sys.argv) == 6 and sys.argv[5] in ("--deactivate", "--deactivate-narrow")
    command_line_case = len(sys.argv) == 6 and sys.argv[5] in ("--command-line", "--command-line-narrow")
    sort_control = len(sys.argv) == 7 and sys.argv[5] in ("--sort-control", "--sort-control-narrow")
    sort = len(sys.argv) == 7 and sys.argv[5] in ("--sort", "--sort-narrow", "--sort-control", "--sort-control-narrow")
    filter_case = len(sys.argv) == 7 and sys.argv[5] in ("--filter", "--filter-narrow")
    tab_case = len(sys.argv) == 7 and sys.argv[5] in ("--tab", "--tab-narrow")
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
    mouse_helper = Path(sys.argv[6]).resolve(strict=True) if sort or filter_case or tab_case else None
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
    if sort or filter_case or tab_case or deactivate_case or command_line_case:
        expected.update({"c.txt": b"charlie\n", "d.txt": b"delta\n"})
    for name, content in expected.items():
        (source / name).write_bytes(content)
    source_sha = {name: hashlib.sha256(content).hexdigest() for name, content in expected.items()}
    second = source / "second"
    second_expected = {"e.txt": b"echo\n", "f.txt": b"foxtrot\n"} if tab_case else {}
    if tab_case:
        second.mkdir()
        for name, content in second_expected.items():
            (second / name).write_bytes(content)
    second_sha = {name: hashlib.sha256(content).hexdigest()
                 for name, content in second_expected.items()}
    with app_path.open("rb") as binary:
        app_sha = hashlib.file_digest(binary, "sha256").hexdigest()
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    report = {"passed": False, "appSha256": app_sha, "input": "uinput touch + keyboard",
              "narrow": narrow, "sort": sort, "sortControl": sort_control,
              "filter": filter_case, "tab": tab_case,
              "deactivate": deactivate_case, "commandLine": command_line_case,
              "sourceSha256": source_sha, "secondSha256": second_sha, "steps": []}
    process = None
    witness = None
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
                             (" folder" in name and " files · " in name)), None)

            def focused():
                return [name for name in (*expected, *second_expected) if any(
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

            clear = ("0 folders, 4 files · 25 bytes" if sort or filter_case or deactivate_case or command_line_case else
                     "0 folders, 2 files · 11 bytes")
            marked = "Marked 1 of 2 files · 6 bytes"
            if tab_case:
                wait_for(lambda: len(named(app, "list item", "second")) == 1 and
                         all(named(app, "list item", name) for name in expected),
                         "the source files and second folder")
                clear = status()
                if clear is None or "4 files · 25 bytes" not in clear or not clear.startswith("1 folder"):
                    raise AssertionError(f"Unexpected two-tab source summary: {clear}")
            else:
                wait_for(lambda: status() == clear, "the complete owned listing")
            anchor = "d.txt" if tab_case else ("b.txt" if sort or filter_case or deactivate_case or command_line_case else "a.txt")
            row = wait_for(lambda: next(iter(named(app, "list item", anchor)), None),
                           f"the owned {anchor} row")
            bounds = row.get_component().get_extents(Atspi.CoordType.SCREEN)
            if bounds.width < 35 or bounds.height < 15:
                raise AssertionError(f"Source row has unusable bounds: {bounds}")
            activate_window(process.pid)
            native_click(round(bounds.x + min(40, bounds.width / 2)),
                         round(bounds.y + bounds.height / 2), touch_helper, sudo_password)
            snapshot(f"touch {anchor}", clear, anchor)
            if sort or filter_case or tab_case:
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
                elif tab_case:
                    key("ctrl+t")
                    folder = wait_for(lambda: next(iter(named(app, "list item", "second")), None),
                                      "second folder in the new tab")
                    folder_bounds = folder.get_component().get_extents(Atspi.CoordType.SCREEN)
                    activate_window(process.pid)
                    native_click(round(folder_bounds.x + min(40, folder_bounds.width / 2)),
                                 round(folder_bounds.y + folder_bounds.height / 2),
                                 touch_helper, sudo_password)
                    key("enter")
                    wait_for(lambda: named(app, "list", f"Panel 1, source, {second}") and
                             all(named(app, "list item", name) for name in second_expected),
                             "the second tab folder and its two files")
                    key("home")
                    key("down")
                    wait_for(lambda: focused() == ["e.txt"], "e.txt focused in the second tab")
                    report["steps"].append({"action": "new tab, enter second, focus e.txt",
                                            "focused": focused()})
                    target_name = "f.txt"
                    target_summary = "Marked 2 of 2 files · 13 bytes"
                    gesture = "Shift+click f.txt in second tab"
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

                header = next((node for node in descendants(app) if header_candidate(node)), None) if sort and not sort_control else None
                if sort_control:
                    lists = [node for node in descendants(app) if node.get_role_name() == "list" and
                             node.get_name().startswith("Panel ")]
                    buttons = [node for node in descendants(app)
                               if node.get_name() == "Sort files: Name, ascending"]
                    if len(lists) != 2 or len(buttons) != 2:
                        raise AssertionError(f"Expected two accessible panel Sort buttons: lists={len(lists)}, buttons={len(buttons)}")
                    found = {}
                    for listing in lists:
                        lb = listing.get_component().get_extents(Atspi.CoordType.SCREEN)
                        peers = []
                        for button in buttons:
                            rect = button.get_component().get_extents(Atspi.CoordType.SCREEN)
                            if (rect.width >= 25 and rect.height >= 12 and
                                    lb.x <= rect.x < lb.x + lb.width and rect.y < lb.y):
                                peers.append((button, rect))
                        if len(peers) != 1:
                            raise AssertionError(f"Expected one bounded Sort button above {listing.get_name()}: {len(peers)}")
                        found[listing.get_name()] = peers[0]
                    source_button, source_bounds = found[f"Panel 1, source, {source}"]
                    source_button_name = source_button.get_name()
                    target_button_name = next(node.get_name() for node in buttons
                                              if node is not source_button)
                    activate_window(process.pid)
                    native_click(round(source_bounds.x + source_bounds.width / 2),
                                 round(source_bounds.y + source_bounds.height / 2),
                                 touch_helper, sudo_password)
                    name_choice = wait_for(lambda: next((node for node in descendants(app)
                        if node.get_name() == "Sort by Name, currently ascending; activate to reverse"), None),
                        "the accessible current Name sort choice")
                    choice_bounds = name_choice.get_component().get_extents(Atspi.CoordType.SCREEN)
                    choice_name = name_choice.get_name()
                    report["sortChoiceBounds"] = [choice_bounds.x, choice_bounds.y,
                                                  choice_bounds.width, choice_bounds.height]
                    if choice_bounds.width < 25 or choice_bounds.height < 12:
                        raise AssertionError("The accessible Name sort choice has unusable bounds")
                    native_click(round(choice_bounds.x + choice_bounds.width / 2),
                                 round(choice_bounds.y + choice_bounds.height / 2),
                                 touch_helper, sudo_password)
                    wait_for(lambda: order() == ["d.txt", "c.txt", "b.txt", "a.txt"],
                             "descending name order from the Sort button")
                    report["sortControl"] = {"source": source_button_name,
                                             "target": target_button_name,
                                             "choice": choice_name,
                                             "after": source_button.get_name()}
                    report["steps"].append({"action": "sort Name descending through accessible Sort button",
                                            "order": order()})
                elif sort and header is None:
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
                if sort and not sort_control:
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
                                            "order": order() if not tab_case else
                                            [name for name in second_expected if named(app, "list item", name)]}
                snapshot(gesture, target_summary, target_name)
            elif deactivate_case or command_line_case:
                key("insert")
                snapshot("Insert marks b.txt and moves focus", "Marked 1 of 4 files · 5 bytes", "c.txt")
                key("up")
                snapshot("Up returns to marked b.txt", "Marked 1 of 4 files · 5 bytes", "b.txt")

                command_point = None
                if command_line_case:
                    fields = [node for node in descendants(app)
                              if node.get_name() == "Command line" and node.get_component() is not None]
                    if len(fields) != 1:
                        raise AssertionError(f"Expected one accessible command line, got {len(fields)}")
                    field_bounds = fields[0].get_component().get_extents(Atspi.CoordType.SCREEN)
                    if field_bounds.width < 40 or field_bounds.height < 15:
                        raise AssertionError(f"Command line has unusable bounds: {field_bounds}")
                    command_point = (round(field_bounds.x + min(80, field_bounds.width / 2)),
                                     round(field_bounds.y + field_bounds.height / 2))
                    report["commandLineBounds"] = [field_bounds.x, field_bounds.y,
                                                   field_bounds.width, field_bounds.height]

                if deactivate_case:
                    witness_code = (
                        "import gi; gi.require_version('Gtk','3.0'); "
                        "from gi.repository import Gtk; "
                        "w=Gtk.Window(title='FileCat native focus witness'); "
                        "w.set_default_size(240,120); w.show_all(); Gtk.main()"
                    )
                    witness_env = os.environ.copy()
                    witness_env["GDK_BACKEND"] = "x11"
                    witness = subprocess.Popen([sys.executable, "-c", witness_code],
                                               cwd=fixture, env=witness_env,
                                               stdout=subprocess.DEVNULL, stderr=subprocess.PIPE,
                                               text=True)

                def witness_id():
                    listing = subprocess.run(["/usr/bin/wmctrl", "-lp"], check=True,
                                             capture_output=True, text=True, timeout=5)
                    return next((line.split()[0] for line in listing.stdout.splitlines()
                                 if len(line.split()) > 2 and line.split()[2] == str(witness.pid)), None)

                other_window = wait_for(witness_id, "the test-owned focus witness", seconds=6) if deactivate_case else None
                activate_window(process.pid)
                release_signal = fixture / "shift-release.signal"
                held = subprocess.Popen(["sudo", "-S", sys.executable, str(key_helper),
                                         "shift+down-hold-controlled", str(release_signal)], stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                        text=True)
                try:
                    held.stdin.write(sudo_password + "\n")
                    held.stdin.flush()
                    ready, _, _ = select.select([held.stdout], [], [], 5)
                    if not ready or held.stdout.readline().strip() != "HELD":
                        raise AssertionError("Physical held-Shift input did not signal readiness")
                    snapshot("held Shift+Down unmarks b.txt", clear, "c.txt")
                    if deactivate_case:
                        if held.poll() is not None:
                            raise AssertionError("Shift was released before the focus witness activated")
                        subprocess.run(["/usr/bin/wmctrl", "-ia", other_window],
                                       check=True, timeout=5)
                        if held.poll() is not None:
                            raise AssertionError("Shift was released before the focus witness took focus")
                        active = subprocess.run(["/usr/bin/xdotool", "getactivewindow", "getwindowpid"],
                                                check=True, capture_output=True, text=True, timeout=5)
                        if active.stdout.strip() != str(witness.pid):
                            raise AssertionError("The test-owned witness did not deactivate FileCat")
                        report["steps"].append({"action": "deactivate FileCat while Shift is held",
                                                "witnessPid": witness.pid})
                    else:
                        if held.poll() is not None:
                            raise AssertionError("Shift was released before the command-line touch")
                        native_click(*command_point, touch_helper, sudo_password)
                        if held.poll() is not None:
                            raise AssertionError("Shift was released before command-line focus")
                        wait_for(lambda: fields[0].get_state_set().contains(Atspi.StateType.FOCUSED),
                                 "command-line focus while Shift is held")
                        report["steps"].append({"action": "focus command line while Shift is held",
                                                "focusedRole": fields[0].get_role_name()})
                    if held.poll() is not None:
                        raise AssertionError("Shift was released before the runner requested key-up")
                    release_signal.touch(exist_ok=False)
                    held.wait(timeout=5)
                    if held.returncode:
                        raise AssertionError(f"Held-Shift helper failed: {held.stderr.read()[-300:]}")
                finally:
                    if held.poll() is None:
                        held.terminate()
                        held.wait(timeout=3)
                    held.stdin.close()
                    held.stdout.close()
                    held.stderr.close()
                if command_line_case:
                    key("escape")
                    wait_for(lambda: focused() == ["c.txt"], "Escape returning focus to the source list")
                else:
                    activate_window(process.pid)
                key("shift+down")
                snapshot("fresh gesture after command-line focus" if command_line_case else
                         "fresh gesture after deactivation", "Marked 1 of 4 files · 8 bytes", "d.txt")
            else:
                key("shift+down")
                snapshot("Shift+Down marks a.txt", marked, "b.txt")
                key("up")
                snapshot("Up retains mark", marked, "a.txt")
                key("shift+down")
                snapshot("Shift+Down clears mark", clear, "b.txt")
            if ({name: hashlib.sha256((source / name).read_bytes()).hexdigest()
                    for name in expected} != source_sha or
                    {name: hashlib.sha256((second / name).read_bytes()).hexdigest()
                     for name in second_expected} != second_sha or list(target.iterdir())):
                raise AssertionError("Native selection changed the source or destination fixture")
            report.update({"passed": True, "sourceRowBounds": [bounds.x, bounds.y,
                           bounds.width, bounds.height], "fixtureUnchanged": True,
                           "targetEmpty": True})
    except Exception as exc:
        report["error"] = f"{type(exc).__name__}: {exc}"
        if witness is not None and witness.poll() is not None and witness.stderr is not None:
            report["witnessErrorTail"] = witness.stderr.read()[-500:]
        log_file = fixture / "app.log"
        if log_file.exists():
            report["appLogTail"] = log_file.read_text(errors="replace")[-1500:]
    finally:
        if witness is not None and witness.poll() is None:
            witness.terminate()
            try:
                witness.wait(timeout=5)
            except subprocess.TimeoutExpired:
                witness.kill()
                witness.wait(timeout=5)
        if witness is not None and witness.stderr is not None:
            witness.stderr.close()
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
