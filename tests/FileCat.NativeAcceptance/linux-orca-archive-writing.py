#!/usr/bin/env python3
"""Hear archive writing through Orca in the logged-in Ubuntu desktop (plan §11.8, AT16).

Usage: linux-orca-archive-writing.py HOST_PATH WORKER_PATH REPORT.json KEY_HELPER
HOST_PATH is the published archive qualification host (tests/FileCat.ArchiveQualificationHost), which composes the native
archive backends that ordinary startup keeps unavailable; WORKER_PATH is the published FileCat it starts as the archive
workers. Both, and REPORT.json, must be below ~/FileCatValidation. FILECAT_NATIVE_SUDO_PASSWORD authorizes only the owned
uinput key helper and is removed before Orca or FileCat starts. The journey mirrors win-nvda-archive-writing.ps1:

1. Two marked files, Alt+F5: Orca speaks the Create archive dialog and its path; Tab reaches "Archive format"; choosing
   gzip speaks "A compressed file holds exactly one file" and leaves Pack unavailable; TAR + gzip names a .tar.gz and
   packing it announces at least one archive phase.
2. One file as gzip is named after the whole file (a.txt.gz).
3. Inside a.txt.gz, F7, F2 and F8 are refused with the compressed-file reason, and the file stays.

The runner judges only what Orca sent to speech ("SPEECH OUTPUT"), starts and stops its own Orca and FileCat, and
refuses to start when another Orca or FileCat runs.
"""

import json
import os
from pathlib import Path
import re
import runpy
import subprocess
import sys
import time
import traceback
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


REFUSAL = "A compressed file holds exactly one file"
PHASE = re.compile(r": (Preparing|Rebuilding|Checking|Saving)")
# Role names differ between AT-SPI versions.
BUTTON = ("button", "push button")


def descendants(node):
    if node is None:
        return
    yield node
    for index in range(node.get_child_count()):
        child = node.get_child_at_index(index)
        if child is not None:
            yield from descendants(child)


def showing(node):
    return node.get_state_set().contains(Atspi.StateType.SHOWING)


def wait_for(check, description, seconds=20):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        value = check()
        if value:
            return value
        time.sleep(0.15)
    raise AssertionError(f"Timed out waiting for {description}")


def text_of(node):
    count = Atspi.Text.get_character_count(node)
    return Atspi.Text.get_text(node, 0, count)


