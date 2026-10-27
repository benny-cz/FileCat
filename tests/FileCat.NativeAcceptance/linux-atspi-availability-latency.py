#!/usr/bin/env python3
"""Native Ubuntu large remote listing focus while its owned relay is offline.

Usage: linux-atspi-availability-latency.py APP REPORT.json KEY_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD and FILECAT_NATIVE_SFTP_PASSWORD transiently.
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
from gi.repository import Atspi


class OwnedRelay(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True

    def __init__(self):
        super().__init__(("127.0.0.1", 0), RelayHandler)
        self.sockets = set()
        self.lock = threading.Lock()
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
        raise AssertionError("Use an isolated Git app, owned key helper and fresh receipt")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    sftp_password = os.environ.pop("FILECAT_NATIVE_SFTP_PASSWORD", None)
    if not sudo_password or not sftp_password:
        raise AssertionError("Transient native input and SFTP credentials are required")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Stop another FileCat before this owned test")
    repo = next(parent for parent in app_path.parents if (parent / ".git").exists())
    helpers = runpy.run_path(str(repo / "tests/FileCat.NativeAcceptance/linux-atspi-ftp-large-listing.py"))
    wait_for, tree, press, action, stop, desktop_environment, count_in_status = (
        helpers[name] for name in ("wait_for", "tree", "press", "action", "stop",
                                   "desktop_environment", "count_in_status"))
    fingerprints = runpy.run_path(str(validation / "linux-sftp-replace-fixture.py"))["fingerprints"]

    root = validation / "AvailabilityLatency" / uuid4().hex
    local, remote, data = (root / name for name in ("local", "server", "data"))
    for folder in (local, remote, data):
        folder.mkdir(parents=True)
    sentinel = remote / "remote-0000.txt"
    for i in range(2000):
        (remote / f"remote-{i:04d}.txt").write_bytes(b"sentinel\n" if i == 0 else b"")
    sentinel_hash = hashlib.sha256(sentinel.read_bytes()).hexdigest()
    relay = OwnedRelay()
    relay_thread = threading.Thread(target=relay.serve_forever, daemon=True)
    relay_thread.start()
    address = f"sftp://benny@127.0.0.1:{relay.server_address[1]}{remote}/"
    result = {"passed": False, "stage": "startup", "fixture": str(root),
              "remoteAddress": address,
              "gitHead": subprocess.check_output(["git", "-C", str(repo), "rev-parse", "HEAD"],
                                               text=True).strip(),
              "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest()}
    process = None

    def shown(node):
        return node.get_state_set().contains(Atspi.StateType.SHOWING)

    def find(app, role, name):
        return next((n for n in tree(app) if n.get_role_name() == role and
                     n.get_name() == name and shown(n)), None)

    def listing(app):
        return next((n for n in tree(app) if n.get_role_name() == "list" and
                     (n.get_name() or "").startswith("Panel 2,") and shown(n)), None)

    def focused(app):
        current = listing(app)
        return next((n.get_name() for n in tree(current) if n.get_role_name() == "list item" and
                     n.get_state_set().contains(Atspi.StateType.FOCUSED)), None) if current else None

    def toolbar_state(app):
        values = {}
        for title in ("Copy…", "Move / rename…", "Delete…"):
            button = find(app, "button", title)
            if button is None:
                raise AssertionError(f"The {title} toolbar button is missing")
            values[title] = {"enabled": button.get_state_set().contains(Atspi.StateType.ENABLED),
                             "help": button.get_help_text()}
        return values

    try:
        os.environ.update(desktop_environment())
        env = os.environ.copy()
        runtime = validation / "dotnet-sdk"
        if not (runtime / "dotnet").is_file():
            raise AssertionError("The isolated .NET runtime is missing")
        env["DOTNET_ROOT"] = str(runtime)
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(local), "--right", str(local)],
                                       cwd=root, env=env, stdout=output, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((n for n in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if n.get_process_id() == process.pid and n.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        result["stage"] = "load two-thousand-entry owned SFTP listing"
        fields = wait_for(lambda: [n for n in tree(app) if n.get_role_name() == "entry" and
                           n.get_name() == "Location" and shown(n)], "Location fields")
        field = fields[-1]
        if not field.grab_focus() or not field.get_editable_text().set_text_contents(address):
            raise AssertionError("Could not enter the owned SFTP location")
        press(key_helper, sudo_password, "enter", process.pid)
        password = wait_for(lambda: find(app, "entry", "Password"), "SFTP password")
        if not password.get_editable_text().set_text_contents(sftp_password):
            raise AssertionError("Could not enter the transient SFTP password")
        action(wait_for(lambda: find(app, "button", "Connect"), "SFTP Connect"), "SFTP Connect")
        wait_for(lambda: any("New server key" in (n.get_name() or "") for n in tree(app)),
                 "SFTP host-key decision")
        names = {n.get_name() for n in tree(app)}
        result["matchedHostKeys"] = sorted(set(fingerprints()) & names)
        if not result["matchedHostKeys"]:
            raise AssertionError("The shown host key failed independent fingerprint verification")
        action(wait_for(lambda: find(app, "button", "Connect once"), "host-key consent"),
               "verified one-time host-key consent")
        result["largeList"] = wait_for(lambda: listing(app).get_name()
                                       if listing(app) and str(remote) in listing(app).get_name() and
                                       any(count_in_status(n.get_name() or "") == 2000 for n in tree(app)
                                           if n.get_role_name() == "label") else None,
                                       "complete 2,000-entry remote listing", seconds=60)
        first = wait_for(lambda: next((n for n in tree(listing(app)) if n.get_role_name() == "list item" and
                                      (n.get_name() or "").startswith("remote-0000.txt")), None),
                         "first remote row")
        if not first.grab_focus():
            raise AssertionError("Could not focus the first remote file")
        result["focusBefore"] = focused(app)
        result["toolbarBeforeDrop"] = toolbar_state(app)
        result["relayChunks"] = relay.delayed_chunks
        result["stage"] = "bounded physical focus while owned remote relay is offline"
        relay.drop_sessions()
        relay.shutdown()
        relay.server_close()
        start = time.monotonic()
        press(key_helper, sudo_password, "down-ten", process.pid)
        result["focusAfterDown"] = wait_for(lambda: focused(app) if
                                            (focused(app) or "").startswith("remote-0010.txt") else None,
                                            "tenth remote row after disconnection", seconds=5)
        result["downSeconds"] = round(time.monotonic() - start, 3)
        result["toolbarAfterDown"] = toolbar_state(app)
        start = time.monotonic()
        press(key_helper, sudo_password, "up-ten", process.pid)
        result["focusAfterUp"] = wait_for(lambda: focused(app) if
                                          (focused(app) or "").startswith("remote-0000.txt") else None,
                                          "first remote row after reverse focus", seconds=5)
        result["upSeconds"] = round(time.monotonic() - start, 3)
        result["toolbarAfterUp"] = toolbar_state(app)
        result["sentinelExact"] = hashlib.sha256(sentinel.read_bytes()).hexdigest() == sentinel_hash
        result["localEmpty"] = not any(local.iterdir())
        result["passed"] = bool(result["relayChunks"] > 0 and
                                result["focusBefore"].startswith("remote-0000.txt") and
                                result["downSeconds"] < 8 and result["upSeconds"] < 8 and
                                result["sentinelExact"] and result["localEmpty"] and
                                result["toolbarBeforeDrop"] == result["toolbarAfterDown"] ==
                                result["toolbarAfterUp"])
        result["stage"] = "complete"
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        if process:
            result["appLogTail"] = (root / "app.log").read_text(errors="replace")[-1200:]
            try:
                result["focusAtFailure"] = focused(app)
                result["listAtFailure"] = listing(app).get_name() if listing(app) else None
            except Exception:
                pass
    finally:
        stop(process)
        relay.drop_sessions()
        relay.shutdown()
        relay.server_close()
        relay_thread.join(timeout=5)
        report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        if root.resolve().parent != validation / "AvailabilityLatency":
            raise AssertionError("Fixture cleanup escaped the owned validation root")
        shutil.rmtree(root)
    return result


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    try:
        outcome = run(*(Path(arg) for arg in sys.argv[1:]))
    except Exception as error:
        outcome = {"passed": False, "stage": "harness setup or cleanup",
                   "error": f"{type(error).__name__}: {error}"}
        Path(sys.argv[2]).write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    raise SystemExit(0 if outcome["passed"] else 1)
