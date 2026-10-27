#!/usr/bin/env python3
"""Native narrow Ubuntu SFTP ancestor menu, keyboard and Orca acceptance.

Usage: linux-atspi-remote-breadcrumb.py APP REPORT.json KEY_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD and FILECAT_NATIVE_SFTP_PASSWORD transiently.
Run only in the isolated Ubuntu VM, with no other FileCat or Orca process.
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
    sftp_password = os.environ.pop("FILECAT_NATIVE_SFTP_PASSWORD", None)
    if not sudo_password or not sftp_password:
        raise AssertionError("Transient native input and SFTP credentials are required")
    for name in ("FileCat", "orca"):
        if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", name],
                          capture_output=True).returncode == 0:
            raise AssertionError(f"Stop another {name} process before this owned test")
    repo = next(parent for parent in app_path.parents if (parent / ".git").exists())
    helpers = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))
    wait_for, press, action, stop, desktop_environment = (
        helpers[name] for name in ("wait_for", "press", "action", "stop", "desktop_environment"))
    speech_source = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-orca-speech.py"))
    fingerprints = runpy.run_path(str(validation / "linux-sftp-replace-fixture.py"))["fingerprints"]

    def tree(owner):
        pending = [owner]
        visited = 0
        while pending:
            node = pending.pop()
            visited += 1
            if visited > 1600:
                raise AssertionError("The native breadcrumb tree exceeded 1,600 nodes")
            yield node
            count = node.get_child_count()
            if count > 800:
                raise AssertionError("One native breadcrumb node exceeded 800 children")
            pending.extend(node.get_child_at_index(i) for i in range(count - 1, -1, -1))

    root = validation / "RemoteBreadcrumb" / uuid4().hex
    local, server, data = root / "local", root / "server", root / "data"
    leaf = server / "long-root-for-narrow-breadcrumb" / "middle" / "distinct-leaf-tail"
    for folder in (local, leaf, data):
        folder.mkdir(parents=True, exist_ok=True)
    sentinel = leaf / "breadcrumb-source.txt"
    sentinel.write_bytes(b"remote breadcrumb source remains byte exact\n")
    expected_hash = hashlib.sha256(sentinel.read_bytes()).hexdigest()
    (local / "local-source.txt").write_bytes(b"local source remains byte exact\n")
    local_hash = hashlib.sha256((local / "local-source.txt").read_bytes()).hexdigest()
    address = f"sftp://benny@127.0.0.1:22{leaf}/"
    result = {"passed": False, "stage": "startup", "fixture": str(root),
              "remoteAddress": address,
              "gitHead": subprocess.check_output(["git", "-C", str(repo), "rev-parse", "HEAD"],
                                               text=True).strip(),
              "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest()}
    process = orca = None
    speech = speech_source["SpeechLog"](root / "orca-debug.log")
    previous_clipboard = None

    def find(app, role, name):
        return next((node for node in tree(app) if node.get_role_name() == role and
                     node.get_name() == name and
                     node.get_state_set().contains(Atspi.StateType.SHOWING)), None)

    def right_list(app):
        return next((node for node in tree(app) if node.get_role_name() == "list" and
                     (node.get_name() or "").startswith("Panel 2,") and
                     node.get_state_set().contains(Atspi.StateType.SHOWING)), None)

    def menu_items(app):
        return [node for node in tree(app) if node.get_role_name() == "menu item" and
                node.get_state_set().contains(Atspi.StateType.SHOWING)]

    def focused(items):
        return next((node for node in items if
                     node.get_state_set().contains(Atspi.StateType.FOCUSED)), None)

    def clipboard():
        value = subprocess.run(["/usr/bin/xclip", "-selection", "clipboard", "-out"],
                               text=True, capture_output=True, timeout=8)
        if value.returncode not in (0, 1):
            raise AssertionError("Could not read isolated VM clipboard")
        return value.stdout

    def set_clipboard(value):
        subprocess.run(["/usr/bin/xclip", "-selection", "clipboard", "-in"],
                       input=value.encode(), stdout=subprocess.DEVNULL,
                       stderr=subprocess.DEVNULL, timeout=5, check=True)

    def choose(app, verb):
        if not right_list(app).grab_focus():
            raise AssertionError("Could not focus the current remote listing")
        speech.take()
        press(key_helper, sudo_password, "alt+up", process.pid)
        ancestor = wait_for(lambda: (n if (n := focused(menu_items(app))) and
                             (n.get_name() or "").startswith("Ancestor ") else None),
                            "focused ancestor menu item")
        target_name = ancestor.get_name()
        result.setdefault("ancestors", []).append(target_name)
        press(key_helper, sudo_password, "right", process.pid)
        target = wait_for(lambda: next((n for n in menu_items(app) if
                           (n.get_name() or "").startswith(verb + ": ")), None),
                          f"{verb} ancestor action")
        for _ in range(5):
            if target.get_state_set().contains(Atspi.StateType.FOCUSED):
                break
            press(key_helper, sudo_password, "down", process.pid)
            time.sleep(0.1)
        if not target.get_state_set().contains(Atspi.StateType.FOCUSED):
            raise AssertionError(f"Keyboard could not reach {verb} in ancestor submenu")
        name = target.get_name()
        result.setdefault("actions", []).append(name)
        heard = []
        until = time.monotonic() + 2
        while time.monotonic() < until:
            heard.extend(speech.take())
            time.sleep(0.1)
        result.setdefault("speech", []).append(heard[-16:])
        press(key_helper, sudo_password, "enter", process.pid)
        return target_name, name

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
            raise AssertionError("Expected the restored narrow 1920×1200, 200% GNOME display")
        result["display"] = "1920x1200@59.950, 200%"
        previous_clipboard = clipboard()
        orca_env = {**env, "GDK_BACKEND": "wayland", "WAYLAND_DISPLAY": "wayland-0"}
        with (root / "orca.err").open("wb") as error_file:
            orca = subprocess.Popen(["/usr/bin/python3", "-c", speech_source["ORCA_WRAPPER"],
                                     str(speech.path)], env=orca_env,
                                    stdout=subprocess.DEVNULL, stderr=error_file)
        wait_for(lambda: [x for x in speech.take() if "screen reader on" in x.casefold()],
                 "owned Orca startup", seconds=20)
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(local), "--right", str(local)],
                                       cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        result["stage"] = "connect to owned SFTP path"
        locations = wait_for(lambda: [n for n in tree(app) if n.get_role_name() == "entry" and
                              n.get_name() == "Location" and
                              n.get_state_set().contains(Atspi.StateType.SHOWING)],
                             "native location fields")
        field = locations[-1]
        if not field.grab_focus() or not field.get_editable_text().set_text_contents(address):
            raise AssertionError("Could not enter the owned SFTP path")
        press(key_helper, sudo_password, "enter", process.pid)
        password = wait_for(lambda: find(app, "entry", "Password"), "SFTP password field")
        if not password.get_editable_text().set_text_contents(sftp_password):
            raise AssertionError("Could not enter the transient SFTP password")
        action(wait_for(lambda: find(app, "button", "Connect"), "SFTP Connect"), "SFTP Connect")
        wait_for(lambda: any("New server key" in (n.get_name() or "") for n in tree(app)),
                 "SFTP host-key decision")
        names = {n.get_name() for n in tree(app)}
        result["matchedHostKeys"] = sorted(set(fingerprints()) & names)
        if not result["matchedHostKeys"]:
            raise AssertionError("The shown host key did not match independent OpenSSH fingerprints")
        action(wait_for(lambda: find(app, "button", "Connect once"), "verified host-key consent"),
               "verified host-key consent")
        result["remoteInitial"] = wait_for(lambda: right_list(app).get_name()
                                           if right_list(app) and str(leaf) in right_list(app).get_name()
                                           else None, "owned remote leaf")

        result["stage"] = "keyboard ancestor and copied path"
        ancestor, copy_name = choose(app, "Copy path")
        expected = copy_name.removeprefix("Copy path: ")
        result["copiedPath"] = clipboard()
        result["copyExact"] = result["copiedPath"] == expected
        result["afterCopyList"] = right_list(app).get_name()
        result["stage"] = "keyboard new-tab and current-list focus"
        ancestor2, new_tab_name = choose(app, "Open in new tab")
        result["afterNewTabList"] = wait_for(lambda: right_list(app).get_name()
                                             if right_list(app) and expected.rstrip("/") in
                                             right_list(app).get_name() and
                                             str(leaf) not in right_list(app).get_name() else None,
                                             "ancestor new tab")
        result["afterNewTabFocused"] = right_list(app).get_state_set().contains(Atspi.StateType.FOCUSED)
        result["stage"] = "keyboard open-here and cancel focus"
        # Returning to the leaf by tab selection is checked separately from
        # the ancestor action: it must not reuse an old focused row coordinate.
        press(key_helper, sudo_password, "ctrl+tab", process.pid)
        wait_for(lambda: right_list(app) if str(leaf) in right_list(app).get_name() else None,
                 "original remote tab")
        if not right_list(app).grab_focus():
            raise AssertionError("Could not focus original remote listing")
        press(key_helper, sudo_password, "alt+up", process.pid)
        wait_for(lambda: focused(menu_items(app)), "cancelable ancestor menu")
        press(key_helper, sudo_password, "escape", process.pid)
        result["afterCancelList"] = right_list(app).get_name()
        result["afterCancelFocused"] = right_list(app).get_state_set().contains(Atspi.StateType.FOCUSED)
        _, open_name = choose(app, "Open here")
        result["afterOpenHereList"] = wait_for(lambda: right_list(app).get_name()
                                              if right_list(app) and expected.rstrip("/") in
                                              right_list(app).get_name() and
                                              str(leaf) not in right_list(app).get_name() else None,
                                              "ancestor opened here")
        result["afterOpenHereFocused"] = right_list(app).get_state_set().contains(Atspi.StateType.FOCUSED)
        result["stage"] = "direct edit remains available and sources stay exact"
        press(key_helper, sudo_password, "ctrl+l", process.pid)
        result["directEdit"] = wait_for(lambda: [n.get_name() for n in tree(app) if
                                         n.get_role_name() == "entry" and
                                         n.get_state_set().contains(Atspi.StateType.FOCUSED)],
                                         "focused direct location editor")
        press(key_helper, sudo_password, "escape", process.pid)
        result["sourcesExact"] = (hashlib.sha256(sentinel.read_bytes()).hexdigest() == expected_hash and
                                  hashlib.sha256((local / "local-source.txt").read_bytes()).hexdigest() == local_hash)
        spoken = [phrase for step in result["speech"] for phrase in step]
        result["fullTargetSpoken"] = any(expected in phrase for phrase in spoken)
        result["passed"] = bool(result["copyExact"] and result["sourcesExact"] and
                                result["afterCopyList"] == result["remoteInitial"] and
                                result["afterNewTabFocused"] and result["afterCancelFocused"] and
                                result["afterOpenHereFocused"] and result["directEdit"] and
                                result["fullTargetSpoken"] and
                                all(expected in item for item in result["actions"]))
        result["stage"] = "complete"
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        result["speechAtFailure"] = speech.take()[-25:]
        if process:
            result["appLogTail"] = (root / "app.log").read_text(errors="replace")[-1200:]
    finally:
        stop(process)
        if orca and orca.poll() is None:
            orca.terminate()
            try:
                orca.wait(timeout=8)
            except subprocess.TimeoutExpired:
                orca.kill()
                orca.wait(timeout=8)
        if previous_clipboard is not None:
            set_clipboard(previous_clipboard)
        report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        if root.resolve().parent != validation / "RemoteBreadcrumb":
            raise AssertionError("Fixture cleanup escaped the owned validation root")
        shutil.rmtree(root)
    return result


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    outcome = run(*(Path(arg) for arg in sys.argv[1:]))
    raise SystemExit(0 if outcome["passed"] else 1)
