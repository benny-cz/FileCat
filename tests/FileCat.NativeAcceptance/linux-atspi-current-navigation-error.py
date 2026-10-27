#!/usr/bin/env python3
"""Hear a current disconnected SFTP navigation failure in isolated Ubuntu.

Usage: linux-atspi-current-navigation-error.py APP REPORT.json KEY_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD and FILECAT_NATIVE_SFTP_PASSWORD transiently.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import socket
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
    sftp_password = os.environ.pop("FILECAT_NATIVE_SFTP_PASSWORD", None)
    if not sudo_password or not sftp_password:
        raise AssertionError("Transient native input and SFTP credentials are required")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0 or subprocess.run(
                          ["pgrep", "-u", str(os.getuid()), "-x", "orca"],
                          capture_output=True).returncode == 0:
        raise AssertionError("Stop other FileCat/Orca processes before this owned test")

    repo = next(parent for parent in app_path.parents if (parent / ".git").exists())
    helpers = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))
    wait_for, tree, matching, action, press, stop, desktop_environment = (
        helpers[name] for name in ("wait_for", "tree", "matching", "action", "press", "stop", "desktop_environment"))
    speech_source = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-orca-speech.py"))
    root = validation / "CurrentNavigationError" / uuid4().hex
    local, data = root / "local", root / "data"
    local.mkdir(parents=True)
    data.mkdir()
    sentinel = local / "still-usable.txt"
    sentinel.write_bytes(b"local panel survives a current remote failure\n")
    sentinel_hash = hashlib.sha256(sentinel.read_bytes()).hexdigest()
    # Bound but not listening: no other process can take the test port, and a
    # connection to this loopback address fails without reaching another host.
    reservation = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    reservation.bind(("127.0.0.1", 0))
    address = f"sftp://benny@127.0.0.1:{reservation.getsockname()[1]}/unreachable/"
    result = {"passed": False, "stage": "startup", "gitHead": subprocess.check_output(
        ["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip(),
        "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
        "fixture": str(root), "unreachableAddress": address}
    process = orca = None
    speech = speech_source["SpeechLog"](root / "orca-debug.log")

    def find(app, role, name):
        return next(iter(matching(app, role, name)), None)

    def visible_names(app):
        return [node.get_name() or "" for node in tree(app)
                if node.get_state_set().contains(Atspi.StateType.SHOWING)]

    def right_list(app):
        return next((node for node in tree(app) if node.get_role_name() == "list" and
                     (node.get_name() or "").startswith("Panel 2,")), None)

    try:
        os.environ.update(desktop_environment())
        env = os.environ.copy()
        runtime = validation / "dotnet-sdk"
        if not (runtime / "dotnet").is_file():
            raise AssertionError("The isolated .NET runtime is missing")
        env["DOTNET_ROOT"] = str(runtime)
        orca_env = {**env, "GDK_BACKEND": "wayland", "WAYLAND_DISPLAY": "wayland-0"}
        with (root / "orca.err").open("wb") as orca_error:
            orca = subprocess.Popen(["/usr/bin/python3", "-c", speech_source["ORCA_WRAPPER"],
                                     str(speech.path)], env=orca_env,
                                    stdout=subprocess.DEVNULL, stderr=orca_error)
        startup = []
        until = time.monotonic() + 20
        while time.monotonic() < until:
            startup.extend(speech.take())
            if any("screen reader on" in phrase.casefold() for phrase in startup):
                break
            if orca.poll() is not None:
                raise AssertionError("Owned Orca exited before the test")
            time.sleep(0.2)
        else:
            raise AssertionError("Owned Orca did not start")
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(local), "--right", str(local)],
                                       cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        initial = wait_for(lambda: right_list(app).get_name() if right_list(app) and
                           str(local) in right_list(app).get_name() else None,
                           "initial local panel")
        result["initialPanel"] = initial
        result["stage"] = "current disconnected remote navigation"
        locations = wait_for(lambda: matching(app, "entry", "Location")
                             if len(matching(app, "entry", "Location")) == 2 else None,
                             "both Location fields")
        field = locations[-1]
        if not field.grab_focus() or not field.get_editable_text().set_text_contents(address):
            raise AssertionError("Could not enter the owned disconnected SFTP address")
        speech.take()
        press(key_helper, sudo_password, "enter", process.pid)
        password = wait_for(lambda: find(app, "entry", "Password"), "SFTP password field")
        if not password.get_editable_text().set_text_contents(sftp_password):
            raise AssertionError("Could not enter the transient SFTP password")
        action(wait_for(lambda: find(app, "button", "Connect"), "SFTP Connect"), "SFTP Connect")
        heard = []
        description = ""
        until = time.monotonic() + 18
        while time.monotonic() < until:
            heard.extend(speech.take())
            listing = right_list(app)
            description = listing.get_description() or "" if listing else ""
            if any("connection refused" in phrase.casefold() for phrase in heard):
                break
            time.sleep(0.2)
        result["listDescription"] = description
        result["speech"] = heard[-30:]
        result["spokenError"] = any("could not reach" in phrase.casefold() and
                                    "connection refused" in phrase.casefold() for phrase in heard)
        result["panelAfterFailure"] = right_list(app).get_name() if right_list(app) else None
        result["stage"] = "return to prior local folder"
        if result["spokenError"]:
            press(key_helper, sudo_password, "alt+left", process.pid)
            result["panelRestored"] = wait_for(
                lambda: right_list(app).get_name() if right_list(app) and
                right_list(app).get_name().endswith(str(local)) else None,
                "prior local panel after Back")
            result["restoredRows"] = [node.get_name() for node in tree(right_list(app))
                                      if node.get_role_name() == "list item"]
            result["sentinelRowVisible"] = any("still-usable.txt" in name for name in result["restoredRows"])
        result["sentinelExact"] = hashlib.sha256(sentinel.read_bytes()).hexdigest() == sentinel_hash
        result["passed"] = (result["spokenError"] and
                            result.get("panelRestored", "").endswith(str(local)) and
                            result.get("sentinelRowVisible") and result["sentinelExact"])
        result["stage"] = "complete"
        return result
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        result["appExit"] = process.poll() if process else None
        if process:
            try:
                result["panelAtFailure"] = right_list(app).get_name() if right_list(app) else None
                result["visibleAtFailure"] = visible_names(app)[-30:]
            except Exception:
                pass
        result["speechAtFailure"] = speech.take()[-25:]
        return result
    finally:
        stop(process)
        if orca and orca.poll() is None:
            orca.terminate()
            try:
                orca.wait(timeout=8)
            except subprocess.TimeoutExpired:
                orca.kill()
                orca.wait(timeout=8)
        reservation.close()
        shutil.rmtree(root)


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    report_path = Path(sys.argv[2]).resolve()
    outcome = run(Path(sys.argv[1]), report_path, Path(sys.argv[3]))
    report_path.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in outcome.items() if key != "speech"}))
    raise SystemExit(0 if outcome["passed"] else 1)
