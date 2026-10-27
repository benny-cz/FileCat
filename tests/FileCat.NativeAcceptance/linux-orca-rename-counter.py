#!/usr/bin/env python3
"""Hear the inline bulk-rename counter error in the native Ubuntu dialog.

Usage: linux-orca-rename-counter.py APP_PATH REPORT.json KEY_HELPER
Run in the logged-in, isolated Ubuntu VM with no other Orca process.
FILECAT_NATIVE_SUDO_PASSWORD is used only for physical keyboard input and is
removed before FileCat and Orca start.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import signal
import subprocess
import sys
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

ui = runpy.run_path(str(Path(__file__).with_name("linux-atspi-dialog-pointer-dismiss.py")))
speech_module = runpy.run_path(str(Path(__file__).with_name("linux-orca-speech.py")))
descendants = ui["descendants"]
named = ui["named"]
wait_for = ui["wait_for"]
activate_window = ui["activate_window"]
SpeechLog = speech_module["SpeechLog"]
ORCA_WRAPPER = speech_module["ORCA_WRAPPER"]


def one(root, role, name):
    matches = [node for node in descendants(root) if node.get_role_name() == role and
               (node.get_name() == name or
                (role == "list item" and node.get_name().startswith(name + ", file,")))]
    if len(matches) != 1:
        raise AssertionError(f"Expected one {role} {name!r}; found {len(matches)}")
    return matches[0]


def modal_rename(root):
    for button in named(root, "button", "Rename"):
        parent = button
        for _ in range(16):
            parent = parent.get_parent() if parent is not None else None
            if parent is None:
                break
            if parent.get_name() == "Bulk rename dialog":
                return button
    raise AssertionError("No modal Rename approval")


def main(app_path, report_path, key_helper):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    key_helper = key_helper.resolve(strict=True)
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            key_helper.name != "linux-uinput-key.py" or validation not in key_helper.parents or
            report_path.parent.resolve() != validation):
        raise AssertionError("Use a Git-built app, owned helper and isolated report")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise AssertionError("The physical-key helper needs transient guest authentication")
    if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", "orca"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Another Orca process owns the speech session")
    os.environ["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")

    fixture = validation / "RenameCounterSpeechFixture" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    original = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    for name, content in original.items():
        (source / name).write_bytes(content)
    report = {"passed": False, "platform": "Ubuntu Orca",
              "appDllSha256": hashlib.sha256(app_path.with_name("FileCat.dll").read_bytes()).hexdigest(),
              "fixture": str(fixture), "speech": [], "steps": []}
    speech = SpeechLog(fixture / "orca-debug.log")
    orca = process = None
    try:
        orca_env = {**os.environ, "GDK_BACKEND": "wayland", "WAYLAND_DISPLAY": "wayland-0"}
        orca = subprocess.Popen(["/usr/bin/python3", "-c", ORCA_WRAPPER,
                                 str(speech.path)], env=orca_env, stdout=subprocess.DEVNULL,
                                stderr=(fixture / "orca.err").open("wb"),
                                start_new_session=True)
        wait_for(lambda: "Screen reader on" in " ".join(speech.take()),
                 "Orca speech startup", seconds=25)
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log,
                                       stderr=subprocess.STDOUT, start_new_session=True)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((candidate for candidate in
            (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
            if candidate.get_name() == "FileCat" and
            named(candidate, "list", f"Panel 1, source, {source}")), None),
            "isolated source panel", seconds=25)

        def send(chord):
            activate_window(process.pid)
            sent = subprocess.run(["sudo", "-S", sys.executable, str(key_helper), chord],
                                  input=sudo_password + "\n", capture_output=True,
                                  text=True, timeout=15)
            if sent.returncode:
                raise AssertionError(f"Physical {chord} failed: {sent.stderr[-300:]}")

        row = wait_for(lambda: next((node for node in descendants(app)
            if node.get_role_name() == "list item" and
            node.get_name().startswith("a.txt, file,")), None), "a.txt row")
        if not row.grab_focus():
            raise AssertionError("Could not focus the source row")
        wait_for(lambda: row.get_state_set().contains(Atspi.StateType.FOCUSED),
                 "focused source row")
        send("ctrl+m")
        wait_for(lambda: named(app, "entry", "Name mask"), "Bulk rename dialog")
        options = wait_for(lambda: next((node for node in descendants(app)
            if node.get_name() == "Counter, search and case options" and
            node.get_n_actions() > 0), None), "counter options")
        if not options.do_action(0):
            raise AssertionError("Could not expand counter options")
        field = wait_for(lambda: next(iter(named(app, "entry", "Counter start")), None),
                         "counter start entry")
        speech.take()
        if not field.get_editable_text().set_text_contents("10x"):
            raise AssertionError("Could not enter the invalid counter draft")
        if not field.grab_focus():
            raise AssertionError("Could not focus the invalid counter")
        rename = modal_rename(app)
        wait_for(lambda: not rename.get_state_set().contains(Atspi.StateType.ENABLED),
                 "Rename disabled for invalid draft")
        wait_for(lambda: any("counter start must be" in node.get_name().lower()
                             for node in descendants(app)), "visible counter error")
        deadline = time.monotonic() + 12
        while time.monotonic() < deadline:
            report["speech"].extend(speech.take())
            if any("counter start must be" in phrase.lower() for phrase in report["speech"]):
                break
            time.sleep(0.2)
        if not any("counter start must be" in phrase.lower() for phrase in report["speech"]):
            raise AssertionError("Orca did not speak the inline counter error")
        if Atspi.Text.get_text(field.get_text_iface(), 0, -1) != "10x":
            raise AssertionError("Invalid counter draft was rewritten")
        report["steps"].append("Orca spoke the inline error; draft stayed exact and approval disabled")
        send("escape")
        wait_for(lambda: not named(app, "entry", "Name mask"), "Cancel closed review")
        if ({entry.name for entry in source.iterdir()} != set(original) or
                any((source / name).read_bytes() != value for name, value in original.items()) or
                list(target.iterdir())):
            raise AssertionError("Cancel changed source bytes or target")
        report["steps"].append("Physical Escape preserved all files")
        report["passed"] = True
    except Exception as exc:
        report["error"] = f"{type(exc).__name__}: {exc}"
    finally:
        for child in (process, orca):
            if child is not None and child.poll() is None:
                os.killpg(child.pid, signal.SIGTERM)
                try:
                    child.wait(timeout=6)
                except subprocess.TimeoutExpired:
                    os.killpg(child.pid, signal.SIGKILL)
                    child.wait(timeout=6)
        report["sourceSha256"] = {name: hashlib.sha256((source / name).read_bytes()).hexdigest()
                                  for name in original if (source / name).is_file()}
        report["targetNames"] = sorted(entry.name for entry in target.iterdir())
        report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    raise SystemExit(main(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])))
