#!/usr/bin/env python3
"""Check the shared dialog's close X and backdrop with native Ubuntu pointer input.

Usage: linux-atspi-dialog-pointer-dismiss.py APP_PATH REPORT.json [UINPUT_HELPER] [Folder|Copy]
Run in the logged-in desktop user's AT-SPI session. XTest requires a working
X11 pointer bridge; the optional owned uinput helper sends physical taps on
Wayland. The app and all fixture paths must remain inside ~/FileCatValidation.
"""

import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


def descendants(node):
    if node is None:
        return
    yield node
    for index in range(node.get_child_count()):
        yield from descendants(node.get_child_at_index(index))


def wait_for(check, description, seconds=15):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        result = check()
        if result:
            return result
        time.sleep(0.15)
    raise AssertionError(f"Timed out waiting for {description}")


def named(root, role, name):
    return [node for node in descendants(root)
            if node.get_role_name() == role and node.get_name() == name]


def activate_window(pid):
    listing = subprocess.run(["/usr/bin/wmctrl", "-lp"], check=True,
                             capture_output=True, text=True, timeout=5)
    matches = [line.split()[0] for line in listing.stdout.splitlines()
               if len(line.split()) > 2 and line.split()[2] == str(pid)]
    if len(matches) != 1:
        raise AssertionError(f"Expected one isolated app window for PID {pid}; found {matches}")
    subprocess.run(["/usr/bin/wmctrl", "-ia", matches[0]], check=True, timeout=5)
    time.sleep(0.3)
    focused = subprocess.run(["/usr/bin/xdotool", "getactivewindow", "getwindowpid"],
                             check=True, capture_output=True, text=True, timeout=5)
    if focused.stdout.strip() != str(pid):
        raise AssertionError(f"The isolated app is not the active X11 window; PID {focused.stdout.strip()}")


def native_click(x, y, helper, sudo_password):
    if helper is not None:
        geometry = subprocess.run(["/usr/bin/xdotool", "getdisplaygeometry"],
                                  check=True, capture_output=True, text=True, timeout=5)
        width, height = (int(value) for value in geometry.stdout.split())
        command = ["sudo", "-S" if sudo_password else "-n", sys.executable,
                   str(helper), str(x), str(y), str(width), str(height)]
        sent = subprocess.run(command, input=(sudo_password + "\n") if sudo_password else None,
                              capture_output=True, text=True, timeout=15)
        if sent.returncode:
            raise AssertionError(f"Native uinput tap failed with exit {sent.returncode}")
        return
    try:
        subprocess.run(["/usr/bin/xdotool", "mousemove", "--sync", str(x), str(y), "click", "1"],
                       check=True, timeout=5)
    except subprocess.TimeoutExpired as exc:
        raise AssertionError("XTest could not move the guest pointer; native click input is unavailable") from exc