def main():
    if len(sys.argv) != 5:
        raise SystemExit(__doc__)
    host_path, worker_path, report_path, key_helper = (Path(value).resolve() for value in sys.argv[1:5])
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    if (host_path.name != "FileCat.ArchiveQualificationHost" or worker_path.name != "FileCat" or
            validation not in host_path.parents or validation not in worker_path.parents or
            report_path.parent != validation or report_path.exists() or not key_helper.is_file()):
        raise SystemExit("Use a published qualification host and worker below ~/FileCatValidation and a fresh report there")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise SystemExit("FILECAT_NATIVE_SUDO_PASSWORD is required for the owned key helper")
    for name in ("orca", "FileCat", "FileCat.ArchiveQualificationHost"):
        if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", name[:15]], capture_output=True).returncode == 0:
            raise SystemExit(f"Another {name} instance is running")
    os.environ.setdefault("DISPLAY", ":0")

    fixture = validation / "ArchiveWritingOrca" / uuid4().hex
    data, left, right = fixture / "data", fixture / "left", fixture / "right"
    for folder in (data, left, right):
        folder.mkdir(parents=True)
    for name, text in (("a.txt", "alpha\n"), ("b.txt", "beta\n")):
        (left / name).write_text(text, encoding="ascii")
        os.utime(left / name, (1700000000, 1700000000))
    shared = runpy.run_path(str(Path(__file__).with_name("linux-orca-speech.py")))
    speech = shared["SpeechLog"](fixture / "orca-debug.log")
    report = {"passed": False, "stage": "starting", "fixture": str(fixture), "steps": [],
              "hostSha256": shared["sha256"](host_path), "workerSha256": shared["sha256"](worker_path)}
    orca = process = None
    try:
        with (fixture / "orca.err").open("wb") as error_log:
            # The app runs on Xwayland (DISPLAY); Orca needs the logged-in Wayland session for native focus events.
            orca_env = {**os.environ, "GDK_BACKEND": "wayland", "WAYLAND_DISPLAY": "wayland-0"}
            orca = subprocess.Popen(["/usr/bin/python3", "-c", shared["ORCA_WRAPPER"], str(speech.path)],
                                    stdout=subprocess.DEVNULL, stderr=error_log, env=orca_env)
        wait_for(lambda: any("Screen reader on" in phrase for phrase in speech.take()), "owned Orca startup", seconds=25)
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(host_path), "--data", str(data), "--worker", str(worker_path), "--new-instance",
                                        "--left", str(left), "--right", str(right)],
                                       cwd=host_path.parent, stdout=log, stderr=subprocess.STDOUT, env=os.environ.copy())
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((candidate for candidate in
            (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
            if candidate is not None and candidate.get_name() == "FileCat" and
            any(node.get_role_name() == "list" and node.get_name().startswith("Panel 1") for node in descendants(candidate))), None),
            "the qualification host's FileCat window", seconds=45)

        def activate():
            owned = shared["windows_of"](process.pid)
            if len(owned) != 1:
                raise AssertionError(f"Expected one owned FileCat window, found {owned}")
            subprocess.run(["/usr/bin/wmctrl", "-ia", owned[0]], check=True, timeout=5)
            active = subprocess.run(["/usr/bin/xdotool", "getactivewindow", "getwindowpid"],
                                    check=True, capture_output=True, text=True, timeout=5).stdout.strip()
            if active != str(process.pid):
                raise AssertionError("The owned FileCat window did not take the foreground")

        def send(chord):
            activate()
            sent = subprocess.run(["sudo", "-S", sys.executable, str(key_helper), chord],
                                  input=sudo_password + "\n", capture_output=True, text=True, timeout=15)
            if sent.returncode:
                raise AssertionError(f"Physical {chord} failed: {sent.stderr[-300:]}")
            time.sleep(0.35)

        def hear(parts, label, seconds=10, pattern=None):
            # Every part must be spoken within one window: Orca often says a dialog and its focused field in one burst.
            parts = (parts,) if isinstance(parts, str) else tuple(parts or ())
            heard, deadline = [], time.monotonic() + seconds
            while time.monotonic() < deadline:
                heard.extend(speech.take())
                joined = " ".join(heard)
                if all(part in joined for part in parts) and (pattern is None or pattern.search(joined)):
                    report["steps"].append({"step": label, "heard": heard, "passed": True})
                    return heard
                time.sleep(0.15)
            report["steps"].append({"step": label, "heard": heard, "passed": False})
            wanted = " + ".join(parts) + (f" /{pattern.pattern}/" if pattern else "")
            raise AssertionError(f"{label}: Orca did not say {wanted}: {heard!r}")

        def focus_row(name):
            # Only a row announcement ("name, file, …") identifies the row: notifications also name files.
            row = re.compile(rf"^{re.escape(name)}, ")
            speech.take(); send("home")
            for _ in range(14):
                if any(row.search(phrase) for phrase in speech.take()):
                    return
                send("down"); time.sleep(0.3)
            raise AssertionError(f"The cursor never reached {name}")

        def dialog(title):
            return wait_for(lambda: next((node for node in descendants(app) if node.get_name() == f"{title} dialog" and showing(node)), None),
                            f"the {title} dialog", seconds=20)

        def named(root, role, name):
            roles = (role,) if isinstance(role, str) else role
            return next((node for node in descendants(root) if node.get_role_name() in roles and node.get_name() == name and showing(node)), None)

        def press(root, name):
            button = wait_for(lambda: (lambda node: node if node is not None and node.get_state_set().contains(Atspi.StateType.ENABLED) else None)(
                named(root, BUTTON, name)), f"an available {name} button", seconds=30)
            if button.get_n_actions() < 1 or not button.do_action(0):
                raise AssertionError(f"Cannot invoke {name}")

        def choose(steps, key, label, also=()):
            for _ in range(steps):
                send(key)
            return hear((label, *also), f"format {label}")

        activate()
        # 1. Two marked files.
        report["stage"] = "two files"
        focus_row("a.txt"); send("insert"); send("insert")
        speech.take(); send("alt+f5")
        create = dialog("Create archive")
        hear(("Create archive", "Archive path"), "dialog opened with its path focused")
        send("tab"); hear("Archive format", "format box reached")
        choose(7, "down", "gzip (compress one file)", also=(REFUSAL,))
        packs = [node for node in descendants(app) if node.get_role_name() in BUTTON and node.get_name() == "Pack"]
        report["packButtons"] = [{"states": [state.value_nick for state in node.get_state_set().get_states()],
                                  "inDialog": any(node is candidate for candidate in descendants(create))} for node in packs]
        if not packs or any(node.get_state_set().contains(Atspi.StateType.ENABLED) for node in packs):
            raise AssertionError(f"Pack stayed available for two files as one compressed file: {report['packButtons']}")
        choose(5, "up", "TAR + gzip")
        tar_path = Path(text_of(wait_for(lambda: named(create, "text", "Archive path") or named(create, "entry", "Archive path"), "the path box")))
        report["tarPath"] = str(tar_path)
        if not tar_path.name.endswith(".tar.gz"):
            raise AssertionError(f"TAR + gzip did not name a .tar.gz: {tar_path}")
        speech.take(); press(create, "Pack")
        wait_for(lambda: tar_path.is_file(), "the packed TAR + gzip archive", seconds=120)
        hear(None, "phase announced", seconds=15, pattern=PHASE)

        # 2. One file as gzip.
        report["stage"] = "one file"
        send("ctrl+kpminus"); focus_row("a.txt")
        speech.take(); send("alt+f5")
        create = dialog("Create archive")
        send("tab"); choose(7, "down", "gzip (compress one file)")
        gzip_path = Path(text_of(wait_for(lambda: named(create, "text", "Archive path") or named(create, "entry", "Archive path"), "the path box")))
        report["gzipPath"] = str(gzip_path)
        if gzip_path.name != "a.txt.gz":
            raise AssertionError(f"The compressed file is not named after the whole file: {gzip_path}")
        speech.take(); press(create, "Pack")
        wait_for(lambda: gzip_path.is_file(), "the compressed file", seconds=120)
        report["gzipSpeech"] = speech.take()

        # 3. Inside the compressed file.
        report["stage"] = "inside the compressed file"
        send("tab")  # the archives were created in the other panel's folder
        focus_row("a.txt.gz"); send("enter")
        wait_for(lambda: any(node.get_role_name() == "list" and "a.txt.gz" in node.get_name() and showing(node) for node in descendants(app)),
                 "the compressed file listing", seconds=20)
        time.sleep(0.8); focus_row("a.txt")
        for key in ("f7", "f2", "f8"):
            speech.take(); send(key)
            hear(REFUSAL, f"{key.upper()} refused inside the compressed file")
            send("escape")
        if not gzip_path.is_file():
            raise AssertionError("The compressed file disappeared")
        report["passed"] = True
        report["stage"] = "done"
    except Exception as error:  # The report records what failed; the exit code still fails.
        report["error"] = f"{type(error).__name__}: {error}"
        report["trace"] = traceback.format_exc()[-4000:]
        try:
            report["lastSpeech"] = speech.take()
        except Exception:
            pass
    finally:
        for owned in (process, orca):
            if owned is not None and owned.poll() is None:
                owned.terminate()
                try:
                    owned.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    owned.kill()
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
