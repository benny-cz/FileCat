#!/usr/bin/env python3
"""Native Ubuntu command availability across toolbar, menu, key bar and search.

Usage: linux-atspi-command-availability.py APP REPORT.json KEY_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD transiently in the isolated Ubuntu VM.
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
        raise AssertionError("Use an isolated Git app, owned key helper and fresh receipt")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise AssertionError("Transient native input credential required")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Stop another FileCat before this owned test")
    repo = next(parent for parent in app_path.parents if (parent / ".git").exists())
    helpers = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))
    wait_for, press, action, stop, desktop_environment = (
        helpers[name] for name in ("wait_for", "press", "action", "stop", "desktop_environment"))

    def tree(owner):
        pending = [owner]
        visited = 0
        while pending:
            node = pending.pop()
            visited += 1
            if visited > 2400:
                raise AssertionError("Command availability AT-SPI tree exceeded 2,400 nodes")
            yield node
            count = node.get_child_count()
            if count > 1000:
                raise AssertionError("One command AT-SPI node exceeded 1,000 children")
            pending.extend(node.get_child_at_index(i) for i in range(count - 1, -1, -1))

    root = validation / "CommandAvailability" / uuid4().hex
    source, target, data = (root / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    file = source / "keep-exact.txt"
    file.write_bytes(b"native availability must not alter this source\n")
    original = hashlib.sha256(file.read_bytes()).hexdigest()
    result = {"passed": False, "stage": "startup", "fixture": str(root),
              "gitHead": subprocess.check_output(["git", "-C", str(repo), "rev-parse", "HEAD"],
                                               text=True).strip(),
              "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest()}
    process = None

    def showing(node):
        return node.get_state_set().contains(Atspi.StateType.SHOWING)

    def enabled(node):
        return node.get_state_set().contains(Atspi.StateType.ENABLED)

    def find(app, name, role=None):
        return next((n for n in tree(app) if n.get_name() == name and
                     (role is None or n.get_role_name() == role) and showing(n)), None)

    def buttons(app, name):
        return [n for n in tree(app) if n.get_name() == name and
                n.get_role_name() in ("push button", "button") and showing(n)]

    def bounds(node):
        rect = node.get_component().get_extents(Atspi.CoordType.SCREEN)
        return [rect.x, rect.y, rect.width, rect.height]

    def toolbar(app, name):
        candidates = [(bounds(n)[1], n) for n in buttons(app, name)]
        return min(candidates)[1] if candidates else None

    def keybar(app, name):
        candidates = [(bounds(n)[1], n) for n in buttons(app, name)]
        return max(candidates)[1] if candidates else None

    def menu(app, name, expected):
        press(key_helper, sudo_password, "alt+f" if name == "File" else "alt+n", process.pid)
        items = wait_for(lambda: [n for n in tree(app) if n.get_role_name() == "menu item" and
                          n.get_name() in expected and showing(n)], f"{name} command menu")
        states = {n.get_name(): enabled(n) for n in items}
        press(key_helper, sudo_password, "escape", process.pid)
        return states

    def capture(app, phase):
        states = {}
        toolbar_names = {"Copy": "Copy…", "Move": "Move / rename…", "Delete": "Delete…",
                         "Back": "Back"}
        for name in ("Copy", "Move", "Delete", "Back"):
            top = toolbar(app, toolbar_names[name])
            bottom = keybar(app, name) if name != "Back" else None
            states[name] = {"toolbar": None if top is None else
                            {"enabled": enabled(top), "help": top.get_help_text(), "bounds": bounds(top)},
                            "keybar": None if bottom is None else
                            {"enabled": enabled(bottom), "bounds": bounds(bottom)}}
        result[phase] = states
        return states

    def search(app, query):
        press(key_helper, sudo_password, "ctrl+shift+p", process.pid)
        field = wait_for(lambda: find(app, "Search commands", "entry"), "command search")
        if not field.get_editable_text().set_text_contents(query):
            raise AssertionError(f"Could not search for {query}")
        time.sleep(0.35)
        rows = [n.get_name() for n in tree(app) if n.get_name() and
                showing(n) and (query.lower() in n.get_name().lower() or
                                "unavailable here" in n.get_name().lower())]
        press(key_helper, sudo_password, "escape", process.pid)
        return rows[-35:]

    try:
        os.environ.update(desktop_environment())
        env = os.environ.copy()
        runtime = validation / "dotnet-sdk"
        if not (runtime / "dotnet").is_file():
            raise AssertionError("The isolated .NET runtime is missing")
        env["DOTNET_ROOT"] = str(runtime)
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((n for n in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if n.get_process_id() == process.pid and n.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        result["stage"] = "local selected file"
        row = wait_for(lambda: next((n for n in tree(app) if n.get_role_name() == "list item" and
                       (n.get_name() or "").startswith("keep-exact.txt") and showing(n)), None),
                       "test-owned source row")
        if not row.grab_focus():
            raise AssertionError("Could not focus the source file")
        result["local"] = capture(app, "local")
        result["localFileMenu"] = menu(app, "File", ("Copy…", "Move / rename…", "Delete…"))
        result["localNavigateMenu"] = menu(app, "Navigate", ("Back",))

        result["stage"] = "switch active panel provider to Computer"
        places = buttons(app, "Computer")
        result["places"] = [bounds(n) for n in places]
        if not places:
            raise AssertionError("No native Computer place is available")
        action(min(places, key=lambda n: bounds(n)[0]), "left Computer place")
        drive_list = wait_for(lambda: next((n for n in tree(app) if n.get_role_name() == "list" and
                              (n.get_name() or "").startswith("Panel 1,") and
                              "Computer" in (n.get_name() or "")), None),
                              "Computer provider listing")
        drive_row = wait_for(lambda: next((n for n in tree(drive_list) if
                              n.get_role_name() == "list item" and showing(n) and
                              not (n.get_name() or "").startswith("..")), None),
                              "focused drive row")
        if not drive_row.grab_focus():
            raise AssertionError("Could not focus a drive row")
        result["drive"] = capture(app, "drive")
        result["driveRow"] = drive_row.get_name()
        result["driveFileMenu"] = menu(app, "File", ("Copy…", "Move / rename…", "Delete…"))
        result["driveNavigateMenu"] = menu(app, "Navigate", ("Back",))
        result["paletteCopy"] = search(app, "Copy")
        result["paletteDelete"] = search(app, "Delete")
        result["paletteMove"] = search(app, "Move")
        result["paletteBack"] = search(app, "Back")

        result["stage"] = "execution recheck and source integrity"
        press(key_helper, sudo_password, "f8", process.pid)
        time.sleep(0.4)
        result["afterF8"] = [n.get_name() for n in tree(app) if n.get_name() and showing(n) and
                              ("Drives are not deleted" in n.get_name() or "Delete" in n.get_name())][-12:]
        result["unexpectedDeleteReview"] = bool(find(app, "Delete selected items") or
                                                find(app, "Move to trash"))
        result["fileExact"] = file.is_file() and hashlib.sha256(file.read_bytes()).hexdigest() == original
        result["targetEmpty"] = not any(target.iterdir())
        result["passed"] = bool(result["fileExact"] and result["targetEmpty"] and
                                all(result["local"][name]["toolbar"] and
                                    result["local"][name]["toolbar"]["enabled"] and
                                    result["local"][name]["keybar"] and
                                    result["local"][name]["keybar"]["enabled"] for name in
                                    ("Copy", "Move", "Delete")) and
                                result["local"]["Back"]["toolbar"] and
                                not result["local"]["Back"]["toolbar"]["enabled"] and
                                result["drive"]["Back"]["toolbar"] and
                                result["drive"]["Back"]["toolbar"]["enabled"] and
                                result["drive"]["Copy"]["toolbar"] and
                                result["drive"]["Copy"]["toolbar"]["enabled"] and
                                result["drive"]["Copy"]["keybar"] and
                                result["drive"]["Copy"]["keybar"]["enabled"] and
                                all(result["drive"][name]["toolbar"] and
                                    not result["drive"][name]["toolbar"]["enabled"] for name in
                                    ("Move", "Delete")) and
                                all(result["drive"][name]["keybar"] and
                                    not result["drive"][name]["keybar"]["enabled"] for name in
                                    ("Move", "Delete")) and
                                result["localFileMenu"].get("Copy…") and
                                result["localFileMenu"].get("Move / rename…") and
                                result["localFileMenu"].get("Delete…") and
                                result["driveFileMenu"].get("Copy…") and
                                result["driveFileMenu"].get("Move / rename…") is False and
                                result["driveFileMenu"].get("Delete…") is False and
                                result["localNavigateMenu"].get("Back") is False and
                                result["driveNavigateMenu"].get("Back") and
                                "Drives are not deleted" in result["drive"]["Delete"]["toolbar"]["help"] and
                                any("Id = file.copy," in text and "Enabled = True" in text
                                    for text in result["paletteCopy"]) and
                                any("Id = file.move," in text and "Enabled = False" in text and
                                    "Drives cannot be moved" in text for text in result["paletteMove"]) and
                                any("Id = nav.back," in text and "Enabled = True" in text
                                    for text in result["paletteBack"]) and
                                any("Drives are not deleted" in text for text in result["paletteDelete"]) and
                                not result["unexpectedDeleteReview"])
        result["stage"] = "complete"
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        if process:
            result["appLogTail"] = (root / "app.log").read_text(errors="replace")[-1200:]
            try:
                result["nodesAtFailure"] = [
                    {"role": n.get_role_name(), "name": n.get_name(), "enabled": enabled(n)}
                    for n in tree(app) if n.get_name() and showing(n) and
                    any(word in n.get_name() for word in
                        ("Copy", "Move", "Delete", "Back", "This PC"))][-65:]
            except Exception:
                pass
    finally:
        stop(process)
        report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        if root.resolve().parent != validation / "CommandAvailability":
            raise AssertionError("Fixture cleanup escaped the owned validation root")
        shutil.rmtree(root)
    return result


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    outcome = run(*(Path(arg) for arg in sys.argv[1:]))
    raise SystemExit(0 if outcome["passed"] else 1)
