#!/usr/bin/env python3
"""Hear FileCat through Orca while physical keys move file-list focus and open a dialog.

Usage: linux-orca-speech.py APP_PATH REPORT.json KEY_HELPER [--record-all]
--record-all continues after an unmet expectation so a complete transcript is kept (the report still fails).
Run in the logged-in GNOME session against an isolated Git-built FileCat below ~/FileCatValidation, with no other
Orca instance running. The caller passes the one-time guest sudo password for the physical-key helper through
FILECAT_NATIVE_SUDO_PASSWORD; it is removed before Orca or FileCat starts. The runner starts its own Orca with a
test-owned debug log and judges only what Orca actually sent to speech ("SPEECH OUTPUT"). It never inspects
FileCat's accessibility tree during those focus tests: enumerating it would create the very nodes whose focus
announcements are tested. A later, separate rename-review phase queries its already-open modal actions.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
from uuid import uuid4


SPEECH = re.compile(r" - SPEECH OUTPUT: '(.*)'(?: \{.*\})?$")
# Orca block-buffers its debug file. This starts the unmodified installed Orca with only that file line-buffered,
# so each phrase can be read as soon as Orca sends it to speech.
ORCA_WRAPPER = r'''
import builtins, runpy, sys
path = sys.argv[1]
real_open = builtins.open
def open_debug_line_buffered(file, mode="r", *args, **kwargs):
    if file == path and "w" in mode:
        kwargs["buffering"] = 1
    return real_open(file, mode, *args, **kwargs)
builtins.open = open_debug_line_buffered
sys.argv = ["/usr/bin/orca", "--replace", "--debug-file", path]
runpy.run_path("/usr/bin/orca", run_name="__main__")
'''


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def windows_of(pid):
    listing = subprocess.run(["/usr/bin/wmctrl", "-lp"], check=True, capture_output=True, text=True, timeout=5)
    return [line.split()[0] for line in listing.stdout.splitlines()
            if len(line.split()) > 2 and line.split()[2] == str(pid)]


class SpeechLog:
    def __init__(self, path):
        self.path = path
        self.offset = 0
        self.partial = b""

    def take(self):
        """Phrases from lines Orca finished writing since the previous call."""
        if not self.path.is_file():
            return []
        with self.path.open("rb") as log:
            log.seek(self.offset)
            data = self.partial + log.read()
            self.offset = log.tell()
        complete, _, self.partial = data.rpartition(b"\n")
        return [match.group(1) for line in complete.decode("utf-8", errors="replace").splitlines()
                if (match := SPEECH.search(line))]


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
    if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", "orca"], capture_output=True).returncode == 0:
        raise SystemExit("Another Orca instance is running; stop it so this test owns the only speech log")

    fixture = validation / "OrcaSpeechFixture" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    expected = {"a.txt": b"alpha\n", "b.txt": b"beta\n", "c.txt": b"charlie\n"}
    for name, content in expected.items():
        (source / name).write_bytes(content)
    source_sha = {name: hashlib.sha256(content).hexdigest() for name, content in expected.items()}
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    orca_version = subprocess.run(["/usr/bin/orca", "--version"], capture_output=True, text=True, timeout=10).stdout.strip()
    # The FileCat launcher is identical across builds; FileCat.dll identifies the tested code.
    report = {"passed": False, "appSha256": sha256(app_path), "appDllSha256": sha256(app_path.with_name("FileCat.dll")),
              "orca": orca_version, "fixture": str(fixture),
              "input": "uinput keyboard and AT-SPI review actions", "sourceSha256": source_sha, "steps": []}
    speech = SpeechLog(fixture / "orca-debug.log")
    orca = process = None
    try:
        # The logged-in GNOME session is Wayland. Over SSH, DISPLAY alone can
        # make GTK choose Xwayland; Orca then may exit on X_QueryPointer's
        # BadWindow before speaking any FileCat item.
        orca_env = {**os.environ, "GDK_BACKEND": "wayland", "WAYLAND_DISPLAY": "wayland-0"}
        orca = subprocess.Popen(["/usr/bin/python3", "-c", ORCA_WRAPPER, str(speech.path)],
                                env=orca_env, stdout=subprocess.DEVNULL,
                                stderr=(fixture / "orca.err").open("wb"))
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

        def active_pid():
            return subprocess.run(["/usr/bin/xdotool", "getactivewindow", "getwindowpid"],
                                  check=True, capture_output=True, text=True, timeout=5).stdout.strip()

        def activate():
            # Any active window of the isolated FileCat keeps the keys there, such as its viewer.
            if active_pid() == str(process.pid):
                return
            window = windows_of(process.pid)
            if len(window) != 1:
                raise AssertionError(f"Expected one isolated FileCat window, found {window}")
            subprocess.run(["/usr/bin/wmctrl", "-ia", window[0]], check=True, timeout=5)
            time.sleep(0.3)
            if (active := active_pid()) != str(process.pid):
                raise AssertionError(f"The isolated FileCat window is not active (PID {active})")

        def step(label, action, expect, seconds=6.0, flush=True):
            if flush:
                speech.take()
            action()
            heard, deadline = [], time.monotonic() + seconds
            while time.monotonic() < deadline:
                if orca.poll() is not None:
                    raise AssertionError(f"The test-owned Orca exited with {orca.returncode} during {label}")
                heard += speech.take()
                if any(expect(phrase) for phrase in heard):
                    time.sleep(0.6)
                    heard += speech.take()
                    break
                time.sleep(0.15)
            passed = any(expect(phrase) for phrase in heard)
            report["steps"].append({"step": label, "heard": heard, "matching": sum(map(expect, heard)), "passed": passed})
            if not passed and not record_all:
                raise AssertionError(f"{label}: Orca said {heard!r}")

        def key(chord):
            def send():
                activate()
                sent = subprocess.run(["sudo", "-S", sys.executable, str(key_helper), chord],
                                      input=sudo_password + "\n", capture_output=True, text=True, timeout=15)
                if sent.returncode:
                    raise AssertionError(f"Physical {chord} input failed: {sent.stderr[-300:]}")
            return send

        def says(text):
            return lambda phrase: text.casefold() in phrase.casefold()

        # Includes everything Orca said since the window opened and became active.
        step("activate the new window", activate, says("Go to parent location"), flush=False)
        step("down to a.txt", key("down"), says("a.txt"))
        step("down to b.txt", key("down"), says("b.txt"))
        step("down to c.txt", key("down"), says("c.txt"))
        step("up to b.txt", key("up"), says("b.txt"))
        step("home to parent", key("home"), says("Go to parent location"))
        step("end to c.txt", key("end"), says("c.txt"))
        step("tab to the target panel", key("tab"), says("Go to parent location"))
        step("tab back to the source panel", key("tab"), says("c.txt"))
        step("F7 opens Create folder", key("f7"), lambda phrase: "folder" in phrase.casefold())
        step("Escape returns to the list", key("escape"), says("c.txt"))
        # The first notice after startup, then the same notice again, must each be announced.
        step("Ctrl+C announces the copy notice", key("ctrl+c"), says("Copied 1 item"))
        step("a repeated copy is announced again", key("ctrl+c"), says("Copied 1 item"))
        # A rejected value in a prompt is spoken as its message appears, and again when Enter is refused.
        step("Ctrl+S opens Quick filter", key("ctrl+s"), says("Quick filter dialog"))
        step("typing an unclosed mask is announced", key("slash"), says("Unclosed regular expression in mask."))

        def type_and_enter():
            key("a")()
            key("enter")()
        step("Enter on the rejected mask explains it again", type_and_enter, says("Unclosed regular expression in mask."))
        step("Escape closes Quick filter", key("escape"), says("c.txt"))
        step("Ctrl+Shift+F focuses the active inline Filter field", key("ctrl+shift+f"),
             lambda phrase: phrase.strip() == "Filter")
        if not any("entry" in phrase.casefold() for phrase in report["steps"][-1]["heard"]):
            raise AssertionError("Orca did not identify the focused Filter field as an entry")
        def reject_inline_mask():
            key("slash")()
            key("enter")()
        step("the inline filter speaks its validation error on Enter", reject_inline_mask,
             says("Unclosed regular expression in mask."))
        step("Escape restores the inline filter and focused row", key("escape"), says("c.txt"))
        # A viewer search reports its result count through a live region.
        step("F3 opens the viewer", key("f3"), says("FileCat Viewer"))
        step("F7 opens the viewer search", key("f7"), says("Find in file"))

        def search_char():
            for name in ("c", "h", "a", "r", "enter"):
                key(name)()
        # The outcome only, not the searching and counting states before it.
        step("a viewer search speaks its match count", search_char, lambda phrase: phrase.strip(" .") == "1 match")
        step("Escape returns from the search to the text", key("escape"), says("No selection"))
        step("Escape closes the viewer", key("escape"), says("c.txt"))
        # A rejected transfer destination is spoken while the form stays open.
        step("F5 opens Copy", key("f5"), says("Copy dialog"))

        def empty_destination():
            key("ctrl+a")()
            key("backspace")()
        step("an empty destination is announced", empty_destination, says("Destination: Enter a destination folder."))
        step("Escape cancels Copy", key("escape"), says("c.txt"))
        # Keyboard panel resizing speaks the boundary and each new share.
        step("Ctrl+Shift+P opens command search", key("ctrl+shift+p"), says("Search commands"))

        def resize_command():
            for name in ("s", "p", "l", "i", "t", "t", "e", "r", "enter"):
                key(name)()
        step("resizing a boundary focuses it", resize_command, says("first side's share of panels 1 and 2"))
        step("Right speaks only the new share", key("right"), lambda phrase: re.fullmatch(r"5[1-9](\.0)?( percent| ?%)?\.?", phrase.strip()) is not None)
        step("Escape restores the boundary", key("escape"), says("c.txt"))        # A test-owned window takes the foreground; returning to FileCat must announce its focused row again.
        # On X11 like FileCat, so the window manager tools used for activation can see it.
        switch = subprocess.Popen([sys.executable, "-c",
                                   "import gi; gi.require_version('Gtk', '3.0'); from gi.repository import Gtk; "
                                   "w = Gtk.Window(title='FileCat speech switch target'); w.connect('destroy', Gtk.main_quit); "
                                   "w.show_all(); Gtk.main()"], env={**os.environ, "GDK_BACKEND": "x11"})
        try:
            deadline = time.monotonic() + 20
            while len(windows_of(switch.pid)) != 1:
                if time.monotonic() > deadline:
                    raise AssertionError("The test-owned switch window did not appear")
                time.sleep(0.2)
            subprocess.run(["/usr/bin/wmctrl", "-ia", windows_of(switch.pid)[0]], check=True, timeout=5)
            time.sleep(2.0)
            step("switch away and back", activate, says("c.txt"))
            report["steps"][-1]["repeats"] = sum("c.txt" in phrase for phrase in report["steps"][-1]["heard"])
        finally:
            switch.terminate()
            switch.wait(10)

        # Orca's own modifier consumes Insert, so use FileCat's documented Shift+movement
        # route to mark the exact two rows. Only after the focus/speech baseline is complete
        # do we inspect the review's native actions and ask Orca what their updates say.
        step("home before marking rename sources", key("home"), says("Go to parent location"))
        step("focus first rename source", key("down"), says("a.txt"))
        step("mark a.txt and focus b.txt", key("shift+down"), says("b.txt"))
        step("mark b.txt and focus c.txt", key("shift+down"), says("c.txt"))
        # Shift+Down marks the row being left. Move back to b.txt for the
        # review, then judge its spoken marked state when Esc restores focus;
        # Orca can suppress a rapid repeat while navigating into the review.
        key("up")()
        speech.take()
        step("open two-item rename review", key("ctrl+m"), says("Bulk rename"))

        import gi
        gi.require_version("Atspi", "2.0")
        from gi.repository import Atspi

        def descendants(node):
            yield node
            for index in range(node.get_child_count()):
                yield from descendants(node.get_child_at_index(index))

        desktop = Atspi.get_desktop(0)
        candidates = [desktop.get_child_at_index(index) for index in range(desktop.get_child_count())]
        apps = [candidate for candidate in candidates if candidate.get_name() == "FileCat" and
                any(node.get_role_name() == "list" and node.get_name() == f"Panel 1, source, {source}"
                    for node in descendants(candidate))]
        if len(apps) != 1:
            raise AssertionError(f"Expected one isolated FileCat accessibility app, found {len(apps)}")

        def button(name):
            found = [node for node in descendants(apps[0])
                     if node.get_role_name() == "button" and node.get_name() == name]
            if len(found) != 1:
                raise AssertionError(f"Expected one native review button {name!r}, found {len(found)}")
            return found[0]

        def mapping_step(label, control, position, name):
            expected_path = str(source / name)
            speech.take()
            if not control.grab_focus():
                raise AssertionError(f"Could not focus {control.get_name()}")
            speech.take()
            if control.get_n_actions() < 1 or not control.do_action(0):
                raise AssertionError(f"Could not invoke {control.get_name()}")
            heard, deadline = [], time.monotonic() + 8
            while time.monotonic() < deadline:
                heard += speech.take()
                combined = " ".join(heard)
                if all(part in combined for part in
                       (f"{position} of 2", f"From: {expected_path}", f"To: {expected_path}")):
                    break
                time.sleep(0.15)
            combined = " ".join(heard)
            passed = all(part in combined for part in
                         (f"{position} of 2", f"From: {expected_path}", f"To: {expected_path}"))
            report["steps"].append({"step": label, "heard": heard, "passed": passed,
                                    "expectedPath": expected_path})
            if not passed and not record_all:
                raise AssertionError(f"{label}: Orca said {heard!r}")

        mapping_step("Next mapping speaks complete b.txt paths", button("Next rename mapping"), 2, "b.txt")
        mapping_step("Previous mapping speaks complete a.txt paths", button("Previous rename mapping"), 1, "a.txt")
        step("cancel spoken rename review", key("escape"), says("b.txt"))
        marked_b_spoken = any("file, 5 bytes" in phrase and ", marked," in phrase
                              for phrase in report["steps"][-1]["heard"])
        report["steps"].append({"step": "Orca announces the newly marked b.txt after review",
                                "heard": report["steps"][-1]["heard"], "passed": marked_b_spoken})
        if not marked_b_spoken and not record_all:
            raise AssertionError("Orca did not announce the exact newly marked b.txt row")

        if sorted(path.name for path in source.iterdir()) != sorted(expected) or any(target.iterdir()):
            raise AssertionError("The speech journey changed the owned fixture folders")
        if any(sha256(source / name) != digest for name, digest in source_sha.items()):
            raise AssertionError("A source file changed during the speech journey")
        report["rowHelpRepetitions"] = {}
        for name, label in (("a.txt", "down to a.txt"), ("b.txt", "down to b.txt"),
                            ("c.txt", "down to c.txt")):
            heard = next(entry["heard"] for entry in report["steps"] if entry["step"] == label)
            descriptions = [phrase.rstrip(" .") for phrase in heard if phrase.startswith("file, ")]
            report["rowHelpRepetitions"][name] = max((descriptions.count(value) for value in set(descriptions)), default=0)
        report["passed"] = all(entry["passed"] for entry in report["steps"])
        if not report["passed"]:
            raise AssertionError("Orca did not announce every expected focus change")
    except Exception as exc:
        report["error"] = f"{type(exc).__name__}: {exc}"
        raise
    finally:
        for owned in (process, orca):
            if owned is not None and owned.poll() is None:
                owned.terminate()
                try:
                    owned.wait(10)
                except subprocess.TimeoutExpired:
                    owned.kill()
        report["speechLog"] = str(speech.path)
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    if report["passed"]:
        shutil.rmtree(data, ignore_errors=True)


if __name__ == "__main__":
    main()
