#!/usr/bin/env python3
"""Native Ubuntu 200% nested four-panel resize, input and geometry acceptance.

Usage: linux-atspi-nested-panel-resize.py APP REPORT.json KEY_HELPER POINTER_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD only for this isolated VM process.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import shutil
import subprocess
import sys
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


def run(app_path: Path, report: Path, key_helper: Path, pointer_helper: Path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, key_helper, pointer_helper = (p.resolve(strict=True)
                                             for p in (app_path, key_helper, pointer_helper))
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            key_helper != validation / "linux-uinput-key.py" or
            pointer_helper != validation / "linux-uinput-pointer.py" or
            report.parent != validation or report.exists()):
        raise AssertionError("Use an isolated Git app, owned input helpers and fresh receipt")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise AssertionError("Transient native input credential required")
    for name in ("FileCat", "orca"):
        if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", name],
                          capture_output=True).returncode == 0:
            raise AssertionError(f"Stop another {name} process before this owned test")
    repo = next(parent for parent in app_path.parents if (parent / ".git").exists())
    helpers = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))
    wait_for, press, stop, desktop_environment = (
        helpers[name] for name in ("wait_for", "press", "stop", "desktop_environment"))
    mouse_type = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-hex-resize.py"))["PhysicalMouse"]

    def tree(owner):
        pending = [owner]
        visited = 0
        while pending:
            node = pending.pop()
            visited += 1
            if visited > 4000:
                raise AssertionError("The four-panel AT-SPI tree exceeded 4,000 nodes")
            yield node
            count = node.get_child_count()
            if count > 1000:
                raise AssertionError("One four-panel AT-SPI node exceeded 1,000 children")
            pending.extend(node.get_child_at_index(i) for i in range(count - 1, -1, -1))
    root = validation / "NestedPanelResize" / uuid4().hex
    data = root / "data"
    data.mkdir(parents=True)
    paths = [root / f"panel-{i}" for i in range(1, 5)]
    original = {}
    for path in paths:
        path.mkdir()
        for name in ("a.txt", "b.txt", "c.txt"):
            item = path / name
            item.write_bytes(f"{path}/{name}\n".encode())
            original[str(item)] = hashlib.sha256(item.read_bytes()).hexdigest()
    ids = [f"native-panel-{i}" for i in range(1, 5)]

    def leaf(index):
        return {"PanelId": ids[index], "Size": 1, "Children": []}

    state = {"SchemaVersion": 1, "Name": "Native nested panels",
             "Panels": [{"Id": ids[i], "TargetPanelId": ids[(i + 1) % 4],
                         "ActiveTab": 0,
                         "Tabs": [{"Location": {"s": "fs", "p": str(paths[i])}}]}
                        for i in range(4)],
             "ActivePanelId": ids[0],
             "Tree": {"Stacked": False, "Size": 1, "Children": [
                 {"Stacked": True, "Size": 1, "Children": [leaf(0), leaf(1)]},
                 {"Stacked": True, "Size": 1, "Children": [leaf(2), leaf(3)]}]}}
    saved = data / "workspaces" / "Native nested panels.json"
    saved.parent.mkdir()
    saved.write_text(json.dumps(state, indent=2), encoding="utf-8")
    saved_hash = hashlib.sha256(saved.read_bytes()).hexdigest()
    result = {"passed": False, "stage": "startup", "fixture": str(root),
              "gitHead": subprocess.check_output(["git", "-C", str(repo), "rev-parse", "HEAD"],
                                                  text=True).strip(),
              "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest()}
    process = None

    def visible(node):
        return node.get_state_set().contains(Atspi.StateType.SHOWING)

    def find(app, role, name):
        return next((node for node in tree(app) if node.get_role_name() == role and
                     node.get_name() == name and visible(node)), None)

    def lists(app):
        found = [node for node in tree(app) if node.get_role_name() == "list" and
                 re.match(r"Panel [1-4],", node.get_name() or "") and visible(node)]
        return sorted(found, key=lambda n: int(re.match(r"Panel ([1-4]),", n.get_name()).group(1)))

    def splitter(app, orientation, identity):
        return next((node for node in tree(app) if node.get_role_name() == "slider" and
                     (node.get_name() or "").startswith(f"{orientation} panel boundary") and
                     identity in (node.get_name() or "") and visible(node)), None)

    def snapshot(node):
        bounds = node.get_component().get_extents(Atspi.CoordType.SCREEN)
        return {"name": node.get_name(), "share": node.get_current_value(),
                "bounds": [bounds.x, bounds.y, bounds.width, bounds.height],
                "description": node.get_description(),
                "focused": node.get_state_set().contains(Atspi.StateType.FOCUSED)}

    def open_workspace(app, pid):
        press(key_helper, sudo_password, "ctrl+shift+p", pid)
        search = wait_for(lambda: find(app, "entry", "Search commands"), "command search")
        if not search.get_editable_text().set_text_contents("Open workspace"):
            raise AssertionError("Could not search for Open workspace")
        time.sleep(0.35)
        press(key_helper, sudo_password, "enter", pid)
        wait_for(lambda: find(app, "list", "Open workspace"), "owned workspace chooser")
        press(key_helper, sudo_password, "enter", pid)

    try:
        os.environ.update(desktop_environment())
        env = os.environ.copy()
        runtime = validation / "dotnet-sdk"
        if not (runtime / "dotnet").is_file():
            raise AssertionError("The isolated .NET runtime is missing")
        env["DOTNET_ROOT"] = str(runtime)
        display = subprocess.run(["/usr/bin/gdctl", "show"], check=True,
                                 capture_output=True, text=True, timeout=10).stdout
        match = re.search(r"\bScale: ([0-9.]+)", display)
        scale = float(match.group(1)) if match else None
        result["displayScale"] = scale
        mode = re.search(r"\bCurrent mode\s*\n\s*[^\n]*\b([0-9]+x[0-9]+)@", display)
        result["displayMode"] = mode.group(1) if mode else None
        if scale != 2.0 or result["displayMode"] != "2560x1600":
            raise AssertionError(f"The Unix route needs 2560x1600 at 200%, found {result['displayMode']} at {scale}")
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(paths[0]), "--right", str(paths[1])],
                                       cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        windows = subprocess.run(["/usr/bin/wmctrl", "-lp"], check=True,
                                 capture_output=True, text=True, timeout=5).stdout.splitlines()
        window = next((parts[0] for line in windows if
                       len(parts := line.split()) > 2 and parts[2] == str(process.pid)), None)
        if window is None:
            raise AssertionError("The test-owned window was absent from the window manager")
        subprocess.run(["/usr/bin/wmctrl", "-ir", window, "-b",
                        "add,maximized_vert,maximized_horz"], check=True, timeout=5)
        subprocess.run(["/usr/bin/wmctrl", "-ia", window], check=True, timeout=5)
        time.sleep(0.4)
        result["stage"] = "open nested arrangement and check list capacity"
        open_workspace(app, process.pid)
        panels = wait_for(lambda: lists(app) if len(lists(app)) == 4 else None,
                          "four named panel lists")
        result["panels"] = []
        for panel in panels:
            bounds = panel.get_component().get_extents(Atspi.CoordType.SCREEN)
            visible_rows = [n for n in tree(panel) if n.get_role_name() == "list item" and
                            (n.get_name() or "").split(",", 1)[0] in ("a.txt", "b.txt", "c.txt") and
                            visible(n) and
                            (row := n.get_component().get_extents(Atspi.CoordType.SCREEN)).height > 0 and
                            row.y >= bounds.y and row.y + row.height <= bounds.y + bounds.height + 2]
            result["panels"].append({"name": panel.get_name(),
                                     "bounds": [bounds.x, bounds.y, bounds.width, bounds.height],
                                     "visibleRows": len(visible_rows)})
        horizontal = wait_for(lambda: splitter(app, "Horizontal", "panels 1 and 2"),
                              "left nested splitter")
        vertical = wait_for(lambda: splitter(app, "Vertical", "panels 1, 2 and 3, 4"),
                            "outer splitter")
        result["horizontalInitial"] = snapshot(horizontal)
        result["verticalInitial"] = snapshot(vertical)

        result["stage"] = "keyboard horizontal change and cancel"
        press(key_helper, sudo_password, "ctrl+shift+p", process.pid)
        search = wait_for(lambda: find(app, "entry", "Search commands"), "resize command search")
        if not search.get_editable_text().set_text_contents("Resize panel"):
            raise AssertionError("Could not search for Resize panel")
        time.sleep(0.35)
        press(key_helper, sudo_password, "enter", process.pid)
        wait_for(lambda: (s if (s := splitter(app, "Horizontal", "panels 1 and 2")) and
                 s.get_state_set().contains(Atspi.StateType.FOCUSED) else None),
                 "focused horizontal splitter")
        press(key_helper, sudo_password, "down", process.pid)
        result["horizontalChanged"] = snapshot(wait_for(
            lambda: (s if (s := splitter(app, "Horizontal", "panels 1 and 2")) and
                     s.get_current_value() > result["horizontalInitial"]["share"] else None),
            "changed horizontal share"))
        press(key_helper, sudo_password, "escape", process.pid)
        result["horizontalCanceled"] = snapshot(wait_for(
            lambda: (s if (s := splitter(app, "Horizontal", "panels 1 and 2")) and
                     s.get_current_value() == result["horizontalInitial"]["share"] else None),
            "canceled horizontal share"))

        result["stage"] = "keyboard vertical commit and pointer drag"
        press(key_helper, sudo_password, "ctrl+shift+p", process.pid)
        search = wait_for(lambda: find(app, "entry", "Search commands"), "second resize search")
        if not search.get_editable_text().set_text_contents("Resize panel"):
            raise AssertionError("Could not search for second Resize panel")
        time.sleep(0.35)
        press(key_helper, sudo_password, "enter", process.pid)
        press(key_helper, sudo_password, "tab", process.pid)
        wait_for(lambda: (s if (s := splitter(app, "Vertical", "panels 1, 2 and 3, 4")) and
                 s.get_state_set().contains(Atspi.StateType.FOCUSED) else None),
                 "focused outer splitter")
        press(key_helper, sudo_password, "right", process.pid)
        result["verticalCommitted"] = snapshot(wait_for(
            lambda: (s if (s := splitter(app, "Vertical", "panels 1, 2 and 3, 4")) and
                     s.get_current_value() > result["verticalInitial"]["share"] else None),
            "committed vertical share"))
        press(key_helper, sudo_password, "enter", process.pid)
        bounds = splitter(app, "Vertical", "panels 1, 2 and 3, 4").get_component().get_extents(
            Atspi.CoordType.SCREEN)
        with mouse_type(pointer_helper, sudo_password, scale) as mouse:
            result["pointerEnd"] = mouse.drag(bounds.x + bounds.width // 2,
                                               bounds.y + bounds.height // 2, 48, 0)
        result["verticalPointer"] = snapshot(wait_for(
            lambda: (s if (s := splitter(app, "Vertical", "panels 1, 2 and 3, 4")) and
                     s.get_current_value() > result["verticalCommitted"]["share"] else None),
            "pointer-adjusted outer share"))

        result["stage"] = "restore saved geometry"
        open_workspace(app, process.pid)
        result["verticalRestored"] = snapshot(wait_for(
            lambda: (s if (s := splitter(app, "Vertical", "panels 1, 2 and 3, 4")) and
                     s.get_current_value() == result["verticalInitial"]["share"] else None),
            "restored vertical share"))
        result["savedExact"] = hashlib.sha256(saved.read_bytes()).hexdigest() == saved_hash
        result["sourcesExact"] = (
            all(item.is_file() and hashlib.sha256(item.read_bytes()).hexdigest() == digest
                for name, digest in original.items() for item in [Path(name)]) and
            sum(len(list(path.iterdir())) for path in paths) == 12)
        first, changed, canceled = (result[k] for k in
                                    ("horizontalInitial", "horizontalChanged", "horizontalCanceled"))
        outer, committed, pointer, restored = (result[k] for k in
                                               ("verticalInitial", "verticalCommitted",
                                                "verticalPointer", "verticalRestored"))
        result["passed"] = (len(result["panels"]) == 4 and
                            all(panel["visibleRows"] >= 2 for panel in result["panels"]) and
                            changed["share"] > first["share"] and
                            canceled["share"] == first["share"] and
                            abs(canceled["bounds"][1] - first["bounds"][1]) <= 2 and
                            committed["share"] > outer["share"] and
                            pointer["share"] > committed["share"] and
                            restored["share"] == outer["share"] and
                            abs(restored["bounds"][0] - outer["bounds"][0]) <= 2 and
                            result["savedExact"] and result["sourcesExact"])
        result["stage"] = "complete"
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        if process:
            result["appLogTail"] = (root / "app.log").read_text(errors="replace")[-1500:]
    finally:
        stop(process)
        report.write_text(json.dumps(result, indent=2), encoding="utf-8")
        if root.resolve().parent != validation / "NestedPanelResize":
            raise AssertionError("Fixture cleanup escaped the owned validation root")
        shutil.rmtree(root)
    return result


if __name__ == "__main__":
    if len(sys.argv) != 5:
        raise SystemExit(__doc__)
    outcome = run(*(Path(arg) for arg in sys.argv[1:]))
    raise SystemExit(0 if outcome["passed"] else 1)
