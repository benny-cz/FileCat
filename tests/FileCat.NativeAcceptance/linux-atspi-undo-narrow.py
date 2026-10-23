#!/usr/bin/env python3
"""Run a real Move/Undo and inspect narrow review and report at Ubuntu 200%.

Usage: linux-atspi-undo-narrow.py APP_PATH REPORT.json [--orca-review-speech]
Run in the logged-in Ubuntu VM's AT-SPI desktop session. This leaves only an
owned fixture below ~/FileCatValidation and closes only its own FileCat.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import runpy
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


def wait_for(check, description, seconds=30):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        result = check()
        if result:
            return result
        time.sleep(0.15)
    raise AssertionError(f"Timed out waiting for {description}")


def one(node, role, name):
    return next((child for child in descendants(node)
                 if child.get_role_name() == role and child.get_name() == name), None)


def dialog_button(app, name):
    for button in descendants(app):
        if button.get_role_name() != "button" or button.get_name() != name:
            continue
        parent = button
        for _ in range(8):
            parent = parent.get_parent() if parent is not None else None
            if parent is None:
                break
            if parent.get_name().endswith(" dialog"):
                return button
    return None


def invoke(node, description):
    if node is None or node.get_n_actions() < 1 or not node.do_action(0):
        raise AssertionError(f"Could not invoke {description}")


def bounds(node):
    area = node.get_component().get_extents(Atspi.CoordType.SCREEN)
    return [area.x, area.y, area.width, area.height]


def inside(frame, area):
    return (area[0] >= frame[0] and area[1] >= frame[1] and
            area[0] + area[2] <= frame[0] + frame[2] and
            area[1] + area[3] <= frame[1] + frame[3])


def resize_owned(pid, width, height, title_fragment="FileCat"):
    lines = subprocess.run(["/usr/bin/wmctrl", "-l", "-p"], check=True,
                           capture_output=True, text=True, timeout=5).stdout.splitlines()
    window_id = next((parts[0] for line in lines if (parts := line.split(maxsplit=3))
                      and len(parts) >= 4 and parts[2] == str(pid) and
                      title_fragment in parts[3]), None)
    if window_id is None:
        raise AssertionError("The exact FileCat process has no owned WM window")
    subprocess.run(["/usr/bin/wmctrl", "-i", "-r", window_id, "-e",
                    f"0,150,100,{width},{height}"], check=True, timeout=5)
    subprocess.run(["/usr/bin/wmctrl", "-ia", window_id], check=True, timeout=5)
    time.sleep(0.35)
    active = subprocess.run(["/usr/bin/xdotool", "getactivewindow", "getwindowpid"],
                            check=True, capture_output=True, text=True, timeout=5).stdout.strip()
    if active != str(pid):
        raise AssertionError("The exact FileCat process did not receive desktop focus")


def main():
    if len(sys.argv) not in (3, 4) or len(sys.argv) == 4 and sys.argv[3] != "--orca-review-speech":
        raise SystemExit(__doc__)
    orca_speech = len(sys.argv) == 4
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = Path(sys.argv[1]).resolve(strict=True)
    report_path = Path(sys.argv[2]).resolve()
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            report_path.parent != validation):
        raise SystemExit("Use an isolated Git-built FileCat and guest-local report")
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    os.environ.setdefault("DOTNET_ROOT", str(validation / "dotnet-sdk"))
    scale_text = subprocess.run(["/usr/bin/gdctl", "show"], check=True,
                                capture_output=True, text=True, timeout=5).stdout
    match = re.search(r"\bScale: ([0-9.]+)", scale_text)
    scale = float(match.group(1)) if match else None
    if scale != 2.0:
        raise SystemExit(f"The isolated Ubuntu display must be at 200%, got {scale}")
    if orca_speech and subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()),
                                      "-x", "orca"], capture_output=True).returncode == 0:
        raise SystemExit("Stop the existing Orca instance before the owned speech test")

    fixture = validation / "UndoNarrow" / uuid4().hex
    source = fixture / "source" / "active-client-work-with-a-long-project-location" / "daily-records"
    target = fixture / "target" / "archived-client-work-with-a-long-project-location" / "daily-records"
    data = fixture / "data"
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    name = "approved-work-item-with-a-long-filename.txt"
    source_file, target_file = source / name, target / name
    source_file.write_bytes(b"owned inverse path wrapping\n")
    digest = hashlib.sha256(source_file.read_bytes()).hexdigest()
    with app_path.with_name("FileCat.dll").open("rb") as assembly:
        app_hash = hashlib.file_digest(assembly, "sha256").hexdigest()
    result = {"passed": False, "fixture": str(fixture), "appDllSha256": app_hash,
              "displayScale": scale, "source": str(source_file), "target": str(target_file)}
    speech = shared_speech = None
    if orca_speech:
        shared_speech = runpy.run_path(str(Path(__file__).with_name("linux-orca-speech.py")))
        speech = shared_speech["SpeechLog"](fixture / "orca-debug.log")
        result["orcaSpeechLog"] = str(speech.path)
        result["speechSteps"] = []
    process = orca = None
    try:
        if orca_speech:
            with (fixture / "orca.err").open("wb") as error_log:
                orca = subprocess.Popen(["/usr/bin/python3", "-c", shared_speech["ORCA_WRAPPER"],
                                         str(speech.path)], stdout=subprocess.DEVNULL,
                                        stderr=error_log, env=os.environ.copy())
            wait_for(lambda: "Screen reader on" in " ".join(speech.take()),
                     "owned Orca startup", seconds=20)
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log,
                                       stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((candidate for candidate in
            (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
            if candidate.get_name() == "FileCat" and
            one(candidate, "list", f"Panel 1, source, {source}")), None),
            "isolated source panel")
        if orca_speech:
            resize_owned(process.pid, 1440, 1080)
        def spoken(label, action, expected):
            speech.take()
            if action() is False:
                raise AssertionError(f"Could not focus {label}")
            heard, deadline = [], time.monotonic() + 8
            while time.monotonic() < deadline:
                heard += speech.take()
                if expected.casefold() in " ".join(heard).casefold():
                    break
                time.sleep(0.15)
            passed = expected.casefold() in " ".join(heard).casefold()
            result["speechSteps"].append({"step": label, "expected": expected,
                                          "heard": heard, "passed": passed})
            return passed
        def focus_after(other, target):
            if not other.grab_focus():
                raise AssertionError("Could not move speech focus away from the target")
            time.sleep(0.25)
            speech.take()
            return target.grab_focus()
        row = wait_for(lambda: one(app, "list item", name), "exact source row")
        if not row.grab_focus():
            raise AssertionError("The owned source row could not receive focus")
        invoke(wait_for(lambda: one(app, "button", "Move / rename…"), "Move command"),
               "Move command")
        invoke(wait_for(lambda: dialog_button(app, "Move"), "Move approval"),
               "Move approval")
        wait_for(lambda: target_file.is_file() and not source_file.exists(),
                 "completed exact Move", seconds=60)
        if hashlib.sha256(target_file.read_bytes()).hexdigest() != digest:
            raise AssertionError("Original Move changed the fixture bytes")

        resize_owned(process.pid, 1440, 1080)
        invoke(wait_for(lambda: one(app, "button", "Undo"), "Undo action"), "Undo")
        approval = wait_for(lambda: dialog_button(app, "Run these inverse actions"),
                            "Undo review approval")
        main_frame = wait_for(lambda: next((node for node in descendants(app)
            if node.get_role_name() == "frame" and "FileCat" in node.get_name()), None),
            "owned FileCat frame")
        path_label = wait_for(lambda: next((node for node in descendants(app)
            if node.get_role_name() in ("label", "text") and
            str(source_file) in node.get_name() and str(target_file) in node.get_name()), None),
            "complete source and target in the Undo action")
        result["reviewAccessibleRows"] = [{"role": node.get_role_name(),
            "name": node.get_name(), "bounds": bounds(node)} for node in descendants(app)
            if node.get_role_name() in ("list item", "table row") and
            str(source_file) in node.get_name() and str(target_file) in node.get_name()]
        cancel = wait_for(lambda: dialog_button(app, "Cancel"), "Undo Cancel action")
        if orca_speech:
            review_row = wait_for(lambda: next((node for node in descendants(app)
                if node.get_role_name() == "list item" and
                str(source_file) in node.get_name() and str(target_file) in node.get_name()), None),
                "complete focusable Undo mapping")
            spoken("complete inverse mapping", lambda: focus_after(cancel, review_row),
                   str(source_file))
        review_bounds = {"frame": bounds(main_frame), "action": bounds(path_label),
                         "approve": bounds(approval), "cancel": bounds(cancel)}
        result["narrowReview"] = review_bounds
        if (not 1300 <= review_bounds["frame"][2] <= 1550 or
            review_bounds["action"][3] <= 60 or
            any(not inside(review_bounds["frame"], review_bounds[key])
                for key in ("action", "approve", "cancel"))):
            raise AssertionError("The 200% narrow Undo review clipped a full path or decision")
        invoke(approval, "inverse approval")
        wait_for(lambda: source_file.is_file() and not target_file.exists(),
                 "completed exact Undo", seconds=60)
        if hashlib.sha256(source_file.read_bytes()).hexdigest() != digest:
            raise AssertionError("Undo changed the original bytes")
        invoke(wait_for(lambda: one(app, "button", "Undo report"), "retained Undo report"),
               "retained Undo report")
        report_frame = wait_for(lambda: next((node for node in descendants(app)
            if node.get_role_name() == "frame" and "Undo report" in node.get_name()), None),
            "native Undo report")
        report_entry = wait_for(lambda: one(report_frame, "entry", "Undo report"),
                                "accessible retained Undo report")
        accessible_report = Atspi.Text.get_text(report_entry.get_text_iface(), 0, -1)
        result["accessibleReportFullPath"] = str(source_file) in accessible_report
        if not result["accessibleReportFullPath"]:
            raise AssertionError("The accessible retained report lost the restored path")
        resize_owned(process.pid, 1200, 920, "Undo report")
        copy_all = wait_for(lambda: one(report_frame, "button", "Copy all"),
                            "report Copy all")
        report_bounds = {"frame": bounds(report_frame), "copyAll": bounds(copy_all)}
        result["reportBounds"] = report_bounds
        if (not 1100 <= report_bounds["frame"][2] <= 1300 or
                not inside(report_bounds["frame"], report_bounds["copyAll"])):
            raise AssertionError("The narrow retained Undo report hides Copy all")
        reader = ("import gi; gi.require_version('Gtk','3.0'); "
                  "gi.require_version('Gdk','3.0'); from gi.repository import Gtk,Gdk; "
                  "Gtk.init([]); c=Gtk.Clipboard.get(Gdk.SELECTION_CLIPBOARD); "
                  "print(c.wait_for_text() or '')")
        invoke(copy_all, "Copy all from the retained Undo report")
        def copied_report():
            read = subprocess.run([sys.executable, "-c", reader], capture_output=True,
                                  text=True, timeout=5,
                                  env={**os.environ, "GDK_BACKEND": "x11"})
            copied = read.stdout if read.returncode == 0 else None
            if copied and "Undo:" in copied and str(source_file) in copied:
                return copied
            return None
        report_text = wait_for(copied_report, "complete copied Undo report", seconds=12)
        result["reportCopy"] = {"bytes": len(report_text.encode("utf-8")),
                                "fullRestoredPath": str(source_file) in report_text,
                                "undoHeader": "Undo:" in report_text}
        if orca_speech and not all(step["passed"] for step in result["speechSteps"]):
            raise AssertionError("Orca did not speak the complete Undo review mapping")
        result["passed"] = True
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
    finally:
        for owned in (process, orca):
            if owned is not None and owned.poll() is None:
                owned.terminate()
                try:
                    owned.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    owned.kill()
                    owned.wait(timeout=5)
        report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
