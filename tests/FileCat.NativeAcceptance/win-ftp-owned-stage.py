#!/usr/bin/env python3
"""Loopback FTP fixture and exact owned-stage inspection for native recovery tests."""

import ftplib
import hashlib
import json
import logging
import pathlib
import re
import sys


def serve(root: pathlib.Path, ready: pathlib.Path) -> None:
    from pyftpdlib.authorizers import DummyAuthorizer
    from pyftpdlib.handlers import FTPHandler
    from pyftpdlib.servers import FTPServer

    if not root.is_dir() or ready.exists():
        raise ValueError("The isolated FTP root or ready path is invalid")
    authorizer = DummyAuthorizer()
    authorizer.add_user("tester", "secret-pass", str(root), perm="elradfmwMT")
    FTPHandler.authorizer = authorizer
    server = FTPServer(("127.0.0.1", 0), FTPHandler)
    ready.write_text(str(server.socket.getsockname()[1]) + "\n", encoding="ascii")
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
    elif len(sys.argv) == 6 and sys.argv[1] == "inspect-delete":
        inspect_delete(
            int(sys.argv[2]), sys.argv[3], pathlib.Path(sys.argv[4]), pathlib.Path(sys.argv[5])
        )
    else:
        raise SystemExit("Usage: serve ROOT READY | inspect-delete PORT STAGE SOURCE RECEIPT")


if __name__ == "__main__":
    main()
