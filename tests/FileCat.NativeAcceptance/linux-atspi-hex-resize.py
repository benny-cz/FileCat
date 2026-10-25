#!/usr/bin/env python3
"""Check that a Git-built Ubuntu Hex viewer reflows when its native window changes width.

Usage: linux-atspi-hex-resize.py APP REPORT.json
Run in the logged-in Ubuntu validation VM with its session bus and Xwayland.
On success, the runner removes only its disposable fixture.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import shutil
import subprocess
import sys
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

common = runpy.run_path(str(Path(__file__).with_name("linux-atspi-viewer-find.py")))
descendants = common["descendants"]
find = common["find"]
wait_for = common["wait_for"]
checked_action = common["checked_action"]
close_viewer = common["close_viewer"]
viewer_window_id = common["exact_viewer_window_id"]
send_key = common["send_key"]

NAME = "hex-resize.bin"
CONTENT = bytes(range(256)) * 32


def command(*args):
    return subprocess.run(args, text=True, capture_output=True, timeout=8, check=True).stdout


def size_of(window_id):
    geometry = command("/usr/bin/xdotool", "getwindowgeometry", "--shell", window_id)
    values = dict(line.split("=", 1) for line in geometry.splitlines() if "=" in line)
    return [int(values["WIDTH"]), int(values["HEIGHT"])]


def resize(window_id, dimensions, scale):
    requested = [round(value * scale) for value in dimensions]
    command("/usr/bin/wmctrl", "-ir", window_id, "-b", "remove,maximized_vert,maximized_horz")
    command("/usr/bin/wmctrl", "-ir", window_id, "-e",
            f"0,80,60,{requested[0]},{requested[1]}")

    def settled():
        actual = size_of(window_id)
        return actual if abs(actual[0] - requested[0]) <= 35 and abs(actual[1] - requested[1]) <= 35 else None

    try:
        pixels = wait_for(settled, f"native Hex viewer at {dimensions}", seconds=3)
    except AssertionError:
        command("/usr/bin/xdotool", "windowsize", window_id, *map(str, requested))
        pixels = wait_for(settled, f"native Hex viewer at {dimensions}", seconds=5)
    return [round(pixels[0] / scale, 1), round(pixels[1] / scale, 1)]


def hex_value(viewer):
    control = find(viewer, "entry", "Hex view")
    if control is None:
        seen = [(node.get_role_name(), node.get_name()) for node in descendants(viewer)
                if "hex" in (node.get_name() or "").lower()]
        raise AssertionError(f"The accessible Hex view is absent: {seen!r}")
    text = control.get_text_iface()
    if text is None:
        raise AssertionError(f"The Hex view exposes no readable text: {control.get_description()!r}")
    return Atspi.Text.get_text(text, 0, -1)


def row_bytes(value):
    match = re.search(r"row [0-9A-F]{8}: ((?:[0-9A-F]{2}(?: |\.|$))+)", value)
    if match is None:
        raise AssertionError(f"The accessible Hex row is absent: {value!r}")
    return re.findall(r"\b[0-9A-F]{2}\b", match.group(1))


def run(app_path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    if app_path.name != "FileCat" or validation not in app_path.parents:
        raise AssertionError("Use a Git-built app below the Ubuntu validation folder")
    repo = next(parent for parent in app_path.parents if (parent / ".git").is_dir())
    revision = command("/usr/bin/git", "-C", str(repo), "rev-parse", "HEAD").strip()
    root = validation / "HexResize" / uuid4().hex
    source, target, data = (root / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    owned = source / NAME
    owned.write_bytes(CONTENT)
    (source / "a.txt").write_bytes(b"alpha\n")
    (source / "b.txt").write_bytes(b"beta\n")
    original_hash = hashlib.sha256(CONTENT).hexdigest()
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    os.environ.setdefault("DISPLAY", ":0")
    xauth = list(Path(os.environ["XDG_RUNTIME_DIR"]).glob(".mutter-Xwaylandauth.*"))
    if len(xauth) != 1 or not xauth[0].is_file() or xauth[0].stat().st_uid != os.getuid():
        raise AssertionError("The logged-in Ubuntu Xwayland authority is unavailable")
    os.environ["XAUTHORITY"] = str(xauth[0])
    sdk = validation / "dotnet-sdk"
    if not (sdk / "dotnet").is_file():
        raise AssertionError("The isolated Ubuntu .NET runtime is unavailable")
    os.environ["DOTNET_ROOT"] = str(sdk)
    os.environ["PATH"] = str(sdk) + os.pathsep + os.environ.get("PATH", "")
    result = {"passed": False, "platform": "Ubuntu", "revision": revision,
              "appSha256": hashlib.sha256(app_path.read_bytes()).hexdigest(), "fixture": str(root)}
    with (root / "app.log").open("wb") as log:
        process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                    "--left", str(source), "--right", str(target)],
                                   cwd=root, env=os.environ.copy(), stdout=log, stderr=subprocess.STDOUT)
        viewer = None
        editor = None
        app = None
        pid = None
        title = f"{NAME} — FileCat Viewer"
        try:
            desktop = Atspi.get_desktop(0)
            app = wait_for(lambda: next((candidate for candidate in
                (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                if candidate.get_name() == "FileCat"
                and find(candidate, "list", f"Panel 1, source, {source}")), None),
                "isolated FileCat source panel", seconds=20)
            pid = app.get_process_id()
            if pid != process.pid or os.fsencode(str(source)) not in Path(f"/proc/{pid}/cmdline").read_bytes():
                raise AssertionError("The AT-SPI app is not the exact owned process")
            listing = find(app, "list", f"Panel 1, source, {source}")
            row = find(listing, "list item", NAME)
            if row is None or not row.grab_focus():
                raise AssertionError("Could not focus the exact Hex resize fixture")
            checked_action(find(app, "button", "View"), "View command")
            viewer = wait_for(lambda: find(app, "frame", title), "owned viewer")
            hex_button = next((node for node in descendants(viewer)
                               if node.get_name() == "Hex" and node.get_role_name() in
                               ("button", "toggle button")), None)
            checked_action(hex_button, "Hex mode")
            wait_for(lambda: find(viewer, "label", "Mode: Hex"), "Hex mode status")
            window_id = viewer_window_id(title, pid)
            hints = command("/usr/bin/xprop", "-id", window_id, "WM_NORMAL_HINTS")
            minimum = re.search(r"minimum size:\s*(\d+) by (\d+)", hints)
            if minimum is None:
                raise AssertionError("The native Hex viewer minimum size is unavailable")
            scale = int(minimum.group(1)) / 480
            if not 0.5 <= scale <= 4:
                raise AssertionError(f"Implausible native UI scale: {scale}")
            result["nativeScale"] = scale
            states = {}
            for label, dimensions in (("narrow", (620, 480)), ("wide", (900, 480))):
                actual = resize(window_id, dimensions, scale)
                value = wait_for(lambda: text if (text := hex_value(viewer)) and
                                 "row 00000000:" in text else None,
                                 "readable first Hex row")
                row = row_bytes(value)
                if row != [f"{byte:02X}" for byte in CONTENT[:len(row)]]:
                    raise AssertionError(f"Hex row bytes differ after {label} resize")
                states[label] = {"sizeDip": actual, "rowBytes": len(row), "firstRow": row}
            if states["wide"]["sizeDip"][0] <= states["narrow"]["sizeDip"][0] + 180:
                raise AssertionError("The native viewer did not widen enough to test reflow")
            if states["wide"]["rowBytes"] <= states["narrow"]["rowBytes"]:
                raise AssertionError("The Hex row did not fit more bytes after widening")
            result["states"] = states

            close_viewer(app, title, pid)
            viewer = None
            windows = [line.split(maxsplit=4) for line in command("/usr/bin/wmctrl", "-lp").splitlines()]
            main_ids = [parts[0] for parts in windows if len(parts) == 5 and
                        parts[2] == str(pid) and "FileCat" in parts[4] and
                        "Viewer" not in parts[4] and "Hex Editor" not in parts[4]]
            if len(main_ids) != 1:
                raise AssertionError(f"Expected one owned main window, found {main_ids!r}")
            command("/usr/bin/wmctrl", "-ia", main_ids[0])
            search = find(app, "entry", "Search commands")
            if search is None or not search.grab_focus() or not search.set_text_contents("Edit file bytes in hex"):
                raise AssertionError("The native command search could not target Hex editing")
            wait_for(lambda: next((node for node in descendants(app)
                      if node.get_role_name() == "list item" and
                      "Id = hex.edit," in (node.get_name() or "")), None),
                     "Hex editor command result")
            wait_for(lambda: search.get_state_set().contains(Atspi.StateType.FOCUSED),
                     "focused command search")
            native_key = validation / "linux-uinput-key.py"
            if not native_key.is_file():
                raise AssertionError("The Ubuntu validation native key helper is unavailable")
            send_key(native_key, "enter")
            editor_title = f"{NAME} — FileCat Hex Editor"
            try:
                editor = wait_for(lambda: find(app, "frame", editor_title), "owned Hex editor")
            except AssertionError as error:
                focused_window = command("/usr/bin/xdotool", "getwindowfocus", "getwindowname").strip()
                raise AssertionError(f"{error}; native focused window={focused_window!r}") from error
            editor_id = viewer_window_id(editor_title, pid)
            editor_hints = command("/usr/bin/xprop", "-id", editor_id, "WM_NORMAL_HINTS")
            editor_minimum = re.search(r"minimum size:\s*(\d+) by (\d+)", editor_hints)
            if editor_minimum is None:
                raise AssertionError("The native Hex editor minimum size is unavailable")
            editor_scale = int(editor_minimum.group(1)) / 620
            if not 0.5 <= editor_scale <= 4:
                raise AssertionError(f"Implausible native editor scale: {editor_scale}")
            if abs(editor_scale - scale) > 0.02:
                raise AssertionError("The viewer and editor disagree about the native UI scale")
            editor_states = {}
            for label, dimensions in (("narrow", (720, 480)), ("wide", (900, 480))):
                actual = resize(editor_id, dimensions, editor_scale)
                value = wait_for(lambda: text if (text := hex_value(editor)) and
                                 "row 00000000:" in text else None,
                                 "readable first editor Hex row")
                row = row_bytes(value)
                if row != [f"{byte:02X}" for byte in CONTENT[:len(row)]]:
                    raise AssertionError(f"Editor Hex row bytes differ after {label} resize")
                editor_states[label] = {"sizeDip": actual, "rowBytes": len(row), "firstRow": row}
            if editor_states["wide"]["rowBytes"] <= editor_states["narrow"]["rowBytes"]:
                raise AssertionError("The Hex editor row did not fit more bytes after widening")
            result["editorStates"] = editor_states
            result["passed"] = True
        finally:
            try:
                if editor is not None and app is not None and pid is not None:
                    editor_title = f"{NAME} — FileCat Hex Editor"
                    editor_id = viewer_window_id(editor_title, pid)
                    command("/usr/bin/wmctrl", "-ic", editor_id)
                    wait_for(lambda: find(app, "frame", editor_title) is None,
                             "the owned Hex editor to close")
                if viewer is not None and app is not None and pid is not None:
                    close_viewer(app, title, pid)
            finally:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=5)
    if hashlib.sha256(owned.read_bytes()).hexdigest() != original_hash:
        raise AssertionError("Native Hex resize changed the exact source bytes")
    result["sourceBytesUnchanged"] = True
    if root.parent.resolve(strict=True) != (validation / "HexResize").resolve(strict=True):
        raise AssertionError("Refusing to clean a fixture outside the validation root")
    shutil.rmtree(root)
    return result


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    report_path = Path(sys.argv[2]).resolve()
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    if report_path.parent != validation:
        raise SystemExit("Report must stay in the Ubuntu validation folder")
    try:
        result = run(Path(sys.argv[1]))
    except Exception as error:
        result = {"passed": False, "error": f"{type(error).__name__}: {error}"}
    report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
