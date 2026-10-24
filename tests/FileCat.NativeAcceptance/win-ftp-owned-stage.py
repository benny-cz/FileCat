#!/usr/bin/env python3
"""Loopback FTP fixture and exact owned-stage inspection for native recovery tests."""

import ftplib
import hashlib
import json
import logging
import pathlib
import re
import secrets
import sys


def serve(root: pathlib.Path, ready: pathlib.Path, tls: bool = False, implicit_tls: bool = False) -> None:
    from pyftpdlib.authorizers import DummyAuthorizer
    from pyftpdlib.servers import FTPServer

    if not root.is_dir() or ready.exists():
        raise ValueError("The isolated FTP root or ready path is invalid")
    authorizer = DummyAuthorizer()
    authorizer.add_user("tester", "secret-pass", str(root), perm="elradfmwMT")
    if tls:
        from OpenSSL import crypto
        from pyftpdlib.handlers import TLS_FTPHandler
        if implicit_tls:
            class handler(TLS_FTPHandler):
                def on_connect(self):
                    # TLS starts before the 220 greeting on implicit FTPS.
                    self.secure_connection(self.get_ssl_context())
        else:
            handler = TLS_FTPHandler

        key = crypto.PKey()
        key.generate_key(crypto.TYPE_RSA, 2048)
        cert = crypto.X509()
        cert.get_subject().CN = "FileCat owned loopback FTPS"
        cert.set_serial_number(int.from_bytes(secrets.token_bytes(16), "big"))
        cert.gmtime_adj_notBefore(-3600)
        cert.gmtime_adj_notAfter(86400)
        cert.set_issuer(cert.get_subject())
        cert.set_pubkey(key)
        cert.sign(key, "sha256")
        cert_path = root.parent / "owned-ftps-cert.pem"
        cert_path.write_bytes(crypto.dump_certificate(crypto.FILETYPE_PEM, cert) +
                              crypto.dump_privatekey(crypto.FILETYPE_PEM, key))
        handler.certfile = str(cert_path)
        handler.tls_control_required = True
        handler.tls_data_required = True
    else:
        from pyftpdlib.handlers import FTPHandler as handler
    handler.authorizer = authorizer
    server = FTPServer(("127.0.0.1", 0), handler)
    publishing = ready.with_name(ready.name + ".publishing")
    publishing.write_text(str(server.socket.getsockname()[1]) + "\n", encoding="ascii")
    publishing.replace(ready)
    server.serve_forever()


def inspect_delete(port: int, stage: str, source: pathlib.Path, receipt: pathlib.Path) -> None:
    if not 1 <= port <= 65535 or re.fullmatch(r"\.fc-[0-9a-f]{8}-[^/\\]+", stage) is None:
        raise ValueError("The exact owned FTP stage is invalid")
    if not source.is_file() or receipt.exists():
        raise ValueError("The owned source or new receipt path is invalid")
    source_bytes = source.read_bytes()
    chunks: list[bytes] = []
    with ftplib.FTP(timeout=10) as ftp:
        ftp.connect("127.0.0.1", port)
        ftp.login("tester", "secret-pass")
        before = ftp.nlst()
        if before != [stage]:
            raise ValueError(f"The remote fixture has an unexpected manifest: {before!r}")
        ftp.retrbinary(f"RETR {stage}", chunks.append)
        stage_bytes = b"".join(chunks)
        if not 0 < len(stage_bytes) < len(source_bytes) or not source_bytes.startswith(stage_bytes):
            raise ValueError("The staged data is not a partial prefix of the exact source")
        ftp.delete(stage)
        after = ftp.nlst()
        if after:
            raise ValueError(f"The inspected stage was not the only removed object: {after!r}")
    receipt.write_text(
        json.dumps(
            {
                "stage": stage,
                "stage_bytes": len(stage_bytes),
                "stage_sha256": hashlib.sha256(stage_bytes).hexdigest(),
                "source_sha256": hashlib.sha256(source_bytes).hexdigest(),
                "manifest_before": before,
                "manifest_after": after,
            },
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )


def main() -> None:
    logging.disable(logging.CRITICAL)
    if len(sys.argv) == 4 and sys.argv[1] == "serve":
        serve(pathlib.Path(sys.argv[2]), pathlib.Path(sys.argv[3]))
    elif len(sys.argv) == 4 and sys.argv[1] == "serve-tls":
        serve(pathlib.Path(sys.argv[2]), pathlib.Path(sys.argv[3]), tls=True)
    elif len(sys.argv) == 4 and sys.argv[1] == "serve-implicit-tls":
        serve(pathlib.Path(sys.argv[2]), pathlib.Path(sys.argv[3]), tls=True, implicit_tls=True)
    elif len(sys.argv) == 6 and sys.argv[1] == "inspect-delete":
        inspect_delete(
            int(sys.argv[2]), sys.argv[3], pathlib.Path(sys.argv[4]), pathlib.Path(sys.argv[5])
        )
    else:
        raise SystemExit("Usage: serve|serve-tls|serve-implicit-tls ROOT READY | inspect-delete PORT STAGE SOURCE RECEIPT")


if __name__ == "__main__":
    main()
