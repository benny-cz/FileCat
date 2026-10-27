#!/usr/bin/env python3
"""Native delayed SFTP listing acceptance with owned files and a loopback SSH relay.

Usage: linux-atspi-sftp-delayed-listing.py APP REPORT.json KEY_HELPER
Set FILECAT_NATIVE_SUDO_PASSWORD and FILECAT_NATIVE_SFTP_PASSWORD transiently.
The relay slows only server-to-client packets; it never interprets SSH data.
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

HERE = Path(__file__).resolve().parent
base = runpy.run_path(str(HERE / "linux-atspi-ftp-large-listing.py"))
wait_for, tree, matching = (base[name] for name in ("wait_for", "tree", "matching"))
action, snapshot, press, stop, desktop_environment = (base[name] for name in
                                                      ("action", "snapshot", "press", "stop", "desktop_environment"))
COUNT = 2000
FIRST = "native-remote-0000.txt"
LAST = "native-remote-1999.txt"
MARKER = b"FileCat delayed SFTP action 1999\n"


class Relay(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True

    def __init__(self):
        super().__init__(("127.0.0.1", 0), RelayHandler)
        self.server_bytes = 0
        self.delayed_chunks = 0
        self.lock = threading.Lock()


class RelayHandler(socketserver.BaseRequestHandler):
    def handle(self):
        import select
        try:
            upstream = socket.create_connection(("127.0.0.1", 22), timeout=5)
        except OSError:
            return
        with upstream:
            sockets = (self.request, upstream)
            while True:
                try:
                    ready, _, _ = select.select(sockets, (), (), 15)
                    if not ready:
                        continue
                    for source in ready:
                        # A small bounded chunk makes the asynchronous listing observable.
                        block = source.recv(512)
                        if not block:
                            return
                        if source is upstream:
                            time.sleep(0.03)
                            with self.server.lock:
                                self.server.server_bytes += len(block)
                                self.server.delayed_chunks += 1
                        (self.request if source is upstream else upstream).sendall(block)
                except (OSError, ValueError):
                    return


def find(app, role, name):
    return next(iter(matching(app, role, name)), None)


def row_named(app, name):
    return next((node for node in tree(app) if node.get_role_name() == "list item"
                 and (node.get_name() or "").startswith(name + ",")), None)


def details_text(app):
    field = find(app, "text", "Full details of the reviewed item") or find(
        app, "entry", "Full details of the reviewed item")
    if field is None:
        return None
    value = field.get_text_iface()
    return Atspi.Text.get_text(value, 0, -1) if value else None


def run(app_path, report_path, key_helper):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, key_helper = (path.resolve(strict=True) for path in (app_path, key_helper))
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            key_helper.name != "linux-uinput-key.py" or validation not in key_helper.parents or
            report_path.parent != validation or report_path.exists()):
        raise AssertionError("Use the isolated Git app and keyboard helper with a fresh report")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    sftp_password = os.environ.pop("FILECAT_NATIVE_SFTP_PASSWORD", None)
    if not sudo_password or not sftp_password:
        raise AssertionError("Transient input and SFTP credentials are required")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"],
                      capture_output=True).returncode == 0:
        raise AssertionError("Close other FileCat processes before the isolated check")
    root = validation / "DelayedSftpAtSpiListing" / uuid4().hex
    source, server_dir, target, data = (root / name for name in
                                        ("source", "server", "target", "data"))
    for folder in (source, server_dir, target, data):
        folder.mkdir(parents=True)
    (source / "source.txt").write_bytes(b"untouched local source\n")
    for number in range(COUNT):
        (server_dir / f"native-remote-{number:04d}.txt").write_bytes(
            MARKER if number == COUNT - 1 else b"")
    independent_keys = runpy.run_path(str(validation / "linux-sftp-replace-fixture.py"))["fingerprints"]()
    relay = Relay()
    relay_thread = threading.Thread(target=relay.serve_forever, daemon=True)
    relay_thread.start()
    address = f"sftp://benny@127.0.0.1:{relay.server_address[1]}{server_dir}/"
    result = {"passed": False, "stage": "startup", "address": address,
              "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
              "delaySecondsPerServerChunk": 0.03, "relayChunkBytes": 512}
    process = None
    try:
        os.environ.update(desktop_environment())
        environment = os.environ.copy()
        runtime = validation / "dotnet-sdk"
        if not (runtime / "dotnet").is_file():
            raise AssertionError("Isolated .NET runtime is missing")
        environment["DOTNET_ROOT"] = str(runtime)
        with (root / "app.log").open("wb") as output:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(target)],
                                       cwd=root, env=environment, stdout=output, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        locations = wait_for(lambda: matching(app, "entry", "Location")
                             if len(matching(app, "entry", "Location")) == 2 else None,
                             "both location fields")
        if not locations[-1].grab_focus() or not locations[-1].get_editable_text().set_text_contents(address):
            raise AssertionError("Could not enter the owned delayed SFTP address")
        press(key_helper, sudo_password, "enter", process.pid)
        password = wait_for(lambda: find(app, "entry", "Password"), "SFTP password field")
        if not password.get_editable_text().set_text_contents(sftp_password):
            raise AssertionError("Could not enter the owned SFTP password")
        action(wait_for(lambda: find(app, "button", "Connect"), "SFTP Connect"), "SFTP Connect")
        wait_for(lambda: any("New server key" in (node.get_name() or "") for node in tree(app)
                             if node.get_state_set().contains(Atspi.StateType.SHOWING)),
                 "new SFTP host-key decision")
        shown = {node.get_name() for node in tree(app)
                 if node.get_state_set().contains(Atspi.StateType.SHOWING)}
        matched = sorted(set(independent_keys) & shown)
        if not matched:
            raise AssertionError("SFTP key did not match independently read SSH public keys")
        result["hostKeyMatched"] = matched
        action(wait_for(lambda: find(app, "button", "Connect once"), "one-time host-key consent"),
               "verified one-time host-key consent")
        result["stage"] = "pending listing"
        pending = []
        until = time.monotonic() + 90
        complete = None
        while time.monotonic() < until:
            try:
                value = snapshot(app)
            except (AssertionError, GLib.Error) as error:
                if ("Expected one right-panel provider list" not in str(error) and
                        "No handler registered" not in str(error)):
                    raise
                time.sleep(0.2)
                continue
            if value["visited"] > 800 or value["rowCount"] > 120 or value["querySeconds"] > 5:
                raise AssertionError(f"Unbounded AT-SPI listing: {value}")
            if address.rstrip("/") in value["listName"] and value["fileCount"] == COUNT:
                complete = value
                break
            if len(pending) < 3:
                pending.append(value)
            time.sleep(0.2)
        if complete is None or not any(name.startswith(FIRST + ",") for name in
                                       complete["visibleNames"]) or len(pending) < 2:
            raise AssertionError(f"Delayed route lacked pending and complete bounded states: {pending}, {complete}")
        result.update({"pending": pending, "complete": complete, "stage": "focus and mark"})
        first = wait_for(lambda: row_named(app, FIRST), "first remote row")
        if not first.grab_focus():
            raise AssertionError("First remote row did not take focus")
        wait_for(lambda: any(name.startswith(FIRST + ",") for name in snapshot(app)["focused"]),
                 "first remote row focus")
        press(key_helper, sudo_password, "insert", process.pid)
        wait_for(lambda: row_named(app, FIRST) is not None and
                 row_named(app, FIRST).get_state_set().contains(Atspi.StateType.SELECTED),
                 "first remote mark")
        press(key_helper, sudo_password, "end", process.pid)
        def focused_last():
            value = snapshot(app)
            return value if any(name.startswith(LAST + ",") for name in value["focused"]) else None
        after_end = wait_for(focused_last, "last unmarked remote focus", seconds=20)
        if after_end["visited"] > 800 or after_end["rowCount"] > 120:
            raise AssertionError("End exposed an unbounded accessibility tree")
        result.update({"afterEnd": after_end, "stage": "review marked origin"})
        press(key_helper, sudo_password, "ctrl+shift+p", process.pid)
        search = wait_for(lambda: find(app, "entry", "Search commands"), "command search")
        if not search.get_editable_text().set_text_contents("Review marked items"):
            raise AssertionError("Could not enter Review marked items")
        wait_for(lambda: any("Review marked items" in (node.get_name() or "")
                             for node in tree(app) if node.get_role_name() == "list item"),
                 "Review marked items command")
        press(key_helper, sudo_password, "enter", process.pid)
        def marked_origin():
            value = details_text(app)
            return value if value and FIRST in value and str(server_dir) in value else None
        detail = wait_for(marked_origin, "marked first file and original SFTP location in review")
        result["reviewDetails"] = detail
        close = wait_for(lambda: find(app, "button", "Close"), "review Close")
        action(close, "review Close")
        result["stage"] = "real View action"
        # The first file remains marked; View must still use the focused last file.
        last = wait_for(lambda: row_named(app, LAST), "last remote row")
        if not last.grab_focus():
            raise AssertionError("Last remote row did not regain focus")
        action(wait_for(lambda: find(app, "button", "View"), "View command"), "View command")
        viewer = wait_for(lambda: find(app, "frame", f"{LAST} — FileCat Viewer"),
                          "last SFTP member viewer", seconds=50)
        action(wait_for(lambda: next((node for node in tree(viewer)
               if node.get_role_name() == "toggle button" and node.get_name() == "Text"), None),
               "viewer Text mode"), "viewer Text mode")
        wait_for(lambda: find(viewer, "label", "Mode: Text"), "remote Text viewer")
        result["viewerTitle"] = viewer.get_name()
        result["stage"] = "fixture integrity"
        files = list(server_dir.iterdir())
        if (len(files) != COUNT or any(file.stat().st_size for file in files if file.name != LAST) or
                (server_dir / LAST).read_bytes() != MARKER or
                (source / "source.txt").read_bytes() != b"untouched local source\n" or
                list(target.iterdir())):
            raise AssertionError("The native journey changed an owned source or target")
        result.update({"passed": True, "serverFilesUnchanged": True, "targetEmpty": True,
                       "relayServerBytes": relay.server_bytes, "relayDelayedChunks": relay.delayed_chunks,
                       "stage": "complete"})
        return result
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
        result["appExit"] = process.poll() if process is not None else None
        if (root / "app.log").exists():
            result["appLogTail"] = (root / "app.log").read_text(
                encoding="utf-8", errors="replace")[-1500:]
        result["relayServerBytes"] = relay.server_bytes
        result["relayDelayedChunks"] = relay.delayed_chunks
        return result
    finally:
        stop(process)
        relay.shutdown()
        relay.server_close()
        relay_thread.join(timeout=5)
        shutil.rmtree(root)


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    report = Path(sys.argv[2]).resolve()
    outcome = run(Path(sys.argv[1]), report, Path(sys.argv[3]))
    report.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in outcome.items()
                      if key not in ("pending", "complete", "afterEnd", "reviewDetails")}))
    raise SystemExit(0 if outcome["passed"] else 1)
