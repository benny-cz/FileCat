#!/usr/bin/env python3
"""Hear current native Hex-editor state with Orca on an owned local file.

Usage: linux-orca-hex-editor-state.py GIT_APP REPORT.json UINPUT_KEY_HELPER
The editor's dirty overlay is never saved; the test verifies original bytes.
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

HERE = Path(__file__).resolve().parent
common = runpy.run_path(str(HERE / "linux-atspi-viewer-find.py"))
find, wait_for, descendants = (common[name] for name in ("find", "wait_for", "descendants"))
hex_value = runpy.run_path(str(HERE / "linux-atspi-hex-resize.py"))["hex_value"]
speech_source = runpy.run_path(str(HERE / "linux-orca-speech.py"))
ORCA_WRAPPER, SpeechLog = speech_source["ORCA_WRAPPER"], speech_source["SpeechLog"]
NAME = "semantic-editor.bin"
CONTENT = bytes(range(64))


def send_key(helper, password, chord):
    sent = subprocess.run(["sudo", "-S", sys.executable, str(helper), chord],
                          input=password + "\n", capture_output=True,
                          text=True, timeout=15)
    if sent.returncode:
        raise AssertionError(f"Native {chord} input was rejected: {sent.stderr[-200:]}")


def hear(speech, orca, expected, description, seconds=4):
    heard = []
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if orca.poll() is not None:
            raise AssertionError(f"Orca exited during {description}")
        heard.extend(speech.take())
        if any(expected.casefold() in phrase.casefold() for phrase in heard):
            return heard
        time.sleep(0.15)
    raise AssertionError(f"Orca did not speak {description}: {heard[:30]!r}")


def run(app_path, report_path, key_helper):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    key_helper = key_helper.resolve(strict=True)
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            key_helper.name != "linux-uinput-key.py" or validation not in key_helper.parents or
            report_path.parent != validation or report_path.exists()):
        raise AssertionError("Use a Git-built app, owned input helper and fresh validation report")
    for process_name in ("FileCat", "orca"):
        if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", process_name],
                          capture_output=True).returncode == 0:
            raise AssertionError(f"Another {process_name} instance is running")
    password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not password:
        raise AssertionError("A transient guest input-helper credential is required")
    root = validation / "HexEditorSpokenState" / uuid4().hex
    source, target, data = (root / name for name in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    file = source / NAME
    file.write_bytes(CONTENT)
    os.environ.update(runpy.run_path(str(HERE / "linux-atspi-ftp-large-listing.py"))["desktop_environment"]())
    environment = os.environ.copy()
    environment["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    speech = SpeechLog(root / "orca-debug.log")
    result = {"passed": False, "stage": "startup", "sourceSha256": hashlib.sha256(CONTENT).hexdigest(),
              "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
              "speechLog": str(speech.path), "spokenSteps": []}
    orca = process = None
    try:
        orca_environment = {**environment, "GDK_BACKEND": "wayland", "WAYLAND_DISPLAY": "wayland-0"}
        with (root / "orca.err").open("wb") as errors:
            orca = subprocess.Popen(["/usr/bin/python3", "-c", ORCA_WRAPPER, str(speech.path)],
                                    stdout=subprocess.DEVNULL, stderr=errors, env=orca_environment)
        hear(speech, orca, "Screen reader on", "owned Orca startup", seconds=25)
        with (root / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=root, env=environment, stdout=log, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "exact owned FileCat", seconds=35)
        row = wait_for(lambda: next((node for node in descendants(app)
                       if node.get_role_name() == "list item" and
                       (node.get_name() or "").startswith(NAME + ", file,")), None),
                       "test-owned source row")
        if not row.grab_focus():
            raise AssertionError("The owned file row could not take focus")
        command = find(app, "entry", "Search commands")
        if command is None or not command.grab_focus() or not command.get_editable_text().set_text_contents("Edit file bytes in hex"):
            raise AssertionError("The Hex editor command search was unavailable")
        wait_for(lambda: next((node for node in descendants(app)
                 if node.get_role_name() == "list item" and "Id = hex.edit," in (node.get_name() or "")), None),
                 "owned Hex editor command")
        send_key(key_helper, password, "enter")
        title = f"{NAME} — FileCat Hex Editor"
        editor = wait_for(lambda: find(app, "frame", title), "owned Hex editor")
        document = wait_for(lambda: find(editor, "entry", "Hex view"), "Hex document")
        if not document.grab_focus():
            raise AssertionError("Hex document could not take focus")
        result["stage"] = "spoken normal editing state"
        def step(name, key, semantic, spoken):
            speech.take()
            send_key(key_helper, password, key)
            value = wait_for(lambda: state if semantic in (state := hex_value(editor)) else None,
                             f"{name} semantic state")
            heard = hear(speech, orca, spoken, name)
            result["spokenSteps"].append({"step": name, "key": key, "semantic": value[:320],
                                          "spoken": heard[:12]})
        step("pending hex nibble", "a", "pending first nibble A", "pending first nibble A")
        step("unsaved hex byte", "c", "1 modified bytes, unsaved", "unsaved")
        step("text typing column", "tab", "active typing column: text (ASCII)", "Typing: text (ASCII)")
        step("unsaved text byte", "a", "2 modified bytes, unsaved", "unsaved")
        step("selected source bytes", "shift+left", "Selected bytes", "Selected bytes")
        # F6 visits the toolbar and returns to the same editor state. Ctrl+F
        # visits Find; Escape returns without changing unsaved bytes.
        for away, back in (("f6", "f6"), ("ctrl+f", "escape")):
            send_key(key_helper, password, away)
            speech.take()
            send_key(key_helper, password, back)
            wait_for(lambda: document.get_state_set().contains(Atspi.StateType.FOCUSED),
                     f"focus return after {away}")
            if "2 modified bytes, unsaved" not in hex_value(editor):
                raise AssertionError(f"Editing state changed after {away}")
            result["spokenSteps"].append({"step": f"focus returned after {away}"})
        if file.read_bytes() != CONTENT or list(target.iterdir()):
            raise AssertionError("Unsaved editor state changed original bytes or target")
        result.update({"passed": True, "stage": "complete", "sourceUnchanged": True,
                       "targetEmpty": True})
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        result["speechTail"] = speech.take()[-30:]
        if (root / "app.log").is_file():
            result["appLogTail"] = (root / "app.log").read_text(
                encoding="utf-8", errors="replace")[-900:]
    finally:
        for owned in (process, orca):
            if owned is not None and owned.poll() is None:
                owned.terminate()
                try:
                    owned.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    owned.kill()
                    owned.wait(timeout=10)
        # Retain only the ignored guest-local debug log and the receipt.
        if speech.path.is_file():
            retained = report_path.with_suffix(".orca.log")
            shutil.copy2(speech.path, retained)
            result["speechLog"] = str(retained)
        shutil.rmtree(root)
    return result


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    report = Path(sys.argv[2]).resolve()
    outcome = run(Path(sys.argv[1]), report, Path(sys.argv[3]))
    report.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in outcome.items() if key != "spokenSteps"}))
    raise SystemExit(0 if outcome["passed"] else 1)
