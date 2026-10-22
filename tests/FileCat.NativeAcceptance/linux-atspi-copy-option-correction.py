#!/usr/bin/env python3
"""Check invalid Copy speed correction in one native Ubuntu dialog.

Usage: linux-atspi-copy-option-correction.py APP_PATH REPORT.json
Run in the logged-in desktop user's AT-SPI session. Only an isolated validation
binary and newly created fixture below ~/FileCatValidation are accepted.
"""

import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


def descendants(node):
    yield node
    for index in range(node.get_child_count()):
        yield from descendants(node.get_child_at_index(index))


def matching(node, role, name):
    return [child for child in descendants(node)
            if child.get_role_name() == role and child.get_name() == name]


def wait_for(check, description, seconds=15):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        result = check()
        if result:
            return result
        time.sleep(0.15)
    raise AssertionError(f"Timed out waiting for {description}")


def action(node, description):
    if node is None or node.get_n_actions() != 1 or not node.do_action(0):
        raise AssertionError(f"The {description} accessibility action was unavailable")


def review_button(app, name):
    for button in matching(app, "button", name):
        parent = button
        for _ in range(8):
            parent = parent.get_parent() if parent is not None else None
            if parent is None:
                break
            # Builds before 9479b280 exposed the card by class name; later ones name it "<title> dialog".
            if parent.get_name() == "DialogCard" or parent.get_name().endswith(" dialog"):
                return button
    return None


def main():
    if len(sys.argv) != 3:
        raise SystemExit("Usage: linux-atspi-copy-option-correction.py APP_PATH REPORT.json")
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = Path(sys.argv[1]).resolve(strict=True)
    if app_path.name != "FileCat" or validation not in app_path.parents:
        raise SystemExit("Use an isolated FileCat binary below the user's FileCatValidation directory")
    report = Path(sys.argv[2])
    fixture = validation / "CopyOptionCorrection" / uuid4().hex
    source, target, data = (fixture / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    source_file, target_file = source / "a.txt", target / "a.txt"
    source_file.write_bytes(b"native option correction\n")
    original_hash = hashlib.sha256(source_file.read_bytes()).hexdigest()
    with app_path.open("rb") as binary:
        app_hash = hashlib.file_digest(binary, "sha256").hexdigest()
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    os.environ.setdefault("DISPLAY", ":0")
    process = None
    try:
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log,
                                       stderr=subprocess.STDOUT)
            desktop = Atspi.get_desktop(0)
            def get_app():
                return next((candidate for candidate in
                             (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                             if candidate.get_name() == "FileCat"
                             and matching(candidate, "list", f"Panel 1, source, {source}")), None)
            app = wait_for(get_app, "the isolated FileCat window")
            row = wait_for(lambda: next(iter(matching(app, "list item", "a.txt")), None),
                           "source file row")
            if not row.grab_focus():
                raise AssertionError("Could not focus the exact source file")
            wait_for(lambda: row.get_state_set().contains(Atspi.StateType.FOCUSED), "source focus")
            action(matching(app, "button", "Copy…")[0], "Copy command")
            summary = f'Copy "a.txt" ({source_file.stat().st_size} bytes)'
            wait_for(lambda: len(matching(app, "label", summary)) == 1
                     and review_button(app, "Copy") is not None, "one Copy review")
            action(wait_for(lambda: next(iter(matching(app, "toggle button", "Options")), None),
                            "Options toggle"), "Options")
            field = wait_for(lambda: next(iter(matching(app, "entry", "Speed limit (MB/s):")), None),
                             "speed-limit field")
            if not field.get_editable_text().set_text_contents("-1"):
                raise AssertionError("Could not enter invalid speed")
            action(wait_for(lambda: review_button(app, "Copy"), "Copy approval"),
                   "invalid Copy approval")
            error_text = "Speed limit: enter a finite, nonnegative number of MB/s."
            wait_for(lambda: len(matching(app, "label", error_text)) == 1,
                     "visible invalid-speed error")
            if len(matching(app, "label", summary)) != 1 or target_file.exists():
                raise AssertionError("The invalid option closed the review or published a target")
            if len(matching(app, "entry", "Speed limit (MB/s):")) != 1:
                raise AssertionError("The editable speed field was replaced after the error")
            field_help = field.get_description()
            if error_text not in field_help:
                raise AssertionError(f"The speed field lacks accessible error help: {field_help!r}")
            if not field.get_editable_text().set_text_contents("0.0625"):
                raise AssertionError("Could not correct speed in the same field")
            wait_for(lambda: not matching(app, "label", error_text), "the error to clear")
            if field.get_description() and error_text in field.get_description():
                raise AssertionError("Corrected speed retains stale error help")
            action(wait_for(lambda: review_button(app, "Copy"), "corrected Copy approval"),
                   "corrected Copy approval")
            wait_for(lambda: target_file.is_file()
                     and hashlib.sha256(target_file.read_bytes()).hexdigest() == original_hash,
                     "exact copied bytes", seconds=30)
            if hashlib.sha256(source_file.read_bytes()).hexdigest() != original_hash:
                raise AssertionError("The source changed during Copy")
            result = {"passed": True, "app": str(app_path), "appSha256": app_hash,
                      "fixture": str(fixture), "invalidSpeed": "-1",
                      "visibleError": error_text, "speedFieldHelp": field_help,
                      "sameDialogCorrectedSpeed": "0.0625", "sourceSha256": original_hash,
                      "targetSha256": original_hash}
    except Exception as exc:
        result = {"passed": False, "error": f"{type(exc).__name__}: {exc}",
                  "app": str(app_path), "fixture": str(fixture)}
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
