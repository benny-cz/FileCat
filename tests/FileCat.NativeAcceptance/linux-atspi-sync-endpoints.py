#!/usr/bin/env python3
"""Inspect both complete synchronization endpoints and Mirror's target on Ubuntu.

Usage: linux-atspi-sync-endpoints.py APP_PATH REPORT.json KEY_HELPER [--pause-review] [--orca-speech]
Run only in the logged-in Ubuntu VM against a Git-built app below
~/FileCatValidation. FILECAT_NATIVE_SUDO_PASSWORD is transient physical-input
authorization and is removed from FileCat's environment before launch.
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


shared = runpy.run_path(str(Path(__file__).with_name("linux-atspi-dialog-pointer-dismiss.py")))
descendants = shared["descendants"]
named = shared["named"]
wait_for = shared["wait_for"]
activate_window = shared["activate_window"]


def one(root, role, name):
    found = named(root, role, name)
    if len(found) != 1:
        raise AssertionError(f"Expected one {role} {name!r}, found {len(found)}")
    return found[0]


def action(node, description):
    if node.get_n_actions() < 1 or not node.do_action(0):
        raise AssertionError(f"Cannot invoke the native {description} action")


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    options = sys.argv[4:]
    if len(sys.argv) < 4 or len(options) != len(set(options)) or not set(options) <= {
            "--pause-review", "--orca-speech"}:
        raise SystemExit(__doc__)
    pause_review = "--pause-review" in options
    orca_speech = "--orca-speech" in options
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = Path(sys.argv[1]).resolve(strict=True)
    report_path = Path(sys.argv[2]).resolve()
    key_helper = Path(sys.argv[3]).resolve(strict=True)
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            report_path.parent != validation or key_helper.name != "linux-uinput-key.py" or
            validation not in key_helper.parents):
        raise SystemExit("Use an owned Git build and paths below ~/FileCatValidation")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise SystemExit("Transient guest physical-input authorization is required")
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    if orca_speech and subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", "orca"],
                                      capture_output=True).returncode == 0:
        raise SystemExit("Stop the existing Orca instance before the owned speech test")

    fixture = validation / "SyncEndpointsFixture" / uuid4().hex
    left = fixture / "source" / "active-client-work-with-a-long-location" / "Project"
    right = fixture / "target" / "archived-client-work-with-a-long-location" / "Project"
    data = fixture / "data"
    for path in (left, right, data):
        path.mkdir(parents=True)
    (left / "current.txt").write_bytes(b"new source content\n")
    (right / "old.txt").write_bytes(b"old target content\n")
    expected = {str(path): sha256(path) for path in (left / "current.txt", right / "old.txt")}
    result = {"passed": False, "app": str(app_path), "appDllSha256": sha256(app_path.with_name("FileCat.dll")),
              "fixture": str(fixture), "left": str(left), "right": str(right),
              "sourceSha256": expected, "steps": []}
    speech = None
    if orca_speech:
        shared_speech = runpy.run_path(str(Path(__file__).with_name("linux-orca-speech.py")))
        speech = shared_speech["SpeechLog"](fixture / "orca-debug.log")
        result["orcaSpeechLog"] = str(speech.path)
        result["speechSteps"] = []
    ready = report_path.with_suffix(".ready")
    resume = report_path.with_suffix(".resume")
    ready.unlink(missing_ok=True)
    resume.unlink(missing_ok=True)
    process = orca = None
    try:
        if orca_speech:
            with (fixture / "orca.err").open("wb") as error_log:
                orca = subprocess.Popen(["/usr/bin/python3", "-c", shared_speech["ORCA_WRAPPER"],
                                         str(speech.path)], stdout=subprocess.DEVNULL,
                                        stderr=error_log, env=os.environ.copy())
            deadline = time.monotonic() + 20
            while not any("Screen reader on" in phrase for phrase in speech.take()):
                if time.monotonic() > deadline or orca.poll() is not None:
                    raise AssertionError("Orca did not start in the isolated desktop")
                time.sleep(0.2)
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(left), "--right", str(right)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((candidate for candidate in
            (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
            if candidate.get_name() == "FileCat" and
            named(candidate, "list", f"Panel 1, source, {left}")), None),
            "isolated source panel", seconds=30)
        activate_window(process.pid)

        def send(chord):
            sent = subprocess.run(["sudo", "-S", sys.executable, str(key_helper), chord],
                                  input=sudo_password + "\n", capture_output=True, text=True, timeout=15)
            if sent.returncode:
                raise AssertionError(f"Physical {chord} failed: {sent.stderr[-300:]}")

        def spoken(label, move, expected_text):
            speech.take()
            if move() is False:
                raise AssertionError(f"Cannot focus {label}")
            heard, deadline = [], time.monotonic() + 8
            while time.monotonic() < deadline:
                heard += speech.take()
                if expected_text.casefold() in " ".join(heard).casefold():
                    break
                time.sleep(0.15)
            passed = expected_text.casefold() in " ".join(heard).casefold()
            result["speechSteps"].append({"step": label, "expected": expected_text,
                                          "heard": heard, "passed": passed})
            return passed

        search = one(app, "entry", "Search commands")
        if not search.get_editable_text().set_text_contents("compare directories"):
            raise AssertionError("Cannot enter Compare directories in native command search")
        if not search.grab_focus():
            raise AssertionError("Cannot focus native command search")
        send("enter")
        compare = wait_for(lambda: next(iter(named(app, "check box",
            "Include subfolders: a preview of every difference below, marking nothing")), None),
            "recursive compare choice")
        action(compare, "recursive compare")
        action(one(app, "button", "Compare"), "Compare")
        result["steps"].append("Compared two different folders with identical final names")

        sync_button = wait_for(lambda: next((node for node in named(app, "button", "Synchronize…")
            if node.get_state_set().contains(Atspi.StateType.ENABLED)), None),
            "completed recursive difference preview and Synchronize action", seconds=40)
        action(sync_button, "Synchronize preview")
        sync_window = wait_for(lambda: next((node for node in descendants(app)
            if node.get_role_name() == "frame" and node.get_name() == "Synchronize folders"), None),
            "owned synchronization preview")

        def endpoints():
            entries = [node for node in descendants(sync_window) if node.get_role_name() == "entry"]
            sources = [node for node in entries if node.get_name().startswith("Source folder: ")]
            targets = [node for node in entries if node.get_name().startswith("Target folder: ")]
            if len(sources) != 1 or len(targets) != 1:
                raise AssertionError(f"Expected one Source and one Target native field, got {len(sources)}, {len(targets)}")
            def value(node):
                return Atspi.Text.get_text(node.get_text_iface(), 0, -1)
            return sources[0], targets[0], value(sources[0]), value(targets[0])

        source_field, target_field, source_text, target_text = endpoints()
        if (source_text != str(left) or target_text != str(right) or
                source_field.get_name() != "Source folder: " + str(left) or
                target_field.get_name() != "Target folder: " + str(right)):
            raise AssertionError("The first direction does not show complete, distinct Source and Target paths")
        result["steps"].append("Full selectable Source and Target paths distinguish identical basenames")
        if orca_speech:
            spoken("Source endpoint, first direction", source_field.grab_focus, str(left))
            spoken("Target endpoint, first direction", target_field.grab_focus, str(right))

        direction = next((node for node in descendants(sync_window)
                          if node.get_role_name() == "combo box" and
                          node.get_name().startswith("Synchronization direction: from ")), None)
        mode = one(sync_window, "combo box", "Mode")
        if direction is None or not direction.grab_focus():
            raise AssertionError("The native direction selector is unavailable")
        send("down")
        wait_for(lambda: endpoints()[2:] == (str(right), str(left)), "reversed full endpoint roles")
        result["steps"].append("Direction reversal swapped complete endpoint roles")
        if orca_speech:
            source_field, target_field, _, _ = endpoints()
            spoken("Source endpoint, reversed direction", source_field.grab_focus, str(right))
            spoken("Target endpoint, reversed direction", target_field.grab_focus, str(left))
        if not mode.grab_focus():
            raise AssertionError("The native synchronization mode is not focusable")
        if orca_speech:
            spoken("Mirror target warning", lambda: send("down"), str(left))
        else:
            send("down")
        warning = wait_for(lambda: next((node.get_name() for node in descendants(sync_window)
            if "Mirror changes the target" in node.get_name() and str(left) in node.get_name()), None),
            "Mirror warning naming the currently selected target")
        result["mirrorWarning"] = warning
        result["steps"].append("Mirror warning names the reversed, full target path")

        sizes = subprocess.run(["/usr/bin/gdctl", "show"], check=True, capture_output=True,
                               text=True, timeout=5).stdout
        match = re.search(r"\bScale: ([0-9.]+)", sizes)
        result["displayScale"] = float(match.group(1)) if match else None
        if result["displayScale"] != 2.0:
            raise AssertionError("Native sync review was not checked at the VM's 200% display scale")
        result["steps"].append("200% native display scale confirmed")
        subprocess.run(["/usr/bin/wmctrl", "-r", "Synchronize folders", "-e", "0,250,150,1280,1000"],
                       check=True, timeout=5)
        time.sleep(0.5)
        def bounds(node):
            area = node.get_component().get_extents(Atspi.CoordType.SCREEN)
            return [area.x, area.y, area.width, area.height]
        frame = bounds(sync_window)
        source_bounds = bounds(source_field)
        target_bounds = bounds(target_field)
        direction_bounds = bounds(direction)
        mode_bounds = bounds(mode)
        cancel_bounds = bounds(one(sync_window, "button", "Cancel"))
        result["narrowBounds"] = {"frame": frame, "source": source_bounds,
                                  "target": target_bounds, "direction": direction_bounds,
                                  "mode": mode_bounds, "cancel": cancel_bounds}
        if not (1200 <= frame[2] <= 1500 and mode_bounds[1] > direction_bounds[1] and
                source_bounds[3] > 70 and target_bounds[3] > 70):
            raise AssertionError("The narrow scaled review did not reflow controls and wrap both paths")
        for label, area in result["narrowBounds"].items():
            if label == "frame":
                continue
            if (area[0] < frame[0] or area[1] < frame[1] or
                    area[0] + area[2] > frame[0] + frame[2] or
                    area[1] + area[3] > frame[1] + frame[3]):
                raise AssertionError(f"Narrow scaled review clips {label}: {area} outside {frame}")
        result["steps"].append("Narrow 200% review wraps both full paths and keeps choices inside the dialog")
        if pause_review:
            ready.write_text("Capture the 200% sync review, then create the matching .resume file.\n")
            wait_for(lambda: resume.exists(), "native review capture signal", seconds=120)
        action(one(sync_window, "button", "Cancel"), "Cancel synchronization")
        for path, digest in expected.items():
            if sha256(Path(path)) != digest:
                raise AssertionError(f"Cancel changed owned bytes: {path}")
        if (sorted(p.name for p in left.iterdir()) != ["current.txt"] or
                sorted(p.name for p in right.iterdir()) != ["old.txt"]):
            raise AssertionError("Cancel changed the owned synchronization fixture")
        result["steps"].append("Cancel preserved both exact folder trees and bytes")
        if orca_speech and not all(step["passed"] for step in result["speechSteps"]):
            raise AssertionError("Orca did not speak every complete endpoint and Mirror target")
        result["passed"] = True
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
    finally:
        ready.unlink(missing_ok=True)
        resume.unlink(missing_ok=True)
        for owned in (process, orca):
            if owned is not None and owned.poll() is None:
                owned.terminate()
                try:
                    owned.wait(10)
                except subprocess.TimeoutExpired:
                    owned.kill()
                    owned.wait(5)
        report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
