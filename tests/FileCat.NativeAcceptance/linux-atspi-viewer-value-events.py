#!/usr/bin/env python3
"""Inspect native Text-viewer value events around keyboard scrolling.

Usage: linux-atspi-viewer-value-events.py GIT_APP REPORT.json UINPUT_KEY_HELPER
Owns a local 400-line fixture and FileCat process on the Ubuntu desktop. This
checks the semantic event boundary, without repeating passed Copy acceptance.
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
from gi.repository import Atspi, GLib

HERE = Path(__file__).resolve().parent
common = runpy.run_path(str(HERE / "linux-atspi-viewer-find.py"))
find, wait_for, checked_action, descendants = (common[name] for name in
                                               ("find", "wait_for", "checked_action", "descendants"))
viewer_window_id = common["exact_viewer_window_id"]
CONTENT = "".join(f"Line {index:04d}: " + chr(65 + index % 26) * 48 + "\n"
                  for index in range(400)).encode("ascii")


def value(document):
    text = document.get_text_iface()
    if text is None:
        raise AssertionError("The native Text document has no text interface")
    return Atspi.Text.get_text(text, 0, -1)


def pump_until(check, description, seconds=12):
    deadline = time.monotonic() + seconds
    context = GLib.MainContext.default()
    while time.monotonic() < deadline:
        while context.pending():
            context.iteration(False)
        found = check()
        if found:
            return found
        time.sleep(0.05)
    raise AssertionError(f"Timed out waiting for {description}")


def run(app_path, report_path, key_helper):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    key_helper = key_helper.resolve(strict=True)
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            key_helper.name != "linux-uinput-key.py" or validation not in key_helper.parents or
            report_path.parent != validation or report_path.exists()):
        raise AssertionError("Use the isolated Git app, owned key helper and fresh report")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Another FileCat process is running")
    password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not password:
        raise AssertionError("A transient guest input-helper credential is required")
    root = validation / "ViewerValueEvents" / uuid4().hex
    source, target, data = (root / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    file = source / "lines.txt"
    file.write_bytes(CONTENT)
    os.environ.update(runpy.run_path(str(HERE / "linux-atspi-ftp-large-listing.py"))["desktop_environment"]())
    environment = os.environ.copy()
    environment["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    result = {"passed": False, "stage": "startup",
              "sourceSha256": hashlib.sha256(CONTENT).hexdigest(),
              "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest()}
    process = None
    listener = None
    try:
        with (root / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=root, env=environment, stdout=log, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "exact owned app", seconds=35)
        row = wait_for(lambda: next((node for node in descendants(app)
                       if node.get_role_name() == "list item" and
                       (node.get_name() or "").startswith("lines.txt, file,")), None),
                       "owned source row")
        if not row.grab_focus():
            raise AssertionError("Source row cannot take native focus")
        checked_action(find(app, "button", "View"), "owned Text View")
        title = "lines.txt — FileCat Viewer"
        viewer = wait_for(lambda: find(app, "frame", title), "owned viewer")
        document = wait_for(lambda: find(viewer, "entry", "Text viewer"), "Text document")
        baseline = wait_for(lambda: text if "Line 0000:" in (text := value(document)) else None,
                            "committed first Text page")
        if document.get_state_set().contains(Atspi.StateType.EDITABLE):
            raise AssertionError("The read-only Text document is marked editable")
        result["stage"] = "native value event"
        events = []
        def on_event(event, _data):
            try:
                source_node = event.source
                if source_node is None or source_node.get_process_id() != process.pid or source_node.get_name() != "Text viewer":
                    return
                events.append({"type": str(event.type), "payload": str(event.any_data),
                               "getterAtEvent": value(document)})
            except Exception as error:
                events.append({"callbackError": f"{type(error).__name__}: {error}"})
        listener = Atspi.EventListener.new(on_event, None)
        if not listener.register("object:property-change:accessible-value"):
            raise AssertionError("AT-SPI refused the value-event subscription")
        window = viewer_window_id(title, process.pid)
        subprocess.run(["/usr/bin/wmctrl", "-ia", window], check=True, timeout=5)
        if not document.grab_focus():
            raise AssertionError("The Text document could not regain native focus")
        # Ignore the focus announcement; inspect only events caused by scroll.
        pump_until(lambda: True, "focus settle", seconds=0.2)
        events.clear()
        sent = subprocess.run(["sudo", "-S", sys.executable, str(key_helper), "pagedown"],
                              input=password + "\n", capture_output=True, text=True, timeout=15)
        if sent.returncode:
            raise AssertionError(f"Native PageDown was rejected: {sent.stderr[-250:]}")
        after = pump_until(lambda: text if "Line 0000:" not in (text := value(document)) else None,
                           "new visible rows after PageDown")
        pump_until(lambda: events if events else None, "committed native value event")
        result["baselinePrefix"] = baseline[:130]
        result["afterPrefix"] = after[:130]
        result["events"] = events[:20]
        if any("callbackError" in event for event in events):
            raise AssertionError(f"Native event callback failed: {events!r}")
        if not any(event["payload"] == after and event["getterAtEvent"] == after and
                   event["getterAtEvent"] != baseline
                   for event in events):
            raise AssertionError("No native event payload and getter reported the committed new viewport")
        if any(event["payload"] != event["getterAtEvent"] or
               event["payload"] == baseline for event in events):
            raise AssertionError("A scroll value event carried stale or mismatched content")
        if file.read_bytes() != CONTENT or list(target.iterdir()):
            raise AssertionError("The semantic read changed source or target")
        result.update({"passed": True, "stage": "complete", "sourceUnchanged": True,
                       "targetEmpty": True})
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        if (root / "app.log").is_file():
            result["appLogTail"] = (root / "app.log").read_text(
                encoding="utf-8", errors="replace")[-800:]
    finally:
        if listener is not None:
            listener.deregister("object:property-change:accessible-value")
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
        shutil.rmtree(root)
    return result


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    report = Path(sys.argv[2]).resolve()
    outcome = run(Path(sys.argv[1]), report, Path(sys.argv[3]))
    report.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(outcome))
    raise SystemExit(0 if outcome["passed"] else 1)
