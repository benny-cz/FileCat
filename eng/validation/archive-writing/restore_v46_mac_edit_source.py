#!/usr/bin/env python3
"""Reset only this task's preliminarily tested Mac edit files before sealed v47 replay."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tarfile


PATHS = {
    "src/FileCat.Core/Edit/EditSessions.cs",
    "src/FileCat.Core/Edit/EditSessions.Storage.cs",
    "tests/FileCat.Core.Tests/UnixEditStorageTests.cs",
    "tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj",
}
NEW = "tests/ArchiveEditDualRootCases.cs"


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True)
    parser.add_argument("--packet-sha256", required=True)
    args = parser.parse_args()
    task = args.task.resolve(strict=True)
    subprocess.run(["python3", str(task / "check-linux-archive-idle.py")], check=True)
    packet = task / "archive-edit-storage-delta-v47.tar.gz"
    if sha(packet.read_bytes()) != args.packet_sha256:
        raise ValueError("The sealed v47 packet changed")
    with tarfile.open(packet) as archive:
        delta = json.load(archive.extractfile("delta.json"))
    rows = {row["path"]: row for row in delta["files"]}
    root = task / "archive-workflow-source-v1"
    restore = task / "archive-edit-storage-mac-restore-v46.tar.gz"
    with tarfile.open(restore) as archive:
        members = {item.name: item for item in archive if item.isfile()}
        if set(members) != PATHS or any(not item.isfile() for item in members.values()):
            raise ValueError("The Mac v46 restore inventory changed")
        previous = {name: archive.extractfile(item).read() for name, item in members.items()}
    for name in PATHS | {NEW}:
        current = root / name
        if not current.is_file() or current.is_symlink() or sha(current.read_bytes()) != rows[name]["sha256"]:
            raise ValueError("Guest source changed since preliminary v47 review: " + name)
        if name in PATHS and sha(previous[name]) != rows[name]["previous_sha256"]:
            normalized = previous[name].replace(b"\r\n", b"\n")
            if sha(normalized) != rows[name]["previous_sha256"]:
                raise ValueError("The exact v46 predecessor is unavailable: " + name)
            previous[name] = normalized
        if name == NEW and rows[name]["previous_sha256"] is not None:
            raise ValueError("The v47-only source existed in the predecessor")
    for name in PATHS:
        (root / name).write_bytes(previous[name])
    (root / NEW).unlink()
    print(json.dumps({"restored": len(PATHS), "removed_new": NEW,
                      "v47_packet_sha256": args.packet_sha256}))


if __name__ == "__main__":
    main()
