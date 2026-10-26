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
import select
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

NAME = "hex-resize.bin"
_content = bytearray(bytes(range(256)) * 32)
_content[3000:3006] = b"NEEDLE"
_content[6000:6006] = b"NEEDLE"
CONTENT = bytes(_content)


def command(*args):
    return subprocess.run(args, text=True, capture_output=True, timeout=8, check=True).stdout


def size_of(window_id):
    geometry = command("/usr/bin/xdotool", "getwindowgeometry", "--shell", window_id)
    values = dict(line.split("=", 1) for line in geometry.splitlines() if "=" in line)
    return [int(values["WIDTH"]), int(values["HEIGHT"])]


def resize(window_id, dimensions, scale):
    requested = [round(value * scale) for value in dimensions]
    command("/usr/bin/wmctrl", "-ir", window_id, "-b", "remove,maximized_vert,maximized_horz")
    # Xwayland can round a 150% GNOME session to a 2x app scale. Keep the
    # whole test window, including its scrollbar, inside the physical display.
    command("/usr/bin/wmctrl", "-ir", window_id, "-e",
            f"0,8,8,{requested[0]},{requested[1]}")

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


def top_byte(value):
    match = re.search(r"viewport begins at byte ([0-9,]+)", value)
    if match is None:
        raise AssertionError(f"The Hex viewport anchor is absent: {value!r}")
    return int(match.group(1).replace(",", ""))


def selected_needle(value):
    match = re.search(r"Selected bytes ([0-9,\u00a0\u202f ]+) through "
                      r"([0-9,\u00a0\u202f ]+), 6 bytes", value)
    if match is None:
        return False
    start, end = (int(re.sub(r"[^0-9]", "", part)) for part in match.groups())
    if (start, end) != (3000, 3005):
        raise AssertionError(f"Hex Find selected the wrong source bytes: {value!r}")
    return True


def send_native_key(helper, chord, sudo_password):
    sent = subprocess.run(["sudo", "-S", sys.executable, str(helper), chord],
                          input=sudo_password + "\n", text=True,
                          capture_output=True, timeout=15)
    if sent.returncode:
        raise AssertionError(f"Native {chord} key failed: {sent.stderr[-300:]}")


class PhysicalMouse:
    def __init__(self, helper, sudo_password):
        self.process = subprocess.Popen(["sudo", "-k", "-S", sys.executable, str(helper)],
                                        stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=subprocess.PIPE, text=True, bufsize=1)
        self.pressed = False
        self.process.stdin.write(sudo_password + "\n")
        self.process.stdin.flush()
        if not select.select([self.process.stdout], [], [], 5)[0] or self.process.stdout.readline().strip() != "READY":
            self.close()
            raise AssertionError("The isolated physical mouse did not become ready")
        self.send("MOVE 1 1")  # Take over the Wayland cursor before measuring it.

    def send(self, instruction):
        self.process.stdin.write(instruction + "\n")
        self.process.stdin.flush()
        if not select.select([self.process.stdout], [], [], 5)[0] or self.process.stdout.readline().strip() != "OK":
            raise AssertionError(f"Physical mouse did not acknowledge {instruction.split()[0]}")
        if instruction == "DOWN":
            self.pressed = True
        elif instruction == "UP":
            self.pressed = False

    def position(self):
        raw = command("/usr/bin/xdotool", "getmouselocation", "--shell")
        found = dict(re.findall(r"^(X|Y)=([0-9]+)$", raw, re.MULTILINE))
        if len(found) != 2:
            raise AssertionError(f"Cannot locate physical VM cursor: {raw!r}")
        return int(found["X"]), int(found["Y"])

    def align(self, target_x, target_y):
        visited = []
        for _ in range(16):
            current_x, current_y = self.position()
            visited.append((current_x, current_y))
            delta_x, delta_y = target_x - current_x, target_y - current_y
            if abs(delta_x) <= 2 and abs(delta_y) <= 2:
                return current_x, current_y
            step_x = max(-500, min(500, round(delta_x * 0.6)))
            step_y = max(-500, min(500, round(delta_y * 0.6)))
            self.send(f"MOVE {step_x} {step_y}")
        raise AssertionError(f"Physical mouse missed {target_x, target_y}: "
                             f"{self.position()}; trail={visited!r}")

    def drag(self, start_x, start_y, delta_x, delta_y):
        self.align(start_x, start_y)
        self.send("DOWN")
        try:
            time.sleep(0.12)
            for step in range(10):
                x = round(delta_x * (step + 1) / 10) - round(delta_x * step / 10)
                y = round(delta_y * (step + 1) / 10) - round(delta_y * step / 10)
                self.send(f"MOVE {x} {y}")
            time.sleep(0.12)
        finally:
            self.send("UP")
        return self.position()

    def close(self):
        if self.process.poll() is None:
            try:
                if self.pressed:
                    self.send("UP")
                self.send("QUIT")
            except (AssertionError, BrokenPipeError):
                pass
            self.process.stdin.close()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=5)
        self.process.stdout.close()
        self.process.stderr.close()

    def __enter__(self):
        return self

    def __exit__(self, _kind, _value, _traceback):
        self.close()


