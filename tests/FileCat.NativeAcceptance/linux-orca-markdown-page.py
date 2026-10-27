#!/usr/bin/env python3
"""Hear a wide-table Markdown page in a narrow viewer through Orca, with physical keys, at the desktop's scale.

Usage: linux-orca-markdown-page.py APP_PATH REPORT.json KEY_HELPER [--record-all]
--record-all continues after an unmet expectation so the whole transcript is kept (the report still fails).
Run in the logged-in GNOME session against an isolated Git-built FileCat below ~/FileCatValidation, with no other Orca
or FileCat running. The guest sudo password for the physical-key helper comes only through
FILECAT_NATIVE_SUDO_PASSWORD and is removed before Orca or FileCat starts. Every spoken expectation is judged from
Orca's own SPEECH OUTPUT. Orca follows focus into the embedded WebKitGTK page, though Avalonia's accessible tree has
no socket for it (UXBUG-021). What the page draws (the narrow page's reflowed prose and the table scrolled by its own
region) is captured beside the report: REPORT.open.png, REPORT.tab.png, REPORT.scrolled.png, REPORT.cells.png,
REPORT.typed.png and REPORT.found.png. Orca's modifier is expected on Insert (its desktop layout).

1. F3 on wide.md opens the viewer in Page mode; Orca names the page's host and says Tab moves into the page.
2. The viewer narrows to 520 by 520 units; Tab enters the page's table region, which Orca names.
3. In Orca's focus mode, Right arrows scroll the table region; back in browse mode, Orca's next-table key enters
   the table and its next-cell key reads the header cells up to the rightmost one.
4. F7 opens Find; "marker" and Enter report 3 matches; F3 and Shift+F3 speak the next and previous match.
5. Escape gives the keyboard back to the page; Orca names its host again.
6. F4 shows the same file as text, which Orca reads.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import time
from uuid import uuid4


speech_runner = runpy.run_path(str(Path(__file__).with_name("linux-orca-speech.py")))
SpeechLog = speech_runner["SpeechLog"]
ORCA_WRAPPER = speech_runner["ORCA_WRAPPER"]
windows_of = speech_runner["windows_of"]
sha256 = speech_runner["sha256"]
CAPTURE = """import sys, gi
gi.require_version("Gdk", "3.0"); gi.require_version("GdkX11", "3.0")
from gi.repository import Gdk, GdkX11
window = GdkX11.X11Window.foreign_new_for_display(Gdk.Display.get_default(), int(sys.argv[1], 16))
image = Gdk.pixbuf_get_from_window(window, 0, 0, window.get_width(), window.get_height())
image.savev(sys.argv[2], "png", [], [])
print(image.get_width(), image.get_height())
"""
NAME = "wide.md"
COLUMNS = ["Item", "Owner", "Location", "Size", "Modified", "Kind", "Checksum", "State", "Notes", "Rightmost marker cell"]
ROWS = [["alpha", "benny", "/srv/data/alpha", "1 MiB", "2026-10-27", "file", "3a7bd3e2360a3d29eea436fcfb7e44c7", "ok", "first", "edge one"],
        ["beta", "benny", "/srv/data/beta", "2 MiB", "2026-10-26", "file", "9b74c9897bac770ffc029102a200c5de", "ok", "second", "edge two"]]
CONTENT = ("# Wide table check\n\nThe first marker paragraph says enough words to need several lines in a narrow viewer, "
           "so its lines must wrap inside the window instead of pushing the page sideways, while the table below keeps "
           "its own horizontal scroll.\n\n"
           + "| " + " | ".join(COLUMNS) + " |\n|" + "---|" * len(COLUMNS) + "\n"
           + "".join("| " + " | ".join(row) + " |\n" for row in ROWS)
           + "\nThe closing paragraph has the last marker.\n").encode()


def main():
    if len(sys.argv) not in (4, 5) or len(sys.argv) == 5 and sys.argv[4] != "--record-all":
        raise SystemExit(__doc__)
    record_all = len(sys.argv) == 5
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, key_helper = (Path(value).resolve(strict=True) for value in (sys.argv[1], sys.argv[3]))
    report_path = Path(sys.argv[2]).resolve()
    if (app_path.name != "FileCat" or validation not in app_path.parents or report_path.parent != validation or
            key_helper.name != "linux-uinput-key.py" or validation not in key_helper.parents):
        raise SystemExit("Use the isolated FileCat build, the owned key helper and a report under ~/FileCatValidation")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise SystemExit("A transient guest sudo credential is required for physical test input")
    # The packaged FileCat runs as "filecat": a window of it left open spoke over this journey once.
    for program in ("orca", "FileCat", "filecat"):
        if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", program], capture_output=True).returncode == 0:
            raise SystemExit(f"Another {program} is running; stop it so this test owns the only one")
    fixture = validation / "OrcaMarkdownFixture" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    (source / NAME).write_bytes(CONTENT)
    desktop_environment = runpy.run_path(
        str(Path(__file__).with_name("linux-atspi-ftp-large-listing.py")))["desktop_environment"]
    os.environ.update(desktop_environment())
    shown = subprocess.run(["/usr/bin/gdctl", "show"], capture_output=True, text=True, timeout=5).stdout
    report = {"passed": False, "appDllSha256": sha256(app_path.with_name("FileCat.dll")),
              "orca": subprocess.run(["/usr/bin/orca", "--version"], capture_output=True, text=True, timeout=10).stdout.strip(),
              "displayScale": next((line.split(":")[1].strip() for line in shown.splitlines() if "Scale:" in line), None),
              "fixture": str(fixture), "sourceSha256": hashlib.sha256(CONTENT).hexdigest(),
              "input": "uinput keyboard", "steps": []}
    speech = SpeechLog(fixture / "orca-debug.log")
    failures = []
    orca = process = None
    try:
        orca_env = {**os.environ, "GDK_BACKEND": "wayland", "WAYLAND_DISPLAY": "wayland-0"}
        orca = subprocess.Popen(["/usr/bin/python3", "-c", ORCA_WRAPPER, str(speech.path)], env=orca_env,
                                stdout=subprocess.DEVNULL, stderr=(fixture / "orca.err").open("wb"))
        deadline = time.monotonic() + 20
        while not any("Screen reader on" in phrase for phrase in speech.take()):
            if time.monotonic() > deadline or orca.poll() is not None:
                raise AssertionError("Orca did not report that the screen reader started")
            time.sleep(0.2)
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log, stderr=subprocess.STDOUT)
        deadline = time.monotonic() + 40
        while len(windows_of(process.pid)) != 1:
            if time.monotonic() > deadline or process.poll() is not None:
                raise AssertionError("The isolated FileCat window did not appear")
            time.sleep(0.25)
        time.sleep(2.0)
        subprocess.run(["/usr/bin/wmctrl", "-ia", windows_of(process.pid)[0]], check=True, timeout=5)
        time.sleep(0.5)

        def send(chord):
            sent = subprocess.run(["sudo", "-S", "/usr/bin/python3", str(key_helper), chord], input=sudo_password + "\n",
                                  capture_output=True, text=True, timeout=20)
            if sent.returncode:
                raise AssertionError(f"Physical {chord} failed with exit {sent.returncode}")

        def says(expect, phrase):
            return expect(phrase) if callable(expect) else expect in phrase

        def step(label, keys, expect, seconds=8.0, required=True):
            speech.take()
            for key in keys:
                send(key)
            heard, deadline = [], time.monotonic() + seconds
            while time.monotonic() < deadline:
                if orca.poll() is not None:
                    raise AssertionError(f"The test-owned Orca exited during {label}")
                heard += speech.take()
                if expect is not None and any(says(expect, phrase) for phrase in heard):
                    time.sleep(0.8)
                    heard += speech.take()
                    break
                time.sleep(0.15)
            passed = expect is None or any(says(expect, phrase) for phrase in heard)
            report["steps"].append({"step": label, "keys": keys, "expect": expect.__doc__ if callable(expect) else expect,
                                    "heard": heard, "passed": passed})
            if required and not passed:
                failures.append(label)
                if not record_all:
                    raise AssertionError(f"{label}: Orca said {heard!r}")
            return heard

        def viewer():
            listing = subprocess.run(["/usr/bin/wmctrl", "-lp"], capture_output=True, text=True, timeout=5).stdout
            return next((line.split()[0] for line in listing.splitlines()
                         if len(line.split()) > 2 and line.split()[2] == str(process.pid) and "Viewer" in line), None)

        def capture(name):
            path = report_path.with_suffix(f".{name}.png")
            done = subprocess.run(["/usr/bin/python3", "-c", CAPTURE, viewer(), str(path)],
                                  env={**os.environ, "GDK_BACKEND": "x11"}, capture_output=True, text=True, timeout=30)
            report[f"{name}Capture"] = {"path": str(path), "size": done.stdout.strip() or done.stderr.strip()[-300:]}

        step("focus wide.md", ["down"], NAME)
        step("F3 opens the page, whose host says how to move into it", ["f3"], "Tab moves into the page")
        window = viewer()
        if window is None:
            raise AssertionError("No viewer window")
        active = subprocess.run(["/usr/bin/xprop", "-root", "_NET_ACTIVE_WINDOW"], capture_output=True, text=True,
                                timeout=5).stdout.split()[-1:]
        report["viewerActiveAfterF3"] = bool(active) and int(active[0], 16) == int(window, 16)
        # Narrow: 520 by 520 units at the desktop's scale. GNOME maximizes a window opened nearly as large as the
        # screen, and a maximized window keeps its size.
        scale = float(report["displayScale"] or 1)
        subprocess.run(["/usr/bin/wmctrl", "-ir", window, "-b", "remove,maximized_vert,maximized_horz"], check=True,
                       timeout=5)
        time.sleep(1.0)
        subprocess.run(["/usr/bin/wmctrl", "-ir", window, "-e", f"0,-1,-1,{int(520 * scale)},{int(520 * scale)}"],
                       check=True, timeout=5)
        time.sleep(2.0)
        geometry = subprocess.run(["/usr/bin/xwininfo", "-id", window], capture_output=True, text=True, timeout=5).stdout
        size = [int(line.split(":")[1]) for line in geometry.splitlines() if line.strip().startswith(("Width:", "Height:"))]
        report["viewerPixels"] = size
        if len(size) != 2 or size[0] > 560 * scale:
            raise AssertionError(f"The viewer did not narrow: {size}")
        capture("open")
        step("Tab enters the page's table region", ["tab"], "scroll horizontally")
        capture("tab")
        step("Orca's focus mode passes the arrows to the page", ["insert+a"], "Focus mode")
        step("Right arrows scroll the table region", ["right"] * 6, None, seconds=2.0, required=False)
        capture("scrolled")
        step("Orca's browse mode reads the page again", ["insert+a"], "Browse mode")
        def names_a_table(phrase):
            """a phrase naming a table, in any case"""
            return "table" in phrase.lower()
        step("Orca's next-table key enters the table", ["t"], names_a_table)
        step("Orca's next-cell key reads to the rightmost header cell", ["alt+shift+right"] * 9, "Rightmost marker cell",
             seconds=12.0)
        capture("cells")
        step("F7 opens Find", ["f7"], "Find in file")
        step("marker is typed into Find", ["type:marker"], None, seconds=1.5, required=False)
        capture("typed")
        step("Enter counts the matches", ["enter"], "3 matches")
        capture("found")
        step("F3 speaks the next match", ["f3"], 'Found "marker" in the page')
        step("Shift+F3 speaks the previous match", ["shift+f3"], 'Found "marker" in the page')
        step("Escape gives the keyboard back to the page", ["escape"], "Tab moves into the page")
        step("F4 shows the file as text", ["f4"], "Text viewer")
        if (source / NAME).read_bytes() != CONTENT or any(target.iterdir()):
            raise AssertionError("The journey changed the owned source or destination")
        if failures:
            raise AssertionError("Unmet: " + "; ".join(failures))
        report["passed"] = True
    except Exception as error:
        report["error"] = str(error)
    finally:
        for owned in (process, orca):
            if owned is not None and owned.poll() is None:
                owned.terminate()
                try:
                    owned.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    owned.kill()
                    owned.wait(timeout=5)
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
