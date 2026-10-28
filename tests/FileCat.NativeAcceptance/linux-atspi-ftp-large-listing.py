#!/usr/bin/env python3
"""Measure bounded native AT-SPI work for a 2,000-file FTP/FTPS/SFTP listing.

Usage: linux-atspi-ftp-large-listing.py APP REPORT.json KEY_HELPER FTP_HELPER ftp|ftpes|ftps|sftp
The runner owns its loopback server, FileCat process and disposable fixture.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import shutil
import stat
import subprocess
import sys
import time
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi, GLib


COUNT = 2000
LIMIT = 800


def wait_for(check, description, seconds=35):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        try:
            value = check()
        except GLib.Error as error:
            # An old AT-SPI object path can disappear while the provider's
            # listing replaces the local panel. Resolve the new tree next tick.
            if "No handler registered" not in str(error):
                raise
            value = None
        except AssertionError as error:
            if "Expected one right-panel provider list" not in str(error):
                raise
            value = None
        if value:
            return value
        time.sleep(0.2)
    raise AssertionError(f"Timed out waiting for {description}")


def tree(root):
    pending = [root]
    visited = 0
    while pending:
        node = pending.pop()
        visited += 1
        if visited > LIMIT:
            raise AssertionError(f"AT-SPI enumerated more than {LIMIT} nodes")
        yield node
        children = node.get_child_count()
        if children > LIMIT:
            raise AssertionError(f"One AT-SPI node exposed {children} children")
        # A child can go between the count and the fetch while the window changes (a dialog opens): skip it.
        pending.extend(child for child in (node.get_child_at_index(i) for i in range(children - 1, -1, -1))
                       if child is not None)


def matching(root, role, name):
    return [node for node in tree(root) if node.get_role_name() == role and node.get_name() == name
            and node.get_state_set().contains(Atspi.StateType.SHOWING)]


def action(node, description):
    if node is None or node.get_n_actions() < 1 or not node.do_action(0):
        raise AssertionError(f"The native {description} action was unavailable")


def count_in_status(text):
    match = re.search(r"([\d\s,.\u00a0]+)\s+files\b", text)
    return int(re.sub(r"\D", "", match.group(1))) if match else None


def snapshot(app):
    started = time.perf_counter()
    nodes = list(tree(app))
    lists = [node for node in nodes if node.get_role_name() == "list" and
             node.get_name().startswith("Panel 2,")]
    if len(lists) != 1:
        raise AssertionError(f"Expected one right-panel provider list, found {[node.get_name() for node in lists]}")
    rows = [node for node in tree(lists[0]) if node.get_role_name() == "list item"]
    statuses = [(node.get_name(), count_in_status(node.get_name())) for node in nodes
                if node.get_role_name() == "label"]
    status = next(((name, count) for name, count in statuses if count == COUNT), None)
    return {"listName": lists[0].get_name(), "status": status[0] if status else None,
            "fileCount": status[1] if status else None, "rowCount": len(rows),
            "visited": len(nodes), "querySeconds": round(time.perf_counter() - started, 3),
            "focused": [row.get_name() for row in rows
                        if row.get_state_set().contains(Atspi.StateType.FOCUSED)],
            "visibleNames": [row.get_name() for row in rows]}


def activate(pid):
    listing = subprocess.run(["/usr/bin/wmctrl", "-lp"], capture_output=True, text=True, check=True, timeout=5)
    windows = [line.split()[0] for line in listing.stdout.splitlines()
               if len(line.split()) > 2 and line.split()[2] == str(pid)]
    if len(windows) != 1:
        raise AssertionError(f"Expected one test-owned FileCat window, found {windows}")
    subprocess.run(["/usr/bin/wmctrl", "-ia", windows[0]], check=True, timeout=5)


def press(helper, password, chord, pid):
    activate(pid)
    sent = subprocess.run(["sudo", "-S", "/usr/bin/python3", str(helper), chord],
                          input=password + "\n", capture_output=True, text=True, timeout=15)
    if sent.returncode:
        raise AssertionError(f"Physical {chord} failed: {sent.stderr[-300:]}")


def stop(process):
    if process is not None and process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=10)


def desktop_environment():
    """Use only the logged-in user's own Xwayland authority after a VM restart."""
    uid = os.getuid()
    runtime = Path(f"/run/user/{uid}").resolve(strict=True)
    candidates = []
    for entry in Path("/proc").iterdir():
        if not entry.name.isdigit():
            continue
        try:
            if entry.stat().st_uid != uid or (entry / "comm").read_text().strip() != "Xwayland":
                continue
            raw = (entry / "environ").read_bytes()
            if len(raw) > 65536:
                continue
            values = dict(part.split("=", 1) for part in raw.decode(errors="replace").split("\0")
                          if "=" in part)
            display = values.get("DISPLAY", "")
            authority = Path(values.get("XAUTHORITY", "")).resolve(strict=True)
            info = authority.stat()
            if (re.fullmatch(r":[0-9]+", display) and authority.parent == runtime and
                    info.st_uid == uid and stat.S_ISREG(info.st_mode) and info.st_size <= 65536):
                candidates.append((display, str(authority)))
        except (OSError, ValueError):
            continue
    if len(candidates) != 1:
        raise AssertionError(f"Expected one owned Xwayland display, found {len(candidates)}")
    display, authority = candidates[0]
    return {"DISPLAY": display, "XAUTHORITY": authority,
            "XDG_RUNTIME_DIR": str(runtime),
            "DBUS_SESSION_BUS_ADDRESS": f"unix:path={runtime}/bus"}


