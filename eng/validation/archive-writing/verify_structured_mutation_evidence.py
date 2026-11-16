#!/usr/bin/env python3
"""Independently verify sealed structured-mutation platform evidence without running product code.

Reads one Windows .zip or Unix .tar.gz bundle produced by the structured-mutation runners, extracts only
bounded JSON/text facts in memory, and checks: the frozen harness/corpus packet identities, the harness
source hashes against an expected source tree, the five required lanes, each lane's completion and
non-trivial coverage, the platform CPU floor, and the PASS status. Prints one JSON summary line.
"""

import argparse
import hashlib
import io
import json
from pathlib import Path, PurePosixPath, PureWindowsPath
import sys
import tarfile
import zipfile

PACKET_SHA256 = "ca7a04047cf09c5a32dab64cf42bcc64fd15f88aba97b47d9512387cc2288297"
SEEDS_SHA256 = "c36d2e822b2505c04f77e96d23844e742da475803c7c2ce0224d344ae8f1297b"
LANES = ("sevenzip-strict", "rar5-strict", "sevenzip-password", "rar5-password", "journal-state")
MAX_MEMBER = 8 * 1024 * 1024
MAX_TOTAL = 64 * 1024 * 1024


def read_bundle(path):
    files = {}
    total = 0
    if path.suffix.lower() == ".zip":
        with zipfile.ZipFile(path) as bundle:
            for info in bundle.infolist():
                if info.is_dir():
                    continue
                name = PureWindowsPath(info.filename).as_posix() if "\\" in info.filename else info.filename
                if info.file_size > MAX_MEMBER:
                    continue
                total += info.file_size
                if total > MAX_TOTAL:
                    raise ValueError("bundle too large")
                files[PurePosixPath(name).name if "/" not in name else name] = bundle.read(info)
    else:
        with tarfile.open(path, "r:gz") as bundle:
            for member in bundle.getmembers():
                if not member.isfile() or member.size > MAX_MEMBER:
                    continue
                total += member.size
                if total > MAX_TOTAL:
                    raise ValueError("bundle too large")
                files[member.name] = bundle.extractfile(member).read()
    return files


def find(files, leaf):
    hits = [name for name in files if PurePosixPath(name).name == leaf]
    if len(hits) != 1:
        raise ValueError(f"expected exactly one {leaf}, found {len(hits)}")
    return files[hits[0]]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("bundle", type=Path)
    parser.add_argument("--platform", required=True, choices=("windows", "mac", "ubuntu"))
    parser.add_argument("--source-root", type=Path, required=True,
                        help="checkout whose eng/validation/archive-writing/ArchiveFuzzHarness sources must match")
    parser.add_argument("--cpu-floor", type=float, default=14400.0)
    args = parser.parse_args()
    files = read_bundle(args.bundle)
    status = find(files, "status.txt").decode("utf-8-sig").strip()
    if status != "PASS":
        raise ValueError("status is " + status)
    hashes = json.loads(find(files, "source-binary-hashes.json").decode("utf-8-sig"))
    if isinstance(hashes, dict):
        hashes = [hashes]
    by_leaf = {}
    for row in hashes:
        path = row.get("path") or row.get("Path")
        digest = (row.get("sha256") or row.get("Hash")).lower()
        leaf = PureWindowsPath(path).name if "\\" in path else PurePosixPath(path).name
        by_leaf[leaf] = digest
    if by_leaf.get("structured-mutation-harness-v1.tar.gz") != PACKET_SHA256:
        raise ValueError("harness packet identity differs")
    if by_leaf.get("structured-mutation-seeds-v1.tar.gz") != SEEDS_SHA256:
        raise ValueError("seed packet identity differs")
    harness_dir = args.source_root / "eng/validation/archive-writing/ArchiveFuzzHarness"
    for leaf in ("Program.cs", "StructuredMutation.cs"):
        local = hashlib.sha256((harness_dir / leaf).read_bytes().replace(b"\r\n", b"\n")).hexdigest()
        local_raw = hashlib.sha256((harness_dir / leaf).read_bytes()).hexdigest()
        if by_leaf.get(leaf) not in (local, local_raw):
            raise ValueError(f"{leaf} differs from {harness_dir}")
    lanes = {}
    for lane in LANES:
        raw = find(files, f"{lane}-long.json").decode("utf-8-sig")
        row = json.loads(raw)
        if row.get("target") != lane or not row.get("complete") or row.get("failure"):
            raise ValueError("lane incomplete or failed: " + lane)
        if row.get("calls", 0) <= 1000 or row.get("accepted", 0) <= 0:
            raise ValueError("lane coverage too small: " + lane)
        lanes[lane] = {key: row.get(key) for key in ("calls", "accepted", "refused", "process_cpu_seconds",
                                                       "elapsed_wall_seconds", "max_private_bytes")}
    cpu = sum(row["process_cpu_seconds"] for row in lanes.values())
    if cpu < args.cpu_floor:
        raise ValueError(f"CPU {cpu} below floor {args.cpu_floor}")
    print(json.dumps({"bundle": args.bundle.name,
                      "bundle_sha256": hashlib.sha256(args.bundle.read_bytes()).hexdigest(),
                      "platform": args.platform, "status": status, "cpu_seconds": round(cpu, 3),
                      "core_dll": by_leaf.get("FileCat.Core.dll"), "archives_dll": by_leaf.get("FileCat.Archives.dll"),
                      "harness_dll": by_leaf.get("FileCat.ArchiveFuzzHarness.dll"), "lanes": lanes}, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
