#!/usr/bin/env python3
"""Owned private-network TCP445 reachability oracle; records peers, never payload."""

import select
import socket
import sys
import time
from pathlib import Path

mode = sys.argv[1]
if mode not in ("positive", "icon"):
    raise SystemExit(2)
duration = 12 if mode == "positive" else 50
result = Path(f"/tmp/filecat-ac43-445-{mode}.txt")
ready = Path(f"/tmp/filecat-ac43-445-{mode}.ready")
peers: list[str] = []
with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as server:
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind(("192.168.58.129", 445))
    server.listen(8)
    ready.write_text("Listening=192.168.58.129:445\n", encoding="utf-8")
    deadline = time.monotonic() + duration
    while time.monotonic() < deadline:
        readable, _, _ = select.select([server], [], [], max(0, min(0.25, deadline - time.monotonic())))
        if readable:
            connection, peer = server.accept()
            peers.append(f"{peer[0]}:{peer[1]}")
            connection.close()
result.write_text(
    f"Listening=192.168.58.129:445\nMode={mode}\nConnections={len(peers)}\n"
    + "".join(f"Peer={peer}\n" for peer in peers),
    encoding="utf-8",
)