def scrollbar_drag_check(window, window_id, scale, validation, sudo_password):
    command("/usr/bin/wmctrl", "-ia", window_id)
    bar = find(window, "scroll bar", "Hex vertical position")
    if bar is None:
        raise AssertionError("The Hex scrollbar is absent")
    area = bar.get_component().get_extents(Atspi.CoordType.SCREEN)
    if area.width < 10 * scale or area.height < 150 * scale:
        raise AssertionError(f"The Hex scrollbar has implausible bounds: {area}")
    x = round(area.x + area.width / 2)
    start_y = round(area.y + 26 * scale)
    end_y = min(round(area.y + area.height - 26 * scale), start_y + round(100 * scale))
    before = hex_value(window)
    if top_byte(before) != 0 or "No byte selection" not in before or not before.startswith("Offset 0 of"):
        raise AssertionError(f"The Hex thumb test did not start at byte zero: {before!r}")
    helper = validation / "linux-uinput-pointer.py"
    if not helper.is_file():
        raise AssertionError("The isolated physical pointer helper is absent")
    with PhysicalMouse(helper, sudo_password) as mouse:
        down_pointer = mouse.drag(x, start_y, 0, end_y - start_y)
        down = hex_value(window)
        if top_byte(down) <= 0 or "No byte selection" not in down or not down.startswith("Offset 0 of"):
            raise AssertionError(f"The Hex thumb drag did not scroll without selection: "
                                 f"{[area.x, area.y, area.width, area.height]!r}, "
                                 f"pointer={down_pointer!r}, {down!r}")
        up_pointer = mouse.drag(*down_pointer, 0, start_y - down_pointer[1] - 30)
        up = hex_value(window)
        if top_byte(up) != 0 or "No byte selection" not in up or not up.startswith("Offset 0 of"):
            raise AssertionError(f"The Hex reverse thumb drag changed selection or cursor: "
                                 f"pointer={up_pointer!r}, {up!r}")
        content = find(window, "entry", "Hex view")
        if content is None:
            raise AssertionError("The Hex document is absent during explicit selection")
        document = content.get_component().get_extents(Atspi.CoordType.SCREEN)
        first_x = round(document.x + 94 * scale)
        row_y = round(document.y + 12 * scale)
        distance = round(84 * scale)
        if first_x + distance >= area.x or document.width < 400 * scale:
            raise AssertionError(f"Hex document bounds do not permit a content drag: {document}")
        mouse.drag(first_x, row_y, distance, 0)
        selected = hex_value(window)
        selection = re.search(r"Selected bytes ([0-9,]+) through ([0-9,]+), ([0-9,]+) bytes", selected)
        if selection is None:
            raise AssertionError(f"Deliberate Hex content drag did not select bytes: {selected!r}")
        start, end, count = (int(group.replace(",", "")) for group in selection.groups())
        if start != 0 or end < 2 or count != end + 1:
            raise AssertionError(f"Deliberate Hex content drag selected the wrong bytes: {selected!r}")
        mouse.drag(x, start_y, 0, end_y - start_y)
        preserved = hex_value(window)
        if top_byte(preserved) <= 0 or selection.group(0) not in preserved:
            raise AssertionError(f"Hex thumb drag changed the explicit selection: {preserved!r}")
    return {"input": "short-lived physical uinput mouse", "scrollBar": [area.x, area.y,
            area.width, area.height], "downViewportByte": top_byte(down),
            "upViewportByte": top_byte(up), "selectedBytes": [start, end],
            "selectionPreservedViewportByte": top_byte(preserved)}


