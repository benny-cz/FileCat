#!/usr/bin/env python3
"""Independently verify the sealed Windows VM FZ02 wrapper replay."""

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


def guest_text(data: bytes) -> str:
    return data.decode("utf-16") if data.startswith((b"\xff\xfe", b"\xfe\xff")) else data.decode("utf-8")


def verify(expected_transport_sha256: str) -> dict:
    packet = ROOT / ".tmp/storage-evidence/windows/fuzz-wrapper-v17/evidence.tar.gz"
    if sha(packet.read_bytes()) != expected_transport_sha256.lower():
        raise ValueError("The Windows replay transport differs from the guest-computed hash")
    with tarfile.open(packet, "r:gz") as archive:
        members = archive.getmembers()
        if any(member.issym() or member.islnk() or member.name.startswith("/") or
               ".." in Path(member.name).parts or not (member.isfile() or member.isdir())
               for member in members):
            raise ValueError("Unreviewable Windows replay transport entry")
        data = {member.name: archive.extractfile(member).read() for member in members if member.isfile()}
    if len(data) != sum(member.isfile() for member in members):
        raise ValueError("Duplicate Windows replay transport entry")
    file = lambda name: data[name]
    result = json.loads(file("result.json"))
    for name, local in {
        "Program.cs": SOURCE / "ArchiveFuzzHarness/Program.cs",
        "ArchiveFuzzHarness.csproj": SOURCE / "ArchiveFuzzHarness/ArchiveFuzzHarness.csproj",
        "packages.lock.json": SOURCE / "ArchiveFuzzHarness/packages.lock.json",
    }.items():
        if file("source/" + name) != local.read_bytes():
            raise ValueError("Windows replay source differs from the implementation: " + name)
    harness_packet = ROOT / ".tmp/fuzz-harness-v17.tar.gz"
    seeds_packet = ROOT / ".tmp/fz02-wrapper-seeds-v17.tar.gz"
    if result["packet_sha256"].lower() != sha(harness_packet.read_bytes()) or \
       result["seed_bundle_sha256"].lower() != sha(seeds_packet.read_bytes()):
        raise ValueError("Windows replay packet or corpus identity changed")
    bin_file = lambda name: file("source/bin/Release/net10.0/" + name)
    if result["core_sha256"].lower() != sha(bin_file("FileCat.Core.dll")) or \
       result["archives_sha256"].lower() != sha(bin_file("FileCat.Archives.dll")) or \
       result["harness_sha256"].lower() != sha(bin_file("FileCat.ArchiveFuzzHarness.dll")) or \
       result["instrumented_archives_sha256"].lower() != sha(file("image/FileCat.Archives.dll")) or \
       result["instrumented_archives_sha256"].lower() == result["archives_sha256"].lower():
        raise ValueError("Windows replay product or instrumentation binary changed")
    instrumented = json.loads(file("instrumented-types.json"))
    embedded = result["instrumented_types"]
    # Windows PowerShell 5.1 wraps a ConvertFrom-Json top-level array with value/Count
    # when that object is embedded in another ConvertTo-Json result. Verify both fields.
    if isinstance(embedded, dict):
        if set(embedded) != {"value", "Count"} or embedded["Count"] != len(instrumented):
            raise ValueError("The Windows JSON array wrapper changed")
        embedded = embedded["value"]
    if embedded != instrumented or not TYPES.issubset(instrumented):
        raise ValueError("Production strict wrapper instrumentation is incomplete")
    expected = []
    for target in TARGETS:
        seeds = variants(target)
        folder = "fz02-" + target.removesuffix("-strict") + "-seeds/"
        manifest = json.loads(file(folder + "manifest.json"))
        if manifest["target"] != target or {row["name"] for row in manifest["seeds"]} != set(seeds):
            raise ValueError("Windows wrapper seed inventory changed")
        for row in manifest["seeds"]:
            name = row["name"]
            raw, accepted = seeds[name]
            if row != {"name": name, "bytes": len(raw), "sha256": sha(raw), "accepted": accepted} or \
               file(folder + name) != raw:
                raise ValueError("Windows wrapper seed bytes changed")
            expected.append((target, row, accepted))
    if len(result["cases"]) != len(expected):
        raise ValueError("Windows wrapper replay omitted a case")
    for case, (target, seed, accepted) in zip(result["cases"], expected):
        if (case["target"], case["name"], case["sha256"].lower(), case["expected"],
            case["accepted"], case["returncode"]) != \
           (target, seed["name"], seed["sha256"], accepted, accepted, 0):
            raise ValueError("Windows wrapper replay outcome changed")
        output = json.loads(case["output"])
        if output != {"target": target, "accepted": accepted}:
            raise ValueError("Windows replay's raw output differs from its summarized result")
    build = guest_text(file("build.log"))
    if "Build succeeded." not in build or "0 Warning(s)" not in build or "0 Error(s)" not in build:
        raise ValueError("The Windows guest harness did not compile cleanly")
    instrument = guest_text(file("instrument.log"))
    if '"production_tar":true' not in instrument or '"production_sevenzip":true' not in instrument:
        raise ValueError("The Windows guest instrumentation command did not report its production assembly")
    return {"transport_sha256": expected_transport_sha256.lower(), "cases": len(expected),
            "accepted": sum(accepted for _, _, accepted in expected),
            "refused": sum(not accepted for _, _, accepted in expected),
            "instrumented_types": len(instrumented),
            "core_sha256": result["core_sha256"].lower(),
            "archives_sha256": result["archives_sha256"].lower()}


if __name__ == "__main__":
    print(json.dumps(verify(sys.argv[1]), indent=2))
