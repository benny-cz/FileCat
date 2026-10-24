#!/usr/bin/env python3
"""Check native Ubuntu focus return from Create, Delete and inline Rename.

Usage: linux-atspi-rename-cancel-focus.py APP_PATH REPORT.json UINPUT_KEY_HELPER [--dialog-only]
Run in the logged-in Ubuntu VM on an isolated Git-built FileCat app. Supply
FILECAT_NATIVE_SUDO_PASSWORD only to this runner for physical keyboard input.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


base = Path(__file__).parent
shared = runpy.run_path(str(base / "linux-atspi-ftp-large-listing.py"))
dialog = runpy.run_path(str(base / "linux-atspi-dialog-pointer-dismiss.py"))
desktop_environment = shared["desktop_environment"]
press = shared["press"]
stop = shared["stop"]
descendants = dialog["descendants"]
named = dialog["named"]
wait_for = dialog["wait_for"]


def run(app_path, report_path, helper, dialog_only=False):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, helper = app_path.resolve(strict=True), helper.resolve(strict=True)
    report_path = report_path.resolve()
    if (app_path.name != "FileCat" or helper.name != "linux-uinput-key.py" or
            validation not in app_path.parents or validation not in helper.parents or
            report_path.parent != validation or report_path.exists()):
        raise AssertionError("Use an isolated Git build, owned keyboard and fresh validation receipt")
    password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not password:
        raise AssertionError("A transient guest sudo credential is required for physical input")
    os.environ.update(desktop_environment())
    fixture = validation / "RenameCancelFocus" / uuid4().hex
    source, target, data = (fixture / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    expected = {"a.txt": b"alpha\n", "b.txt": b"beta\n", "c.txt": b"charlie\n"}
    for name, contents in expected.items():
        (source / name).write_bytes(contents)
    hashes = {name: hashlib.sha256(contents).hexdigest() for name, contents in expected.items()}
    result = {"passed": False, "dialogOnly": dialog_only, "appDllSha256": hashlib.sha256(
        app_path.with_name("FileCat.dll").read_bytes()).hexdigest(), "steps": []}
    process = None
    try:
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log,
                                       stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
            (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
            if node.get_name() == "FileCat" and
            named(node, "list", f"Panel 1, source, {source}")), None),
            "isolated FileCat panel", seconds=35)
        panel = named(app, "list", f"Panel 1, source, {source}")[0]

        def focus_name():
            return [node.get_name() for node in descendants(panel)
                    if node.get_role_name() == "list item" and
                    node.get_state_set().contains(Atspi.StateType.FOCUSED)]

        def returned(label):
            wait_for(lambda: focus_name() == ["c.txt"], f"{label} to restore c.txt")
            time.sleep(0.5)
            focused = focus_name()
            result["steps"].append({"step": label, "focused": focused, "passed": focused == ["c.txt"]})
            if focused != ["c.txt"]:
                raise AssertionError(f"{label} settled on {focused!r}")

        press(helper, password, "end", process.pid)
        returned("End focuses c.txt")
        cases = (
                [] if dialog_only else [("Rename", "f2", lambda: named(app, "entry", "Rename selected item"))]) + [
                ("Create folder", "f7", lambda: named(app, "button", "Close dialog")),
                ("Delete", "f8", lambda: named(app, "button", "Close dialog"))]
        for label, chord, opened in cases:
            press(helper, password, chord, process.pid)
            try:
                wait_for(opened, f"{label} review")
            except AssertionError:
                result["steps"].append({"step": f"{label} accessibility probe",
                                        "nodes": [(node.get_role_name(), node.get_name())
                                                  for node in descendants(app)
                                                  if node.get_role_name() in
                                                  ("entry", "text", "button", "list item")][:120]})
                raise
            press(helper, password, "escape", process.pid)
            wait_for(lambda: not opened(), f"{label} cancellation")
            returned(f"Escape from {label}")
        actual = {path.name: path.read_bytes() for path in source.iterdir()}
        if actual != expected or any(target.iterdir()) or any(
                hashlib.sha256(contents).hexdigest() != hashes[name]
                for name, contents in actual.items()):
            raise AssertionError("A canceled decision changed the isolated fixture")
        result["sourceSha256"] = hashes
        result["targetEmpty"] = True
        result["passed"] = True
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
        if (fixture / "app.log").exists():
            result["appLogTail"] = (fixture / "app.log").read_text(errors="replace")[-1500:]
    finally:
        stop(process)
        shutil.rmtree(fixture)
        report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) not in (4, 5) or len(sys.argv) == 5 and sys.argv[4] != "--dialog-only":
        raise SystemExit(__doc__)
    raise SystemExit(run(*(Path(value) for value in sys.argv[1:4]), len(sys.argv) == 5))
