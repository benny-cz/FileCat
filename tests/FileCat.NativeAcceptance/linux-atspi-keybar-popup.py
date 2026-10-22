#!/usr/bin/env python3
"""Verify the key bar recovers when a popup or Find window receives modifier release.

Usage: linux-atspi-keybar-popup.py APP_PATH REPORT.json KEY_HELPER [--expect-find]
Run in the logged-in Ubuntu session. The isolated Git-built app, report and
test input helper must live below ~/FileCatValidation. Pass the guest's sudo
password only through FILECAT_NATIVE_SUDO_PASSWORD for the physical uinput
helper; the runner removes it before starting FileCat.
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


def sha256(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def windows_of(pid):
    result = subprocess.run(["/usr/bin/wmctrl", "-lp"], capture_output=True,
                            text=True, check=True, timeout=5)
    return [line for line in result.stdout.splitlines()
            if len(line.split()) > 2 and line.split()[2] == str(pid)]


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
            buttons.append((bounds.y, bounds.x, node.get_name()))
    if not buttons:
        raise AssertionError("The accessibility tree has no visible key-bar buttons")
    bottom = max(y for y, _, _ in buttons)
    row = sorted((x, name) for y, x, name in buttons if abs(y - bottom) <= 2)
    if len(row) != 12 or len({x for x, _ in row}) != 12:
        raise AssertionError(f"Expected twelve distinct bottom-row key buttons, found {row}")
    return [name for _, name in row]


def main():
    if len(sys.argv) not in (4, 5) or len(sys.argv) == 5 and sys.argv[4] != "--expect-find":
        raise SystemExit(__doc__)
    expect_find = len(sys.argv) == 5
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
    os.environ["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")

    fixture = validation / "KeyBarPopupFixture" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    payload = b"key bar popup source\n"
    (source / "a.txt").write_bytes(payload)
    scale = None
    if Path("/usr/bin/gdctl").is_file():
        shown = subprocess.run(["/usr/bin/gdctl", "show"], capture_output=True,
                               text=True, timeout=5)
        if shown.returncode == 0 and (match := re.search(r"\bScale: ([0-9.]+)", shown.stdout)):
            scale = float(match.group(1))
    report = {"passed": False, "appDllSha256": sha256(app_path.with_name("FileCat.dll")),
              "displayScale": scale, "fixture": str(fixture), "sourceSha256": sha256(source / "a.txt"),
              "input": "physical uinput keyboard", "expectFind": expect_find, "steps": []}
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
                "the isolated FileCat source panel", seconds=25)
            activate_window(process.pid)
            baseline = wait_for(lambda: keybar(app), "twelve visible key-bar buttons")
            report["baseline"] = baseline

            def send(chord):
                command = ["sudo", "-S", "/usr/bin/python3", str(helper), chord]
                sent = subprocess.run(command, input=sudo_password + "\n", capture_output=True,
                                      text=True, timeout=15)
                if sent.returncode:
                    raise AssertionError(f"Physical {chord} failed with exit {sent.returncode}")

            send("shift+f10")
            menu = wait_for(lambda: [(node.get_role_name(), node.get_name()) for node in descendants(app)
                                    if node.get_role_name() in ("menu", "menu item")],
                            "the keyboard-opened context menu")
            report["steps"].append({"action": "Shift+F10", "menu": menu[:20],
                                    "keyBarWhileOpen": keybar(app)})
            send("escape")
            after_menu = wait_for(lambda: keybar(app) == baseline and keybar(app),
                                  "normal key bar after context menu dismissal")
            report["steps"].append({"action": "Escape context menu", "keyBar": after_menu})

            activate_window(process.pid)
            send("alt+f7")
            time.sleep(0.4)
            find_windows = (wait_for(lambda: [line for line in windows_of(process.pid)
                                              if "Find" in line], "Find window from Alt+F7")
                            if expect_find else [line for line in windows_of(process.pid)
                                                 if "Find" in line])
            bar_after_alt = keybar(app)
            # GNOME reserves Alt+F7 for Move window on some installations. The
            # test still exercises that focus transition and its swallowed Alt-up.
            if not find_windows and bar_after_alt == baseline:
                raise AssertionError("Alt+F7 neither opened Find nor changed the held key-bar layer")
            report["steps"].append({"action": "Alt+F7", "findWindows": find_windows[:3],
                                    "keyBarWhileOpen": bar_after_alt})
            send("escape")
            if find_windows:
                wait_for(lambda: len(windows_of(process.pid)) == 1,
                         "Find window to close after Escape")
            after_find = wait_for(lambda: keybar(app) == baseline and keybar(app),
                                  "normal key bar after Alt+F7 focus returns")
            report["steps"].append({"action": "Escape Alt+F7", "keyBar": after_find})
            if sha256(source / "a.txt") != report["sourceSha256"] or any(target.iterdir()):
                raise AssertionError("Keyboard inspection changed the owned source or destination")
            report["passed"] = True
    except Exception as error:
        report["error"] = str(error)
    finally:
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
