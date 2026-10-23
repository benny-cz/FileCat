#!/usr/bin/env python3
"""Seed and verify a single owned SFTP replacement target in the Ubuntu VM."""

import base64
import hashlib
import json
from pathlib import Path
import re
import sys


TARGET = b"owned existing remote content\n"


def fingerprints() -> list[str]:
    values = []
    for public_key in sorted(Path("/etc/ssh").glob("ssh_host_*_key.pub")):
        parts = public_key.read_text(encoding="ascii").split()
        if len(parts) < 2:
            continue
        blob = base64.b64decode(parts[1], validate=True)
        digest = base64.b64encode(hashlib.sha256(blob).digest()).decode("ascii").rstrip("=")
        values.append("SHA256:" + digest)
    if not values:
        raise ValueError("No SSH server public keys were available for verification")
    return values


def main() -> None:
    if len(sys.argv) != 3 or sys.argv[1] not in {"seed", "verify"}:
        raise SystemExit("Usage: linux-sftp-replace-fixture.py seed|verify 32_HEX_ID")
    action, fixture_id = sys.argv[1:]
    if re.fullmatch(r"[0-9a-f]{32}", fixture_id) is None:
        raise ValueError("A fresh 32-hex fixture ID is required")
    base = Path.home() / "FileCatValidation" / "SftpReplaceReview"
    root = base / fixture_id
    receipt = base / f"{action}-{fixture_id}.json"
    if receipt.exists():
        raise ValueError("The fixture receipt already exists")
    if action == "seed":
        base.mkdir(parents=True, exist_ok=True)
        root.mkdir(exist_ok=False)
        (root / "target.txt").write_bytes(TARGET)
    elif not root.is_dir():
        raise ValueError("The owned fixture directory is missing")
    entries = sorted(p.name for p in root.iterdir())
    target = root / "target.txt"
    exact = entries == ["target.txt"] and target.is_file() and target.read_bytes() == TARGET
    record = {
        "action": action,
        "fixture_id": fixture_id,
        "root": str(root),
        "target_sha256": hashlib.sha256(TARGET).hexdigest(),
        "entries": entries,
        "target_unchanged": exact,
        "host_key_fingerprints": fingerprints(),
    }
    receipt.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    if not exact:
        raise AssertionError("The owned SFTP target or manifest changed")


if __name__ == "__main__":
    main()
