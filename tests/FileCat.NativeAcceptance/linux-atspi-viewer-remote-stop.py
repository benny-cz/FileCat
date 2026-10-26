#!/usr/bin/env python3
"""Exercise native Find ownership and Stop over a test-owned loopback SFTP file.

Usage: linux-atspi-viewer-remote-stop.py APP REPORT.json UINPUT_KEY_HELPER [--orca-error]
Run as the logged-in Ubuntu user with FILECAT_NATIVE_SUDO_PASSWORD and
FILECAT_NATIVE_SFTP_PASSWORD set for this isolated VM. No server files outside
the new FileCatValidation fixture are changed. --orca-error requires actual
speech of Stop and a provider error after intentionally truncating that fixture.
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
remote = runpy.run_path(str(HERE / "linux-atspi-ftp-large-listing.py"))
viewer_helpers = runpy.run_path(str(HERE / "linux-atspi-viewer-find.py"))
fingerprints = runpy.run_path(str(HERE / "linux-sftp-replace-fixture.py"))["fingerprints"]
wait_for = remote["wait_for"]
matching = remote["matching"]
tree = remote["tree"]
action = remote["action"]
desktop_environment = remote["desktop_environment"]
find = viewer_helpers["find"]
descendants = viewer_helpers["descendants"]

LENGTH = 2 * 1024 * 1024 * 1024
NAME = "remote-search.bin"


def intact(path):
    if path.stat().st_size != LENGTH:
        return False
    with path.open("rb") as source:
        for offset in (0, LENGTH // 2, LENGTH - 32):
            source.seek(offset)
            if source.read(32) != bytes(32):
                return False
    return True


def visible(viewer, role, name):
    return find(viewer, role, name)


def search_retired(viewer, retired_query):
    names = [node.get_name() or "" for node in descendants(viewer)
             if node.get_role_name() == "label"]
    return (not any(name.startswith("Searching ") or retired_query in name for name in names)
            and visible(viewer, "button", "Stop search") is None)


def send_enter(key_helper, password):
    sent = subprocess.run(["sudo", "-S", "/usr/bin/python3", str(key_helper), "enter"],
                          input=password + "\n", capture_output=True, text=True, timeout=15)
    if sent.returncode:
        raise AssertionError(f"The isolated native Enter input failed: {sent.stderr[-300:]}")


def expect_stop(viewer, description):
    try:
        return wait_for(lambda: visible(viewer, "button", "Stop search"), description, seconds=8)
    except AssertionError as error:
        labels = [node.get_name() or "" for node in descendants(viewer)
                  if node.get_role_name() == "label"]
        raise AssertionError(f"{error}; viewer labels={labels[-18:]!r}") from error


def hear(speech, orca, fragment, description):
    heard = []
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        if orca.poll() is not None:
            raise AssertionError("The owned Orca exited during " + description)
        heard.extend(speech.take())
        if any(fragment in phrase.casefold() for phrase in heard):
            return heard
        time.sleep(0.15)
    raise AssertionError(f"Orca did not speak {description}: {heard[:25]!r}; log={speech.path}")


def run(app_path, key_helper, sudo_password, sftp_password, orca_error):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    app_path = app_path.resolve(strict=True)
    key_helper = key_helper.resolve(strict=True)
    if app_path.name != "FileCat" or validation not in app_path.parents or \
            key_helper != HERE / "linux-uinput-key.py":
        raise AssertionError("Use the Git-built app and tracked key helper in the isolated VM")
    root = validation / "SearchStopRemote" / uuid4().hex
    server, local, data = (root / name for name in ("server", "local", "data"))
    for folder in (server, local, data):
        folder.mkdir(parents=True)
    source = server / NAME
    with source.open("xb") as output:
        output.truncate(LENGTH)
    if not intact(source):
        raise AssertionError("The owned sparse SFTP fixture is invalid")
    address = f"sftp://benny@127.0.0.1{server}/"
    title = f"{NAME} — FileCat Viewer"
    env = os.environ.copy()
    env.update(desktop_environment())
    env["DOTNET_ROOT"] = str(validation / "dotnet-sdk")
    env.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    env.pop("FILECAT_NATIVE_SFTP_PASSWORD", None)
    process = None
    orca = None
    speech = None
    viewer = None
    app = None
    observed = []
    try:
        if orca_error:
            prior = subprocess.run(["/usr/bin/pgrep", "-u", str(os.getuid()), "-x", "orca"],
                                   capture_output=True, timeout=5)
            if prior.returncode == 0:
                raise AssertionError("An unrelated Orca instance is already running")
            speech_source = runpy.run_path(str(HERE / "linux-orca-speech.py"))
            speech = speech_source["SpeechLog"](root / "orca-debug.log")
            orca_env = {**env, "GDK_BACKEND": "wayland", "WAYLAND_DISPLAY": "wayland-0"}
            with (root / "orca.err").open("wb") as error_log:
                orca = subprocess.Popen(["/usr/bin/python3", "-c", speech_source["ORCA_WRAPPER"],
                                         str(speech.path)], env=orca_env,
                                        stdout=subprocess.DEVNULL, stderr=error_log)
            hear(speech, orca, "screen reader on", "Orca startup")
        with (root / "app.log").open("wb") as log:
            process = subprocess.Popen([str(app_path), "--new-instance", "--data", str(data),
                                        "--left", str(local), "--right", str(local)],
                                       cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
        desktop = Atspi.get_desktop(0)
        app = wait_for(lambda: next((node for node in
                       (desktop.get_child_at_index(i) for i in range(desktop.get_child_count()))
                       if node.get_process_id() == process.pid and node.get_name() == "FileCat"), None),
                       "test-owned FileCat")
        locations = wait_for(lambda: matching(app, "entry", "Location")
                             if len(matching(app, "entry", "Location")) == 2 else None,
                             "both location fields")
        if not locations[-1].grab_focus() or not locations[-1].get_editable_text().set_text_contents(address):
            raise AssertionError("Could not enter the exact test-owned SFTP address")
        send_enter(key_helper, sudo_password)
        password_entry = wait_for(lambda: next(iter(matching(app, "entry", "Password")), None),
                                  "SFTP password field")
        if not password_entry.get_editable_text().set_text_contents(sftp_password):
            raise AssertionError("Could not enter the SFTP password")
        action(wait_for(lambda: next(iter(matching(app, "button", "Connect")), None),
                        "SFTP Connect"), "SFTP Connect")
        known_keys = set(fingerprints())
        shown = wait_for(lambda: ({node.get_name() for node in tree(app)
                         if node.get_state_set().contains(Atspi.StateType.SHOWING)}
                         if any("New server key" in (node.get_name() or "") for node in tree(app))
                         else None), "new host key")
        if not known_keys.intersection(shown):
            raise AssertionError("The displayed SFTP key did not match the server's public key")
        action(wait_for(lambda: next(iter(matching(app, "button", "Connect once")), None),
                        "verified host-key decision"), "verified host-key decision")
        panel = wait_for(lambda: next((node for node in tree(app)
                         if node.get_role_name() == "list" and
                         (node.get_name() or "").startswith("Panel 2,") and
                         str(server) in (node.get_name() or "")), None),
                         "exact SFTP panel", seconds=60)
        row = wait_for(lambda: next((node for node in descendants(panel)
                       if node.get_role_name() == "list item" and
                       (node.get_name() or "").startswith(NAME + ", file,")), None),
                       "exact SFTP file row", seconds=40)
        if not row.grab_focus():
            raise AssertionError("The remote file row could not take focus")
        action(wait_for(lambda: next(iter(matching(app, "button", "View")), None),
                        "View command"), "View command")
        viewer = wait_for(lambda: find(app, "frame", title), "native SFTP viewer", seconds=40)
        action(wait_for(lambda: visible(viewer, "toggle button", "Text"), "Text mode"),
               "Text mode")
        wait_for(lambda: visible(viewer, "label", "Mode: Text"), "Text mode status")
        action(visible(viewer, "button", "Show or hide viewer Find bar"), "Find bar")
        query = wait_for(lambda: visible(viewer, "entry", "Find in file"), "Find query")

        if orca_error:
            if not query.get_editable_text().set_text_contents("REMOTE_STOP_ABSENT"):
                raise AssertionError("Could not set the owned Stop query")
            action(visible(viewer, "button", "Find next match"), "Find next")
            stop = expect_stop(viewer, "spoken remote Stop")
            speech.take()
            action(stop, "Stop search")
            wait_for(lambda: visible(viewer, "label", "Search canceled."),
                     "visible spoken-Stop outcome")
            spoken_stop = hear(speech, orca, "search canceled", "Stop")
            if not intact(source):
                raise AssertionError("Stop changed the test-owned remote source")
            if not query.get_editable_text().set_text_contents("REMOTE_ERROR_ABSENT"):
                raise AssertionError("Could not set the owned error query")
            action(visible(viewer, "button", "Find next match"), "Find next")
            expect_stop(viewer, "held remote read before failure")
            speech.take()
            with source.open("r+b") as changing:
                changing.truncate(0)
            error = wait_for(lambda: next((node.get_name() for node in descendants(viewer)
                             if node.get_role_name() == "label" and
                             (node.get_name() or "").startswith("Search could not finish:")), None),
                             "live provider-error status", seconds=30)
            spoken_error = hear(speech, orca, "search could not finish",
                                "remote provider error")
            labels = [node.get_name() or "" for node in descendants(viewer)
                      if node.get_role_name() == "label"]
            if any("0 matches" in label for label in labels):
                raise AssertionError("Failed remote search claimed a complete negative count")
            if source.stat().st_size != 0 or sorted(path.name for path in server.iterdir()) != [NAME]:
                raise AssertionError("Provider-error fixture did not retain its deliberate truncation")
            return {"passed": True, "build": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
                    "source": str(source), "sourceLengthBeforeFailure": LENGTH,
                    "sourceLengthAfterFailure": 0, "address": address,
                    "spokenStop": spoken_stop, "visibleProviderError": error,
                    "spokenProviderError": spoken_error, "falseZeroCount": False,
                    "nativeBackend": "Ubuntu Wayland AT-SPI plus Orca over loopback OpenSSH SFTP"}

        for first, replacement, transition in (("REMOTE_OLD_ABSENT", "REMOTE_NEW_ABSENT", "query"),
                                               ("REMOTE_NEW_ABSENT", "REMOTE_HEX_ABSENT", "mode")):
            if not query.get_editable_text().set_text_contents(first):
                raise AssertionError("Could not set the owned search query")
            action(visible(viewer, "button", "Find next match"), "Find next")
            expect_stop(viewer, "remote search Stop")
            if transition == "query":
                if not query.get_editable_text().set_text_contents(replacement):
                    raise AssertionError("Could not replace the held query")
            else:
                action(visible(viewer, "toggle button", "Hex"), "Hex mode")
                wait_for(lambda: visible(viewer, "label", "Mode: Hex"), "Hex mode status")
            wait_for(lambda: search_retired(viewer, first), "retired search status")
            observed.append(transition + " retired the held request")

        if not query.get_editable_text().set_text_contents("REMOTE_STOP_ABSENT"):
            raise AssertionError("Could not set the Stop query")
        action(visible(viewer, "button", "Find next match"), "Find next")
        stop = expect_stop(viewer, "final remote Stop")
        action(stop, "Stop search")
        wait_for(lambda: visible(viewer, "label", "Search canceled."), "canceled search status")
        wait_for(lambda: visible(viewer, "button", "Stop search") is None,
                 "Stop to retire")
        labels = [node.get_name() or "" for node in descendants(viewer)
                  if node.get_role_name() == "label"]
        if any("0 matches" in label for label in labels):
            raise AssertionError("Canceled remote search claimed a complete negative count")
        if not intact(source) or sorted(path.name for path in server.iterdir()) != [NAME]:
            raise AssertionError("The remote fixture changed during Find")
        return {"passed": True, "build": hashlib.sha256((app_path.parent / "FileCat.dll").read_bytes()).hexdigest(),
                "source": str(source), "sourceLength": LENGTH, "address": address,
                "transitions": observed, "stopVisible": True, "canceledStatus": True,
                "falseZeroCount": False, "fixtureUnchanged": True,
                "nativeBackend": "Ubuntu Wayland AT-SPI over loopback OpenSSH SFTP"}
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=8)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
        if orca is not None and orca.poll() is None:
            orca.terminate()
            try:
                orca.wait(timeout=10)
            except subprocess.TimeoutExpired:
                orca.kill()
                orca.wait(timeout=5)
        if source.exists():
            source.unlink()


def main():
    if len(sys.argv) not in (4, 5) or len(sys.argv) == 5 and sys.argv[4] != "--orca-error":
        raise SystemExit("Usage: linux-atspi-viewer-remote-stop.py APP REPORT.json UINPUT_KEY_HELPER [--orca-error]")
    orca_error = len(sys.argv) == 5
    report = Path(sys.argv[2])
    if report.parent.resolve() != (Path.home() / "FileCatValidation").resolve(strict=True):
        raise SystemExit("Write the report directly below FileCatValidation")
    sudo_password = os.environ.pop("FILECAT_NATIVE_SUDO_PASSWORD", None)
    sftp_password = os.environ.pop("FILECAT_NATIVE_SFTP_PASSWORD", None)
    if not sudo_password or not sftp_password:
        raise SystemExit("The isolated input and SFTP fixture credentials are required")
    try:
        result = run(Path(sys.argv[1]), Path(sys.argv[3]), sudo_password, sftp_password,
                     orca_error)
    except Exception as error:
        result = {"passed": False, "error": f"{type(error).__name__}: {error}"}
    report.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
