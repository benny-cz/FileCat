#!/usr/bin/env python3
"""Restore a test-owned four-panel/40-tab workspace at native narrow scale.

Usage: linux-atspi-workspace-40-tabs.py APP REPORT.json KEY_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD transiently; run with no other FileCat.
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


def run(app_path: Path, report: Path, key_helper: Path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, key_helper = (p.resolve(strict=True) for p in (app_path, key_helper))
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            key_helper != validation / "linux-uinput-key.py" or
            report.parent != validation or report.exists()):
        raise AssertionError("Use an isolated Git app, keyboard helper and fresh report")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise AssertionError("Transient native input credential required")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Stop other FileCat processes before this owned test")
    repo = next(parent for parent in app_path.parents if (parent / ".git").exists())
    helpers = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))
    wait_for, press, stop, desktop_environment = (
        helpers[name] for name in ("wait_for", "press", "stop", "desktop_environment"))
    # Four ten-tab strips exceed the 800-node bound used by large-file-list
    # acceptance. This bounded fixture has 40 tabs and only one file per tab.
    def tree(root):
        pending = [root]
        visited = 0
        while pending:
            node = pending.pop()
            visited += 1
            if visited > 4000:
                raise AssertionError("The 40-tab AT-SPI tree exceeded 4,000 nodes")
            yield node
            children = node.get_child_count()
            if children > 1000:
                raise AssertionError("One 40-tab AT-SPI node exposed over 1,000 children")
            pending.extend(node.get_child_at_index(i) for i in range(children - 1, -1, -1))

    def matching(root, role, name):
        return [node for node in tree(root) if node.get_role_name() == role and
                node.get_name() == name and
                node.get_state_set().contains(Atspi.StateType.SHOWING)]
    root = validation / "Workspace40Tabs" / uuid4().hex
    data = root / "data"
    data.mkdir(parents=True)
    ids = [f"native-panel-{i + 1}" for i in range(4)]
    active = [2, 4, 6, 8]
    paths = [[root / f"panel-{i + 1}" / f"tab-{j + 1:02d}" for j in range(10)]
             for i in range(4)]
    for panel_paths in paths:
        for path in panel_paths:
            path.mkdir(parents=True)
            (path / "sentinel.txt").write_bytes(str(path).encode() + b"\n")
    def leaf(index):
        return {"PanelId": ids[index], "Size": 1, "Children": []}
    state = {"SchemaVersion": 1, "Name": "Native 40 tabs",
             "Panels": [{"Id": ids[i], "TargetPanelId": ids[(i + 1) % 4],
                         "ActiveTab": active[i],
                         "Tabs": [{"Location": {"s": "fs", "p": str(path)},
                                   "Locked": j % 2 == 0, "ReturnToRoot": False}
                                  for j, path in enumerate(paths[i])]}
                        for i in range(4)],
             "ActivePanelId": ids[2],
             "Tree": {"Stacked": False, "Size": 1, "Children": [
                 {"Stacked": True, "Size": 1, "Children": [leaf(0), leaf(1)]},
                 {"Stacked": True, "Size": 1, "Children": [leaf(2), leaf(3)]}]}}
    saved = data / "workspaces" / "Native 40 tabs.json"
    saved.parent.mkdir()
    saved.write_text(json.dumps(state, indent=2), encoding="utf-8")
    result = {"passed": False, "stage": "startup", "gitHead": subprocess.check_output(
        ["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip(),
        "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
        "fixture": str(root), "savedWorkspaceSha256": hashlib.sha256(saved.read_bytes()).hexdigest()}
    process = None

    def panel_lists(app):
        return sorted((n for n in tree(app) if n.get_role_name() == "list" and
                       re.match(r"Panel [1-4],", n.get_name() or "")),
                      key=lambda n: int(re.match(r"Panel (\d+),", n.get_name()).group(1)))

    def find(app, role, label):
        return next(iter(matching(app, role, label)), None)

    try:
        os.environ.update(desktop_environment())
        env = os.environ.copy()
        runtime = validation / "dotnet-sdk"
        if not (runtime / "dotnet").is_file():
            raise AssertionError("The isolated .NET runtime is missing")
        env["DOTNET_ROOT"] = str(runtime)
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(paths[0][0]), "--right", str(paths[1][0])],
                                       cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        wait_for(lambda: panel_lists(app) if len(panel_lists(app)) == 2 else None,
                 "initial two panels")
        result["stage"] = "open saved arrangement"
        press(key_helper, sudo_password, "ctrl+shift+p", process.pid)
        search = wait_for(lambda: find(app, "entry", "Search commands"), "command search")
        if not search.get_editable_text().set_text_contents("Open workspace"):
            raise AssertionError("Could not search for Open workspace")
        wait_for(lambda: any("Open workspace" in (n.get_name() or "")
                             for n in tree(app) if n.get_role_name() == "list item"),
                 "Open workspace command")
        press(key_helper, sudo_password, "enter", process.pid)
        wait_for(lambda: find(app, "list", "Open workspace"), "saved workspace chooser")
        press(key_helper, sudo_password, "enter", process.pid)
        lists = wait_for(lambda: panel_lists(app) if len(panel_lists(app)) == 4 else None,
                         "four restored panels")
        result["restoredPanelNames"] = [n.get_name() for n in lists]
        result["stage"] = "native narrow-scale panel and keyboard inspection"
        display = subprocess.run(["/usr/bin/gdctl", "show"], check=True,
                                 capture_output=True, text=True, timeout=10).stdout
        match = re.search(r"\bScale: ([0-9.]+)", display)
        scale = float(match.group(1)) if match else None
        result["displayScale"] = scale
        window = next((parts[0] for line in subprocess.run(
            ["/usr/bin/wmctrl", "-lp"], check=True, capture_output=True,
            text=True, timeout=5).stdout.splitlines()
            if len(parts := line.split()) > 2 and parts[2] == str(process.pid)), None)
        if window is None:
            raise AssertionError("The test-owned main window was not in WM")
        requested = [1440, 1080] if scale == 2.0 else [1080, 810]
        subprocess.run(["/usr/bin/wmctrl", "-ir", window, "-e",
                        f"0,20,20,{requested[0]},{requested[1]}"], check=True, timeout=5)
        time.sleep(0.5)
        geometry = subprocess.run(["/usr/bin/xwininfo", "-id", window], check=True,
                                  capture_output=True, text=True, timeout=5).stdout
        width = re.search(r"\bWidth: (\d+)", geometry)
        height = re.search(r"\bHeight: (\d+)", geometry)
        result["physicalWindow"] = [int(width.group(1)), int(height.group(1))]
        result["logicalWindow"] = [round(result["physicalWindow"][0] / scale),
                                   round(result["physicalWindow"][1] / scale)]
        checks = []
        def current_panel(index):
            return next((n for n in panel_lists(app)
                         if n.get_name().startswith(f"Panel {index + 1},")), None)

        def focus_panel(index):
            node = current_panel(index)
            return node if node and node.grab_focus() else None

        def panel_at(index, path):
            node = current_panel(index)
            return node.get_name() if node and path in node.get_name() else None

        for i in range(4):
            wait_for(lambda: focus_panel(i), f"focus restored panel {i + 1}")
            expected = str(paths[i][active[i]])
            before = wait_for(lambda: panel_at(i, expected),
                              f"restored active tab in panel {i + 1}")
            press(key_helper, sudo_password, "ctrl+shift+l", process.pid)
            tab_list = wait_for(lambda: find(app, "list", "Tabs"), "all restored tabs chooser")
            tab_names = [n.get_name() or "" for n in tree(tab_list)
                         if n.get_role_name() == "list item"]
            press(key_helper, sudo_password, "escape", process.pid)
            role = wait_for(lambda: [n.get_name() or "" for n in tree(app)
                                     if n.get_role_name() == "button" and
                                     f"F5 and F6 copy and move to panel {(i + 1) % 4 + 1}." in (n.get_name() or "")],
                            f"panel {i + 1} target announcement")
            cycled = []
            focused = []
            for step in range(1, 11):
                press(key_helper, sudo_password, "ctrl+tab", process.pid)
                next_path = str(paths[i][(active[i] + step) % 10])
                cycled.append(wait_for(lambda: panel_at(i, next_path),
                                       f"keyboard tab {step} in panel {i + 1}"))
                if step == 1:
                    focused = [n.get_name() or "" for n in tree(current_panel(i))
                               if n.get_state_set().contains(Atspi.StateType.FOCUSED)]
            checks.append({"panel": i + 1, "before": before, "after": cycled[0],
                           "cycledTabPaths": cycled, "focusAfterFirstSwitch": focused[:4],
                           "visibleChooserRows": len(tab_names), "targetAnnouncement": role[:2]})
        result["checks"] = checks
        result["savedWorkspaceUnchanged"] = hashlib.sha256(saved.read_bytes()).hexdigest() == result["savedWorkspaceSha256"]
        result["passed"] = (scale in (1.5, 2.0) and
                            result["logicalWindow"][0] <= 900 and result["logicalWindow"][1] <= 650 and
                            all(len(c["cycledTabPaths"]) == 10 and
                                len(set(c["cycledTabPaths"])) == 10 and
                                any(c["after"] in name for name in c["focusAfterFirstSwitch"]) and
                                c["visibleChooserRows"] > 0 and c["targetAnnouncement"]
                                for c in checks) and result["savedWorkspaceUnchanged"])
        result["stage"] = "complete"
        return result
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        result["appExit"] = process.poll() if process else None
        return result
    finally:
        stop(process)
        shutil.rmtree(root)


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    report_path = Path(sys.argv[2]).resolve()
    outcome = run(Path(sys.argv[1]), report_path, Path(sys.argv[3]))
    report_path.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(outcome))
    raise SystemExit(0 if outcome["passed"] else 1)
