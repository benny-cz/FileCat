#!/usr/bin/env python3
"""Measure native Mac AX latency for an owned 2,001-item FTP, FTPS or SFTP panel.

Usage: mac-ax-remote-large-listing.py BUNDLE.app REPORT.json FTP_HELPER ftp|ftpes|sftp
The runner owns and removes its loopback server, data profile, app and fixture.
"""

import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import shutil
import signal
import socket
import subprocess
import sys
import time
from uuid import uuid4


base = Path(__file__).parent
remote = runpy.run_path(str(base / "mac-ax-remote-warning.py"))
large = runpy.run_path(str(base / "mac-ax-large-listing.py"))
prepare_sftp = runpy.run_path(str(base / "mac-sftp-owned-selftest.py"))["prepare"]
app_pids = remote["app_pids"]
click_ax = remote["click_ax"]
click_node = remote["click_node"]
run_command = remote["run_command"]
set_field = remote["set_field"]
wait_for = remote["wait_for"]
snapshot_data = large["snapshot_data"]
snapshot = large["snapshot"]
focused_name = large["focused_name"]
osa = large["osa"]
COUNT = 2000
LIMIT = 800


def set_exact_field(name, value, **options):
    try:
        set_field(name, value, **options)
    except AssertionError as error:
        raise AssertionError(f"Could not set the native {name} field: {error}") from error


def compile_helper(source, target):
    developer = run_command(["/usr/bin/xcode-select", "-p"], seconds=5)
    sdk = Path(developer) / "Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk"
    if not sdk.is_dir():
        raise AssertionError("The native Mac SDK is unavailable")
    run_command(["/usr/bin/swiftc", "-sdk", str(sdk), str(base / source), "-o", str(target)],
                seconds=45)


def stop(process):
    if process is not None and process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=10)


