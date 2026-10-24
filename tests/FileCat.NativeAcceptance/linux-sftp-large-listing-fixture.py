#!/usr/bin/env python3
"""Seed, verify and remove one owned 2,001-file Ubuntu SFTP acceptance fixture.

Usage: linux-sftp-large-listing-fixture.py seed|verify|clean 32_HEX_ID
Receipts stay under ~/FileCatValidation/WindowsLargeSftpListing.
"""

import hashlib
import json
from pathlib import Path
import re
import runpy
import shutil
import sys


COUNT = 2000
TARGET = b"owned unchanged remote target\n"
fingerprints = runpy.run_path(str(Path(__file__).with_name("linux-sftp-replace-fixture.py")))["fingerprints"]


def run(action, fixture_id):
    if action not in ("seed", "verify", "clean") or not re.fullmatch(r"[0-9a-f]{32}", fixture_id):
        raise ValueError("Expected seed, verify or clean and one exact fresh 32-hex fixture ID")
    validation = (Path.home() / "FileCatValidation").resolve(strict=True)
    base = validation / "WindowsLargeSftpListing"
    base.mkdir(exist_ok=True)
    if base.resolve(strict=True) != validation / "WindowsLargeSftpListing":
        raise ValueError("The SFTP fixture base is not the isolated validation folder")
    root = base / fixture_id
    receipt = base / f"{action}-{fixture_id}.json"
    if receipt.exists():
        raise ValueError("The requested fixture receipt already exists")
    host_keys = fingerprints()
    if action == "seed":
        root.mkdir(exist_ok=False)
        (root / "target.txt").write_bytes(TARGET)
        for index in range(COUNT):
            (root / f"native-list-{index:04d}.txt").write_bytes(b"")
    elif not root.is_dir():
        raise ValueError("The exact owned SFTP fixture is absent")
    if root.is_symlink() or root.resolve(strict=True) != base / fixture_id:
        raise ValueError("The SFTP fixture path changed identity")
    expected = {"target.txt"} | {f"native-list-{index:04d}.txt" for index in range(COUNT)}
    entries = list(root.iterdir())
    exact = ({path.name for path in entries} == expected and len(entries) == COUNT + 1 and
             (root / "target.txt").is_file() and (root / "target.txt").read_bytes() == TARGET and
             all(path.is_file() and path.stat().st_size == 0 for path in entries
                 if path.name != "target.txt"))
    record = {"action": action, "fixture_id": fixture_id, "root": str(root),
              "file_count": len(entries), "target_sha256": hashlib.sha256(TARGET).hexdigest(),
              "manifest_and_bytes_exact": exact, "host_key_fingerprints": host_keys}
    if not exact:
        receipt.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        raise AssertionError("The owned SFTP listing or target bytes changed")
    if action == "clean":
        shutil.rmtree(root)
        record["fixture_removed"] = not root.exists()
        if not record["fixture_removed"]:
            raise AssertionError("The owned SFTP fixture was not removed")
    receipt.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    return record


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    result = run(sys.argv[1], sys.argv[2])
    print(json.dumps(result))
