#!/usr/bin/env python3
"""Measure bounded native AT-SPI work for a 2,000-file Ubuntu listing.

Usage: linux-atspi-large-listing.py APP_PATH REPORT.json KEY_HELPER
Run in the logged-in GNOME desktop against an isolated Git-built FileCat.
FILECAT_NATIVE_SUDO_PASSWORD is used only for the disposable physical-key helper.
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

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


COUNT = 2000
LIMIT = 800


def wait_for(check, description, seconds=25):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        result = check()
        if result:
            return result
        time.sleep(0.15)
    raise AssertionError(f"Timed out waiting for {description}")


def tree(root):
    pending = [root]
    visited = 0
    while pending:
        node = pending.pop()
        visited += 1
        if visited > LIMIT:
            raise AssertionError(f"AT-SPI enumerated more than {LIMIT} nodes")
        yield node
        children = node.get_child_count()
        if children > LIMIT:
            raise AssertionError(f"One AT-SPI node exposed {children} children")
        pending.extend(node.get_child_at_index(i) for i in range(children - 1, -1, -1))


def count_in_status(text):
    match = re.search(r"([\d\s,\.\u00a0]+)\s+files\b", text)
    return int(re.sub(r"\D", "", match.group(1))) if match else None


def snapshot(app, source):
    started = time.perf_counter()
    nodes = list(tree(app))
    matching = [node for node in nodes if node.get_role_name() == "list"
                and node.get_name() == f"Panel 1, source, {source}"]
    if len(matching) != 1:
        raise AssertionError("The exact source list was not exposed once")
    rows = [node for node in tree(matching[0]) if node.get_role_name() == "list item"]
    statuses = [(node.get_name(), count_in_status(node.get_name())) for node in nodes
                if node.get_role_name() == "label"]
    status = next(((name, count) for name, count in statuses if count == COUNT + 2), None)
    elapsed = round(time.perf_counter() - started, 3)
    return {"status": status[0] if status else None, "fileCount": status[1] if status else None,
            "rowCount": len(rows), "visited": len(nodes), "querySeconds": elapsed,
            "focused": [row.get_name() for row in rows
                        if row.get_state_set().contains(Atspi.StateType.FOCUSED)],
            "visibleNames": [row.get_name() for row in rows]}


def press(helper, password, chord):
    run = subprocess.run(["sudo", "-S", "/usr/bin/python3", str(helper), chord],
                         input=password + "\n", capture_output=True, text=True, timeout=15)
    if run.returncode:
        raise AssertionError(f"Physical {chord} failed with exit {run.returncode}: {run.stderr[-300:]}")


def run(app_path, report_path, helper):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, helper = (path.resolve(strict=True) for path in (app_path, helper))
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            helper.name != "linux-uinput-key.py" or validation not in helper.parents or
            report_path.parent != validation or report_path.exists()):
        raise AssertionError("Use an isolated Git build, owned key helper and fresh validation report")
    password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not password:
        raise AssertionError("The physical-key helper needs the transient test credential")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"], capture_output=True).returncode == 0:
        raise AssertionError("Close other FileCat processes before this isolated listing check")
    root = validation / "LargeAtSpiListing" / uuid4().hex
    source, target, data = (root / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    original = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    for name, content in original.items():
        (source / name).write_bytes(content)
    for index in range(COUNT):
        (source / f"native-list-{index:04d}.txt").write_bytes(b"")
    env = os.environ.copy()
    env.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    env.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={env['XDG_RUNTIME_DIR']}/bus")
    env.setdefault("DISPLAY", ":0")
    result = {"passed": False, "fixture": str(root), "generatedFiles": COUNT,
              "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest()}
    with (root / "app.log").open("wb") as log:
        process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                    "--left", str(source), "--right", str(target)], cwd=root,
                                   env=env, stdout=log, stderr=subprocess.STDOUT)
        try:
            desktop = Atspi.get_desktop(0)
            app = wait_for(lambda: next((node for node in
                           (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                           if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                           "the exact test-owned FileCat")
            first = wait_for(lambda: (value if value["fileCount"] == COUNT + 2 else None)
                             if (value := snapshot(app, source)) else None, "the complete listing")
            if first["rowCount"] > 120 or first["visited"] > LIMIT or first["querySeconds"] > 5:
                raise AssertionError(f"The native listing was not bounded: {first}")
            press(helper, password, "home")
            for _ in range(3):  # Parent, a.txt, b.txt, then the first generated file.
                press(helper, password, "down")
            first_name = "native-list-0000.txt"
            wait_for(lambda: first_name in snapshot(app, source)["focused"], "first-file focus")
            started = time.perf_counter()
            press(helper, password, "end")
            last_name = f"native-list-{COUNT - 1:04d}.txt"
            last = wait_for(lambda: (value if last_name in value["focused"] else None)
                            if (value := snapshot(app, source)) else None, "last-file focus")
            jump_seconds = round(time.perf_counter() - started, 3)
            if (last["rowCount"] > 120 or last["visited"] > LIMIT or
                    last["querySeconds"] > 5 or jump_seconds > 10):
                raise AssertionError(f"End navigation was not bounded: {last}")
            if ({name: (source / name).read_bytes() for name in original} != original or
                    any(target.iterdir()) or len(list(source.iterdir())) != COUNT + 2):
                raise AssertionError("The large listing changed its isolated fixture")
            result.update({"passed": True, "loaded": first, "afterEnd": last,
                           "endKeySeconds": jump_seconds, "originalSourceBytesUnchanged": True,
                           "targetEmpty": True})
            return result
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=10)
            shutil.rmtree(root)


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    report = Path(sys.argv[2]).resolve()
    try:
        outcome = run(Path(sys.argv[1]), report, Path(sys.argv[3]))
    except Exception as error:
        outcome = {"passed": False, "error": f"{type(error).__name__}: {error}"}
    report.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in outcome.items()
                      if key not in ("loaded", "afterEnd")}))
    raise SystemExit(0 if outcome["passed"] else 1)
