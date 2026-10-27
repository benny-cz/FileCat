#!/usr/bin/env python3
"""Stop an owned native Find during a bounded child-directory read delay.

Usage: linux-atspi-find-stop-held.py APP REPORT.json [--speech]
Set FILECAT_NATIVE_SUDO_PASSWORD transiently for path-filtered strace.
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
import traceback
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

HERE = Path(__file__).resolve().parent
remote = runpy.run_path(str(HERE / "linux-atspi-sftp-delayed-listing.py"))
wait_for = remote["wait_for"]
tree = remote["tree"]
desktop_environment = remote["desktop_environment"]
speech_module = runpy.run_path(str(HERE / "linux-orca-speech.py"))


def text_of(node):
    iface = node.get_text_iface()
    return Atspi.Text.get_text(iface, 0, -1) if iface is not None else node.get_name()


def button(root, name):
    return next((node for node in tree(root) if node.get_role_name() == "button"
                 and (node.get_name() or "") == name), None)


def label_text(root, prefix):
    return next((text_of(node) for node in tree(root) if node.get_role_name() == "label"
                 and text_of(node).startswith(prefix)), None)


def main(app_path, report_path, speech_mode):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            report_path.parent.resolve() != validation or report_path.exists() or
            not report_path.name.startswith("ac-s2-044-find-stop-") or
            report_path.suffix != ".json"):
        raise AssertionError("Use the isolated Git app and a fresh owned report")
    password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not password:
        raise AssertionError("Transient guest authentication is needed for the owned delay")
    os.environ.update(desktop_environment())
    os.environ["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    root = validation / "FindHeldFixture" / uuid4().hex
    source, target, data, slow = (root / leaf for leaf in ("source", "target", "data", "source/zz-slow"))
    for folder in (source, target, data, slow):
        folder.mkdir(parents=True)
    files = [source / "visible.txt", slow / "untouched.txt"]
    files[0].write_bytes(b"visible native Find source\n")
    files[1].write_bytes(b"slow native Find source\n")
    before = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in files}
    result = {"passed": False, "buildDllSha256": hashlib.sha256(
        app_path.with_name("FileCat.dll").read_bytes()).hexdigest(),
        "fixture": str(root), "sourceSha256": before, "steps": []}
    process = tracer = orca = None
    speech = None
    spoken = []
    hold_seconds = 30 if speech_mode else 20
    stop_deadline = 25 if speech_mode else 15
    try:
        child_env = os.environ.copy()
        with (root / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=root, env=child_env, stdout=log,
                                       stderr=subprocess.STDOUT, start_new_session=True)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
            (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
            if node is not None and node.get_process_id() == process.pid and
            node.get_name() == "FileCat"), None), "owned FileCat", seconds=25)
        wait_for(lambda: any(node.get_role_name() == "list item" and
            (node.get_name() or "").startswith("visible.txt,") for node in tree(app)),
            "owned source row", seconds=20)

        if speech_mode:
            if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", "orca"],
                              capture_output=True).returncode == 0:
                raise AssertionError("Another Orca instance owns the speech session")
            speech = speech_module["SpeechLog"](root / "orca-debug.log")
            orca_env = {**child_env, "GDK_BACKEND": "wayland", "WAYLAND_DISPLAY": "wayland-0"}
            orca = subprocess.Popen(["/usr/bin/python3", "-c", speech_module["ORCA_WRAPPER"],
                                     str(speech.path)], env=orca_env,
                                    stdout=subprocess.DEVNULL,
                                    stderr=(root / "orca.err").open("wb"), start_new_session=True)
            wait_for(lambda: "Screen reader on" in " ".join(speech.take()),
                     "Orca speech startup", seconds=25)

        toolbar = wait_for(lambda: next((node for node in tree(app)
            if node.get_role_name() == "button" and
            (node.get_name() or "").startswith("Find files")), None), "toolbar Find")
        if not toolbar.do_action(0):
            raise AssertionError("Could not open native Find")
        find = wait_for(lambda: next((node for node in tree(app)
            if node.get_role_name() in ("frame", "window") and
            node.get_name() == "Find files"), None), "Find window", seconds=20)
        names = wait_for(lambda: next((node for node in tree(find)
            if node.get_role_name() == "entry" and node.get_name() == "Names"), None),
            "Find Names field")
        query = "native-absent-" + uuid4().hex
        if not names.get_editable_text().set_text_contents(query):
            raise AssertionError("Could not set the owned no-match query")

        trace = root / "slow-open.trace"
        tracer = subprocess.Popen(["sudo", "-S", "/usr/bin/strace", "-f", "-T", "-p",
                                   str(process.pid), "-o", str(trace), "-e", "trace=openat",
                                   "-e", f"inject=openat:delay_enter={hold_seconds}s", "-P", str(slow)],
                                  stdin=subprocess.PIPE, stdout=subprocess.DEVNULL,
                                  stderr=(root / "strace.err").open("wb"), start_new_session=True)
        tracer.stdin.write((password + "\n").encode())
        tracer.stdin.flush()
        tracer.stdin.close()
        wait_for(lambda: any(line.startswith("TracerPid:") and int(line.split()[1]) > 0
                             for line in Path(f"/proc/{process.pid}/status").read_text().splitlines()),
                 "path-filtered tracer attachment", seconds=8)

        start = time.monotonic()
        find_action = wait_for(lambda: button(find, "Find"), "Find action")
        if not find_action.do_action(0):
            raise AssertionError("Could not start the owned search")
        stop = wait_for(lambda: next((node for node in tree(find)
            if node.get_role_name() == "button" and node.get_name() == "Stop" and
            node.get_state_set().contains(Atspi.StateType.ENABLED)), None),
            "enabled Stop", seconds=4)
        progress = wait_for(lambda: next((text_of(node) for node in tree(find)
            if node.get_role_name() == "label" and str(slow) in text_of(node)), None),
            "search progress in owned slow folder", seconds=4)
        busy = label_text(find, "Searching")
        result["beforeStop"] = {"progress": progress, "busy": busy,
            "elapsedSeconds": round(time.monotonic() - start, 3)}
        if not busy or time.monotonic() - start >= stop_deadline:
            raise AssertionError("Find was not visibly searching within the bounded delay")
        if speech is not None:
            speech.take()
            stop.grab_focus()
        stop_at = time.monotonic()
        if not stop.do_action(0):
            raise AssertionError("Could not stop the active Find")
        stopped = wait_for(lambda: label_text(find, "Stopped · no results yet"),
                           "partial stopped completion", seconds=35)
        explanation = wait_for(lambda: next((text_of(node) for node in tree(find)
            if node.get_role_name() == "label" and "Only part of the scope was searched" in text_of(node)), None),
            "incomplete-scope explanation", seconds=10)
        finished_at = time.monotonic()
        if stop_at - start >= stop_deadline or finished_at - start < hold_seconds - 2:
            raise AssertionError("The search did not stop during the owned delayed read")
        if speech is not None:
            def heard(fragment):
                spoken.extend(speech.take())
                return any(fragment in phrase.lower() for phrase in spoken)
            wait_for(lambda: heard("only part of the scope was searched"),
                     "Orca incomplete-scope speech", seconds=10)
            result["speech"] = spoken
        after = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in files}
        if after != before or list(target.iterdir()):
            raise AssertionError("Stopped Find changed the owned source or target")
        result["steps"].append({"progress": progress, "busy": busy,
                                "stopClickedAfterSeconds": round(stop_at - start, 3),
                                "finishedAfterSeconds": round(finished_at - start, 3),
                                "headline": stopped, "explanation": explanation,
                                "sourcePreserved": True, "targetEmpty": True})
        result["passed"] = True
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
        result["traceback"] = traceback.format_exc(limit=7)
    finally:
        if speech is not None:
            spoken.extend(speech.take())
            result["speech"] = spoken
        if tracer is not None and tracer.poll() is None:
            tracer.terminate()
            try: tracer.wait(timeout=5)
            except subprocess.TimeoutExpired: tracer.kill()
        if process is not None and process.poll() is None:
            os.killpg(process.pid, signal.SIGTERM)
            process.wait(timeout=10)
        if orca is not None and orca.poll() is None:
            os.killpg(orca.pid, signal.SIGTERM)
            orca.wait(timeout=10)
        report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) not in (3, 4) or (len(sys.argv) == 4 and sys.argv[3] != "--speech"):
        raise SystemExit(__doc__)
    raise SystemExit(main(Path(sys.argv[1]), Path(sys.argv[2]), len(sys.argv) == 4))
