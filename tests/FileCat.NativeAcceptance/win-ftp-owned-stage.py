#!/usr/bin/env python3
"""Loopback FTP fixture and exact owned-stage inspection for native recovery tests."""

from __future__ import annotations

import ftplib
from contextlib import closing
import datetime
import hashlib
import ipaddress
import json
import logging
import pathlib
import re
import socket
import ssl
import sys
import time


def serve(root: pathlib.Path, ready: pathlib.Path, tls: bool = False, implicit_tls: bool = False) -> None:
    from pyftpdlib.authorizers import DummyAuthorizer
    from pyftpdlib.servers import FTPServer

    if not root.is_dir() or ready.exists():
        raise ValueError("The isolated FTP root or ready path is invalid")
    authorizer = DummyAuthorizer()
    authorizer.add_user("tester", "secret-pass", str(root), perm="elradfmwMT")
    if tls:
        from cryptography import x509
        from cryptography.hazmat.primitives import hashes, serialization
        from cryptography.hazmat.primitives.asymmetric import rsa
        from pyftpdlib.handlers import TLS_FTPHandler
        if implicit_tls:
            class handler(TLS_FTPHandler):
                def on_connect(self):
                    # TLS starts before the 220 greeting on implicit FTPS.
                    self.secure_connection(self.get_ssl_context())
        else:
            handler = TLS_FTPHandler

        key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
        name = x509.Name([x509.NameAttribute(x509.NameOID.COMMON_NAME,
                                              "FileCat owned loopback FTPS")])
        now = datetime.datetime.now(datetime.timezone.utc)
        cert = (x509.CertificateBuilder()
                .subject_name(name).issuer_name(name).public_key(key.public_key())
                .serial_number(x509.random_serial_number())
                .not_valid_before(now - datetime.timedelta(hours=1))
                .not_valid_after(now + datetime.timedelta(days=1))
                .add_extension(x509.SubjectAlternativeName(
                    [x509.IPAddress(ipaddress.ip_address("127.0.0.1"))]), critical=False)
                .sign(key, hashes.SHA256()))
        cert_path = root.parent / "owned-ftps-cert.pem"
        cert_path.write_bytes(cert.public_bytes(serialization.Encoding.PEM) +
                              key.private_bytes(serialization.Encoding.PEM,
                                                serialization.PrivateFormat.TraditionalOpenSSL,
                                                serialization.NoEncryption()))
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


class ImplicitFTP(ftplib.FTP_TLS):
    def connect(self, host: str, port: int) -> str:
        self.host, self.port = host, port
        self.sock = socket.create_connection((host, port), timeout=self.timeout)
        self.af = self.sock.family
        self.sock = self.context.wrap_socket(self.sock, server_hostname=host)
        self.file = self.sock.makefile("r", encoding=self.encoding)
        self.welcome = self.getresp()
        return self.welcome


def certificate_fingerprint(cert_path: pathlib.Path) -> str:
    text = cert_path.read_text(encoding="ascii")
    end = "-----END CERTIFICATE-----"
    if end not in text:
        raise ValueError("The owned FTPS certificate is missing")
    certificate = text[:text.index(end) + len(end)]
    return hashlib.sha256(ssl.PEM_cert_to_DER_cert(certificate)).hexdigest().upper()


def inspect_delete(port: int, stage: str, source: pathlib.Path, receipt: pathlib.Path,
                   protocol: str = "ftp", cert_path: pathlib.Path | None = None) -> None:
    if not 1 <= port <= 65535 or re.fullmatch(r"\.fc-[0-9a-f]{8}-[^/\\]+", stage) is None:
        raise ValueError("The exact owned FTP stage is invalid")
    if not source.is_file() or receipt.exists():
        raise ValueError("The owned source or new receipt path is invalid")
    if protocol not in ("ftp", "ftpes", "ftps") or (protocol != "ftp" and
            (cert_path is None or not cert_path.is_file())):
        raise ValueError("The FTPS cleanup requires the exact owned certificate")
    source_bytes = source.read_bytes()
    chunks: list[bytes] = []
    context = ssl.create_default_context(cafile=str(cert_path)) if cert_path else None
    client = (ftplib.FTP if protocol == "ftp" else
              ftplib.FTP_TLS if protocol == "ftpes" else ImplicitFTP)

    def connect():
        ftp = client(timeout=10, **({"context": context} if context else {}))
        try:
            ftp.connect("127.0.0.1", port)
            if protocol == "ftpes":
                ftp.auth()
            ftp.login("tester", "secret-pass")
            if protocol != "ftp":
                ftp.prot_p()
            return ftp
        except BaseException:
            ftp.close()
            raise

    def tls_shutdown_race(error: ssl.SSLError) -> bool:
        return protocol != "ftp" and getattr(error, "reason", None) == "SHUTDOWN_WHILE_IN_INIT"

    with closing(connect()) as ftp:
        before = ftp.nlst()
        if before != [stage]:
            raise ValueError(f"The remote fixture has an unexpected manifest: {before!r}")
        ftp.retrbinary(f"RETR {stage}", chunks.append)
        stage_bytes = b"".join(chunks)
        if not 0 < len(stage_bytes) < len(source_bytes) or not source_bytes.startswith(stage_bytes):
            raise ValueError("The staged data is not a partial prefix of the exact source")
        ftp.delete(stage)
        try:
            after = ftp.nlst()
        except ssl.SSLError as error:
            if not tls_shutdown_race(error):
                raise
            # Some FTPS servers close a completed data connection while the
            # client is unwrapping TLS. That invalidates this control session's
            # reply sequence, so verify the deletion on a fresh pinned session.
            after = None
    if after is None:
        for attempt in range(5):
            with closing(connect()) as verification:
                try:
                    after = verification.nlst()
                    break
                except ssl.SSLError as error:
                    if not tls_shutdown_race(error) or attempt == 4:
                        raise
            time.sleep(0.05 * (attempt + 1))
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
                "protocol": protocol,
                "certificate_sha256": certificate_fingerprint(cert_path) if cert_path else None,
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
    elif len(sys.argv) == 8 and sys.argv[1] == "inspect-delete-tls":
        inspect_delete(int(sys.argv[2]), sys.argv[3], pathlib.Path(sys.argv[4]),
                       pathlib.Path(sys.argv[5]), sys.argv[6],
                       pathlib.Path(sys.argv[7]))
    elif len(sys.argv) == 3 and sys.argv[1] == "fingerprint":
        print(certificate_fingerprint(pathlib.Path(sys.argv[2])))
    else:
        raise SystemExit("Usage: serve|serve-tls|serve-implicit-tls ROOT READY | "
                         "inspect-delete PORT STAGE SOURCE RECEIPT | "
                         "inspect-delete-tls PORT STAGE SOURCE RECEIPT PROTOCOL CERT | "
                         "fingerprint CERT")


if __name__ == "__main__":
    main()
