#!/usr/bin/env python3
"""Create an isolated, byte-checkable SFTP source in the Ubuntu guest."""

import hashlib
import json
import os
import pathlib
import re
import sys


if len(sys.argv) != 2 or re.fullmatch(r"[0-9a-f]{32}", sys.argv[1]) is None:
    raise SystemExit("Usage: linux-sftp-recovery-seed.py <32-hex-fixture-id>")

fixture_id = sys.argv[1]
root = pathlib.Path.home() / "FileCatValidation" / "SftpRecovery" / fixture_id
if not root.is_dir() or not (root / "probe.txt").is_file():
    raise SystemExit("The isolated SFTP probe fixture is unavailable.")

source = root / "slow.bin"
marker = ("FileCat live SFTP interruption " + fixture_id).encode("ascii")
block = bytearray(bytes(range(256)) * 256)
block[: len(marker)] = marker
digest = hashlib.sha256()
with source.open("xb") as output:
    for _ in range(256):
        output.write(block)
        digest.update(block)
    output.flush()
    os.fsync(output.fileno())

report = {
    "fixture_id": fixture_id,
    "source": str(source),
    "size": source.stat().st_size,
    "sha256": digest.hexdigest(),
    "marker": marker.decode("ascii"),
}
(root / "slow-manifest.json").write_text(json.dumps(report, indent=2) + "\n")
