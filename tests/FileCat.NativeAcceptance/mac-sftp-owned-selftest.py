#!/usr/bin/env python3
"""Test a disposable, public-key-only loopback SFTP server on the Mac.

Usage: mac-sftp-owned-selftest.py REPORT.json
       mac-sftp-owned-selftest.py prepare FIXTURE_ROOT SERVER_DIR
The server, host key and client key belong solely to a fresh fixture below
~/FileCatValidation. No system SSH configuration or authorized_keys is changed.
"""

import json
from pathlib import Path
import re
import socket
import subprocess
import sys
import time
from uuid import uuid4


def checked(args, seconds=15, input_text=None):
    done = subprocess.run(args, input=input_text, capture_output=True, text=True, timeout=seconds)
    if done.returncode:
        raise AssertionError((done.stderr or done.stdout)[-1200:])
    return done.stdout.strip()


def prepare(fixture, server_dir):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    fixture = fixture.resolve(strict=True)
    server_dir = server_dir.resolve(strict=True)
    if (fixture.parent not in (validation / "MacOwnedSftp", validation / "MacRemoteWarning",
                               validation / "MacRemoteLargeListing") or
            not re.fullmatch(r"[0-9a-f]{32}", fixture.name) or
            server_dir != fixture / "server" or not (server_dir / "target.txt").is_file()):
        raise AssertionError("The SFTP fixture must contain one owned server target below FileCatValidation")
    ssh_dir = fixture / "ssh"
    ssh_dir.mkdir()
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        port = listener.getsockname()[1]
    host_key = ssh_dir / "host_ed25519"
    client_key = ssh_dir / "client_ed25519"
    for key in (host_key, client_key):
        checked(["/usr/bin/ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", str(key)])
    (ssh_dir / "authorized_keys").write_bytes(client_key.with_suffix(".pub").read_bytes())
    fingerprint = checked(["/usr/bin/ssh-keygen", "-lf", str(host_key) + ".pub"])
    match = re.search(r"SHA256:[A-Za-z0-9+/]+", fingerprint)
    if not match:
        raise AssertionError("The generated host key fingerprint is unavailable")
    config = ssh_dir / "sshd_config"
    config.write_text("\n".join([
        f"Port {port}", "ListenAddress 127.0.0.1", f"HostKey {host_key}",
        f"AuthorizedKeysFile {ssh_dir / 'authorized_keys'}", "StrictModes no",
        "PasswordAuthentication no", "KbdInteractiveAuthentication no",
        "PubkeyAuthentication yes", "UsePAM no", "AllowUsers benny",
        "PermitRootLogin no", "AllowTcpForwarding no", "AllowAgentForwarding no",
        "X11Forwarding no", "PermitTunnel no", "ForceCommand internal-sftp",
        "Subsystem sftp internal-sftp", "LogLevel VERBOSE", f"PidFile {ssh_dir / 'sshd.pid'}",
        "",]), encoding="ascii")
    checked(["/usr/sbin/sshd", "-t", "-f", str(config)])
    return {"fixture": str(fixture), "serverDir": str(server_dir), "port": port,
            "hostFingerprint": match.group(0), "clientKey": str(client_key), "config": str(config)}


def run(report_path):
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    report_path = report_path.resolve()
    if report_path.parent != validation or report_path.suffix != ".json":
        raise AssertionError("The receipt must stay in the isolated Mac validation folder")
    fixture = validation / "MacOwnedSftp" / uuid4().hex
    server_dir = fixture / "server"
    server_dir.mkdir(parents=True)
    (server_dir / "target.txt").write_bytes(b"owned unchanged SFTP target\n")
    owned = prepare(fixture, server_dir)
    port, client_key, config = owned["port"], Path(owned["clientKey"]), Path(owned["config"])
    receipt = {"passed": False, "fixture": str(fixture), "port": port,
               "hostFingerprint": owned["hostFingerprint"], "serverPid": None}
    server = None
    try:
        with (fixture / "ssh" / "sshd.log").open("wb") as log:
            server = subprocess.Popen(["/usr/sbin/sshd", "-D", "-e", "-f", str(config)],
                                      stdout=log, stderr=subprocess.STDOUT)
        receipt["serverPid"] = server.pid
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            if server.poll() is not None:
                raise AssertionError("The owned sshd exited: " + (fixture / "ssh" / "sshd.log").read_text()[-1200:])
            with socket.socket() as probe:
                probe.settimeout(0.2)
                if probe.connect_ex(("127.0.0.1", port)) == 0:
                    break
            time.sleep(0.1)
        else:
            raise AssertionError("The owned loopback sshd did not listen")
        output = checked(["/usr/bin/sftp", "-o", "BatchMode=yes", "-o", "IdentitiesOnly=yes",
                          "-o", "StrictHostKeyChecking=accept-new", "-o",
                          f"UserKnownHostsFile={fixture / 'ssh' / 'known_hosts'}", "-i", str(client_key),
                          "-P", str(port), "-b", "-", "benny@127.0.0.1"],
                         seconds=15, input_text=f"ls {server_dir / 'target.txt'}\n")
        if "target.txt" not in output:
            raise AssertionError("SFTP did not list the exact owned fixture target")
        receipt["passed"] = True
        return receipt
    except Exception as exc:
        receipt["error"] = f"{type(exc).__name__}: {exc}"
        return receipt
    finally:
        if server is not None and server.poll() is None:
            server.terminate()
            try:
                server.wait(timeout=5)
            except subprocess.TimeoutExpired:
                server.kill()
        receipt["serverStopped"] = server is not None and server.poll() is not None
        report_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    if len(sys.argv) == 4 and sys.argv[1] == "prepare":
        prepared = prepare(Path(sys.argv[2]), Path(sys.argv[3]))
        print(json.dumps(prepared))
        raise SystemExit(0)
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    result = run(Path(sys.argv[1]))
    raise SystemExit(0 if result["passed"] and result["serverStopped"] else 1)
