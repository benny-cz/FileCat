#!/usr/bin/env python3
"""Hear FTP/FTPS/SFTP replacement risk through Orca in an isolated Ubuntu desktop.

Usage: linux-atspi-ftp-warning-speech.py APP_PATH REPORT.json KEY_HELPER FTP_HELPER ftp|ftpes|sftp
The caller supplies FILECAT_NATIVE_SUDO_PASSWORD only for the owned uinput key
helper. The secret is removed from child environments before FileCat, Orca or
the loopback server starts. SFTP also requires FILECAT_NATIVE_SFTP_PASSWORD;
its host key must match independently read SSH server public keys. Cancel and
Skip preserve both exact fixture files.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import subprocess
import sys
import time
import traceback
from uuid import uuid4

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi


WARNING_START = "Another app can change this remote file"
WARNING_PARTS = ("after FileCat's final check", "newer change could be lost", "Choose Keep both")


def descendants(node):
    yield node
    for index in range(node.get_child_count()):
        yield from descendants(node.get_child_at_index(index))


def matching(node, role, name):
    return [child for child in descendants(node)
            if child.get_role_name() == role and child.get_name() == name
            and child.get_state_set().contains(Atspi.StateType.SHOWING)]


def wait_for(check, description, seconds=20):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        value = check()
        if value:
            return value
        time.sleep(0.15)
    raise AssertionError(f"Timed out waiting for {description}")


def action(node, description):
    if node is None or node.get_n_actions() < 1 or not node.do_action(0):
        raise AssertionError(f"Cannot invoke native {description}")


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def windows_of(pid):
    listing = subprocess.run(["/usr/bin/wmctrl", "-lp"], check=True,
                             capture_output=True, text=True, timeout=5)
    return [line.split()[0] for line in listing.stdout.splitlines()
            if len(line.split()) > 2 and line.split()[2] == str(pid)]


def main():
    if len(sys.argv) != 6 or sys.argv[5] not in ("ftp", "ftpes", "sftp"):
        raise SystemExit(__doc__)
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = Path(sys.argv[1]).resolve(strict=True)
    report_path = Path(sys.argv[2]).resolve()
    key_helper = Path(sys.argv[3]).resolve(strict=True)
    ftp_helper = Path(sys.argv[4]).resolve(strict=True)
    protocol = sys.argv[5]
    if (app_path.name != "FileCat" or validation not in app_path.parents or
            report_path.parent != validation or
            key_helper.name != "linux-uinput-key.py" or validation not in key_helper.parents or
            ftp_helper.name != "win-ftp-owned-stage.py" or validation not in ftp_helper.parents):
        raise SystemExit("Use only the isolated Git build and validation helpers")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    if not sudo_password:
        raise SystemExit("Transient physical-input authorization is required")
    sftp_password = os.environ.pop("FILECAT_NATIVE_SFTP_PASSWORD", None)
    if protocol == "sftp" and not sftp_password:
        raise SystemExit("SFTP requires the transient owned test-server password")
    if subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", "orca"],
                      capture_output=True).returncode == 0:
        raise SystemExit("Another Orca instance is running")
    os.environ.setdefault("DISPLAY", ":0")
    os.environ.setdefault("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")
    os.environ.setdefault("DBUS_SESSION_BUS_ADDRESS", f"unix:path={os.environ['XDG_RUNTIME_DIR']}/bus")
    runtime = validation / "dotnet-sdk"
    if not (runtime / "dotnet").is_file():
        raise SystemExit("The isolated Ubuntu .NET runtime is missing")
    os.environ["DOTNET_ROOT"] = str(runtime)

    fixture = validation / "RemoteWarningOrca" / uuid4().hex
    source_dir, server_dir, data = (fixture / name for name in ("source", "server", "data"))
    for folder in (source_dir, server_dir, data):
        folder.mkdir(parents=True)
    source = source_dir / "target.txt"
    target = server_dir / "target.txt"
    source.write_bytes(b"owned incoming content\n")
    target.write_bytes(b"owned existing remote content\n")
    source_hash, target_hash = sha256(source), sha256(target)
    ready = fixture / "server.ready"
    shared = runpy.run_path(str(Path(__file__).with_name("linux-orca-speech.py")))
    speech = shared["SpeechLog"](fixture / "orca-debug.log")
    report = {"passed": False, "protocol": protocol, "appDllSha256": sha256(app_path.with_name("FileCat.dll")),
              "fixture": str(fixture), "sourceSha256": source_hash, "targetSha256": target_hash,
              "warningSpoken": False, "conflictWarningSpoken": False, "steps": []}
    server = orca = process = None
    try:
        if protocol == "sftp":
            independent_keys = runpy.run_path(str(Path(__file__).with_name(
                "linux-sftp-replace-fixture.py")))["fingerprints"]()
            address = f"sftp://benny@127.0.0.1{server_dir}/"
        else:
            server_python = validation / "ftps-venv" / "bin" / "python"
            if not server_python.is_file():
                raise AssertionError("The owned FTP fixture environment is missing")
            with (fixture / "server.log").open("wb") as log:
                server = subprocess.Popen([str(server_python), str(ftp_helper),
                                           "serve-tls" if protocol == "ftpes" else "serve",
                                           str(server_dir), str(ready)],
                                          cwd=fixture, stdout=log, stderr=subprocess.STDOUT)
            port = wait_for(lambda: ready.read_text(encoding="ascii").strip()
                            if ready.is_file() and server.poll() is None else None,
                            "owned loopback FTP port")
            if not re.fullmatch(r"\d{1,5}", port) or not 1 <= int(port) <= 65535:
                raise AssertionError("The loopback FTP fixture returned an invalid port")
            address = f"{protocol}://tester@127.0.0.1:{port}/"

        with (fixture / "orca.err").open("wb") as error_log:
            orca = subprocess.Popen(["/usr/bin/python3", "-c", shared["ORCA_WRAPPER"], str(speech.path)],
                                    stdout=subprocess.DEVNULL, stderr=error_log, env=os.environ.copy())
        wait_for(lambda: any("Screen reader on" in phrase for phrase in speech.take()),
                 "owned Orca startup", seconds=25)
        with (fixture / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(source_dir), "--right", str(server_dir)],
                                       cwd=fixture, stdout=log, stderr=subprocess.STDOUT, env=os.environ.copy())
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((candidate for candidate in
            (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
            if candidate.get_name() == "FileCat" and
            matching(candidate, "list", f"Panel 1, source, {source_dir}")), None),
            "isolated FileCat source panel", seconds=35)

        def activate():
            owned = windows_of(process.pid)
            if len(owned) != 1:
                raise AssertionError(f"Expected one owned FileCat window, found {owned}")
            subprocess.run(["/usr/bin/wmctrl", "-ia", owned[0]], check=True, timeout=5)
            active = subprocess.run(["/usr/bin/xdotool", "getactivewindow", "getwindowpid"],
                                    check=True, capture_output=True, text=True, timeout=5).stdout.strip()
            if active != str(process.pid):
                raise AssertionError("The owned FileCat window did not take the foreground")

        def send(chord):
            activate()
            sent = subprocess.run(["sudo", "-S", sys.executable, str(key_helper), chord],
                                  input=sudo_password + "\n", capture_output=True, text=True, timeout=15)
            if sent.returncode:
                raise AssertionError(f"Physical {chord} failed: {sent.stderr[-300:]}")

        def button(name):
            return wait_for(lambda: next(iter(matching(app, "button", name)), None), f"visible {name} button")

        def review_button(name):
            def current():
                for candidate in matching(app, "button", name):
                    parent = candidate
                    for _ in range(8):
                        parent = parent.get_parent() if parent is not None else None
                        if parent is None:
                            break
                        if parent.get_name().endswith(" dialog"):
                            return candidate
                return None
            return wait_for(current, f"{name} action in the owned review")

        def warning():
            return next((node for node in descendants(app)
                         if node.get_role_name() == "label" and
                         node.get_name().startswith(WARNING_START) and
                         node.get_state_set().contains(Atspi.StateType.SHOWING)), None)

        def spoken_warning(label):
            heard, deadline = [], time.monotonic() + 8
            while time.monotonic() < deadline:
                heard.extend(speech.take())
                if all(part in " ".join(heard) for part in WARNING_PARTS):
                    break
                time.sleep(0.15)
            passed = all(part in " ".join(heard) for part in WARNING_PARTS)
            report["steps"].append({"step": label, "heard": heard, "passed": passed})
            if not passed:
                raise AssertionError(f"{label}: Orca did not speak the complete risk: {heard!r}")
            return heard

        def bounds(node):
            area = node.get_component().get_extents(Atspi.CoordType.SCREEN)
            return [area.x, area.y, area.width, area.height]

        def in_frame(area, frame):
            return (area[2] > 0 and area[3] > 0 and area[0] >= frame[0] and
                    area[1] >= frame[1] and area[0] + area[2] <= frame[0] + frame[2] and
                    area[1] + area[3] <= frame[1] + frame[3])

        activate()
        display = subprocess.run(["/usr/bin/gdctl", "show"], check=True,
                                 capture_output=True, text=True, timeout=5).stdout
        scale_match = re.search(r"\bScale: ([0-9.]+)", display)
        scale = float(scale_match.group(1)) if scale_match else None
        if scale not in (1.0, 1.5, 2.0):
            raise AssertionError(f"Unexpected Ubuntu display scale: {scale}")
        report["displayScale"] = scale
        owned_window = windows_of(process.pid)[0]
        subprocess.run(["/usr/bin/wmctrl", "-ir", owned_window, "-e",
                        f"0,80,80,{round(720 * scale)},{round(540 * scale)}"],
                       check=True, timeout=5)
        time.sleep(0.5)
        report["frames"] = [(node.get_role_name(), node.get_name()) for node in descendants(app)
                            if node.get_role_name() in ("frame", "window")]
        frame = next((node for node in descendants(app) if node.get_role_name() in ("frame", "window") and
                      any(str(source_dir) in child.get_name() for child in descendants(node)
                          if child.get_role_name() == "list")), None)
        if frame is None:
            raise AssertionError("The owned FileCat window frame is not exposed")
        locations = matching(app, "entry", "Location")
        if len(locations) != 2 or not locations[-1].get_editable_text().set_text_contents(address):
            raise AssertionError("Could not set the exact remote panel location")
        if not locations[-1].grab_focus():
            raise AssertionError("Remote location did not take focus")
        send("enter")
        if protocol == "ftp":
            action(button("Connect unencrypted"), "explicit plaintext consent")
            report["steps"].append("Explicit FTP plaintext consent")
        password = wait_for(lambda: next(iter(matching(app, "entry", "Password")), None),
                            "remote password entry")
        if not password.get_editable_text().set_text_contents(
                sftp_password if protocol == "sftp" else "secret-pass"):
            raise AssertionError("Cannot enter the owned FTP fixture password")
        action(button("Connect"), "remote Connect")
        if protocol == "sftp":
            wait_for(lambda: any("New server key" in node.get_name() for node in descendants(app)
                        if node.get_state_set().contains(Atspi.StateType.SHOWING)),
                     "new SFTP server key decision")
            shown_keys = {node.get_name() for node in descendants(app)
                          if node.get_state_set().contains(Atspi.StateType.SHOWING)}
            matched = sorted(set(independent_keys) & shown_keys)
            if not matched:
                raise AssertionError("The SFTP host key did not match independent SSH server public keys")
            report["hostKeyMatched"] = matched
            action(button("Connect once"), "checked SFTP host key")
            report["steps"].append("New SFTP host key matched an independent server public key")
        if protocol == "ftpes":
            action(button("Connect once"), "self-signed FTPS certificate consent")
            report["steps"].append("Fresh FTPS certificate accepted for this connection only")
        wait_for(lambda: len(matching(app, "list item", "target.txt")) >= 2,
                 "source and remote target listings", seconds=55)
        report["panelLists"] = [node.get_name() for node in descendants(app)
                                if node.get_role_name() == "list" and str(source_dir) in node.get_name()]
        source_list = next((node for node in descendants(app)
                            if node.get_role_name() == "list" and str(source_dir) in node.get_name()), None)
        if source_list is None:
            raise AssertionError("The exact source panel list disappeared after remote sign-in")
        row = next(iter(matching(source_list, "list item", "target.txt")), None)
        if row is None or not row.grab_focus():
            raise AssertionError("Could not focus the exact source row")
        action(button("Copy…"), "Copy command")
        wait_for(lambda: matching(app, "entry", "Destination"), "remote Copy destination")
        action(wait_for(lambda: next(iter(matching(app, "toggle button", "Options")), None),
                        "Copy Options"), "Copy Options")
        conflict = wait_for(lambda: next((node for node in descendants(app)
            if node.get_role_name() == "combo box" and node.get_name() == "If an item already exists:"), None),
            "conflict policy")
        if not conflict.grab_focus():
            raise AssertionError("Conflict policy did not take focus")
        speech.take()
        for key in ("home", "down", "down"):
            send(key)
        shown = wait_for(warning, "Copy final-check warning")
        if not all(part in shown.get_name() for part in WARNING_PARTS):
            raise AssertionError("The visible Copy warning omitted the complete risk")
        copy_frame, copy_warning, copy_cancel = bounds(frame), bounds(shown), bounds(review_button("Cancel"))
        report["copyBounds"] = {"frame": copy_frame, "warning": copy_warning, "cancel": copy_cancel}
        if not in_frame(copy_warning, copy_frame) or not in_frame(copy_cancel, copy_frame):
            raise AssertionError("The scaled narrow Copy review clipped its warning or Cancel choice")
        spoken_warning("Copy Replace warning")
        report["warningSpoken"] = True
        for key in ("home", "down", "down", "down", "down"):
            send(key)
        wait_for(lambda: not warning(), "warning to clear for Keep both")
        action(review_button("Cancel"), "Cancel Copy review")

        action(button("Copy…"), "second Copy command")
        wait_for(lambda: matching(app, "entry", "Destination"), "second Copy destination")
        action(review_button("Copy"), "Ask Copy approval")
        review = wait_for(lambda: next((node for node in descendants(app)
            if node.get_role_name() == "button" and node.get_name().startswith("Review decision") and
            node.get_state_set().contains(Atspi.StateType.SHOWING)), None),
            "waiting operation review", seconds=35)
        speech.take()
        action(review, "per-item review decision")
        shown = wait_for(warning, "per-item final-check warning")
        if not all(part in shown.get_name() for part in WARNING_PARTS):
            raise AssertionError("The visible per-item warning omitted the complete risk")
        conflict_frame, conflict_warning, conflict_skip = bounds(frame), bounds(shown), bounds(review_button("Skip"))
        report["conflictBounds"] = {"frame": conflict_frame, "warning": conflict_warning, "skip": conflict_skip}
        if not in_frame(conflict_warning, conflict_frame) or not in_frame(conflict_skip, conflict_frame):
            raise AssertionError("The scaled narrow conflict review clipped its warning or Skip choice")
        spoken_warning("Per-item conflict warning")
        report["conflictWarningSpoken"] = True
        action(review_button("Skip"), "safe Skip decision")
        wait_for(lambda: not warning(), "per-item review to close")

        report["sourceUnchanged"] = sha256(source) == source_hash
        report["targetUnchanged"] = sha256(target) == target_hash
        report["manifest"] = sorted(path.name for path in server_dir.iterdir())
        if not report["sourceUnchanged"] or not report["targetUnchanged"] or report["manifest"] != ["target.txt"]:
            raise AssertionError("Cancel/Skip changed the owned source or remote target")
        report["passed"] = True
    except Exception as exc:
        report["error"] = f"{type(exc).__name__}: {exc}"
        report["traceback"] = traceback.format_exc()
    finally:
        for owned in (process, orca, server):
            if owned is not None and owned.poll() is None:
                owned.terminate()
                try:
                    owned.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    owned.kill()
        report["speechLog"] = str(speech.path)
        report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
