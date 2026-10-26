#!/usr/bin/env python3
"""Check enlarged-font pointer selection and Orca navigation on long lines.

Usage: linux-atspi-compare-pointer-font.py APP REPORT.json
Run in the isolated Ubuntu VM with FILECAT_NATIVE_SUDO_PASSWORD.
"""

import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import time

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

BASE = runpy.run_path(str(Path(__file__).with_name("linux-atspi-compare-long-lines.py")))
PhysicalMouse = runpy.run_path(str(Path(__file__).with_name("linux-atspi-hex-resize.py")))["PhysicalMouse"]
run = BASE["run"]
original_copy = BASE["copy_source_line"]
find = BASE["find"]
descendants = BASE["descendants"]
checked_action = BASE["checked_action"]
clipboard = BASE["clipboard"]
set_clipboard = BASE["set_clipboard"]
captures = []
pointer_copy = None
speech_after_next = []
sudo_password = None
speech = None
orca = None


def start_orca(frame):
    global orca
    shared = runpy.run_path(str(Path(__file__).with_name("linux-orca-speech.py")))
    if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", "orca"],
                      capture_output=True).returncode == 0:
        raise AssertionError("An unrelated Orca instance is already running")
    env = os.environ.copy()
    env.update(runpy.run_path(str(Path(__file__).with_name("linux-atspi-ftp-large-listing.py")))["desktop_environment"]())
    env.update({"GDK_BACKEND": "wayland", "WAYLAND_DISPLAY": "wayland-0"})
    env.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    orca = subprocess.Popen(["/usr/bin/python3", "-c", shared["ORCA_WRAPPER"], str(speech.path)],
                            env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    deadline = time.monotonic() + 20
    while not any("screen reader on" in value.casefold() for value in speech.take()):
        if time.monotonic() >= deadline or orca.poll() is not None:
            raise AssertionError("The test-owned Orca did not start")
        time.sleep(0.2)
    portal = next((node for node in descendants(Atspi.get_desktop(0))
                   if node.get_role_name() == "frame" and node.get_name() == "Remote Desktop"), None)
    if portal is not None:
        cancel = find(portal, "button", "Cancel")
        if cancel is None or not cancel.get_action().do_action(0):
            raise AssertionError("The screen-reader portal request blocked spoken navigation")
    windows = [line.split(maxsplit=3)[0] for line in subprocess.run(
        ["/usr/bin/wmctrl", "-l"], capture_output=True, text=True, check=True, timeout=5).stdout.splitlines()
        if len(line.split(maxsplit=3)) == 4 and line.split(maxsplit=3)[3] == frame.get_name()]
    if len(windows) != 1:
        raise AssertionError("Cannot refocus the single owned Compare window for speech")
    subprocess.run(["/usr/bin/wmctrl", "-ia", windows[0]], check=True, timeout=5)
    speech.take()


def capture(frame, stage):
    nodes = []
    for node in descendants(frame):
        name = node.get_name() or ""
        if any(term in name.casefold() for term in ("source line", "content size", "difference", "copy selection")):
            bounds = node.get_component().get_extents(Atspi.CoordType.SCREEN)
            nodes.append({"role": node.get_role_name(), "name": name[:130],
                          "rect": [bounds.x, bounds.y, bounds.width, bounds.height]})
    captures.append({"stage": stage, "nodes": nodes[:60]})


def probe_copy(frame, sentinel, expected):
    global pointer_copy, speech_after_next
    if not captures:
        larger = find(frame, "button", "Increase comparison content size")
        if larger is None:
            raise AssertionError("The content font control is missing")
        for _ in range(4):
            checked_action(larger, "larger comparison content font")
        capture(frame, "first changed row after enlarging font")
        time.sleep(.35)
        cell = find(frame, "label", "Left source line 1")
        if cell is None:
            raise AssertionError("The first changed source cell is unavailable")
        rect = cell.get_component().get_extents(Atspi.CoordType.SCREEN)
        if rect.x < 0 or rect.y < 0:
            raise AssertionError("The source cell has no usable screen bounds")
        desktop = Atspi.get_desktop(0)
        portal = next((desktop.get_child_at_index(i) for i in range(desktop.get_child_count())
                       if desktop.get_child_at_index(i).get_name() == "xdg-desktop-portal-gnome"), None)
        if portal is not None:
            remote = next((portal.get_child_at_index(i) for i in range(portal.get_child_count())
                           if portal.get_child_at_index(i).get_name() == "Remote Desktop"), None)
            if remote is not None:
                cancel = find(remote, "button", "Cancel")
                if cancel is None or not cancel.get_action().do_action(0):
                    raise AssertionError("The overlapping Remote Desktop request could not be dismissed")
                BASE["wait_for"](lambda: True if all(
                    portal.get_child_at_index(i).get_name() != "Remote Desktop"
                    for i in range(portal.get_child_count())) else None,
                    "Remote Desktop dialog dismissal", seconds=5)
                captures.append({"stage": "dismissed overlapping Remote Desktop request"})
        lines = subprocess.run(["/usr/bin/wmctrl", "-l"], capture_output=True,
                               text=True, check=True, timeout=5).stdout.splitlines()
        windows = [line.split(maxsplit=3)[0] for line in lines
                   if len(line.split(maxsplit=3)) == 4 and line.split(maxsplit=3)[3] == frame.get_name()]
        if len(windows) != 1:
            raise AssertionError("Cannot identify the single owned Compare window")
        subprocess.run(["/usr/bin/wmctrl", "-ia", windows[0]], check=True, timeout=5)
        time.sleep(0.2)
        helper = Path.home() / "FileCatValidation" / "linux-uinput-pointer.py"
        if not helper.is_file():
            raise AssertionError("The isolated physical pointer helper is unavailable")
        copy = find(frame, "button", "Copy selection")
        with PhysicalMouse(helper, sudo_password, 1.0) as mouse:
            for offset, distance in ((460, 55), (455, 60)):
                mouse.drag(rect.x + offset, rect.y + rect.height // 2, distance, 0)
                actual = mouse.position()
                if not (rect.x <= actual[0] < rect.x + rect.width and
                        rect.y <= actual[1] < rect.y + rect.height):
                    raise AssertionError(f"The physical pointer missed the source cell: {actual}")
                pointer = subprocess.run(["/usr/bin/xdotool", "getmouselocation", "--shell"],
                                         capture_output=True, text=True, timeout=5)
                active = subprocess.run(["/usr/bin/xdotool", "getactivewindow", "getwindowname"],
                                        capture_output=True, text=True, timeout=5)
                entry = {"stage": "physical source selection", "offset": offset,
                         "pointer": pointer.stdout.strip(), "activeWindow": active.stdout.strip(),
                         "input": "short-lived physical uinput mouse",
                         "copyEnabled": copy is not None and copy.get_state_set().contains(Atspi.StateType.ENABLED),
                         "cellFocused": cell.get_state_set().contains(Atspi.StateType.FOCUSED)}
                captures.append(entry)
                try:
                    BASE["wait_for"](
                        lambda: copy if copy is not None and copy.get_state_set().contains(
                            Atspi.StateType.ENABLED) else None, "pointer source selection", seconds=4)
                except AssertionError as error:
                    raise AssertionError(f"{error}; pointer state={entry!r}") from error
                set_clipboard("FileCat pointer selection sentinel")
                checked_action(copy, "Copy pointer selection")
                pointer_copy = BASE["wait_for"](
                    lambda: value if (value := clipboard()) != "FileCat pointer selection sentinel" else None,
                    "pointer-selected source text")
                entry["copied"] = pointer_copy
                if "LEFT" in pointer_copy:
                    break
        if pointer_copy is None or "LEFT" not in pointer_copy:
            raise AssertionError(f"The measured visible suffix could not be copied: {captures[-2:]!r}")
        start_orca(frame)
    else:
        capture(frame, "second changed row after F8")
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline:
            speech_after_next.extend(speech.take())
            if any("line 3" in phrase.casefold() for phrase in speech_after_next):
                break
            time.sleep(0.15)
    return original_copy(frame, sentinel, expected)


def main():
    global sudo_password, speech
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    run.__globals__["copy_source_line"] = probe_copy
    report = Path(sys.argv[2]).resolve()
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    if report.parent != validation or report.suffix != ".json" or report.exists() or report.with_suffix(".orca.log").exists():
        raise SystemExit("Use a fresh JSON report and speech log inside FileCatValidation")
    sudo_password = os.environ.get("FILECAT_NATIVE_SUDO_PASSWORD")
    if not sudo_password:
        raise SystemExit("The isolated native input credential is required")
    shared = runpy.run_path(str(Path(__file__).with_name("linux-orca-speech.py")))
    speech = shared["SpeechLog"](report.with_suffix(".orca.log"))
    try:
        result = run(Path(sys.argv[1]))
        result["fontPointerProbe"] = captures
        result["pointerCopiedSuffix"] = pointer_copy
        result["speechAfterNextDifference"] = speech_after_next
        if not any("line 3" in phrase.casefold() for phrase in speech_after_next):
            raise AssertionError(f"Orca did not name the second changed line: {speech_after_next!r}")
    except Exception as error:
        result = {"passed": False, "error": f"{type(error).__name__}: {error}",
                  "fontPointerProbe": captures, "pointerCopiedSuffix": pointer_copy,
                  "speechAfterNextDifference": speech_after_next}
    finally:
        if orca is not None and orca.poll() is None:
            orca.terminate()
            try:
                orca.wait(timeout=8)
            except subprocess.TimeoutExpired:
                orca.kill()
                orca.wait(timeout=5)
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