def run(app_path):
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise AssertionError("The native Ubuntu journey needs one-time sudo authentication")
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
            main_window = next((parts[0] for line in command("/usr/bin/wmctrl", "-lp").splitlines()
                                if (parts := line.split(maxsplit=4)) and len(parts) >= 5
                                and parts[2].isdigit() and int(parts[2]) == pid
                                and "FileCat" in parts[4]), None)
            if main_window is None:
                raise AssertionError("The exact owned FileCat main window is unavailable")
            command("/usr/bin/wmctrl", "-ia", main_window)
            listing = find(app, "list", f"Panel 1, source, {source}")
            try:
                row = wait_for(lambda: next((node for node in descendants(listing)
                                             if node.get_role_name() == "list item"
                                             and ((node.get_name() or "") == NAME or
                                                  (node.get_name() or "").startswith(NAME + ", file,"))), None),
                               "load the exact Hex resize fixture")
            except AssertionError as error:
                visible = [(node.get_role_name(), node.get_name())
                           for node in descendants(listing) if node.get_role_name() == "list item"]
                raise AssertionError(f"{error}; visible rows={visible[:16]!r}; "
                                     f"source files={sorted(item.name for item in source.iterdir())!r}") from error
            wait_for(lambda: row if row.grab_focus() else None,
                     "focus the exact Hex resize fixture", seconds=3)
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
            wide_width = 900 if scale < 1.25 else 840
            states = {}
            for label, dimensions in (("narrow", (620, 480)), ("wide", (wide_width, 480))):
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
            result["viewerThumbDrag"] = scrollbar_drag_check(viewer, window_id, scale,
                                                               validation, sudo_password)
            hex_control = find(viewer, "entry", "Hex view")
            if hex_control is None or not hex_control.grab_focus():
                raise AssertionError("The Hex document could not regain focus after the thumb test")
            send_native_key(validation / "linux-uinput-key.py", "ctrl+home", sudo_password)
            wait_for(lambda: value if top_byte(value := hex_value(viewer)) == 0 and
                     "No byte selection" in value and value.startswith("Offset 0 of") else None,
                     "reset Hex selection before the Find journey")

            checked_action(find(viewer, "button", "Show or hide viewer Find bar"),
                           "viewer Find toggle")
            query = wait_for(lambda: find(viewer, "entry", "Find in file"), "Hex Find field")
            hex_bytes = find(viewer, "check box", "Hex bytes")
            if hex_bytes is None:
                raise AssertionError("The Hex byte search option is unavailable")
            if not hex_bytes.get_state_set().contains(Atspi.StateType.CHECKED):
                checked_action(hex_bytes, "Hex byte search option")
            if not query.get_editable_text().set_text_contents("01 02 03"):
                raise AssertionError("The Hex Find field could not receive the owned query")
            checked_action(find(viewer, "button", "Find next match"), "Hex Find Next")
            selected = wait_for(lambda: value if "Selected bytes 1 through 3, 3 bytes" in
                                (value := hex_value(viewer)) else None,
                                "three exact searched bytes selected")
            if "Offset 3 of 8,192" not in selected:
                raise AssertionError(f"Hex Find selected the wrong source bytes: {selected!r}")
            selection = {"query": "01 02 03", "sourceOffsets": [1, 3]}
            for label, dimensions in (("narrow", (620, 480)), ("wide", (wide_width, 480))):
                resize(window_id, dimensions, scale)
                value = wait_for(lambda: text if "Selected bytes 1 through 3, 3 bytes" in
                                 (text := hex_value(viewer)) else None,
                                 f"search selection after {label} resize")
                selection[label] = {"rowBytes": len(row_bytes(value)),
                                    "viewportFirstByte": top_byte(value)}
            if selection["wide"]["rowBytes"] <= selection["narrow"]["rowBytes"]:
                raise AssertionError("The searched Hex selection did not reflow on resize")
            result["searchSelection"] = selection

            before_zoom = len(row_bytes(hex_value(viewer)))
            checked_action(find(viewer, "button", "Increase text and hex size"),
                           "Hex content zoom")
            zoomed = wait_for(lambda: value if len(row_bytes(value := hex_value(viewer))) <
                              before_zoom else None, "Hex row to shrink after zoom")
            if "Selected bytes 1 through 3, 3 bytes" not in zoomed:
                raise AssertionError("Zoom discarded the exact Hex Find selection")
            result["zoom"] = {"rowBytesBefore": before_zoom,
                              "rowBytesAfter": len(row_bytes(zoomed))}
            checked_action(find(viewer, "button", "Decrease text and hex size"),
                           "restore Hex content size")

            hex_control = find(viewer, "entry", "Hex view")
            if hex_control is None or not hex_control.grab_focus():
                raise AssertionError("The native Hex view could not receive keyboard focus")
            command("/usr/bin/wmctrl", "-ia", window_id)
            send_native_key(validation / "linux-uinput-key.py", "pagedown", sudo_password)
            wait_for(lambda: value if "Offset 3 of 8,192" not in
                     (value := hex_value(viewer)) else None,
                     "native PageDown to move the Hex cursor")
            # The first page ends on the bottom visible row. The next page must
            # move the viewport, after which its byte anchor can be tested.
            send_native_key(validation / "linux-uinput-key.py", "pagedown", sudo_password)
            moved = wait_for(lambda: value if top_byte(value := hex_value(viewer)) > 0
                             else None, "native PageDown to scroll the Hex viewport")
            first = top_byte(moved)
            resize(window_id, (620, 480), scale)
            scrolled = wait_for(lambda: value if len(row_bytes(value := hex_value(viewer))) <
                                before_zoom else None, "scrolled Hex row to reflow narrower")
            after = top_byte(scrolled)
            if not 0 <= first - after < len(row_bytes(scrolled)):
                raise AssertionError(f"Resize lost the first visible Hex byte: {first} -> {after}")
            result["scrollAnchor"] = {"beforeByte": first, "afterByte": after,
                                      "rowBytesAfter": len(row_bytes(scrolled))}

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
            send_native_key(native_key, "enter", sudo_password)
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
            for label, dimensions in (("narrow", (720, 480)), ("wide", (wide_width, 480))):
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
            result["editorThumbDrag"] = scrollbar_drag_check(editor, editor_id,
                                                               editor_scale, validation, sudo_password)
            checked_action(find(editor, "button", "Show or hide hex editor Find bar"),
                           "editor Find toggle")
            editor_query = wait_for(lambda: find(editor, "entry", "Find in file"),
                                    "visible Hex editor Find field")
            editor_hex_bytes = find(editor, "check box", "Hex bytes")
            if editor_hex_bytes is None:
                raise AssertionError("The Hex editor search mode is unavailable")
            if editor_hex_bytes.get_state_set().contains(Atspi.StateType.CHECKED):
                checked_action(editor_hex_bytes, "text search mode")
            if not editor_query.get_editable_text().set_text_contents("NEEDLE"):
                raise AssertionError("The Hex editor Find field refused the owned query")
            checked_action(find(editor, "button", "Find next match"), "editor Find Next")
            first_hit = wait_for(lambda: value if selected_needle(value := hex_value(editor))
                                 else None, "exact six-byte Hex editor Find result")
            editor_search = {"query": "NEEDLE", "sourceOffsets": [3000, 3005],
                             "firstViewportByte": top_byte(first_hit)}
            for label, dimensions in (("narrow", (720, 480)),
                                      ("wide", (wide_width, 480))):
                resize(editor_id, dimensions, editor_scale)
                selected = wait_for(lambda: value if selected_needle(value := hex_value(editor))
                                    else None, f"editor Find selection after {label} resize")
                editor_search[label] = {"rowBytes": len(row_bytes(selected)),
                                        "viewportFirstByte": top_byte(selected)}
            if editor_search["wide"]["rowBytes"] <= editor_search["narrow"]["rowBytes"]:
                raise AssertionError("The searched Hex editor row did not reflow on resize")
            before_zoom = editor_search["wide"]["rowBytes"]
            zoom_steps = 0
            try:
                while zoom_steps < 3:
                    checked_action(find(editor, "button", "Increase editor content size"),
                                   "increase Hex editor content size")
                    zoom_steps += 1
                    zoomed = wait_for(lambda: value if selected_needle(value := hex_value(editor))
                                      else None, "Hex editor Find selection after zoom")
                    if len(row_bytes(zoomed)) < before_zoom:
                        break
                if len(row_bytes(zoomed)) >= before_zoom:
                    raise AssertionError("Hex editor zoom did not reduce its fitted row")
            finally:
                for _ in range(zoom_steps):
                    checked_action(find(editor, "button", "Decrease editor content size"),
                                   "restore Hex editor content size")
            restored = wait_for(lambda: value if selected_needle(value := hex_value(editor))
                                else None, "Hex editor Find selection after zoom restore")
            if len(row_bytes(restored)) != before_zoom:
                raise AssertionError("Hex editor zoom restore changed the fitted row")
            editor_search["zoom"] = {"steps": zoom_steps, "rowBytesBefore": before_zoom,
                                     "rowBytesAfter": len(row_bytes(zoomed)),
                                     "rowBytesRestored": len(row_bytes(restored))}
            result["editorCombinedSearchResizeZoom"] = editor_search
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
    if any(target.iterdir()):
        raise AssertionError("Native Hex inspection wrote to the empty target")
    result["sourceBytesUnchanged"] = True
    result["targetEmpty"] = True
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
