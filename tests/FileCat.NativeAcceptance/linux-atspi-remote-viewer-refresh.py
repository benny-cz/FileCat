#!/usr/bin/env python3
"""Exercise delayed SFTP Text viewer Refresh in the isolated Ubuntu desktop.

Usage: linux-atspi-remote-viewer-refresh.py APP REPORT.json KEY_HELPER
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
    app_path, helper = (path.resolve(strict=True) for path in (app_path, helper))
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
    helpers = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))
    wait_for, tree, matching, action, press, stop, desktop_environment = (
        helpers[name] for name in ("wait_for", "tree", "matching", "action", "press", "stop",
                                     "desktop_environment"))
    relay_class = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-navigation-capacity.py"))[
        "DelayedRelay"]
    viewer_id = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-viewer-find.py"))[
        "exact_viewer_window_id"]
    fingerprints = runpy.run_path(str(validation / "linux-sftp-replace-fixture.py"))["fingerprints"]
    root = validation / "RemoteViewerRefresh" / uuid4().hex
    local, remote, data = (root / name for name in ("local", "remote", "data"))
    for folder in (local, remote, data):
        folder.mkdir(parents=True)
    member = remote / "member.txt"
    old, new = b"old remote revision\n", b"new remote revision\n" + b"N" * 8192
    member.write_bytes(old)
    relay = relay_class()
    relay.delay_seconds = 0.005
    relay_thread = threading.Thread(target=relay.serve_forever, daemon=True)
    relay_thread.start()
    address = f"sftp://benny@127.0.0.1:{relay.server_address[1]}{remote}/"
    result = {"passed": False, "stage": "startup", "gitHead": subprocess.check_output(
        ["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip(),
        "appDllSha256": hashlib.sha256(app_path.with_name("FileCat.dll").read_bytes()).hexdigest(),
        "fixture": str(root), "remoteAddress": address,
        "relayDelaySeconds": relay.delay_seconds,
        "oldSha256": hashlib.sha256(old).hexdigest(), "newSha256": hashlib.sha256(new).hexdigest()}
    process = None

    def find(root_node, role, name):
        return next((node for node in tree(root_node) if node.get_role_name() == role and
                     node.get_name() == name), None)

    def text_of(document):
        interface = document.get_text_iface() if document is not None else None
        return Atspi.Text.get_text(interface, 0, -1) if interface is not None else ""

    def banners(viewer):
        return [node.get_name() for node in tree(viewer) if node.get_role_name() == "label" and
                node.get_state_set().contains(Atspi.StateType.SHOWING) and
                ((node.get_name() or "").startswith("Refresh failed:") or
                 (node.get_name() or "").startswith("File changed"))]

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
        result["stage"] = "connect through owned delayed SFTP relay"
        locations = wait_for(lambda: matching(app, "entry", "Location")
                             if len(matching(app, "entry", "Location")) == 2 else None,
                             "both Location fields")
        field = locations[-1]
        if not field.grab_focus() or not field.get_editable_text().set_text_contents(address):
            raise AssertionError("Could not enter the owned SFTP location")
        press(helper, sudo_password, "enter", process.pid)
        password = wait_for(lambda: find(app, "entry", "Password"), "SFTP password")
        if not password.get_editable_text().set_text_contents(sftp_password):
            raise AssertionError("Could not fill isolated SFTP password")
        action(wait_for(lambda: find(app, "button", "Connect"), "SFTP Connect"), "SFTP Connect")
        wait_for(lambda: any("New server key" in (node.get_name() or "") for node in tree(app)
                             if node.get_state_set().contains(Atspi.StateType.SHOWING)),
                 "new SFTP host-key review")
        shown = {node.get_name() for node in tree(app)
                 if node.get_state_set().contains(Atspi.StateType.SHOWING)}
        result["matchedHostKeys"] = sorted(set(fingerprints()) & shown)
        if not result["matchedHostKeys"]:
            raise AssertionError("The server key differs from independent OpenSSH fingerprints")
        action(wait_for(lambda: find(app, "button", "Connect once"), "one-time trust"),
               "verified one-time trust")
        row = wait_for(lambda: next((node for node in tree(app) if
                       node.get_role_name() == "list item" and
                       (node.get_name() or "").startswith("member.txt,")), None),
                       "SFTP member row", seconds=25)
        if not row.grab_focus():
            raise AssertionError("Could not focus the SFTP member")
        press(helper, sudo_password, "f3", process.pid)
        title = "member.txt — FileCat Viewer"
        viewer = wait_for(lambda: find(app, "frame", title), "native SFTP Text viewer", seconds=25)
        initial_viewer_id = viewer_id(title, process.pid)
        document = wait_for(lambda: find(viewer, "entry", "Text viewer"),
                            "accessible Text document")
        result["initialText"] = wait_for(lambda: value if "old remote revision" in
                                         (value := text_of(document)) else None,
                                         "original remote text", seconds=25)
        result["stage"] = "delayed changed-source refresh"
        member.write_bytes(new)
        result["beforeRefreshText"] = text_of(document)
        if "old remote revision" not in result["beforeRefreshText"]:
            raise AssertionError("Remote text changed before explicit Refresh")
        refresh = wait_for(lambda: find(viewer, "push button", "Refresh viewer content") or
                           find(viewer, "button", "Refresh viewer content"), "viewer Refresh")
        if refresh.get_n_actions() < 1 or not refresh.do_action(0):
            raise AssertionError("Refresh action is unavailable")
        # The relay throttles SFTP read chunks; the old snapshot must remain
        # while replacement bytes are still being prepared.
        wait_for(lambda: not refresh.get_state_set().contains(Atspi.StateType.ENABLED),
                 "pending refresh", seconds=5)
        result["duringRefreshText"] = text_of(document)
        result["relayChunksDuringRefresh"] = relay.delayed_chunks
        if "old remote revision" not in result["duringRefreshText"]:
            raise AssertionError("Delayed refresh discarded the old view before completion")
        try:
            result["refreshedText"] = wait_for(lambda: value if "new remote revision" in
                                               (value := text_of(document)) else None,
                                               "new remote text", seconds=35)
        except AssertionError:
            result["textAfterTimeout"] = text_of(document)
            result["bannersAfterTimeout"] = banners(viewer)
            result["visibleLabelsAfterTimeout"] = [node.get_name() for node in tree(viewer)
                if node.get_role_name() == "label" and
                node.get_state_set().contains(Atspi.StateType.SHOWING)]
            result["refreshEnabledAfterTimeout"] = refresh.get_state_set().contains(Atspi.StateType.ENABLED)
            result["relayDelayedChunks"] = relay.delayed_chunks
            raise
        result["sameViewer"] = viewer_id(title, process.pid) == initial_viewer_id
        result["afterRefreshBanners"] = banners(viewer)
        if not result["sameViewer"] or result["afterRefreshBanners"]:
            raise AssertionError("Successful remote Refresh replaced the window or left a warning")
        result["stage"] = "unavailable remote reopen rollback"
        relay.drop_sessions()
        relay.shutdown()
        relay.server_close()
        if refresh.get_n_actions() < 1 or not refresh.do_action(0):
            raise AssertionError("Refresh could not be retried after relay disconnection")
        result["failedRefreshBanner"] = wait_for(lambda: banners(viewer),
                                                 "failed-refresh banner", seconds=25)
        result["retainedAfterFailure"] = text_of(document)
        if "new remote revision" not in result["retainedAfterFailure"]:
            raise AssertionError("Failed remote reopen discarded the last good snapshot")
        result["sameViewerAfterFailure"] = viewer_id(title, process.pid) == initial_viewer_id
        result["sourceBytesExact"] = hashlib.sha256(member.read_bytes()).hexdigest() == result["newSha256"]
        result["targetEmpty"] = not any(local.iterdir())
        result["relayDelayedChunks"] = relay.delayed_chunks
        result["passed"] = (result["sameViewerAfterFailure"] and result["sourceBytesExact"] and
                            result["targetEmpty"] and result["relayDelayedChunks"] > 0)
        result["stage"] = "complete"
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
    finally:
        stop(process)
        app_log = root / "app.log"
        if app_log.exists():
            result["appLogTail"] = app_log.read_text(encoding="utf-8", errors="replace")[-2000:]
        relay.drop_sessions()
        relay.shutdown()
        relay.server_close()
        relay_thread.join(timeout=5)
        shutil.rmtree(root)
        report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    raise SystemExit(run(*(Path(arg) for arg in sys.argv[1:])))
