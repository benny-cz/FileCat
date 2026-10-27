#!/usr/bin/env python3
"""Exercise delayed, canceled and superseded native Quick View over owned SFTP.

Usage: linux-atspi-quick-view-remote.py APP REPORT.json KEY_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD and FILECAT_NATIVE_SFTP_PASSWORD transiently.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import threading
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


def run(app_path: Path, report: Path, helper: Path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, helper = (p.resolve(strict=True) for p in (app_path, helper))
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            helper != validation / "linux-uinput-key.py" or report.parent != validation or
            report.exists()):
        raise AssertionError("Use the isolated Git app, keyboard and fresh report")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    sftp_password = os.environ.pop("FILECAT_NATIVE_SFTP_PASSWORD", None)
    if not sudo_password or not sftp_password:
        raise AssertionError("Transient input and SFTP credentials are required")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Close other FileCat processes before this isolated check")
    repo = next(parent for parent in app_path.parents if (parent / ".git").exists())
    common = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))
    wait_for, tree, matching, action, press, stop, desktop_environment = (
        common[name] for name in ("wait_for", "tree", "matching", "action", "press", "stop",
                                    "desktop_environment"))
    relay_class = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-navigation-capacity.py"))[
        "DelayedRelay"]
    fingerprints = runpy.run_path(str(validation / "linux-sftp-replace-fixture.py"))["fingerprints"]
    root = validation / "QuickViewRemote" / uuid4().hex
    local, remote, data = (root / name for name in ("local", "remote", "data"))
    for folder in (local, remote, data):
        folder.mkdir(parents=True)
    content = {"a-first.txt": b"quick view first selection\n",
               "b-second.txt": b"quick view second selection\n"}
    for name, value in content.items():
        (remote / name).write_bytes(value)
    source_hashes = {name: hashlib.sha256(value).hexdigest() for name, value in content.items()}
    relay = relay_class()
    relay.delay_seconds = 0.005
    thread = threading.Thread(target=relay.serve_forever, daemon=True)
    thread.start()
    address = f"sftp://benny@127.0.0.1:{relay.server_address[1]}{remote}/"
    result = {"passed": False, "stage": "startup", "gitHead": subprocess.check_output(
        ["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip(),
        "appDllSha256": hashlib.sha256(app_path.with_name("FileCat.dll").read_bytes()).hexdigest(),
        "fixture": str(root), "remoteAddress": address, "sourceSha256": source_hashes}
    process = None

    def find(app, role, name):
        return next((node for node in tree(app) if node.get_role_name() == role and
                     node.get_name() == name and
                     node.get_state_set().contains(Atspi.StateType.SHOWING)), None)

    def shown(app, fragment):
        return next((node.get_name() for node in tree(app) if
                     node.get_role_name() == "label" and
                     node.get_state_set().contains(Atspi.StateType.SHOWING) and
                     fragment in (node.get_name() or "")), None)

    def text_of(app):
        control = find(app, "entry", "Text viewer")
        interface = control.get_text_iface() if control else None
        return Atspi.Text.get_text(interface, 0, -1) if interface else ""

    def select(app, name):
        row = wait_for(lambda: next((node for node in tree(app) if
                       node.get_role_name() == "list item" and
                       (node.get_name() or "").startswith(name + ",")), None), name)
        if not row.grab_focus():
            raise AssertionError(f"Could not focus {name}")

    try:
        os.environ.update(desktop_environment())
        env = os.environ.copy()
        env["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(local), "--right", str(local)],
                                       cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        result["stage"] = "verified owned SFTP connection"
        locations = wait_for(lambda: matching(app, "entry", "Location")
                             if len(matching(app, "entry", "Location")) == 2 else None,
                             "both Location fields")
        field = locations[0]
        if not field.grab_focus() or not field.get_editable_text().set_text_contents(address):
            raise AssertionError("Could not enter owned SFTP source")
        press(helper, sudo_password, "enter", process.pid)
        password = wait_for(lambda: find(app, "entry", "Password"), "SFTP password")
        if not password.get_editable_text().set_text_contents(sftp_password):
            raise AssertionError("Could not enter isolated SFTP password")
        action(wait_for(lambda: find(app, "button", "Connect"), "SFTP Connect"), "SFTP Connect")
        wait_for(lambda: shown(app, "New server key"), "server-key review")
        names = {node.get_name() for node in tree(app)
                 if node.get_state_set().contains(Atspi.StateType.SHOWING)}
        result["matchedHostKeys"] = sorted(set(fingerprints()) & names)
        if not result["matchedHostKeys"]:
            raise AssertionError("Server key differs from independent OpenSSH fingerprints")
        action(wait_for(lambda: find(app, "button", "Trust and connect"), "owned host trust"),
               "verified test-profile trust")
        select(app, "a-first.txt")
        relay.delay_seconds = 0.35
        chunks_before_preview = relay.delayed_chunks
        press(helper, sudo_password, "ctrl+q", process.pid)
        result["stage"] = "delayed Loading and Cancel"
        result["loading"] = wait_for(lambda: shown(app, 'Loading preview of "a-first.txt"'),
                                     "selection-owned loading state", seconds=15)
        cancel = wait_for(lambda: find(app, "button", "Cancel quick preview"), "Cancel preview")
        action(cancel, "cancel delayed preview")
        result["cancelMessage"] = wait_for(lambda: shown(app, "Preview canceled."),
                                           "canceled preview state")
        result["cancelWhileRelayDelayed"] = relay.delayed_chunks > chunks_before_preview
        result["stage"] = "current-item Retry"
        relay.delay_seconds = 0.005
        retry = wait_for(lambda: find(app, "button", "Retry quick preview"), "Retry preview")
        action(retry, "retry current preview")
        result["retriedText"] = wait_for(lambda: value if "quick view first selection" in
                                         (value := text_of(app)) else None,
                                         "first file content after Retry", seconds=30)
        result["stage"] = "rapid selection ownership"
        relay.delay_seconds = 0.35
        select(app, "b-second.txt")
        result["secondLoading"] = wait_for(lambda: shown(app, 'Loading preview of "b-second.txt"'),
                                           "second-file Loading", seconds=15)
        select(app, "a-first.txt")
        relay.delay_seconds = 0.005
        result["returnedText"] = wait_for(lambda: value if "quick view first selection" in
                                           (value := text_of(app)) else None,
                                           "returned first-file content", seconds=30)
        time.sleep(1.0)  # Give the abandoned second-file request time to finish.
        result["lastTitle"] = shown(app, "a-first.txt")
        result["staleSecondVisible"] = bool(shown(app, "b-second.txt") or
                                             "quick view second selection" in text_of(app))
        result["sourceUnchanged"] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                      for p in remote.iterdir()} == source_hashes
        result["targetEmpty"] = not any(local.iterdir())
        result["relayDelayedChunks"] = relay.delayed_chunks
        result["passed"] = (result["cancelWhileRelayDelayed"] and
                            not result["staleSecondVisible"] and result["lastTitle"] is not None and
                            result["sourceUnchanged"] and result["targetEmpty"])
        result["stage"] = "complete"
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        if process is not None and process.poll() is None:
            try:
                result["visibleAtFailure"] = [(node.get_role_name(), node.get_name())
                    for node in tree(app) if node.get_state_set().contains(Atspi.StateType.SHOWING)
                    and node.get_role_name() in ("label", "button", "entry")][-100:]
            except Exception:
                pass
    finally:
        stop(process)
        relay.drop_sessions()
        relay.shutdown()
        relay.server_close()
        thread.join(timeout=5)
        shutil.rmtree(root)
        report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    raise SystemExit(run(*(Path(arg) for arg in sys.argv[1:])))
