#!/usr/bin/env python3
"""Race two isolated FileCat Save workspace as prompts on one owned name.

Usage: linux-atspi-workspace-save-race.py APP REPORT.json KEY_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD transiently. Run with no other FileCat.
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
    wait_for, tree, matching, action, press, stop, desktop_environment = (
        helpers[name] for name in ("wait_for", "tree", "matching", "action", "press", "stop", "desktop_environment"))
    root = validation / "WorkspaceSaveRace" / uuid4().hex
    data, first, second = root / "data", root / "first", root / "second"
    for folder in (data, first, second):
        folder.mkdir(parents=True)
    (first / "first.txt").write_bytes(b"first isolated workspace\n")
    (second / "second.txt").write_bytes(b"second isolated workspace\n")
    name = "Native race " + root.name[:8]
    target = data / "workspaces" / (name + ".json")
    result = {"passed": False, "stage": "startup", "gitHead": subprocess.check_output(
        ["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip(),
        "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
        "workspaceName": name, "fixture": str(root)}
    processes = []

    def owned_app(pid):
        desktop = Atspi.get_desktop(0)
        return wait_for(lambda: next((node for node in
                        (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                        if node.get_process_id() == pid and node.get_name() == "FileCat"), None),
                        f"test-owned FileCat {pid}")

    def find(app, role, label):
        return next(iter(matching(app, role, label)), None)

    def start(folder):
        env = os.environ.copy()
        runtime = validation / "dotnet-sdk"
        if not (runtime / "dotnet").is_file():
            raise AssertionError("The isolated .NET runtime is missing")
        env["DOTNET_ROOT"] = str(runtime)
        with (root / (folder.name + ".log")).open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(folder), "--right", str(folder)],
                                       cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        processes.append(process)
        app = owned_app(process.pid)
        wait_for(lambda: next((n for n in tree(app) if n.get_role_name() == "list" and
                               str(folder) in (n.get_name() or "")), None),
                 f"initial {folder.name} panel")
        return process, app

    def open_save_prompt(process, app):
        press(key_helper, sudo_password, "ctrl+shift+p", process.pid)
        search = wait_for(lambda: find(app, "entry", "Search commands"), "command search")
        if not search.get_editable_text().set_text_contents("Save workspace as"):
            raise AssertionError("Could not search for Save workspace as")
        wait_for(lambda: any("Save workspace as" in (n.get_name() or "")
                             for n in tree(app) if n.get_role_name() == "list item"),
                 "Save workspace as command")
        press(key_helper, sudo_password, "enter", process.pid)
        prompt = wait_for(lambda: find(app, "entry", "Name for the current panels, tabs, and targets:"),
                          "workspace name prompt")
        if not prompt.get_editable_text().set_text_contents(name):
            raise AssertionError("Could not set the owned workspace name")
        return prompt

    try:
        os.environ.update(desktop_environment())
        a, app_a = start(first)
        b, app_b = start(second)
        result["stage"] = "both prompts open before publication"
        open_save_prompt(a, app_a)
        open_save_prompt(b, app_b)
        result["bothPromptsOpen"] = all(find(app, "button", "Save as") is not None
                                        for app in (app_a, app_b)) and not target.exists()
        if not result["bothPromptsOpen"]:
            raise AssertionError("Both Save as prompts were not pending before publication")
        result["stage"] = "first process saves"
        action(find(app_a, "button", "Save as"), "first Save as")
        wait_for(lambda: target.is_file(), "first workspace publication")
        original = target.read_bytes()
        saved = json.loads(original)
        result["firstWorkspaceHash"] = hashlib.sha256(original).hexdigest()
        result["firstWorkspacePath"] = saved["Panels"][0]["Tabs"][0]["Location"]["p"]
        result["stage"] = "second process reviews collision and cancels"
        action(find(app_b, "button", "Save as"), "second Save as")
        confirmation = wait_for(lambda: [n.get_name() or "" for n in tree(app_b)
                                         if n.get_state_set().contains(Atspi.StateType.SHOWING) and
                                         "Replace" in (n.get_name() or "") and name in (n.get_name() or "")],
                                "explicit same-name replacement review")
        result["replacementReview"] = confirmation[:8]
        action(wait_for(lambda: find(app_b, "button", "Cancel"), "replacement Cancel"),
               "replacement Cancel")
        result["targetUnchanged"] = target.read_bytes() == original
        result["noBackupCreated"] = not (target.parent / (target.name + ".bak")).exists()
        result["sourceBytesExact"] = ((first / "first.txt").read_bytes() == b"first isolated workspace\n" and
                                       (second / "second.txt").read_bytes() == b"second isolated workspace\n")
        result["passed"] = (result["firstWorkspacePath"] == str(first) and
                            result["targetUnchanged"] and result["noBackupCreated"] and
                            result["sourceBytesExact"])
        result["stage"] = "complete"
        return result
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        result["processExits"] = [p.poll() for p in processes]
        return result
    finally:
        for process in processes:
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
