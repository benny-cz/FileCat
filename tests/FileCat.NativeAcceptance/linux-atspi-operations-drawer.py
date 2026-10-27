#!/usr/bin/env python3
"""Check the native Operations drawer at 200% scale in an isolated Ubuntu VM.

Usage: linux-atspi-operations-drawer.py APP REPORT.json KEY_HELPER POINTER_HELPER
The runner owns its fixture, FileCat process and transient pointer device.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import signal
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


def unique(root, role, name):
    matches = named(root, role, name)
    if len(matches) != 1:
        raise AssertionError(f"Expected one {role} {name!r}, found {len(matches)}")
    return matches[0]


def bounds(node):
    rect = node.get_component().get_extents(Atspi.CoordType.SCREEN)
    return [rect.x, rect.y, rect.width, rect.height]


def within(inner, outer):
    return (inner[0] >= outer[0] and inner[1] >= outer[1] and
            inner[0] + inner[2] <= outer[0] + outer[2] + 2 and
            inner[1] + inner[3] <= outer[1] + outer[3] + 2)


def main(app_path, report_path, key_helper, pointer_helper):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, key_helper, pointer_helper = (p.resolve(strict=True) for p in
                                           (app_path, key_helper, pointer_helper))
    if (app_path.name != "FileCat" or key_helper.name != "linux-uinput-key.py" or
            pointer_helper.name != "linux-uinput-pointer.py" or
            any(validation not in p.parents for p in (app_path, key_helper, pointer_helper)) or
            report_path.parent.resolve() != validation):
        raise AssertionError("Use only the owned Git app, helpers and report")
    password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not password:
        raise AssertionError("Transient guest authentication is required for physical input")
    os.environ["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    fixture = validation / "OperationsDrawerFixture" / uuid4().hex
    source, target, data = (fixture / leaf for leaf in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    payload = source / "a.txt"
    payload.write_bytes(b"FileCat native operations drawer\n")
    digest = hashlib.sha256(payload.read_bytes()).hexdigest()
    result = {"passed": False, "buildDllSha256": hashlib.sha256(
        app_path.with_name("FileCat.dll").read_bytes()).hexdigest(),
        "displayScale": "200%", "fixture": str(fixture), "steps": []}
    process = pointer = None
    try:
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log,
                                       stderr=subprocess.STDOUT, start_new_session=True)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((candidate for candidate in
            (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
            if candidate.get_name() == "FileCat" and
            named(candidate, "list", f"Panel 1, source, {source}")), None), "isolated FileCat", seconds=25)

        def key(chord, reactivate=True):
            if reactivate:
                activate_window(process.pid)
            sent = subprocess.run(["sudo", "-S", sys.executable, str(key_helper), chord],
                                  input=password + "\n", capture_output=True, text=True, timeout=15)
            if sent.returncode:
                raise AssertionError(f"Physical {chord} failed: {sent.stderr[-250:]}")

        row = wait_for(lambda: next((n for n in descendants(app)
            if n.get_role_name() == "list item" and n.get_name().startswith("a.txt, file,")), None),
            "source file row")
        if not row.grab_focus():
            raise AssertionError("Could not focus the exact source row")
        key("f5")
        copy = wait_for(lambda: next((button for button in named(app, "button", "Copy")
            if any(parent.get_name() == "Copy dialog" for parent in ancestors(button))), None),
            "Copy dialog approval")
        if not copy.do_action(0):
            raise AssertionError("Could not start the isolated Copy")
        copied = target / payload.name
        wait_for(lambda: copied.is_file() and hashlib.sha256(copied.read_bytes()).hexdigest() == digest,
                 "exact copied bytes", seconds=20)
        wait_for(lambda: bool(named(app, "button", "Details (Ctrl+J)")), "operations Details")
        if not unique(app, "button", "Details (Ctrl+J)").do_action(0):
            raise AssertionError("Could not open Operations details")
        grip = wait_for(lambda: next((n for n in descendants(app)
            if n.get_name() == "Resize operations details"), None), "drawer resize grip")
        report = unique(app, "button", "Open report")
        history = unique(app, "list", "Operations history")
        before = bounds(grip)
        result["steps"].append({"initialWindow": bounds(next(n for n in descendants(app)
            if n.get_role_name() == "frame")), "initialHistory": bounds(history),
            "initialReport": bounds(report)})
        if not grip.grab_focus():
            raise AssertionError("The native drawer grip did not accept keyboard focus")
        result["steps"].append({"keyboardGripBefore": before,
                                "gripFocused": grip.get_state_set().contains(Atspi.StateType.FOCUSED)})
        key("down", reactivate=False)
        time.sleep(0.5)
        result["steps"][-1]["afterDown"] = bounds(grip)
        result["steps"][-1]["gripFocusedAfterDown"] = grip.get_state_set().contains(Atspi.StateType.FOCUSED)
        result["steps"][-1]["focusedNodesAfterDown"] = [n.get_name() for n in descendants(app)
            if n.get_state_set().contains(Atspi.StateType.FOCUSED)][:10]
        keyboard_changed = bounds(grip)[1] > before[1] + 8
        if not keyboard_changed:
            raise AssertionError("Focused physical Down did not shrink the drawer")
        if keyboard_changed:
            key("up", reactivate=False)
            wait_for(lambda: bounds(grip)[1] < result["steps"][-1]["afterDown"][1] - 8,
                     "keyboard drawer expansion")
        result["steps"][-1].update({"keyboardGripAfter": bounds(grip),
                                "reportReachable": report.get_state_set().contains(Atspi.StateType.SHOWING),
                                "historyVisible": history.get_state_set().contains(Atspi.StateType.SHOWING)})

        # XTest positions the pointer; the actual press, motion and release come
        # from one short-lived uinput device. Measure the final geometry, not
        # an assumed physical-pixel to DIP conversion.
        rect = bounds(grip)
        x, y = rect[0] + min(rect[2] // 2, 80), rect[1] + rect[3] // 2
        activate_window(process.pid)
        subprocess.run(["/usr/bin/xdotool", "mousemove", "--sync", str(x), str(y)],
                       check=True, timeout=5)
        pointer = subprocess.Popen(["sudo", "-S", sys.executable, str(pointer_helper)],
                                   stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                   stderr=(fixture / "pointer.err").open("wb"),
                                   text=True, bufsize=1, start_new_session=True)
        pointer.stdin.write(password + "\n")
        pointer.stdin.flush()
        if pointer.stdout.readline().strip() != "READY":
            raise AssertionError("The physical pointer did not become ready")

        def pointer_command(command):
            pointer.stdin.write(command + "\n")
            pointer.stdin.flush()
            if pointer.stdout.readline().strip() != "OK":
                raise AssertionError(f"Physical pointer command {command} failed")

        pointer_command("DOWN")
        for _ in range(4):
            pointer_command("MOVE 0 20")
        pointer_command("UP")
        pointer_command("QUIT")
        pointer.wait(timeout=5)
        pointer = None
        after_pointer = bounds(grip)
        if after_pointer[1] <= rect[1] + 8:
            raise AssertionError(f"Native pointer drag did not shrink drawer: {rect} → {after_pointer}")
        result["steps"].append({"physicalPointerGripBefore": rect,
                                "physicalPointerGripAfter": after_pointer})

        # 200% at 1280×840 physical is a stricter 640×420-DIP viewport.
        win = subprocess.run(["/usr/bin/wmctrl", "-lp"], capture_output=True,
                             text=True, check=True, timeout=5)
        lines = [line for line in win.stdout.splitlines() if len(line.split()) > 2 and
                 line.split()[2] == str(process.pid)]
        if len(lines) != 1:
            raise AssertionError("Cannot identify the exact test window")
        subprocess.run(["/usr/bin/wmctrl", "-ir", lines[0].split()[0],
                        "-e", "0,30,30,1280,840"], check=True, timeout=5)
        time.sleep(1)
        app_bounds = bounds(next(n for n in descendants(app) if n.get_role_name() == "frame"))
        report_bounds, history_bounds, grip_bounds = bounds(report), bounds(history), bounds(grip)
        if not (within(report_bounds, app_bounds) and within(history_bounds, app_bounds) and
                report_bounds[3] >= 20 and history_bounds[3] >= 45):
            raise AssertionError(f"Narrow drawer clipped report/history: {app_bounds}, {report_bounds}, {history_bounds}")
        result["steps"].append({"narrowWindow": app_bounds, "report": report_bounds,
                                "history": history_bounds, "grip": grip_bounds,
                                "sourceAndTargetSha256": digest})
        result["passed"] = True
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
        raise
    finally:
        if pointer is not None:
            try:
                pointer.stdin.write("QUIT\n")
                pointer.stdin.flush()
                pointer.wait(timeout=3)
            except (OSError, subprocess.TimeoutExpired):
                pointer.kill()
        if process is not None and process.poll() is None:
            os.killpg(process.pid, signal.SIGTERM)
            process.wait(timeout=10)
        report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")


def ancestors(node):
    for _ in range(15):
        node = node.get_parent()
        if node is None:
            return
        yield node


if __name__ == "__main__":
    if len(sys.argv) != 5:
        raise SystemExit("Usage: linux-atspi-operations-drawer.py APP REPORT KEY_HELPER POINTER_HELPER")
    main(*(Path(arg) for arg in sys.argv[1:]))