def run(app_path, report_path, helper, ftp_helper, protocol):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path, helper, ftp_helper = (path.resolve(strict=True)
                                    for path in (app_path, helper, ftp_helper))
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            helper.name != "linux-uinput-key.py" or validation not in helper.parents or
            ftp_helper.name != "win-ftp-owned-stage.py" or validation not in ftp_helper.parents or
            report_path.parent != validation or report_path.exists()):
        raise AssertionError("Use the isolated Git app and helpers with a fresh validation report")
    password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    sftp_password = os.environ.pop("FILECAT_NATIVE_SFTP_PASSWORD", None)
    if not password:
        raise AssertionError("The disposable physical keyboard requires a transient credential")
    if protocol == "sftp" and not sftp_password:
        raise AssertionError("The owned SFTP login needs a transient credential")
    venv = validation / "ftps-venv" / "bin" / "python"
    if not venv.is_file():
        raise AssertionError("The isolated loopback FTP fixture runtime is missing")
    if subprocess.run(["pgrep", "-u", str(os.getuid()), "-x", "FileCat"], capture_output=True).returncode == 0:
        raise AssertionError("Close other FileCat processes before the isolated provider test")
    root = validation / "LargeFtpAtSpiListing" / uuid4().hex
    source, server_dir, data = (root / name for name in ("source", "server", "data"))
    for folder in (source, server_dir, data):
        folder.mkdir(parents=True)
    original = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    for name, content in original.items():
        (source / name).write_bytes(content)
    for index in range(COUNT):
        (server_dir / f"native-remote-{index:04d}.txt").write_bytes(b"")
    ready = root / "server.ready"
    server = process = None
    result = {"passed": False, "fixture": str(root), "generatedFiles": COUNT,
              "appDllSha256": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest()}
    try:
        if protocol == "sftp":
            sftp_helper = validation / "linux-sftp-replace-fixture.py"
            if not sftp_helper.is_file():
                raise AssertionError("The independent SSH host-key fixture helper is missing")
            independent_keys = runpy.run_path(str(sftp_helper))["fingerprints"]()
            address = f"sftp://benny@127.0.0.1{server_dir}/"
        else:
            with (root / "server.log").open("wb") as log:
                server = subprocess.Popen([str(venv), str(ftp_helper),
                                           {"ftpes": "serve-tls", "ftps": "serve-implicit-tls"}.get(protocol, "serve"),
                                           str(server_dir), str(ready)],
                                          cwd=root, stdout=log, stderr=subprocess.STDOUT)
            port = wait_for(lambda: ready.read_text(encoding="ascii").strip()
                            if ready.is_file() and server.poll() is None else None, "owned FTP port")
            if not re.fullmatch(r"\d{1,5}", port) or not 1 <= int(port) <= 65535:
                raise AssertionError("The owned FTP port was invalid")
            address = f"{protocol}://tester@127.0.0.1:{port}/"
        os.environ.update(desktop_environment())
        env = os.environ.copy()
        with (root / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source), "--right", str(server_dir)],
                                       cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        try:
            app = wait_for(lambda: next((node for node in
                           (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                           if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                           "the exact test-owned FileCat")
        except AssertionError as error:
            # Keep a bounded startup diagnosis before the owned fixture is removed.
            log_tail = (root / "app.log").read_text(encoding="utf-8", errors="replace")[-1200:]
            raise AssertionError(f"{error}; exit={process.poll()}; appLogTail={log_tail!r}") from error
        wait_for(lambda: snapshot(app)["fileCount"] == COUNT,
                 "complete initial local right-panel listing")
        locations = wait_for(lambda: matching(app, "entry", "Location")
                             if len(matching(app, "entry", "Location")) == 2 else None,
                             "both location entries")
        if not locations[-1].grab_focus() or not locations[-1].get_editable_text().set_text_contents(address):
            raise AssertionError("The right panel did not accept the exact owned FTP address")
        press(helper, password, "enter", process.pid)
        if protocol == "ftp":
            action(wait_for(lambda: next(iter(matching(app, "button", "Connect unencrypted")), None),
                            "explicit plaintext consent"), "plaintext consent")
        password_entry = wait_for(lambda: next(iter(matching(app, "entry", "Password")), None),
                                  "owned FTP password entry")
        if not password_entry.get_editable_text().set_text_contents(
                sftp_password if protocol == "sftp" else "secret-pass"):
            raise AssertionError("The owned remote password was not entered")
        action(wait_for(lambda: next(iter(matching(app, "button", "Connect")), None),
                        "remote Connect button"), "remote Connect")
        if protocol == "sftp":
            wait_for(lambda: any("New server key" in node.get_name() for node in tree(app)
                                 if node.get_state_set().contains(Atspi.StateType.SHOWING)),
                     "new SFTP host-key decision")
            shown = {node.get_name() for node in tree(app)
                     if node.get_state_set().contains(Atspi.StateType.SHOWING)}
            matched = sorted(set(independent_keys) & shown)
            if not matched:
                raise AssertionError("The shown SFTP key did not match independently read SSH public keys")
            result["hostKeyMatched"] = matched
            action(wait_for(lambda: next(iter(matching(app, "button", "Connect once")), None),
                            "verified SFTP host-key consent"), "one-time SFTP consent")
        if protocol in ("ftpes", "ftps"):
            action(wait_for(lambda: next(iter(matching(app, "button", "Connect once")), None),
                            "fresh FTPS certificate consent"), "one-time FTPS consent")
        first_name = "native-remote-0000.txt"
        address_prefix = address.rstrip("/")
        first = wait_for(lambda: (value if address_prefix in value["listName"] and
                          value["fileCount"] == COUNT and
                          first_name in value["visibleNames"] else None)
                         if (value := snapshot(app)) else None, "complete remote listing", seconds=70)
        if first["rowCount"] > 120 or first["visited"] > LIMIT or first["querySeconds"] > 5:
            raise AssertionError(f"The provider listing was not bounded: {first}")
        row = next((node for node in tree(app) if node.get_role_name() == "list item"
                    and node.get_name() == first_name), None)
        if row is None or not row.grab_focus():
            raise AssertionError("The first remote row did not take native AT-SPI focus")
        wait_for(lambda: first_name in snapshot(app)["focused"], "first remote focus")
        started = time.perf_counter()
        press(helper, password, "end", process.pid)
        last_name = f"native-remote-{COUNT - 1:04d}.txt"
        last = wait_for(lambda: (value if last_name in value["focused"] else None)
                        if (value := snapshot(app)) else None, "last remote focus", seconds=15)
        jump_seconds = round(time.perf_counter() - started, 3)
        if last["rowCount"] > 120 or last["visited"] > LIMIT or last["querySeconds"] > 5 or jump_seconds > 15:
            raise AssertionError(f"The provider End route was not bounded: {last}")
        if ({name: (source / name).read_bytes() for name in original} != original or
                any(path.stat().st_size != 0 for path in server_dir.iterdir()) or
                len(list(server_dir.iterdir())) != COUNT):
            raise AssertionError("The provider journey changed its isolated source or server files")
        result.update({"passed": True, "protocol": protocol, "loaded": first, "afterEnd": last,
                       "endKeySeconds": jump_seconds, "sourceBytesUnchanged": True,
                       "serverFilesUnchanged": True})
        return result
    finally:
        stop(process)
        stop(server)
        shutil.rmtree(root)


if __name__ == "__main__":
    if len(sys.argv) != 6 or sys.argv[5] not in ("ftp", "ftpes", "ftps", "sftp"):
        raise SystemExit(__doc__)
    report = Path(sys.argv[2]).resolve()
    try:
        outcome = run(Path(sys.argv[1]), report, Path(sys.argv[3]), Path(sys.argv[4]), sys.argv[5])
    except Exception as error:
        outcome = {"passed": False, "error": f"{type(error).__name__}: {error}"}
    report.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in outcome.items()
                      if key not in ("loaded", "afterEnd")}))
    raise SystemExit(0 if outcome["passed"] else 1)
