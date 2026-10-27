#!/usr/bin/env python3
"""Native scaled Settings user-tool, filter and association draft acceptance.

Usage: linux-atspi-settings-structured.py APP REPORT.json KEY_HELPER
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
    for name in ("FileCat", "orca"):
        if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", name],
                          capture_output=True).returncode == 0:
            raise AssertionError(f"Stop another {name} process before this owned test")
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
            if visited > 4000:
                raise AssertionError("The Settings AT-SPI tree exceeded 4,000 nodes")
            yield node
            count = node.get_child_count()
            if count > 1000:
                raise AssertionError("One Settings AT-SPI node exceeded 1,000 children")
            pending.extend(node.get_child_at_index(i) for i in range(count - 1, -1, -1))

    root = validation / "StructuredSettings" / uuid4().hex
    source, data = root / "source", root / "data"
    source.mkdir(parents=True)
    data.mkdir()
    sample = source / "secret file.txt"
    sample.write_bytes(b"settings preview must not alter the sample\n")
    sample_hash = hashlib.sha256(sample.read_bytes()).hexdigest()
    program = root / "safe preview tool"
    marker = root / "PROGRAM_WAS_RUN"
    program.write_text(f"#!/bin/sh\nprintf run > '{marker}'\n", encoding="utf-8")
    program.chmod(0o700)
    result = {"passed": False, "stage": "startup", "fixture": str(root),
              "gitHead": subprocess.check_output(["git", "-C", str(repo), "rev-parse", "HEAD"],
                                               text=True).strip(),
              "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
              "program": str(program), "sample": str(sample)}
    process = None

    def visible(node):
        return node.get_state_set().contains(Atspi.StateType.SHOWING)

    def find(app, name, role=None, showing=True):
        return next((node for node in tree(app) if node.get_name() == name and
                     (role is None or node.get_role_name() == role) and
                     (not showing or visible(node))), None)

    def named(app, text):
        return [node for node in tree(app) if text in (node.get_name() or "") and visible(node)]

    def edit(app, name, value):
        field = wait_for(lambda: find(app, name, "entry", showing=False), name)
        if not field.get_editable_text().set_text_contents(value):
            raise AssertionError(f"Could not edit {name}")
        return field

    def click(app, name):
        node = wait_for(lambda: find(app, name, "push button", showing=False)
                        or find(app, name, "button", showing=False), name)
        action(node, name)
        return node

    def select_tab(app, name):
        tab = wait_for(lambda: find(app, name, "page tab", showing=False)
                       or find(app, name, "tab", showing=False), f"{name} tab")
        action(tab, f"{name} tab")
        wait_for(lambda: visible(tab), f"visible {name} tab")

    def toggle(app, name):
        box = wait_for(lambda: find(app, name, "check box", showing=False), name)
        action(box, name)
        return box

    def open_settings(app):
        press(key_helper, sudo_password, "ctrl+shift+p", process.pid)
        search = wait_for(lambda: find(app, "Search commands", "entry"), "command search")
        if not search.get_editable_text().set_text_contents("Settings"):
            raise AssertionError("Could not search for Settings")
        time.sleep(0.4)
        press(key_helper, sudo_password, "enter", process.pid)
        wait_for(lambda: find(app, "Search settings", "entry"), "Settings dialog")

    try:
        os.environ.update(desktop_environment())
        env = os.environ.copy()
        runtime = validation / "dotnet-sdk"
        if not (runtime / "dotnet").is_file():
            raise AssertionError("The isolated .NET runtime is missing")
        env["DOTNET_ROOT"] = str(runtime)
        display = subprocess.run(["/usr/bin/gdctl", "show"], capture_output=True,
                                 text=True, check=True, timeout=10).stdout
        if "Scale: 2.0" not in display or "1920x1200@59.950" not in display:
            raise AssertionError("Expected the 1920×1200, 200% GNOME display")
        result["display"] = "1920x1200@59.950, 200%"
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(source)],
                                       cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        settings_file = data / "settings.json"
        before = hashlib.sha256(settings_file.read_bytes()).hexdigest() if settings_file.is_file() else None
        result["settingsBefore"] = before
        result["stage"] = "open native Settings and build exclusion filter"
        open_settings(app)
        select_tab(app, "Behavior")
        click(app, "Add filter")
        edit(app, "Selected filter name", "native-exclusion")
        edit(app, "Selected filter mask", "*.txt|secret*.txt")
        result["filterList"] = [n.get_name() for n in named(app, "native-exclusion")][:4]

        result["stage"] = "user tool with spaced program and nonexecuting preview"
        select_tab(app, "Tools")
        click(app, "Add command")
        edit(app, "Selected command or menu name", "Native preview command")
        command_program = edit(app, "Selected command program", str(program))
        edit(app, "Selected command arguments", '--view "{file}"')
        result["commandLabels"] = [n.get_name() for n in tree(app) if
                                   n.get_name() in ("Selected command or menu name",
                                                    "Selected command program",
                                                    "Selected command arguments",
                                                    "Command preview file")]
        if not command_program.grab_focus():
            raise AssertionError("Could not focus the user command program")
        press(key_helper, sudo_password, "tab", process.pid)
        result["browseFocused"] = bool((b := find(app, "Browse for command program…", showing=False)) and
                                       b.get_state_set().contains(Atspi.StateType.FOCUSED))
        edit(app, "Command preview file", str(sample))
        click(app, "Preview command invocation")
        result["commandPreview"] = wait_for(lambda: next((n.get_name() for n in
                                            named(app, "Sample invocation; no program") if
                                            str(program) in n.get_name()), None),
                                            "nonexecuting command invocation preview")
        if marker.exists():
            raise AssertionError("A program ran during the nonexecuting preview")

        result["stage"] = "overlapping association priority and raw round trip"
        for name, mask in (("Broad", "*.txt"), ("Specific", "secret*.txt")):
            click(app, "Add association")
            edit(app, "Association name", name)
            edit(app, "Association mask", mask)
            edit(app, "Association program", str(program))
            edit(app, "Association arguments", "{file}")
        edit(app, "Association preview filename", sample.name)
        click(app, "Preview first match")
        result["broadPreview"] = wait_for(lambda: next((n.get_name() for n in
                                          named(app, "association 1 wins") if "*.txt" in n.get_name()), None),
                                          "first broad association preview")
        click(app, "Move association up")
        click(app, "Preview first match")
        result["specificPreview"] = wait_for(lambda: next((n.get_name() for n in
                                             named(app, "association 1 wins") if
                                             "secret*.txt" in n.get_name()), None),
                                             "specific association preview after reorder")
        toggle(app, "Edit associations as text")
        raw_field = find(app, "Associations advanced text", "entry", showing=False)
        if raw_field is None or not 0 < raw_field.get_character_count() <= 16384:
            raise AssertionError("The association raw draft was absent or unexpectedly large")
        result["rawRoundTrip"] = "".join(chr(raw_field.get_character_at_offset(i))
                                          for i in range(raw_field.get_character_count()))
        toggle(app, "Edit associations as text")
        click(app, "Preview first match")
        result["afterRawPreview"] = wait_for(lambda: next((n.get_name() for n in
                                             named(app, "association 1 wins") if
                                             "secret*.txt" in n.get_name()), None),
                                             "specific association after raw round trip")
        result["specificStillFirst"] = bool(result["afterRawPreview"])

        result["stage"] = "Cancel and reopen original configuration"
        click(app, "Cancel")
        wait_for(lambda: not find(app, "Search settings", "entry"), "Settings close")
        result["settingsAfterCancel"] = (hashlib.sha256(settings_file.read_bytes()).hexdigest()
                                         if settings_file.is_file() else None)
        # First-run defaults can be written after startup; compare the saved
        # semantics when there was no initial settings file.
        saved_text = settings_file.read_text(encoding="utf-8") if settings_file.is_file() else ""
        result["savedDraftAbsent"] = all(token not in saved_text for token in
                                         ("Native preview command", "native-exclusion", str(program),
                                          "secret*.txt"))
        open_settings(app)
        select_tab(app, "Tools")
        result["commandRestored"] = not any("Native preview command" in (n.get_name() or "")
                                            for n in tree(app))
        result["associationsRestored"] = not any((n.get_name() or "").startswith("1. Specific")
                                                for n in tree(app))
        select_tab(app, "Behavior")
        result["filterRestored"] = not any("native-exclusion" in (n.get_name() or "")
                                           for n in tree(app))
        click(app, "Cancel")
        result["sampleExact"] = hashlib.sha256(sample.read_bytes()).hexdigest() == sample_hash
        result["programNotRun"] = not marker.exists()
        result["passed"] = bool(result["browseFocused"] and result["commandPreview"] and
                                result["broadPreview"] and result["specificPreview"] and
                                result["rawRoundTrip"] and result["specificStillFirst"] and
                                (result["settingsBefore"] == result["settingsAfterCancel"]
                                 if result["settingsBefore"] else result["savedDraftAbsent"]) and
                                result["commandRestored"] and result["associationsRestored"] and
                                result["filterRestored"] and result["sampleExact"] and
                                result["programNotRun"])
        result["stage"] = "complete"
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        if process:
            result["appLogTail"] = (root / "app.log").read_text(errors="replace")[-1500:]
            try:
                result["settingsNodesAtFailure"] = [
                    {"role": n.get_role_name(), "name": n.get_name(), "showing": visible(n)}
                    for n in tree(app) if (n.get_name() or "") and
                    any(word in (n.get_name() or "") for word in
                        ("Settings", "filter", "command", "Association", "Tools", "Behavior"))][-70:]
            except Exception:
                pass
    finally:
        stop(process)
        report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        if root.resolve().parent != validation / "StructuredSettings":
            raise AssertionError("Fixture cleanup escaped the owned validation root")
        shutil.rmtree(root)
    return result


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    outcome = run(*(Path(arg) for arg in sys.argv[1:]))
    raise SystemExit(0 if outcome["passed"] else 1)
