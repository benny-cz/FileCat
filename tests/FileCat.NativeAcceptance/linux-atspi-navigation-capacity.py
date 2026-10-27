#!/usr/bin/env python3
"""Native Ubuntu delayed SFTP navigation and distinct-mount capacity probe.

Usage: linux-atspi-navigation-capacity.py APP REPORT.json KEY_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD and FILECAT_NATIVE_SFTP_PASSWORD transiently.
Run only in the isolated Ubuntu VMware guest. All files are test-owned.
"""

import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import socket
import socketserver
import subprocess
import sys
import threading
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi, GLib


class DelayedRelay(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True

    def __init__(self):
        super().__init__(("127.0.0.1", 0), RelayHandler)
        self.lock = threading.Lock()
        self.sockets = set()
        self.delayed_chunks = 0

    def drop_sessions(self):
        with self.lock:
            sockets = list(self.sockets)
        for connection in sockets:
            try:
                connection.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
            try:
                connection.close()
            except OSError:
                pass


class RelayHandler(socketserver.BaseRequestHandler):
    def handle(self):
        import select
        try:
            upstream = socket.create_connection(("127.0.0.1", 22), timeout=5)
        except OSError:
            return
        with upstream:
            with self.server.lock:
                self.server.sockets.update((self.request, upstream))
            try:
                while True:
                    ready, _, _ = select.select((self.request, upstream), (), (), 15)
                    for source in ready:
                        block = source.recv(512)
                        if not block:
                            return
                        if source is upstream:
                            time.sleep(0.03)
                            with self.server.lock:
                                self.server.delayed_chunks += 1
                        (self.request if source is upstream else upstream).sendall(block)
            except (OSError, ValueError):
                return
            finally:
                with self.server.lock:
                    self.server.sockets.discard(self.request)
                    self.server.sockets.discard(upstream)


def run(app_path: Path, report: Path, key_helper: Path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, key_helper = (p.resolve(strict=True) for p in (app_path, key_helper))
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            key_helper != validation / "linux-uinput-key.py" or
            report.parent != validation or report.exists()):
        raise AssertionError("Use the isolated Git app, keyboard helper and fresh report")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    sftp_password = os.environ.pop("FILECAT_NATIVE_SFTP_PASSWORD", None)
    if not sudo_password or not sftp_password:
        raise AssertionError("Transient input and SFTP credentials are required")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Close other FileCat processes before this isolated check")

    repo = next(parent for parent in app_path.parents if (parent / ".git").exists())
    helpers = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))
    wait_for, tree, matching, action, press, stop, desktop_environment, count_in_status = (
        helpers[name] for name in ("wait_for", "tree", "matching", "action", "press", "stop",
                              "desktop_environment", "count_in_status"))
    fingerprints = runpy.run_path(str(validation / "linux-sftp-replace-fixture.py"))["fingerprints"]

    root = validation / "NavigationCapacity" / uuid4().hex
    local, remote, data = (root / name for name in ("local", "server", "data"))
    ram = Path("/dev/shm") / ("FileCatNavigation-" + uuid4().hex)
    for folder in (local, remote, data, ram):
        folder.mkdir(parents=True)
    (local / "home-sentinel.txt").write_bytes(b"current local home mount\n")
    (ram / "ram-sentinel.txt").write_bytes(b"distinct tmpfs mount\n")
    for index in range(2000):
        (remote / f"remote-{index:04d}.txt").write_bytes(b"")
    local_hash = hashlib.sha256((local / "home-sentinel.txt").read_bytes()).hexdigest()
    ram_hash = hashlib.sha256((ram / "ram-sentinel.txt").read_bytes()).hexdigest()
    relay = DelayedRelay()
    relay_thread = threading.Thread(target=relay.serve_forever, daemon=True)
    relay_thread.start()
    address = f"sftp://benny@127.0.0.1:{relay.server_address[1]}{remote}/"
    result = {"passed": False, "stage": "startup", "gitHead": subprocess.check_output(
        ["git", "-C", str(repo), "rev-parse", "HEAD"], text=True).strip(),
        "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
        "fixture": str(root), "ramFixture": str(ram), "remoteAddress": address}
    process = None

    def find(app, role, name):
        return next(iter(matching(app, role, name)), None)

    def right_list(app):
        return next((node for node in tree(app) if node.get_role_name() == "list" and
                     (node.get_name() or "").startswith("Panel 2,")), None)

    def right_free(app):
        candidates = []
        for node in tree(app):
            name = node.get_name() or ""
            if node.get_role_name() == "label" and name.endswith(" free"):
                try:
                    x = node.get_component().get_extents(Atspi.CoordType.SCREEN).x
                    candidates.append((x, name))
                except (GLib.Error, AttributeError):
                    pass
        return max(candidates)[1] if len(candidates) >= 2 else None

    def navigate(app, path):
        locations = wait_for(lambda: matching(app, "entry", "Location")
                             if len(matching(app, "entry", "Location")) == 2 else None,
                             "both native Location fields")
        field = locations[-1]
        if not field.grab_focus() or not field.get_editable_text().set_text_contents(str(path)):
            raise AssertionError("Could not enter the owned navigation destination")
        press(key_helper, sudo_password, "enter", process.pid)

    try:
        os.environ.update(desktop_environment())
        env = os.environ.copy()
        runtime = validation / "dotnet-sdk"
        if not (runtime / "dotnet").is_file():
            raise AssertionError("The isolated .NET runtime is missing")
        env["DOTNET_ROOT"] = str(runtime)
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(local), "--right", str(ram)],
                                       cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        result["stage"] = "distinct initial mount capacity"
        result["ramFree"] = wait_for(lambda: right_free(app), "right tmpfs free-space label")
        result["ramDevice"] = os.stat(ram).st_dev
        result["localDevice"] = os.stat(local).st_dev
        if result["ramDevice"] == result["localDevice"]:
            raise AssertionError("The two owned local directories do not cross a mount boundary")

        result["stage"] = "delayed remote preparation"
        navigate(app, address)
        password = wait_for(lambda: find(app, "entry", "Password"), "SFTP password field")
        if not password.get_editable_text().set_text_contents(sftp_password):
            raise AssertionError("Could not enter the owned SFTP password")
        action(wait_for(lambda: find(app, "button", "Connect"), "SFTP Connect"), "SFTP Connect")
        wait_for(lambda: any("New server key" in (node.get_name() or "") for node in tree(app)
                             if node.get_state_set().contains(Atspi.StateType.SHOWING)),
                 "new SFTP host-key decision")
        names = {node.get_name() for node in tree(app)
                 if node.get_state_set().contains(Atspi.StateType.SHOWING)}
        result["matchedHostKeys"] = sorted(set(fingerprints()) & names)
        if not result["matchedHostKeys"]:
            raise AssertionError("The shown host key did not match independent OpenSSH fingerprints")
        action(wait_for(lambda: find(app, "button", "Connect once"), "host-key consent"),
               "verified one-time host-key consent")
        result["stage"] = "two newer locations before remote listing"
        result["remotePending"] = (right_list(app).get_name() if right_list(app) else None)
        result["remoteCompleteBeforeNewIntent"] = any(
            count_in_status(node.get_name() or "") == 2000 for node in tree(app)
            if node.get_role_name() == "label")
        if result["remoteCompleteBeforeNewIntent"]:
            raise AssertionError("The delayed remote listing finished before the newer navigation intents")
        navigate(app, ram)
        navigate(app, local)
        result["finalList"] = wait_for(lambda: right_list(app).get_name()
                                       if right_list(app) and str(local) in right_list(app).get_name() else None,
                                       "current local panel after two navigation intents")
        result["localFree"] = wait_for(lambda: right_free(app)
                                       if right_free(app) and right_free(app) != result["ramFree"] else None,
                                       "current filesystem capacity after tmpfs")
        result["delayedChunksBeforeDrop"] = relay.delayed_chunks
        relay.drop_sessions()
        relay.shutdown()
        relay.server_close()
        time.sleep(2)
        result["afterDisconnectList"] = right_list(app).get_name() if right_list(app) else None
        result["afterDisconnectFree"] = right_free(app)
        result["visibleErrorNames"] = [node.get_name() for node in tree(app)
                                      if "connection" in (node.get_name() or "").casefold() and
                                      node.get_state_set().contains(Atspi.StateType.SHOWING)][:12]
        result["filesExact"] = (hashlib.sha256((local / "home-sentinel.txt").read_bytes()).hexdigest() == local_hash and
                                hashlib.sha256((ram / "ram-sentinel.txt").read_bytes()).hexdigest() == ram_hash)
        result["passed"] = (result["finalList"] == result["afterDisconnectList"] and
                            result["localFree"] == result["afterDisconnectFree"] and
                            result["localFree"] != result["ramFree"] and result["filesExact"] and
                            not result["visibleErrorNames"] and result["delayedChunksBeforeDrop"] > 0)
        result["stage"] = "complete"
        return result
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        result["appExit"] = process.poll() if process else None
        return result
    finally:
        stop(process)
        relay.drop_sessions()
        relay.shutdown()
        relay.server_close()
        relay_thread.join(timeout=5)
        shutil.rmtree(root)
        shutil.rmtree(ram)


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    report_path = Path(sys.argv[2]).resolve()
    outcome = run(Path(sys.argv[1]), report_path, Path(sys.argv[3]))
    report_path.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in outcome.items()
                      if key not in ("visibleErrorNames", "matchedHostKeys")}))
    raise SystemExit(0 if outcome["passed"] else 1)
