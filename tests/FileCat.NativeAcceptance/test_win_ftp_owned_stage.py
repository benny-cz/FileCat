"""Owned loopback FTP/FTPS fixture smoke test; no external service is used."""

import ftplib
from pathlib import Path
import ssl
import subprocess
import sys
import tempfile
import time


def test_server(mode: str) -> None:
    workspace = Path(__file__).resolve().parent / "obj"
    workspace.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="ftp-owned-stage-", dir=workspace) as folder:
        fixture = Path(folder)
        root = fixture / "server"
        root.mkdir()
        (root / "target.txt").write_bytes(b"owned existing remote content\n")
        ready = fixture / "ready.txt"
        process = subprocess.Popen([sys.executable,
                                    str(Path(__file__).with_name("win-ftp-owned-stage.py")),
                                    mode, str(root), str(ready)],
                                   stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        try:
            deadline = time.monotonic() + 10
            while not ready.is_file():
                if process.poll() is not None or time.monotonic() > deadline:
                    error = process.stderr.read().decode("utf-8", errors="replace") if process.poll() is not None else ""
                    raise AssertionError(f"Owned {mode} server did not start: {error[-800:]}")
                time.sleep(0.05)
            port = int(ready.read_text().strip())
            if mode == "serve-tls":
                with ftplib.FTP_TLS(context=ssl._create_unverified_context(), timeout=5) as ftp:
                    ftp.connect("127.0.0.1", port)
                    ftp.auth()
                    ftp.login("tester", "secret-pass")
                    ftp.prot_p()
                    assert ftp.nlst() == ["target.txt"]
                assert (fixture / "owned-ftps-cert.pem").is_file()
            else:
                with ftplib.FTP(timeout=5) as ftp:
                    ftp.connect("127.0.0.1", port)
                    ftp.login("tester", "secret-pass")
                    assert ftp.nlst() == ["target.txt"]
            assert (root / "target.txt").read_bytes() == b"owned existing remote content\n"
        finally:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
            process.stderr.close()


if __name__ == "__main__":
    for server_mode in ("serve", "serve-tls"):
        test_server(server_mode)
    print("Owned FTP and explicit-TLS FTPS fixtures passed")
