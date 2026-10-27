#!/usr/bin/env python3
"""Test a live Copy form when an old filesystem destination probe is held.

Usage: linux-atspi-transfer-stale-destination.py APP_PATH REPORT.json KEY_HELPER
The isolated Ubuntu guest needs strace and a logged-in desktop. Only a fresh
fixture beneath ~/FileCatValidation is changed. A transient sudo password is
used for physical F5 and removed from FileCat's environment.
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


def one(root, role, name):
    matches = [node for node in descendants(root) if node.get_role_name() == role and
               (node.get_name() == name or
                (role == "list item" and node.get_name().startswith(name + ", file,")))]
    if len(matches) != 1:
        raise AssertionError(f"Expected one {role} {name!r}; found {len(matches)}")
    return matches[0]


def review_button(root, name):
    matches = []
    for button in named(root, "button", name):
        parent = button
        for _ in range(16):
            parent = parent.get_parent() if parent is not None else None
            if parent is None:
                break
            if parent.get_name() == "Copy dialog":
                matches.append(button)
                break
    if len(matches) != 1:
        raise AssertionError(f"Expected one modal Copy button {name!r}; found {len(matches)}")
    return matches[0]


def main(app_path, report_path, key_helper):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    key_helper = key_helper.resolve(strict=True)
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            key_helper.name != "linux-uinput-key.py" or validation not in key_helper.parents or
            report_path.parent.resolve() != validation):
        raise AssertionError("Use an isolated Git-built app, owned key helper and report")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise AssertionError("Physical F5 needs transient guest authentication")
    os.environ["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    os.environ["LANG"] = "cs_CZ.UTF-8"
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")

    fixture = validation / "TransferStaleDestinationFixture" / uuid4().hex
    source, slow, current, data = (fixture / part for part in ("source", "slow", "current", "data"))
    for folder in (source, slow, current, data):
        folder.mkdir(parents=True)
    source_file = source / "a.txt"
    source_file.write_bytes(b"native current destination\n")
    source_hash = hashlib.sha256(source_file.read_bytes()).hexdigest()
    trace = fixture / "destination-stat.log"
    result = {"passed": False, "platform": "Ubuntu",
              "appDllSha256": hashlib.sha256(app_path.with_name("FileCat.dll").read_bytes()).hexdigest(),
              "fixture": str(fixture), "delayedPath": str(slow),
              "sourceSha256": source_hash, "steps": []}
    process = tracer = None
    try:
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(current)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log,
                                       stderr=subprocess.STDOUT, start_new_session=True)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((candidate for candidate in
            (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
            if candidate.get_name() == "FileCat" and
            named(candidate, "list", f"Panel 1, source, {source}")), None),
            "isolated source panel", seconds=25)

        def send(chord):
            activate_window(process.pid)
            sent = subprocess.run(["sudo", "-S", sys.executable, str(key_helper), chord],
                                  input=sudo_password + "\n", capture_output=True,
                                  text=True, timeout=15)
            if sent.returncode:
                raise AssertionError(f"Physical {chord} failed: {sent.stderr[-300:]}")

        row = wait_for(lambda: next((node for node in descendants(app)
            if node.get_role_name() == "list item" and
            node.get_name().startswith("a.txt, file,")), None), "source row")
        if not row.grab_focus():
            raise AssertionError("Could not focus exact source file")
        send("f5")
        wait_for(lambda: named(app, "entry", "Destination") and
                 named(app, "toggle button", "Options"), "Copy review")
        destination = one(app, "entry", "Destination")
        options = one(app, "toggle button", "Options")
        if not options.do_action(0):
            raise AssertionError("Could not expand Copy options")
        speed = wait_for(lambda: next(iter(named(app, "entry", "Speed limit (MB/s):")), None),
                         "speed field")
        filter_field = one(app, "entry", "Only files matching:")
        if not filter_field.get_editable_text().set_text_contents("*.txt"):
            raise AssertionError("Could not retain the file filter")
        if not speed.get_editable_text().set_text_contents("0,0625x"):
            raise AssertionError("Could not enter invalid localized speed")
        start = review_button(app, "Copy")
        queue = review_button(app, "Queue")

        tracer = subprocess.Popen(["sudo", "-S", "/usr/bin/strace", "-f", "-p",
                                   str(process.pid), "-o", str(trace), "-e", "trace=%file",
                                   "-e", "inject=stat:delay_enter=8s", "-P", str(slow)],
                                  stdin=subprocess.PIPE, stdout=subprocess.DEVNULL,
                                  stderr=(fixture / "strace.err").open("wb"),
                                  start_new_session=True)
        tracer.stdin.write((sudo_password + "\n").encode())
        tracer.stdin.flush()
        tracer.stdin.close()
        wait_for(lambda: any(line.startswith("TracerPid:") and int(line.split()[1]) > 0
                             for line in Path(f"/proc/{process.pid}/status").read_text().splitlines()),
                 "test-owned app trace attachment", seconds=10)
        if not destination.get_editable_text().set_text_contents(str(slow) + "/"):
            raise AssertionError("Could not enter delayed destination")
        wait_for(lambda: not start.get_state_set().contains(Atspi.StateType.ENABLED) and
                 not queue.get_state_set().contains(Atspi.StateType.ENABLED),
                 "both submission routes disabled during validation")
        time.sleep(0.6)  # Pass the dialog's 150 ms debounce into the owned lstat.
        slow.rmdir()  # Remove only the isolated target while the old call is held.
        changed = time.monotonic()
        if not destination.get_editable_text().set_text_contents(str(current) + "/"):
            raise AssertionError("Could not replace the removed destination")
        wait_for(lambda: start.get_state_set().contains(Atspi.StateType.ENABLED) and
                 queue.get_state_set().contains(Atspi.StateType.ENABLED),
                 "current destination admission", seconds=8)
        result["currentAdmissionSeconds"] = round(time.monotonic() - changed, 3)
        if result["currentAdmissionSeconds"] >= 7.5:
            raise AssertionError("The current destination waited for the eight-second old check")
        result["steps"].append("Current destination admitted while removed old target was delayed")
        if not start.do_action(0):
            raise AssertionError("Could not try Copy with invalid speed")
        speed_error = "Speed limit: enter a finite, nonnegative number of MB/s."
        try:
            wait_for(lambda: any(node.get_name() == speed_error for node in descendants(app)),
                     "visible invalid-speed reason")
        except AssertionError:
            result["visibleErrorCandidates"] = [(node.get_role_name(), node.get_name())
                for node in descendants(app) if "speed" in node.get_name().lower() or
                "error" in node.get_name().lower()][-20:]
            result["speedDraft"] = Atspi.Text.get_text(speed.get_text_iface(), 0, -1)
            result["speedHelp"] = speed.get_description()
            raise
        if (speed_error not in speed.get_description() or
                Atspi.Text.get_text(speed.get_text_iface(), 0, -1) != "0,0625x"):
            raise AssertionError("Invalid speed lost its draft or accessible reason")
        if not speed.get_editable_text().set_text_contents("0,0625"):
            raise AssertionError("Could not correct localized speed")
        wait_for(lambda: not any(node.get_name() == speed_error for node in descendants(app)),
                 "speed error cleared")
        if not start.do_action(0):
            raise AssertionError("Could not submit corrected current Copy")
        target_file = current / "a.txt"
        wait_for(lambda: target_file.is_file() and
                 hashlib.sha256(target_file.read_bytes()).hexdigest() == source_hash,
                 "current destination byte-exact Copy", seconds=30)
        wait_for(lambda: "(DELAYED)" in trace.read_text(errors="replace") and
                 str(slow) in trace.read_text(errors="replace"),
                 "old delayed target check returned", seconds=15)
        if (slow.exists() or hashlib.sha256(source_file.read_bytes()).hexdigest() != source_hash or
                sorted(entry.name for entry in current.iterdir()) != ["a.txt"] or
                named(app, "entry", "Destination")):
            raise AssertionError("The stale destination reappeared or an extra job ran")
        result["targetSha256"] = hashlib.sha256(target_file.read_bytes()).hexdigest()
        result["steps"].append("Invalid localized speed corrected in same form; only current target received exact bytes")
        result["passed"] = True
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
    finally:
        for child in (tracer, process):
            if child is not None and child.poll() is None:
                os.killpg(child.pid, signal.SIGTERM)
                try:
                    child.wait(timeout=6)
                except subprocess.TimeoutExpired:
                    os.killpg(child.pid, signal.SIGKILL)
                    child.wait(timeout=6)
        if trace.is_file():
            result["delayedStatCalls"] = [line.strip() for line in
                trace.read_text(errors="replace").splitlines()
                if str(slow) in line or "lstat resumed" in line][-5:]
        result["sourcePreserved"] = source_file.is_file() and hashlib.sha256(
            source_file.read_bytes()).hexdigest() == source_hash
        result["currentNames"] = sorted(entry.name for entry in current.iterdir())
        result["removedOldTarget"] = not slow.exists()
        report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    raise SystemExit(main(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])))
