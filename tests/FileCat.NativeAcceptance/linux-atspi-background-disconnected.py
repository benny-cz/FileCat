#!/usr/bin/env python3
"""Check a disconnected SFTP panel's native whitespace actions.

Usage: linux-atspi-background-disconnected.py APP REPORT.json KEY_HELPER POINTER_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD and FILECAT_NATIVE_SFTP_PASSWORD transiently.
The relay, files, profile and FileCat child are test-owned inside Ubuntu VMware.
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


def run(app_path: Path, report: Path, key_helper: Path, pointer_helper: Path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, key_helper, pointer_helper = (
        path.resolve(strict=True) for path in (app_path, key_helper, pointer_helper))
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            (key_helper, pointer_helper) != (validation / "linux-uinput-key.py",
                                             validation / "linux-uinput-pointer.py") or
            report.parent != validation or report.exists()):
        raise AssertionError("Use an isolated Git app, owned input and fresh validation report")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    sftp_password = os.environ.pop("FILECAT_NATIVE_SFTP_PASSWORD", None)
    if not sudo_password or not sftp_password:
        raise AssertionError("Transient input and SFTP credentials are required")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Close other FileCat instances before this isolated check")

    repo = next(parent for parent in app_path.parents if (parent / ".git").exists())
    helpers = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))
    wait_for, tree, matching, action, press, stop, desktop_environment = (
        helpers[name] for name in ("wait_for", "tree", "matching", "action", "press", "stop",
                                     "desktop_environment"))
    relay_class = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-navigation-capacity.py"))[
        "DelayedRelay"]
    mouse_class = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-hex-resize.py"))[
        "PhysicalMouse"]
    fingerprints = runpy.run_path(str(validation / "linux-sftp-replace-fixture.py"))["fingerprints"]
    root = validation / "BackgroundDisconnected" / uuid4().hex
    local, remote, data = (root / name for name in ("local", "remote", "data"))
    for folder in (local, remote, data):
        folder.mkdir(parents=True)
    (local / "unchanged.txt").write_bytes(b"owned disconnected-menu local source\n")
    (remote / "member.txt").write_bytes(b"owned disconnected-menu remote member\n")
    local_hash = hashlib.sha256((local / "unchanged.txt").read_bytes()).hexdigest()
    remote_hash = hashlib.sha256((remote / "member.txt").read_bytes()).hexdigest()
    relay = relay_class()
    relay_thread = threading.Thread(target=relay.serve_forever, daemon=True)
    relay_thread.start()
    address = f"sftp://benny@127.0.0.1:{relay.server_address[1]}{remote}/"
    result = {"passed": False, "stage": "startup", "gitHead": subprocess.check_output(
        ["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip(),
        "appDllSha256": hashlib.sha256(app_path.with_name("FileCat.dll").read_bytes()).hexdigest(),
        "fixture": str(root), "remoteAddress": address, "localSha256": local_hash,
        "remoteSha256": remote_hash}
    process = None
    mouse = None

    def find(app, role, name):
        return next(iter(matching(app, role, name)), None)

    def right_list(app):
        return next((node for node in tree(app) if node.get_role_name() == "list" and
                     (node.get_name() or "").startswith("Panel 2,")), None)

    def menu_items(app):
        return [(node.get_name(), node.get_state_set().contains(Atspi.StateType.ENABLED),
                 node.get_description() or "") for node in tree(app)
                if node.get_role_name() == "menu item" and
                node.get_state_set().contains(Atspi.StateType.SHOWING)]

    try:
        os.environ.update(desktop_environment())
        child_env = os.environ.copy()
        child_env["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(local), "--right", str(local)],
                                       cwd=root, env=child_env, stdout=output,
                                       stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        result["stage"] = "connect through owned relay"
        locations = wait_for(lambda: matching(app, "entry", "Location")
                             if len(matching(app, "entry", "Location")) == 2 else None,
                             "both Location fields")
        field = locations[-1]
        if not field.grab_focus() or not field.get_editable_text().set_text_contents(address):
            raise AssertionError("Could not enter the owned SFTP destination")
        press(key_helper, sudo_password, "enter", process.pid)
        password = wait_for(lambda: find(app, "entry", "Password"), "SFTP password")
        if not password.get_editable_text().set_text_contents(sftp_password):
            raise AssertionError("Could not fill the isolated SFTP password")
        action(wait_for(lambda: find(app, "button", "Connect"), "SFTP Connect"), "SFTP Connect")
        wait_for(lambda: any("New server key" in (node.get_name() or "") for node in tree(app)
                             if node.get_state_set().contains(Atspi.StateType.SHOWING)),
                 "new SFTP host-key review")
        shown = {node.get_name() for node in tree(app)
                 if node.get_state_set().contains(Atspi.StateType.SHOWING)}
        matched = sorted(set(fingerprints()) & shown)
        if not matched:
            raise AssertionError("The host key differs from independent OpenSSH fingerprints")
        result["matchedHostKeys"] = matched
        action(wait_for(lambda: find(app, "button", "Connect once"), "one-time trust"),
               "verified one-time trust")
        connected = wait_for(lambda: right_list(app) if right_list(app) and
                             address.rstrip("/") in right_list(app).get_name() and
                             any((node.get_name() or "").startswith("member.txt,")
                                 for node in tree(right_list(app))) else None,
                             "connected one-member SFTP panel")
        result["connectedList"] = connected.get_name()
        result["stage"] = "disconnect and fail refresh"
        relay.drop_sessions()
        relay.shutdown()
        relay.server_close()
        if not connected.grab_focus():
            raise AssertionError("Could not focus the connected remote listing")
        press(key_helper, sudo_password, "ctrl+r", process.pid)
        disconnected = wait_for(lambda: right_list(app) if right_list(app) and
                                (right_list(app).get_description() or "").startswith("Refresh failed.")
                                else None, "remote refresh failure", seconds=25)
        result["disconnectedList"] = disconnected.get_name()
        result["disconnectedDescription"] = disconnected.get_description()
        result["stage"] = "physical background context"
        bounds = disconnected.get_component().get_extents(Atspi.CoordType.SCREEN)
        rows = [node for node in tree(disconnected) if node.get_role_name() == "list item" and
                (node.get_name() or "").startswith("member.txt,")]
        if len(rows) != 1 or bounds.width < 200 or bounds.height < 180:
            raise AssertionError("The retained remote snapshot lacks a blank-space target")
        member_bounds = rows[0].get_component().get_extents(Atspi.CoordType.SCREEN)
        x, y = round(bounds.x + 80), round(bounds.y + bounds.height - 32)
        if y < member_bounds.y + member_bounds.height + 24:
            raise AssertionError("The background pointer would intersect the member row")
        mouse = mouse_class(pointer_helper, sudo_password, 2.0)
        result["pointer"] = mouse.align(x, y)
        mouse.send("RIGHT_DOWN")
        time.sleep(0.1)
        mouse.send("RIGHT_UP")
        items = wait_for(lambda: menu_items(app) if any(
            name == "Copy location name" for name, _, _ in menu_items(app)) else None,
            "disconnected location menu")
        result["menu"] = items
        by_name = {name: (enabled, help_text) for name, enabled, help_text in items}
        for name in ("Paste items or go to pasted path", "Create folder…"):
            enabled, help_text = by_name[name]
            if enabled or "Reread" not in help_text:
                raise AssertionError(f"{name} was offered without an offline reason: {help_text!r}")
        if not by_name["Reread"][0] or not by_name["Copy location name"][0]:
            raise AssertionError("The disconnected panel lost its safe recovery/location actions")
        if any(name in by_name for name in ("Delete…", "Move…", "Rename…")):
            raise AssertionError("The location menu exposed a destructive item action")
        press(key_helper, sudo_password, "escape", process.pid)
        result["bytesExact"] = (hashlib.sha256((local / "unchanged.txt").read_bytes()).hexdigest() == local_hash and
                                hashlib.sha256((remote / "member.txt").read_bytes()).hexdigest() == remote_hash)
        if not result["bytesExact"]:
            raise AssertionError("The disconnected menu changed an owned source")
        result["passed"] = True
        result["stage"] = "complete"
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
    finally:
        if mouse is not None:
            mouse.close()
        stop(process)
        relay.drop_sessions()
        relay.shutdown()
        relay.server_close()
        relay_thread.join(timeout=5)
        shutil.rmtree(root)
        report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) != 5:
        raise SystemExit(__doc__)
    raise SystemExit(run(*(Path(arg) for arg in sys.argv[1:])))