def main():
    if len(sys.argv) not in (3, 4, 5):
        raise SystemExit("Usage: linux-atspi-dialog-pointer-dismiss.py APP_PATH REPORT.json [UINPUT_HELPER] [Folder|Copy]")
    dialog_kind = sys.argv[4] if len(sys.argv) == 5 else "Folder"
    if dialog_kind not in ("Folder", "Copy"):
        raise SystemExit("The isolated dialog kind must be Folder or Copy")
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = Path(sys.argv[1]).resolve(strict=True)
    if app_path.name != "FileCat" or validation not in app_path.parents:
        raise SystemExit("Use an isolated FileCat binary below the user's FileCatValidation directory")
    helper = Path(sys.argv[3]).resolve(strict=True) if len(sys.argv) >= 4 else None
    if helper is not None and (helper.name not in ("linux-uinput-touch.py", "linux-uinput-mouse.py")
                               or validation not in helper.parents):
        raise SystemExit("Use the owned uinput helper below the user's FileCatValidation directory")
    # Only the runner receives this one-time guest credential; child processes,
    # including the tested FileCat instance, inherit an environment without it.
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    report = Path(sys.argv[2])
    fixture = validation / "DialogPointerFixture" / uuid4().hex
    source, target, data = (fixture / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    source_file = source / "a.txt"
    if dialog_kind == "Copy":
        source_file.write_bytes(b"FileCat native dialog pointer source\n")
    source_hash = hashlib.sha256(source_file.read_bytes()).hexdigest() if dialog_kind == "Copy" else None
    with app_path.open("rb") as binary:
        app_hash = hashlib.file_digest(binary, "sha256").hexdigest()
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    os.environ.setdefault("DISPLAY", ":0")
    process = None
    result = {"passed": False, "app": str(app_path), "appSha256": app_hash,
              "dialogKind": dialog_kind,
              "input": helper.name.removeprefix("linux-").removesuffix(".py") if helper is not None else "XTest-mouse"}
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
                "the isolated FileCat window")

            def open_dialog():
                if dialog_kind == "Copy":
                    rows = named(app, "list item", "a.txt")
                    if len(rows) != 1:
                        raise AssertionError(f"The disposable Copy source row is missing: {len(rows)}")
                    row = rows[0].get_component().get_extents(Atspi.CoordType.SCREEN)
                    result["sourceRowBounds"] = [row.x, row.y, row.width, row.height]
                    activate_window(process.pid)
                    selection_helper = helper.with_name("linux-uinput-touch.py") if (
                        helper is not None and helper.name == "linux-uinput-mouse.py") else helper
                    if selection_helper is not None and not selection_helper.is_file():
                        raise AssertionError("The owned native source-selection helper is missing")
                    native_click(round(row.x + min(40, row.width / 2)), round(row.y + row.height / 2),
                                 selection_helper, sudo_password)
                    result["sourceSelectionInput"] = (selection_helper.name if selection_helper else "XTest-mouse")
                label = "Copy" if dialog_kind == "Copy" else "Create folder"
                buttons = [node for node in descendants(app) if node.get_role_name() == "button"
                           and node.get_name().startswith(label)]
                try:
                    button = wait_for(lambda: next((node for node in buttons
                                                    if node.get_state_set().contains(Atspi.StateType.ENABLED)), None),
                                      f"an enabled {label} action", seconds=5)
                except AssertionError:
                    names = [node.get_name() for node in descendants(app) if node.get_role_name() == "button"]
                    actions = [node.get_n_actions() for node in buttons]
                    enabled = [node.get_state_set().contains(Atspi.StateType.ENABLED) for node in buttons]
                    selected = (rows[0].get_state_set().contains(Atspi.StateType.SELECTED)
                                if dialog_kind == "Copy" else None)
                    raise AssertionError(f"The isolated {label} action is unavailable; "
                                         f"actions={actions}, enabled={enabled}, rowSelected={selected}, "
                                         f"buttons={names[:40]}")
                if button.get_n_actions() != 1 or not button.do_action(0):
                    raise AssertionError(f"The isolated {label} action did not invoke")
                close = wait_for(lambda: next(iter(named(app, "button", "Close dialog")), None),
                                 f"the {label} close X")
                if dialog_kind == "Copy" and not named(app, "entry", "Destination"):
                    raise AssertionError("The Copy review does not expose its destination")
                return close

            activate_window(process.pid)
            close = open_dialog()
            bounds = close.get_component().get_extents(Atspi.CoordType.SCREEN)
            result["closeBounds"] = [bounds.x, bounds.y, bounds.width, bounds.height]
            if bounds.width < 25 or bounds.height < 25:
                raise AssertionError(f"Close X pointer target is too small: {bounds.width}x{bounds.height}")
            if "Cancel this dialog" not in close.get_description():
                raise AssertionError("Close X lacks its accessible cancel explanation")
            result["closeAccessibleName"] = close.get_name()
            result["closeAccessibleDescription"] = close.get_description()
            activate_window(process.pid)
            native_click(round(bounds.x + bounds.width / 2), round(bounds.y + bounds.height / 2),
                         helper, sudo_password)
            wait_for(lambda: not named(app, "button", "Close dialog"), "close X cancellation")

            close = open_dialog()
            windows = [node for node in descendants(app) if node.get_role_name() == "frame"
                       and named(node, "list", f"Panel 1, source, {source}")]
            if len(windows) != 1:
                raise AssertionError(f"Expected one isolated FileCat frame, found {len(windows)}")
            frame = windows[0].get_component().get_extents(Atspi.CoordType.SCREEN)
            outside = (round(frame.x + 20), round(frame.y + frame.height - 80))
            activate_window(process.pid)
            native_click(*outside, helper, sudo_password)
            wait_for(lambda: not named(app, "button", "Close dialog"), "backdrop cancellation")
            if (dialog_kind == "Folder" and (list(source.iterdir()) or list(target.iterdir()))) or (
                    dialog_kind == "Copy" and (
                        hashlib.sha256(source_file.read_bytes()).hexdigest() != source_hash or list(target.iterdir()))):
                raise AssertionError(f"A canceled {dialog_kind} prompt changed the fixture")
            result.update({"passed": True, "nativeCloseX": True, "nativeBackdropClick": True,
                           "outsidePoint": outside,
                           "fixtureUnchanged": True, "sourceSha256": source_hash,
                           "targetEmpty": not list(target.iterdir())})
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
        log_file = fixture / "app.log"
        if log_file.exists():
            result["appLogTail"] = log_file.read_text(errors="replace")[-2000:]
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        shutil.rmtree(fixture)
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
