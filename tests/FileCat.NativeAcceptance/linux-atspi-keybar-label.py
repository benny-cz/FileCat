#!/usr/bin/env python3
"""Check that the key bar's Shift layer shows and names its commands in full at the desktop's scale.

Usage: linux-atspi-keybar-label.py APP_PATH REPORT.json KEY_HELPER
Run in the logged-in Ubuntu session. The isolated Git-built app, report and test input helper must live below
~/FileCatValidation. Pass the guest's sudo password only through FILECAT_NATIVE_SUDO_PASSWORD for the physical uinput
helper; the runner removes it before starting FileCat. While a physical Shift is held it reads the twelve key-bar
buttons from AT-SPI and captures the window beside the report (REPORT.png) for the visible labels.
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
wait_for = shared["wait_for"]
named = shared["named"]
activate_window = shared["activate_window"]

CAPTURE = """import sys, gi
gi.require_version("Gdk", "3.0"); gi.require_version("GdkX11", "3.0")
from gi.repository import Gdk, GdkX11
window = GdkX11.X11Window.foreign_new_for_display(Gdk.Display.get_default(), int(sys.argv[1], 16))
image = Gdk.pixbuf_get_from_window(window, 0, 0, window.get_width(), window.get_height())
image.savev(sys.argv[2], "png", [], [])
print(image.get_width(), image.get_height())
"""


def sha256(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def keybar(app):
    buttons = []
    for node in descendants(app):
        if node.get_role_name() != "button":
            continue
        try:
            bounds = node.get_component().get_extents(Atspi.CoordType.SCREEN)
        except Exception:
            continue
        if bounds.width > 0 and bounds.height > 0:
            buttons.append((bounds.y, bounds.x, bounds.width, node.get_name(), node.get_description()))
    if not buttons:
        raise AssertionError("The accessibility tree has no visible key-bar buttons")
    bottom = max(button[0] for button in buttons)
    row = sorted(button[1:] for button in buttons if abs(button[0] - bottom) <= 2)
    if len(row) != 12 or len({x for x, *_ in row}) != 12:
        raise AssertionError(f"Expected twelve distinct bottom-row key buttons, found {row}")
    return [{"x": x, "width": width, "name": name, "help": help} for x, width, name, help in row]


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, helper = (Path(value).resolve(strict=True) for value in (sys.argv[1], sys.argv[3]))
    report_path = Path(sys.argv[2]).resolve()
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            helper.name != "linux-uinput-key.py" or validation not in helper.parents or
            report_path.parent != validation):
        raise SystemExit("Use only the owned app, input helper and report under FileCatValidation")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise SystemExit("Transient guest sudo authentication is required for physical key input")
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")

    fixture = validation / "KeyBarLabelFixture" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    (source / "a.txt").write_bytes(b"key bar label source\n")
    scale = None
    shown = subprocess.run(["/usr/bin/gdctl", "show"], capture_output=True, text=True, timeout=5)
    if shown.returncode == 0 and (match := re.search(r"\bScale: ([0-9.]+)", shown.stdout)):
        scale = float(match.group(1))
    report = {"passed": False, "appDllSha256": sha256(app_path.with_name("FileCat.dll")), "displayScale": scale,
              "fixture": str(fixture), "sourceSha256": sha256(source / "a.txt"),
              "input": "physical uinput Shift hold", "capture": str(report_path.with_suffix(".png"))}
    process = hold = None
    release = fixture / "shift-release.signal"
    try:
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log, stderr=subprocess.STDOUT)
            desktop = Atspi.get_desktop(0)
            app = wait_for(lambda: next((candidate for candidate in
                (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                if candidate.get_name() == "FileCat" and
                named(candidate, "list", f"Panel 1, source, {source}")), None),
                "the isolated FileCat source panel", seconds=25)
            activate_window(process.pid)
            report["normal"] = wait_for(lambda: keybar(app), "twelve visible key-bar buttons")
            hold = subprocess.Popen(["sudo", "-S", "/usr/bin/python3", str(helper), "shift-hold-controlled", str(release)],
                                    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
            hold.stdin.write(sudo_password + "\n")
            hold.stdin.flush()
            shift = wait_for(lambda: (bar := keybar(app))[9]["name"] == "Context menu" and bar,
                             "the Shift layer while Shift is held")
            report["shift"] = shift
            windows = subprocess.run(["/usr/bin/wmctrl", "-lp"], capture_output=True, text=True, timeout=5).stdout
            window = next(line.split()[0] for line in windows.splitlines()
                          if len(line.split()) > 2 and line.split()[2] == str(process.pid))
            captured = subprocess.run(["/usr/bin/python3", "-c", CAPTURE, window, report["capture"]],
                                      env={**os.environ, "GDK_BACKEND": "x11"}, capture_output=True, text=True,
                                      timeout=30)
            report["captureSize"] = captured.stdout.strip() or captured.stderr.strip()[-300:]
            release.write_text("release\n", encoding="utf-8")
            hold.wait(timeout=20)
            report["afterRelease"] = wait_for(lambda: (bar := keybar(app))[9]["name"] == report["normal"][9]["name"]
                                              and bar, "the normal layer after Shift is released")
            if shift[9]["help"] != "Shift+F10: Context menu":
                raise AssertionError(f"F10 help is {shift[9]['help']!r}")
            # Keys without a command in the layer are named for their key, not for their content.
            unnamed = [key["name"] for key in shift if not key["name"] or key["name"] == "Button" or
                       key["name"].startswith("Avalonia.")]
            if unnamed or shift[0]["name"] != "Shift+F1: no command":
                raise AssertionError(f"Unnamed Shift-layer keys: {[key['name'] for key in shift]}")
            if sha256(source / "a.txt") != report["sourceSha256"] or any(target.iterdir()):
                raise AssertionError("Holding Shift changed the owned source or destination")
            report["passed"] = True
    except Exception as error:
        report["error"] = str(error)
    finally:
        if not release.exists():
            release.write_text("release\n", encoding="utf-8")
        if hold is not None and hold.poll() is None:
            hold.wait(timeout=20)
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
