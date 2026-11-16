#!/usr/bin/env python3
"""Independently check the sealed Mac FZ02 replay and exact deterministic seed corpus."""

import hashlib
import json
from pathlib import Path
import sys
import tarfile

from make_wrapper_fuzz_seeds import variants


ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / "eng/validation/archive-writing"
TARGETS = ("gzip-strict", "bzip2-strict", "zstd-strict")
TYPES = {"FileCat.Archives.Writing.StrictGzipReadStream",
         "FileCat.Archives.Writing.StrictBzip2ReadStream",
         "FileCat.Archives.Writing.StrictZstdReadStream",
         "FileCat.Archives.Writing.ZstdFrameHeader"}


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def verify(expected_transport_sha256: str) -> dict:
    packet = ROOT / ".tmp/storage-evidence/mac/fuzz-wrapper-v17/fuzz-wrapper-v17-replay.tar.gz"
    if sha(packet.read_bytes()) != expected_transport_sha256.lower():
        raise ValueError("Mac FZ02 replay transport differs from the guest-computed hash")
    with tarfile.open(packet, "r:gz") as archive:
        members = archive.getmembers()
        if any(member.issym() or member.islnk() or member.name.startswith("/") or
               ".." in Path(member.name).parts or not (member.isfile() or member.isdir())
               for member in members):
            raise ValueError("Unreviewable replay transport entry")
        files = {member.name: archive.extractfile(member).read() for member in members if member.isfile()}
    if len(files) != sum(member.isfile() for member in members):
        raise ValueError("Duplicate replay transport entry")
    file = lambda name: files[name]
    result = json.loads(file("result/result.json"))
    for name, local in {
        "Program.cs": SOURCE / "ArchiveFuzzHarness/Program.cs",
        "ArchiveFuzzHarness.csproj": SOURCE / "ArchiveFuzzHarness/ArchiveFuzzHarness.csproj",
        "packages.lock.json": SOURCE / "ArchiveFuzzHarness/packages.lock.json",
        "make_wrapper_fuzz_seeds.py": SOURCE / "make_wrapper_fuzz_seeds.py",
        "replay_wrapper_guest.py": SOURCE / "replay_wrapper_guest.py",
    }.items():
        actual = sha(file("source/" + name))
        if actual != sha(local.read_bytes()) or result["source_sha256"][name] != actual:
            raise ValueError("The Mac replay source differs from the implementation: " + name)
    bin_file = lambda name: file("source/bin/Release/net10.0/" + name)
    if result["core_sha256"] != sha(bin_file("FileCat.Core.dll")) or \
       result["archives_sha256"] != sha(bin_file("FileCat.Archives.dll")) or \
       result["harness_sha256"] != sha(bin_file("FileCat.ArchiveFuzzHarness.dll")) or \
       result["instrumented_archives_sha256"] != sha(file("result/image/FileCat.Archives.dll")) or \
       result["instrumented_archives_sha256"] == result["archives_sha256"]:
        raise ValueError("The replay binary binding or instrumentation changed")
    instrumented = json.loads(file("result/instrumented-types.json"))
    if instrumented != result["instrumented_types"] or not TYPES.issubset(instrumented):
        raise ValueError("Production strict wrapper instrumentation is incomplete")
    expected_rows = []
    for target in TARGETS:
        seeds = variants(target)
        manifest = result["manifests"][target]
        archive_manifest = json.loads(file("result/fz02-" + target.removesuffix("-strict") + "-seeds/manifest.json"))
        if manifest != archive_manifest or manifest["target"] != target or \
           {row["name"] for row in manifest["seeds"]} != set(seeds):
            raise ValueError("The wrapper seed manifest changed")
        for row in manifest["seeds"]:
            name = row["name"]
            encoded, accepted = seeds[name]
            if row != {"name": name, "bytes": len(encoded), "sha256": sha(encoded), "accepted": accepted} or \
               file("result/fz02-" + target.removesuffix("-strict") + "-seeds/" + name) != encoded:
                raise ValueError("The replay omitted or changed a deterministic wrapper seed")
            expected_rows.append({"target": target, "name": name, "sha256": row["sha256"],
                                  "expected": accepted, "accepted": accepted, "returncode": 0, "stderr": ""})
    if result["replay"] != expected_rows:
        raise ValueError("The Mac wrapper replay differs from its expected acceptance inventory")
    build = file("result/build.log").decode()
    if "Build succeeded." not in build or "0 Warning(s)" not in build or "0 Error(s)" not in build:
        raise ValueError("The guest-built FZ02 harness did not compile cleanly")
    summary = json.loads(file("result/replay.log").decode().strip())
    accepted = sum(row["accepted"] for row in expected_rows)
    if summary != {"accepted": accepted, "refused": len(expected_rows) - accepted,
                   "instrumented_types": len(instrumented)}:
        raise ValueError("The guest replay summary differs from the raw cases")
    return {"transport_sha256": expected_transport_sha256.lower(), "cases": len(expected_rows),
            "accepted": accepted, "refused": len(expected_rows) - accepted,
            "instrumented_types": len(instrumented), "core_sha256": result["core_sha256"],
            "archives_sha256": result["archives_sha256"]}


if __name__ == "__main__":
    print(json.dumps(verify(sys.argv[1]), indent=2))
