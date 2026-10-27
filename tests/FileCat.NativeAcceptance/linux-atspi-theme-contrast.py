#!/usr/bin/env python3
"""Stage native High Contrast panel, danger, selection and search captures.

Usage: linux-atspi-theme-contrast.py GIT_APP REPORT.json UINPUT_KEY_HELPER
After each READY line, capture the Ubuntu VM screen, then create its printed
.done marker. Every file and dialog belongs to the disposable fixture.
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

HERE = Path(__file__).resolve().parent
common = runpy.run_path(str(HERE / "linux-atspi-viewer-find.py"))
find, wait_for, descendants, checked_action = (common[key] for key in
    ("find", "wait_for", "descendants", "checked_action"))
text_value = runpy.run_path(str(HERE / "linux-atspi-viewer-copy-reflow.py"))["text_value"]
desktop_environment = runpy.run_path(str(HERE / "linux-atspi-ftp-large-listing.py"))["desktop_environment"]
NAME = "visible.txt"
CONTENT = b"visible keyword stays readable in High Contrast\n"


def key(helper, password, chord):
    sent = subprocess.run(["sudo", "-S", sys.executable, str(helper), chord],
                          input=password + "\n", text=True, capture_output=True, timeout=15)
    if sent.returncode:
        raise AssertionError(f"Physical {chord} failed: {sent.stderr[-240:]}")


def run(app_path, report_path, helper):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, helper = (path.resolve(strict=True) for path in (app_path, helper))
    if app_path.name != "FileCat" or validation not in app_path.parents or \
            helper.name != "linux-uinput-key.py" or validation not in helper.parents or \
            report_path.parent != validation or report_path.exists():
        raise AssertionError("Use the Git-built app, owned key helper and fresh guest receipt")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Another FileCat is already running")
    password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not password:
        raise AssertionError("The physical-key helper needs a transient guest credential")
    root = validation / "ThemeContrast" / uuid4().hex
    source, target, data = (root / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    (source / NAME).write_bytes(CONTENT)
    (source / ".hidden.txt").write_text("hidden but actionable\n", encoding="utf-8")
    (data / "settings.json").write_text(json.dumps({"SchemaVersion": 1,
        "Theme": "HighContrast", "ThemeAnimations": False}), encoding="utf-8")
    env = os.environ.copy()
    env.update(desktop_environment())
    env["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    result = {"passed": False, "appDllSha256": hashlib.sha256(
        (app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
        "sourceSha256": hashlib.sha256(CONTENT).hexdigest(), "phases": []}
    process = None
    mouse = helper.with_name("linux-uinput-mouse.py")
    if not mouse.is_file():
        raise AssertionError("The owned native pointer helper is unavailable")

    def handoff(phase, app):
        if process.poll() is not None:
            raise AssertionError(f"FileCat exited before {phase}")
        moved = subprocess.run(["sudo", "-S", sys.executable, str(mouse),
                                "1200", "600", "1280", "800", "move"],
                               input=password + "\n", text=True, capture_output=True,
                               timeout=20)
        if moved.returncode:
            raise AssertionError(f"Could not move the pointer off content: {moved.stderr[-200:]}")
        ready = report_path.with_name(report_path.stem + f".{phase}.ready")
        done = report_path.with_name(report_path.stem + f".{phase}.done")
        ready.write_text(phase + "\n", encoding="utf-8")
        result["phases"].append({"name": phase, "appPid": process.pid,
                                 "accessibleNodes": sum(1 for _ in descendants(app))})
        print(f"READY {phase} {done}", flush=True)
        deadline = time.monotonic() + 240
        while not done.exists():
            if process.poll() is not None or time.monotonic() >= deadline:
                raise AssertionError(f"The {phase} screenshot handoff expired")
            time.sleep(0.2)
        ready.unlink(missing_ok=True)
        done.unlink(missing_ok=True)

    try:
        with (root / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid), None),
                       "owned High Contrast FileCat", seconds=35)
        row = wait_for(lambda: next((node for node in descendants(app)
                       if node.get_role_name() == "list item" and
                       (node.get_name() or "").startswith(NAME + ", file,")), None),
                       "owned visible file row")
        hidden = wait_for(lambda: next((node for node in descendants(app)
                          if node.get_role_name() == "list item" and
                          (node.get_name() or "").startswith(".hidden.txt, file,")), None),
                          "actionable hidden row")
        result["hiddenAccessibleName"] = hidden.get_name()
        window = wait_for(lambda: next((parts[0] for line in
                          subprocess.run(["/usr/bin/wmctrl", "-lp"], env=env,
                                         text=True, capture_output=True, timeout=5).stdout.splitlines()
                          if (parts := line.split(maxsplit=4)) and len(parts) == 5 and
                          parts[2] == str(process.pid)), None), "owned window ID")
        subprocess.run(["/usr/bin/wmctrl", "-ir", window, "-e", "0,100,75,1000,660"],
                       env=env, check=True, timeout=8)
        time.sleep(1)
        handoff("panel", app)
        if not row.grab_focus():
            raise AssertionError("The owned visible row could not take focus")
        key(helper, password, "f8")
        wait_for(lambda: find(app, "button", "Close dialog"), "Delete review")
        result["deleteButtons"] = [node.get_name() for node in descendants(app)
                                   if node.get_role_name() == "button" and node.get_name()]
        handoff("danger", app)
        key(helper, password, "escape")
        wait_for(lambda: find(app, "button", "Close dialog") is None,
                 "Delete review cancellation")
        if not row.grab_focus():
            raise AssertionError("The owned row lost focus after Cancel")
        checked_action(find(app, "button", "View"), "owned text viewer")
        viewer = wait_for(lambda: find(app, "frame", f"{NAME} — FileCat Viewer"),
                          "owned Text viewer")
        wait_for(lambda: find(viewer, "label", "Mode: Text"), "Text mode")
        text = wait_for(lambda: find(viewer, "entry", "Text viewer"), "read-only Text document")
        if not text.grab_focus():
            raise AssertionError("The Text document could not take focus")
        key(helper, password, "ctrl+a")
        wait_for(lambda: "Selected source bytes 0 through" in text_value(viewer)[2],
                 "explicit Text selection")
        result["selectionStatus"] = text_value(viewer)[2]
        handoff("selection", app)
        key(helper, password, "right")
        checked_action(find(viewer, "button", "Show or hide viewer Find bar"), "Find toggle")
        query = wait_for(lambda: find(viewer, "entry", "Find in file"), "Find field")
        if not query.get_editable_text().set_text_contents("visible"):
            raise AssertionError("Could not set the owned Text search query")
        checked_action(find(viewer, "button", "Find next match"), "Find next")
        wait_for(lambda: "visible" in text_value(viewer)[1].lower(),
                 "visible search hit")
        result["searchStatus"] = text_value(viewer)[2]
        handoff("search", app)
        if (source / NAME).read_bytes() != CONTENT or list(target.iterdir()):
            raise AssertionError("Theme review changed the source or target")
        result["sourceUnchanged"] = result["targetEmpty"] = result["passed"] = True
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        if (root / "app.log").is_file():
            result["appLogTail"] = (root / "app.log").read_text(
                encoding="utf-8", errors="replace")[-600:]
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
        for phase in ("panel", "danger", "selection", "search"):
            report_path.with_name(report_path.stem + f".{phase}.ready").unlink(missing_ok=True)
            report_path.with_name(report_path.stem + f".{phase}.done").unlink(missing_ok=True)
        shutil.rmtree(root)
    return result


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    receipt = Path(sys.argv[2]).resolve()
    outcome = run(Path(sys.argv[1]), receipt, Path(sys.argv[3]))
    receipt.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(outcome), flush=True)
    raise SystemExit(0 if outcome["passed"] else 1)
