#!/usr/bin/env python3
"""Exercise obscured FileCat commands through Ubuntu AT-SPI while reviews own focus.

Usage: linux-atspi-modal-guard.py /home/benny/FileCatValidation/BUILD/FileCat REPORT.json
Run as the logged-in desktop user with its AT-SPI session bus available.
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


def button(app, name, toolbar):
    matches = matching(app, "button", name)
    if not matches:
        available = [node.get_name() for node in descendants(app)
                     if node.get_role_name() == "button"]
        raise AssertionError(f"The background {name} buttons are unavailable: {available[:50]}")
    positioned = sorted(matches, key=lambda item: item.get_component().get_extents(Atspi.CoordType.SCREEN).y)
    return positioned[0] if toolbar else positioned[-1]


def main():
    if len(sys.argv) != 3:
        raise SystemExit("Usage: linux-atspi-modal-guard.py APP_PATH REPORT.json")
    app_path = Path(sys.argv[1]).resolve(strict=True)
    report = Path(sys.argv[2])
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    if app_path.name != "FileCat" or validation not in app_path.parents:
        raise SystemExit("Use an isolated FileCat binary below the user's FileCatValidation directory")
    fixture = validation / "ModalGuardFixture" / uuid4().hex
    source, target, data = (fixture / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    source_file = source / "a.txt"
    source_file.write_bytes(b"modal source remains")
    original_hash = hashlib.sha256(source_file.read_bytes()).hexdigest()
    with app_path.open("rb") as binary:
        app_hash = hashlib.file_digest(binary, "sha256").hexdigest()
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    os.environ.setdefault("DISPLAY", ":0")
    if (validation / "dotnet-sdk").is_dir():
        os.environ.setdefault("DOTNET_ROOT", str(validation / "dotnet-sdk"))
    env = os.environ.copy()
    process = None
    try:
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=env, stdout=log, stderr=subprocess.STDOUT)
            desktop = Atspi.get_desktop(0)
            def get_app():
                return next((candidate for candidate in
                             (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                             if candidate.get_name() == "FileCat"
                             and matching(candidate, "list", f"Panel 1, source, {source}")), None)
            app = wait_for(get_app, "the isolated FileCat window")
            def source_rows():
                panels = matching(app, "list", f"Panel 1, source, {source}")
                return [node for node in descendants(panels[0]) if node.get_role_name() == "list item"
                        and (node.get_name() or "").startswith("a.txt, file,")] if len(panels) == 1 else []
            row = wait_for(source_rows, "the exact source file")
            if len(row) != 1:
                raise AssertionError(f"Expected one exact source row, found {len(row)}")
            wait_for(lambda: row[0].grab_focus() and
                     row[0].get_state_set().contains(Atspi.StateType.FOCUSED), "source focus")
            journeys = []
            for verb in ("Copy", "Move", "Delete"):
                toolbar_command = "Move / rename…" if verb == "Move" else f"{verb}…"
                action(button(app, toolbar_command, True), f"{verb} command")
                summary = f'{verb} "a.txt" ({source_file.stat().st_size} bytes)'
                def summary_count():
                    if verb == "Delete":
                        return sum(1 for node in descendants(app)
                                   if node.get_role_name() == "label"
                                   and "a.txt" in (node.get_name() or "")
                                   and "Recycle Bin" in (node.get_name() or ""))
                    return len(matching(app, "label", summary))
                def review_count():
                    return (summary_count(),
                            len(matching(app, "entry", "Destination")),
                            len(matching(app, "button", "Cancel")))
                expected = (1, 0 if verb == "Delete" else 1, 1)
                wait_for(lambda: review_count() == expected, f"one complete {verb} review")
                attempts = []
                for name in ("Copy…", "Move / rename…", "Delete…", "Copy", "Move", "Delete"):
                    toolbar = name.endswith("…")
                    action(button(app, name, toolbar), f"background {name}")
                    time.sleep(0.25)
                    if review_count() != expected:
                        raise AssertionError(f"Background {name} changed the {verb} review")
                    if (target / "a.txt").exists() or hashlib.sha256(source_file.read_bytes()).hexdigest() != original_hash:
                        raise AssertionError(f"Background {name} changed the source or destination")
                    attempts.append({"command": name, "toolbar": toolbar, "modalStillSingle": True})
                action(matching(app, "button", "Cancel")[0], "Cancel")
                wait_for(lambda: review_count() == (0, 0, 0), f"{verb} review to close")
                if (target / "a.txt").exists() or hashlib.sha256(source_file.read_bytes()).hexdigest() != original_hash:
                    raise AssertionError(f"Cancel changed the source or destination after {verb}")
                journeys.append({"review": verb, "before": list(expected), "attempts": attempts,
                                 "afterCancel": [0, 0, 0], "targetAbsent": True})
            result = {"passed": True, "app": str(app_path), "appSha256": app_hash,
                      "fixture": str(fixture), "sourceSha256": original_hash,
                      "journeys": journeys}
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
