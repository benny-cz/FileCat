"""Smoke-test the owned SFTP fixture before using it with Windows EFS."""

import base64
from contextlib import contextmanager
import hashlib
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time

import paramiko


def ready_endpoint(ready, process):
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError("The owned SFTP server exited")
        try:
            port, fingerprint = ready.read_text(encoding="ascii").splitlines()
        except (FileNotFoundError, PermissionError):
            time.sleep(0.05)
            continue
        return int(port), fingerprint
    raise TimeoutError("The owned SFTP server did not publish its endpoint")


@contextmanager
def connect(port, fingerprint, password):
    with socket.create_connection(("127.0.0.1", port), timeout=5) as endpoint:
        with paramiko.Transport(endpoint) as transport:
            transport.start_client(timeout=5)
            digest = hashlib.sha256(transport.get_remote_server_key().asbytes()).digest()
            assert "SHA256:" + base64.b64encode(digest).decode("ascii").rstrip("=") == fingerprint
            transport.auth_password("tester", password)
            with paramiko.SFTPClient.from_transport(transport) as client:
                yield client


def main():
    workspace = Path(__file__).resolve().parent / "obj"
    workspace.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="sftp-owned-stage-", dir=workspace) as folder:
        fixture = Path(folder)
        root = fixture / "server"
        root.mkdir()
        ready = fixture / "ready.txt"
        script = Path(__file__).with_name("win-sftp-owned-stage.py")
        with subprocess.Popen([sys.executable, str(script), str(root), str(ready)]) as process:
            try:
                port, fingerprint = ready_endpoint(ready, process)
                try:
                    with connect(port, fingerprint, "incorrect"):
                        raise AssertionError("The owned SFTP fixture accepted an invalid password")
                except paramiko.AuthenticationException:
                    pass
                with connect(port, fingerprint, "secret-pass") as client:
                    client.mkdir("/upload")
                    assert client.listdir("/upload") == []
                    for path in ("/../ready.txt", "/upload/invalid:stream", "/upload/NUL"):
                        try:
                            client.stat(path)
                        except PermissionError:
                            pass
                        else:
                            raise AssertionError("The owned fixture accepted an unsupported path")
                    payload = b"owned payload\x00\x1a\r\n"
                    with connect(port, fingerprint, "secret-pass") as writer:
                        with writer.open("/upload/.stage", "wx") as output:
                            output.write(payload)
                        writer.rename("/upload/.stage", "/upload/file.bin")
                    assert client.listdir("/upload") == ["file.bin"]
                    with client.open("/upload/file.bin", "rb") as result:
                        assert result.read() == payload
                    client.remove("/upload/file.bin")
                    client.rmdir("/upload")
                    assert client.listdir("/") == []
                assert list(root.iterdir()) == []
            finally:
                process.kill()
                process.wait(timeout=5)


if __name__ == "__main__":
    main()
