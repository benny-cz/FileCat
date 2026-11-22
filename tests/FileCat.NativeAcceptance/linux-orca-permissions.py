#!/usr/bin/env python3
"""U1 G-UX: hear File -> Inspect -> Permissions and ownership and the Unix mode editor through Orca.

Usage: linux-orca-permissions.py APP_PATH REPORT.json KEY_HELPER
Run in the logged-in GNOME session against an isolated Git-built FileCat below ~/FileCatValidation, with no other
Orca instance running. The caller passes the one-time guest sudo password for the physical-key helper through
FILECAT_NATIVE_SUDO_PASSWORD; it is removed before Orca or FileCat starts. Keyboard only, except that AT-SPI focuses
the editor's octal field, as a Tab would. The command palette opens the inspector, one Tab cycle must speak the item,
its owner and group and the actions, the mode editor must open on its first permission, an invalid octal value must be
spoken, and Escape must offer to discard the draft. Nothing is applied: the witness keeps its mode and ACL.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import subprocess
import sys
import time
from uuid import uuid4

speech_helpers = runpy.run_path(str(Path(__file__).with_name("linux-orca-speech.py")))
SpeechLog = speech_helpers["SpeechLog"]
windows_of = speech_helpers["windows_of"]
INVALID_OCTAL = "Enter three or four octal digits"


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def acl_of(path):
    return subprocess.run(["/usr/bin/getfacl", "-cp", str(path)], check=True, capture_output=True, text=True,
                          timeout=10).stdout


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    if (validation / "dotnet-sdk" / "dotnet").is_file():
        os.environ.setdefault("DOTNET_ROOT", str(validation / "dotnet-sdk"))
    app_path, key_helper = (Path(value).resolve(strict=True) for value in (sys.argv[1], sys.argv[3]))
    report_path = Path(sys.argv[2]).resolve()
    if (app_path.name != "FileCat" or validation not in app_path.parents or report_path.parent != validation or
            key_helper.name != "linux-uinput-key.py" or validation not in key_helper.parents):
        raise SystemExit("Use the isolated FileCat build, the owned key helper and a report under ~/FileCatValidation")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise SystemExit("A transient guest sudo credential is required for physical test input")
    if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", "orca"], capture_output=True).returncode == 0:
        raise SystemExit("Another Orca instance is running; stop it so this test owns the only speech log")

    fixture = validation / "OrcaPermissionsFixture" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    witness = source / "acl-witness.txt"
    witness.write_bytes(b"Permissions editor reader fixture\n")
    os.chmod(witness, 0o640)
    # A named entry makes the ACL section non-trivial (nobody, UID 65534, read).
    subprocess.run(["/usr/bin/setfacl", "-m", "u:65534:r", str(witness)], check=True, timeout=10)
    mode_before, acl_before = os.stat(witness).st_mode & 0o7777, acl_of(witness)
    desktop_environment = runpy.run_path(
        str(Path(__file__).with_name("linux-atspi-ftp-large-listing.py")))["desktop_environment"]
    os.environ.update(desktop_environment())
    orca_version = subprocess.run(["/usr/bin/orca", "--version"], capture_output=True, text=True, timeout=10).stdout.strip()
    report = {"passed": False, "appDllSha256": sha256(app_path.with_name("FileCat.dll")), "orca": orca_version,
              "fixture": str(fixture), "witness": str(witness), "input": "uinput keyboard; AT-SPI focus for one field",
              "steps": []}
    speech = SpeechLog(fixture / "orca-debug.log")
    orca = process = None
    try:
        orca_env = {**os.environ, "GDK_BACKEND": "wayland", "WAYLAND_DISPLAY": "wayland-0"}
        orca = subprocess.Popen(["/usr/bin/python3", "-c", speech_helpers["ORCA_WRAPPER"], str(speech.path)],
                                env=orca_env, stdout=subprocess.DEVNULL, stderr=(fixture / "orca.err").open("wb"))
        deadline = time.monotonic() + 20
        while not any("Screen reader on" in phrase for phrase in speech.take()):
            if time.monotonic() > deadline or orca.poll() is not None:
                raise AssertionError("Orca did not report that the screen reader started")
            time.sleep(0.2)
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=fixture, env=os.environ.copy(), stdout=log, stderr=subprocess.STDOUT)
        deadline = time.monotonic() + 40
        while len(windows_of(process.pid)) != 1:
            if time.monotonic() > deadline or process.poll() is not None:
                raise AssertionError("The isolated FileCat window did not appear")
            time.sleep(0.25)
        time.sleep(2.0)

        def active_pid():
            return subprocess.run(["/usr/bin/xdotool", "getactivewindow", "getwindowpid"],
                                  check=True, capture_output=True, text=True, timeout=5).stdout.strip()

        def activate():
            if active_pid() == str(process.pid):
                return
            window = windows_of(process.pid)
            if len(window) != 1:
                raise AssertionError(f"Expected one isolated FileCat window, found {window}")
            subprocess.run(["/usr/bin/wmctrl", "-ia", window[0]], check=True, timeout=5)
            time.sleep(0.3)
            if (active := active_pid()) != str(process.pid):
                raise AssertionError(f"The isolated FileCat window is not active (PID {active})")

        def key(chord):
            def send():
                activate()
                sent = subprocess.run(["sudo", "-S", sys.executable, str(key_helper), chord],
                                      input=sudo_password + "\n", capture_output=True, text=True, timeout=15)
                if sent.returncode:
                    raise AssertionError(f"Physical {chord} input failed: {sent.stderr[-300:]}")
            return send

        def typed(text):
            # The helper types lowercase letters and spaces from one device, at most 40 at a time.
            return key("type:" + text)

        def says(*parts):
            return lambda phrases: all(any(part.casefold() in phrase.casefold() for phrase in phrases) for part in parts)

        def step(label, action, expect, seconds=8.0, flush=True):
            if flush:
                speech.take()
            action()
            heard, deadline = [], time.monotonic() + seconds
            while time.monotonic() < deadline:
                if orca.poll() is not None:
                    raise AssertionError(f"The test-owned Orca exited with {orca.returncode} during {label}")
                heard += speech.take()
                if expect(heard):
                    time.sleep(0.6)
                    heard += speech.take()
                    break
                time.sleep(0.15)
            passed = expect(heard)
            report["steps"].append({"step": label, "heard": heard, "passed": passed})
            if not passed:
                raise AssertionError(f"{label}: Orca said {heard!r}")
            return heard

        step("activate the new window", activate, says("Go to parent location"), flush=False)
        step("down to the witness", key("down"), says("acl-witness.txt"))
        step("Ctrl+Shift+P opens command search", key("ctrl+shift+p"), says("Search commands"))
        typed("permissions and ownership")()
        time.sleep(0.8)
        step("Enter opens the inspector", key("enter"), says("Permissions and ownership"))

        # One Tab cycle through the inspector's focusable controls must name the item, its owner and group and the
        # actions; the values themselves are plain text the keyboard never reaches.
        cycle = []
        for index in range(30):
            speech.take()
            key("tab")()
            heard, deadline = [], time.monotonic() + 2.5
            while time.monotonic() < deadline:
                heard += speech.take()
                if heard:
                    time.sleep(0.5)
                    heard += speech.take()
                    break
                time.sleep(0.15)
            cycle.append(heard)
            joined = " ".join(" ".join(entry) for entry in cycle)
            if index > 3 and "Edit mode" in joined and any("Close" in phrase for phrase in heard):
                break
        joined = " ".join(" ".join(entry) for entry in cycle)
        expected = [str(witness), "Copy exact owner ID", "Copy exact primary group ID", "Edit mode", "Edit access ACL"]
        report["tabCycle"] = cycle
        report["tabCycleMissing"] = [part for part in expected if part not in joined]
        # Each copy button speaks the value it copies (Orca drops a description it finds similar to the name).
        for name, value in (("Copy exact owner ID", str(os.getuid())), ("Copy exact primary group ID", str(os.getgid()))):
            entry = next((" ".join(heard) for heard in cycle if any(name in phrase for phrase in heard)), "")
            if value not in entry.replace(name, ""):
                report["tabCycleMissing"].append(f"{name}: {value}")
        if report["tabCycleMissing"]:
            raise AssertionError(f"The Tab cycle never spoke {report['tabCycleMissing']}")

        import gi
        gi.require_version("Atspi", "2.0")
        from gi.repository import Atspi

        def descendants(node):
            pending = [node]
            while pending:
                current = pending.pop()
                if current is None:
                    continue
                yield current
                try:
                    pending.extend(current.get_child_at_index(i) for i in reversed(range(current.get_child_count())))
                except (AttributeError, RuntimeError):
                    continue

        def app_node():
            desktop = Atspi.get_desktop(0)
            for index in range(desktop.get_child_count()):
                candidate = desktop.get_child_at_index(index)
                if candidate is not None and candidate.get_name() == "FileCat" and candidate.get_process_id() == process.pid:
                    return candidate
            raise AssertionError("The isolated FileCat accessibility app is unavailable")

        # at-spi2-core renamed "push button" to "button" and "entry" to "text" between Ubuntu 24.04 and 26.04.
        def node(roles, name):
            found = [item for item in descendants(app_node()) if item.get_role_name() in roles and item.get_name() == name]
            if len(found) != 1:
                raise AssertionError(f"Expected one {roles} {name!r}, found {len(found)}")
            return found[0]

        edit = node(("push button", "button"), "Edit mode…")

        def focus_edit():
            if not edit.grab_focus():
                raise AssertionError("Could not focus Edit mode")
        step("focus Edit mode", focus_edit, says("Edit mode"))
        # Apply is unavailable until there is a change: the editor must open on its first permission.
        step("Enter opens the mode editor on its first permission", key("enter"), says("Edit mode", "Owner read"))

        octal = node(("entry", "text"), "Permissions as octal")

        def focus_octal():
            if not octal.grab_focus():
                raise AssertionError("Could not focus the octal field")
        heard = step("focus the octal field", focus_octal, says("Permissions as octal", "640"))
        if any("mixed" in phrase.casefold() for phrase in heard):
            raise AssertionError(f"A definite mode was read as mixed: {heard!r}")

        def invalid_digit():
            key("end")()
            key("backspace")()
            typed("x")()
        step("an invalid octal value is announced", invalid_digit, says(INVALID_OCTAL))
        step("Escape with a dirty draft asks to discard it", key("escape"), says("Discard changes?", "Discard"))
        step("Enter discards the draft", key("enter"), says("acl-witness.txt"), seconds=10)

        mode_after, acl_after = os.stat(witness).st_mode & 0o7777, acl_of(witness)
        report["witness"] = {"modeBefore": oct(mode_before), "modeAfter": oct(mode_after),
                             "aclBefore": acl_before, "aclAfter": acl_after}
        if (mode_after, acl_after) != (mode_before, acl_before):
            raise AssertionError("The witness mode or ACL changed")
        report["passed"] = all(entry["passed"] for entry in report["steps"])
    except Exception as exc:
        report["error"] = f"{type(exc).__name__}: {exc}"
        raise
    finally:
        for owned in (process, orca):
            if owned is not None and owned.poll() is None:
                owned.terminate()
                try:
                    owned.wait(10)
                except subprocess.TimeoutExpired:
                    owned.kill()
        report["speechLog"] = str(speech.path)
        report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
