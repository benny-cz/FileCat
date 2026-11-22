#!/usr/bin/env python3
"""G-UX: hear the Linux group and default-ACL editors through Orca.

Usage: linux-orca-group-defaults.py APP_PATH REPORT.json KEY_HELPER
Run in the logged-in GNOME session against an isolated Git-built FileCat below ~/FileCatValidation, with no other Orca
instance running, and with FILECAT_QUALIFY_LINUX_GROUP=1 and FILECAT_QUALIFY_LINUX_DEFAULT_ACL=1 so the App composes
both writers. The caller passes the one-time guest sudo password for the physical-key helper through
FILECAT_NATIVE_SUDO_PASSWORD; it is removed before Orca or FileCat starts. The command palette opens the inspector and
AT-SPI focuses one action or field at a time, as a Tab would; everything else is physical keys. The default-ACL editor
must open on its entry list with its size spoken, an invalid account ID must be announced, and Escape must offer to
discard the changed draft. The group editor must open on its group choice, a changed choice must be spoken, and Escape
must offer to discard it. Nothing is applied: both witnesses keep their group, mode and ACLs.
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
INVALID_ID = "Enter a numeric ID from 0 to 4294967294."


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def state_of(path):
    info = path.stat()
    acl = subprocess.run(["/usr/bin/getfacl", "-cp", str(path)], check=True, capture_output=True, text=True, timeout=10).stdout
    return {"uid": info.st_uid, "gid": info.st_gid, "mode": oct(info.st_mode & 0o7777), "acl": acl}


def main():
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    if os.environ.get("FILECAT_QUALIFY_LINUX_GROUP") != "1" or os.environ.get("FILECAT_QUALIFY_LINUX_DEFAULT_ACL") != "1":
        raise SystemExit("Start with FILECAT_QUALIFY_LINUX_GROUP=1 and FILECAT_QUALIFY_LINUX_DEFAULT_ACL=1")
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

    fixture = validation / "OrcaGroupDefaultsFixture" / uuid4().hex
    source, target, data = (fixture / part for part in ("source", "target", "data"))
    for folder in (source, target, data):
        folder.mkdir(parents=True)
    witness, folder_witness = source / "acl-witness.txt", source / "acl-folder"
    witness.write_bytes(b"Group and default ACL reader fixture\n")
    os.chmod(witness, 0o640)
    folder_witness.mkdir()
    os.chmod(folder_witness, 0o750)
    subprocess.run(["/usr/bin/setfacl", "-m", "u:65534:r", str(witness)], check=True, timeout=10)
    subprocess.run(["/usr/bin/setfacl", "-d", "-m", "u:65534:rx", str(folder_witness)], check=True, timeout=10)
    default_acl = subprocess.run(["/usr/bin/getfacl", "-cpd", str(folder_witness)], check=True, capture_output=True,
                                 text=True, timeout=10).stdout
    default_entries = len([line for line in default_acl.splitlines() if line.strip()])
    groups = sorted({str(group) for group in os.getgroups()})
    before = {"file": state_of(witness), "folder": state_of(folder_witness)}
    desktop_environment = runpy.run_path(
        str(Path(__file__).with_name("linux-atspi-ftp-large-listing.py")))["desktop_environment"]
    os.environ.update(desktop_environment())
    orca_version = subprocess.run(["/usr/bin/orca", "--version"], capture_output=True, text=True, timeout=10).stdout.strip()
    report = {"passed": False, "appDllSha256": sha256(app_path.with_name("FileCat.dll")), "orca": orca_version,
              "fixture": str(fixture), "defaultAclEntries": default_entries, "groups": groups,
              "input": "uinput keyboard; AT-SPI focus for one action or field at a time", "steps": []}
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

        def says(*parts):
            return lambda phrases: all(any(part.casefold() in phrase.casefold() for phrase in phrases) for part in parts)

        def says_any(choices):
            return lambda phrases: any(choice in phrase for phrase in phrases for choice in choices)

        def step(label, action, expect, seconds=8.0):
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

        def focus(roles, name):
            def grab():
                if not node(roles, name).grab_focus():
                    raise AssertionError(f"Could not focus {name}")
            return grab

        buttons, entries = ("push button", "button"), ("entry", "text")

        def open_inspector(item):
            step(f"Ctrl+Shift+P opens command search on {item}", key("ctrl+shift+p"), says("Search commands"))
            key("type:permissions and ownership")()
            time.sleep(0.8)
            step(f"Enter opens the inspector of {item}", key("enter"), says("Permissions and ownership"))

        def offered_actions():
            return sorted({item.get_name() for item in descendants(app_node())
                           if item.get_role_name() in buttons and item.get_name().endswith("…")})

        step("activate the new window", activate, says("Go to parent location"))
        step("down to the folder", key("down"), says("acl-folder"))

        # Default ACL editor: opens on its entry list, which speaks its size; a non-numeric account ID is announced.
        open_inspector("the folder")
        report["folderActions"] = offered_actions()
        step("focus Edit default ACL", focus(buttons, "Edit default ACL…"), says("Edit default ACL"))
        step("Enter opens the default ACL editor on its entries", key("enter"),
             says("Edit default ACL", "Default ACL entries", f"List with {default_entries} items"))
        step("focus the new account ID", focus(entries, "New ACL account ID"), says("New ACL account ID"))
        step("a non-numeric account ID is announced", key("type:x"), says(INVALID_ID))
        step("Escape with a dirty draft asks to discard it", key("escape"), says("Discard changes?", "Discard"))
        step("Enter discards the draft", key("enter"), says("acl-folder"), seconds=10)

        # Group editor: opens on its group choice; a changed choice is spoken and can be discarded.
        step("down to the file", key("down"), says("acl-witness.txt"))
        open_inspector("the file")
        report["fileActions"] = offered_actions()
        step("focus Change group", focus(buttons, "Change group…"), says("Change group"))
        step("Enter opens the group editor on its group choice", key("enter"), says("Change group and mode", "Assignable group ID"))
        step("Down chooses an assignable group", key("down"), says_any(groups))
        step("Escape with a changed group asks to discard it", key("escape"), says("Discard changes?", "Discard"))
        step("Enter discards the group draft", key("enter"), says("acl-witness.txt"), seconds=10)

        after = {"file": state_of(witness), "folder": state_of(folder_witness)}
        report["state"] = {"before": before, "after": after}
        if after != before:
            raise AssertionError("A witness changed")
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