def run(bundle, report_path, ftp_helper, protocol):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    bundle = bundle.resolve(strict=True)
    ftp_helper = ftp_helper.resolve(strict=True)
    report_path = report_path.resolve()
    executable = bundle / "Contents/MacOS/FileCat"
    if (bundle.parent != validation or bundle.suffix != ".app" or not executable.is_file() or
            ftp_helper.parent != validation or ftp_helper.name != "win-ftp-owned-stage.py" or
            report_path.parent != validation or report_path.exists()):
        raise AssertionError("Use the isolated signed app and a fresh validation receipt")
    run_command(["/usr/bin/codesign", "--verify", "--deep", "--strict", str(bundle)])
    if app_pids():
        raise AssertionError("An existing FileCat process makes the AX test ambiguous")
    if "output muted:true" not in osa("get volume settings"):
        raise AssertionError("Mute Mac output during native accessibility acceptance")
    root = validation / "MacRemoteLargeListing" / uuid4().hex
    source, local_target, server_dir, data = (root / name for name in
                                              ("source", "local-target", "server", "data"))
    for folder in (source, local_target, server_dir, data):
        folder.mkdir(parents=True)
    originals = {"a.txt": b"alpha\n", "b.txt": b"beta\n"}
    for name, content in originals.items():
        (source / name).write_bytes(content)
    target = server_dir / "target.txt"
    target.write_bytes(b"owned unchanged remote target\n")
    for index in range(COUNT):
        (server_dir / f"native-list-{index:04d}.txt").write_bytes(b"")
    target_hash = hashlib.sha256(target.read_bytes()).hexdigest()
    helper = root / "ax-list-snapshot"
    detail_helper = root / "ax-detail-snapshot"
    server = None
    pid = None
    receipt = {"passed": False, "platform": "macOS", "protocol": protocol,
               "fixture": str(root), "generatedFiles": COUNT,
               "appDllSha256": hashlib.sha256(executable.with_name("FileCat.dll").read_bytes()).hexdigest()}
    try:
        compile_helper("mac-ax-panel-sort-snapshot.swift", helper)
        compile_helper("mac-ax-rename-snapshot.swift", detail_helper)
        ready = root / "server.ready"
        if protocol == "sftp":
            owned = prepare_sftp(root, server_dir)
            port = int(owned["port"])
            receipt["hostFingerprint"] = owned["hostFingerprint"]
            with (root / "sshd.log").open("wb") as log:
                server = subprocess.Popen(["/usr/sbin/sshd", "-D", "-e", "-f", owned["config"]],
                                          stdout=log, stderr=subprocess.STDOUT)

            def listening():
                if server.poll() is not None:
                    raise AssertionError("The owned SFTP server exited before connection")
                with socket.socket() as probe:
                    probe.settimeout(0.2)
                    return probe.connect_ex(("127.0.0.1", port)) == 0

            wait_for(listening, "owned loopback SFTP server", seconds=10)
        else:
            with (root / "server.log").open("wb") as log:
                server = subprocess.Popen([sys.executable, str(ftp_helper),
                                           "serve-tls" if protocol == "ftpes" else "serve",
                                           str(server_dir), str(ready)], stdout=log,
                                          stderr=subprocess.STDOUT)
            port = int(wait_for(lambda: ready.read_text(encoding="ascii").strip()
                                if ready.is_file() and server.poll() is None else None,
                                "owned loopback FTP port", seconds=10))
        if not 1 <= port <= 65535:
            raise AssertionError("The owned loopback port is invalid")
        address = (f"sftp://benny@127.0.0.1:{port}{server_dir}/" if protocol == "sftp" else
                   f"{protocol}://tester@127.0.0.1:{port}/")
        run_command(["/usr/bin/open", "-n", str(bundle), "--args", "--new-instance",
                     "--data", str(data), "--left", str(source), "--right", str(local_target)])
        pid = wait_for(lambda: next(iter(app_pids()), None), "owned FileCat process")
        command = run_command(["/bin/ps", "-p", str(pid), "-o", "command="])
        if not command.startswith(str(executable) + " --new-instance --data " + str(data)):
            raise AssertionError("The active process is not the exact signed validation app")

        def nodes():
            state = snapshot_data(detail_helper, executable, pid)
            return state["nodes"]

        # The empty local target makes remote completion unambiguous: its
        # 2,001 files cannot be read from the initial local panel.
        def initial_ready():
            try:
                return snapshot(helper, executable, pid)["fileCount"] == 2
            except AssertionError as error:
                if "accessibility tree was unavailable" not in str(error):
                    raise
                return False

        wait_for(initial_ready,
                 "complete initial local source panel", seconds=45)
        if protocol == "sftp":
            controls = [node for node in nodes() if node["role"] == "AXButton" and
                        node["name"] == "Connect to a server…" and node["bounds"][2] > 8]
            if len(controls) not in (1, 2):
                raise AssertionError(f"Expected the right Connect button, found {len(controls)}")
            click_node(max(controls, key=lambda node: node["bounds"][0]))
            wait_for(lambda: any(node["name"] == "Server" and node["role"] == "AXTextField"
                                 for node in nodes()), "fresh SFTP connection form")
            set_exact_field("Server", "127.0.0.1")
            set_exact_field("Port", str(port))
            set_exact_field("User name", "benny")
            click_ax("AXPopUpButton", "Sign in with")
            choice = wait_for(lambda: next((node for node in nodes() if
                              node["name"] == "Private key file" and
                              node["role"] == "AXStaticText" and node["bounds"][2] > 8), None),
                              "native private-key choice")
            for _ in range(3):
                osa('tell application "System Events" to tell process "FileCat" to set frontmost to true')
                click_node(choice)
                try:
                    wait_for(lambda: any(node["name"] == "Private key file" and
                                         node["role"] == "AXTextField" for node in nodes()),
                             "private-key file input", seconds=3)
                    break
                except AssertionError:
                    choice = next((node for node in nodes() if node["name"] == "Private key file"
                                   and node["role"] == "AXStaticText" and node["bounds"][2] > 8),
                                  None)
                    if choice is None:
                        fields = [(node["role"], node["name"], node["value"])
                                  for node in nodes() if node["role"] in
                                  ("AXTextField", "AXPopUpButton", "AXStaticText") and
                                  any(word in node["name"] for word in
                                      ("key", "Key", "Sign in", "Password", "Start folder"))]
                        raise AssertionError(f"The private-key choice closed without its input: {fields[:12]}")
            else:
                raise AssertionError("The private-key choice did not reveal its input")
            set_exact_field("Private key file", owned["clientKey"])
            set_exact_field("Start folder", str(server_dir))
            click_ax("AXButton", "Connect")
            wait_for(lambda: any("New server key" in node["name"] for node in nodes()),
                     "new SFTP host-key decision")
            if not any(owned["hostFingerprint"] in node["name"] for node in nodes()):
                raise AssertionError("The native SFTP fingerprint differs from the independent key")
            trust = [node for node in nodes() if node["role"] == "AXButton" and
                     node["name"] == "Connect once" and node["bounds"][2] > 8]
            if len(trust) != 1:
                raise AssertionError("The verified one-time SFTP trust action was unavailable")
            click_node(trust[0])
            wait_for(lambda: not any(node["role"] == "AXButton" and node["name"] == "Connect once"
                                     for node in nodes()), "SFTP host-key decision closure")
            receipt["hostKeyMatched"] = True
        else:
            set_exact_field("Location", address, rightmost=True, enter=True)
            if protocol == "ftp":
                wait_for(lambda: any(node["name"] == "Connect unencrypted" for node in nodes()),
                         "explicit FTP plaintext decision")
                click_ax("AXButton", "Connect unencrypted")
            wait_for(lambda: any(node["name"] == "Password" and node["role"] == "AXTextField"
                                 for node in nodes()), "owned FTP password field")
            set_exact_field("Password", "secret-pass")
            click_ax("AXButton", "Connect")
            if protocol == "ftpes":
                wait_for(lambda: any(node["name"] == "Connect once" and node["role"] == "AXButton"
                                     for node in nodes()), "fresh FTPS certificate decision")
                click_ax("AXButton", "Connect once")
        first_name = "native-list-0000.txt"
        try:
            first = wait_for(lambda: (state if state["fileCount"] == COUNT + 1 and
                              any(row["name"] == first_name for row in state["rows"]) else None)
                             if (state := snapshot(helper, executable, pid)) else None,
                             "complete remote listing", seconds=70)
        except AssertionError as error:
            state = snapshot(helper, executable, pid)
            visible = [node["name"] for node in nodes() if node["role"] in
                       ("AXWindow", "AXButton", "AXStaticText") and
                       any(word in node["name"] for word in
                           ("Connect", "error", "Error", "files", "server", "Server"))]
            log = root / ("sshd.log" if protocol == "sftp" else "server.log")
            tail = log.read_text(errors="replace")[-700:] if log.is_file() else ""
            raise AssertionError(f"{error}; listing={state['status']!r}, "
                                 f"rows={[row['name'] for row in state['rows']]}, "
                                 f"dialogs={visible[:16]}, serverLog={tail!r}") from error
        if first["rowCount"] > 120 or first["visited"] > LIMIT or first["querySeconds"] > 5:
            raise AssertionError(f"Remote AX tree was not bounded on load: {first}")
        first_row = next(row for row in first["rows"] if row["name"] == first_name)
        if first_row["bounds"][2] < 40 or first_row["bounds"][3] < 12:
            raise AssertionError("The first remote row has unusable native bounds")
        # macOS can consume the first pointer click while dismissing its
        # connection popup. Verify focus after each physical click.
        osa('tell application "System Events" to tell process "FileCat" to set frontmost to true')
        for _ in range(3):
            click_node(first_row)
            time.sleep(0.3)
            if focused_name() == first_name:
                break
        else:
            raise AssertionError(f"The remote row did not take focus: {focused_name()}")
        started = time.perf_counter()
        osa('''tell application "System Events" to tell process "FileCat"
    set frontmost to true
    key code 119
end tell''')
        tail = wait_for(lambda: (state if any(row["name"] == "target.txt"
                          for row in state["rows"]) else None)
                        if (state := snapshot(helper, executable, pid)) else None,
                        "remote listing tail after End", seconds=15)
        end_seconds = round(time.perf_counter() - started, 3)
        end_focus = focused_name()
        # The Mac End key scrolls to the tail while retaining the nearest
        # visible row focus. Continue with physical Down keys to verify the
        # final item itself is keyboard reachable and announced by AX.
        started = time.perf_counter()
        osa('''tell application "System Events" to tell process "FileCat"
    set frontmost to true
    repeat 25 times
        key code 125
    end repeat
end tell''')
        try:
            wait_for(lambda: focused_name() == "target.txt", "final remote row by Down", seconds=15)
        except AssertionError as error:
            state = snapshot(helper, executable, pid)
            raise AssertionError(f"{error}; focused={focused_name()!r}, "
                                 f"visible={[row['name'] for row in state['rows']]}, "
                                 f"status={state['status']!r}") from error
        down_seconds = round(time.perf_counter() - started, 3)
        last = snapshot(helper, executable, pid)
        if (last["fileCount"] != COUNT + 1 or last["rowCount"] > 120 or
                last["visited"] > LIMIT or last["querySeconds"] > 5 or
                end_seconds > 15 or down_seconds > 15):
            raise AssertionError(f"Remote AX tree or End navigation was not bounded: {last}")
        if ({name: (source / name).read_bytes() for name in originals} != originals or
                list(local_target.iterdir()) or
                hashlib.sha256(target.read_bytes()).hexdigest() != target_hash or
                len(list(server_dir.iterdir())) != COUNT + 1 or
                any((server_dir / f"native-list-{index:04d}.txt").stat().st_size != 0
                    for index in range(COUNT))):
            raise AssertionError("The native journey changed an owned source or server file")
        receipt.update({"passed": True,
                        "loaded": {key: value for key, value in first.items() if key != "rows"},
                        "afterEnd": {key: value for key, value in tail.items() if key != "rows"},
                        "afterDown": {key: value for key, value in last.items() if key != "rows"},
                        "endFocused": end_focus, "endKeySeconds": end_seconds,
                        "tailDownKeysSeconds": down_seconds, "sourceBytesUnchanged": True,
                        "serverFilesUnchanged": True})
        return receipt
    finally:
        if pid is not None:
            command = subprocess.run(["/bin/ps", "-p", str(pid), "-o", "command="],
                                     capture_output=True, text=True).stdout.strip()
            if command.startswith(str(executable) + " --new-instance --data " + str(data)):
                os.kill(pid, signal.SIGTERM)
                wait_for(lambda: pid not in app_pids(), "owned FileCat process exit", seconds=10)
        stop(server)
        shutil.rmtree(root)


if __name__ == "__main__":
    if len(sys.argv) != 5 or sys.argv[4] not in ("ftp", "ftpes", "sftp"):
        raise SystemExit(__doc__)
    report = Path(sys.argv[2]).resolve()
    try:
        outcome = run(Path(sys.argv[1]), report, Path(sys.argv[3]), sys.argv[4])
    except Exception as exc:
        outcome = {"passed": False, "error": f"{type(exc).__name__}: {exc}"}
    report.write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in outcome.items()
                      if key not in ("loaded", "afterEnd")}))
    raise SystemExit(0 if outcome["passed"] else 1)
