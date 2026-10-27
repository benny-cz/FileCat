#!/usr/bin/env python3
"""Open native Find from an owned SFTP panel and verify its truthful scope.

Usage: linux-atspi-find-sftp-scope.py APP REPORT.json KEY_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD and FILECAT_NATIVE_SFTP_PASSWORD transiently.
Only the guest's loopback SSH service and this runner's fixture are used.
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

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi

HERE = Path(__file__).resolve().parent
remote = runpy.run_path(str(HERE / "linux-atspi-sftp-delayed-listing.py"))
wait_for = remote["wait_for"]
find = remote["find"]
action = remote["action"]
tree = remote["tree"]
desktop_environment = remote["desktop_environment"]


def text_of(node):
    iface = node.get_text_iface()
    return Atspi.Text.get_text(iface, 0, -1) if iface is not None else node.get_name()


def in_find_window(node):
    for _ in range(12):
        if node is None:
            return False
        if "Find files" in (node.get_name() or "") and node.get_role_name() in ("frame", "window", "dialog"):
            return True
        node = node.get_parent()
    return False


def main(app_path, report_path, key_helper):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    key_helper = key_helper.resolve(strict=True)
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            key_helper.name != "linux-uinput-key.py" or validation not in key_helper.parents or
            report_path.parent.resolve() != validation or report_path.exists()):
        raise AssertionError("Use the isolated Git app, owned input helper and a fresh report")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    sftp_password = os.environ.pop("FILECAT_NATIVE_SFTP_PASSWORD", None)
    if not sudo_password or not sftp_password:
        raise AssertionError("Transient guest input and loopback SFTP credentials are required")
    os.environ.update(desktop_environment())
    os.environ["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    root = validation / "FindSftpScopeFixture" / uuid4().hex
    source, server, data = (root / part for part in ("source", "server", "data"))
    for folder in (source, server, data):
        folder.mkdir(parents=True)
    (source / "unchanged.txt").write_bytes(b"native Find SFTP source\n")
    (server / "remote-only.txt").write_bytes(b"native Find remote-only marker\n")
    address = f"sftp://benny@127.0.0.1:22{server}/"
    result = {"passed": False, "appDllSha256": hashlib.sha256(
        app_path.with_name("FileCat.dll").read_bytes()).hexdigest(),
        "fixture": str(root), "sftpAddress": address, "steps": []}
    process = None
    try:
        child_env = os.environ.copy()
        with (root / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(server)],
                                       cwd=root, env=child_env, stdout=log,
                                       stderr=subprocess.STDOUT, start_new_session=True)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
            (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
            if node is not None and node.get_process_id() == process.pid and
            node.get_name() == "FileCat"), None), "owned FileCat", seconds=25)
        locations = wait_for(lambda: [node for node in tree(app)
            if node.get_role_name() == "entry" and node.get_name() == "Location"]
            if len([node for node in tree(app) if node.get_role_name() == "entry"
                    and node.get_name() == "Location"]) == 2 else None,
            "two location fields", seconds=20)
        if not locations[-1].grab_focus() or not locations[-1].get_editable_text().set_text_contents(address):
            raise AssertionError("Could not enter the owned SFTP location")
        sent = subprocess.run(["sudo", "-S", sys.executable, str(key_helper), "enter"],
                              input=sudo_password + "\n", capture_output=True,
                              text=True, timeout=15)
        if sent.returncode:
            raise AssertionError("Physical Enter did not navigate to SFTP")
        password = wait_for(lambda: find(app, "entry", "Password"), "SFTP password", seconds=20)
        if not password.get_editable_text().set_text_contents(sftp_password):
            raise AssertionError("Could not fill the isolated SFTP password")
        action(wait_for(lambda: find(app, "button", "Connect"), "SFTP Connect"), "SFTP Connect")
        wait_for(lambda: any("New server key" in (node.get_name() or "") for node in tree(app)),
                 "new SFTP host-key review", seconds=20)
        independent_keys = runpy.run_path(str(validation / "linux-sftp-replace-fixture.py"))["fingerprints"]()
        shown = {node.get_name() for node in tree(app)}
        matched = sorted(set(independent_keys) & shown)
        if not matched:
            raise AssertionError("The displayed SFTP key does not match guest SSH public keys")
        result["hostKeyMatched"] = matched
        action(wait_for(lambda: find(app, "button", "Connect once"), "one-time SFTP trust"),
               "verified one-time SFTP trust")
        def remote_loaded():
            lists = [node.get_name() for node in tree(app) if node.get_role_name() == "list"]
            rows = [node.get_name() for node in tree(app) if node.get_role_name() == "list item"
                    and (node.get_name() or "").startswith("remote-only.txt,")]
            result["lastLists"] = lists
            result["lastRemoteRows"] = rows
            return lists if rows and any("sftp://" in name for name in lists) else None
        result["sftpLists"] = wait_for(remote_loaded, "SFTP panel", seconds=35)
        result["steps"].append("Connected to owned loopback SFTP directory")

        result["toolbarButtons"] = [node.get_name() for node in tree(app)
                                    if node.get_role_name() == "button"][:80]
        toolbar_find = wait_for(lambda: next((node for node in tree(app)
            if node.get_role_name() == "button" and
            (node.get_name() or "").startswith("Find")), None), "toolbar Find")
        action(toolbar_find, "toolbar Find from SFTP")
        result["afterFindWindows"] = [(node.get_role_name(), node.get_name(), node.get_process_id())
            for node in (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
            if node is not None and node.get_process_id() == process.pid]
        result["afterFindLabels"] = [(node.get_role_name(), node.get_name())
            for node in tree(app) if node.get_role_name() in ("label", "entry")][-80:]
        scope = wait_for(lambda: next((node for node in tree(app)
            if node.get_role_name() == "label" and
            (node.get_name() or "").startswith("Find cannot search")), None),
                         "Find scope guidance", seconds=20)
        look_in = wait_for(lambda: find(app, "entry", "Look in"), "Look in")
        result["findButtons"] = [(node.get_name(), in_find_window(node)) for node in tree(app)
                                 if node.get_role_name() == "button" and
                                 (node.get_name() or "").startswith("Find")]
        find_button = wait_for(lambda: next((node for node in tree(app)
            if node.get_role_name() == "button" and node.get_name() == "Find"
            and in_find_window(node)), None), "Find form action")
        guidance = text_of(scope)
        root_text = text_of(look_in)
        result["find"] = {"guidance": guidance, "lookIn": root_text,
                          "buttonEnabled": find_button.get_state_set().contains(Atspi.StateType.ENABLED)}
        if ("cannot search" not in guidance.lower() or "sftp" not in guidance.lower() or
                root_text or result["find"]["buttonEnabled"]):
            raise AssertionError("Native Find silently substituted a local scope for SFTP")
        result["steps"].append("Find identifies SFTP limitation and requires an explicit local scope")
        if ((source / "unchanged.txt").read_bytes() != b"native Find SFTP source\n" or
                (server / "remote-only.txt").read_bytes() != b"native Find remote-only marker\n"):
            raise AssertionError("Read-only Find scope inspection changed source bytes")
        result["passed"] = True
    except Exception as exc:
        result["error"] = f"{type(exc).__name__}: {exc}"
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=8)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=8)
        report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    raise SystemExit(main(Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])))
