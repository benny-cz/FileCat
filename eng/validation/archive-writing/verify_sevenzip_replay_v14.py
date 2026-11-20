#!/usr/bin/env python3
"""Check guest 7z corpus/replay bytes without executing guest product code on the host."""

import hashlib
import io
import json
from pathlib import Path
import tarfile


ROOT = Path(__file__).resolve().parents[3]
TEMP = ROOT / ".tmp"
SEED_NAMES = {
    "EmptyEntries.7z", "EmptyEntries.solid.7z", "FileCat.Methods.Copy.7z",
    "FileCat.Methods.LZMA.7z", "FileCat.Methods.BZip2.7z",
    "FileCat.Methods.Deflate.7z", "FileCat.Methods.Deflate64.7z", "7Zip.LZMA2.7z",
    *(f"generated-codec-{i}.7z" for i in range(7)),
}
TYPES = {
    "FileCat.Archives.Writing.StrictSevenZipCatalog",
    "FileCat.Archives.Writing.StrictSevenZipFraming",
    "FileCat.Archives.Writing.SevenZipStrictMetadata",
    "FileCat.Archives.Writing.SevenZipStrictHeaderReader",
    "FileCat.Archives.Writing.StrictSevenZipFolderDecoder",
    "FileCat.Archives.Writing.StrictSevenZipHeaderDecoder",
}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def members(path):
    with tarfile.open(path) as tar:
        result = {}
        for member in tar:
            if member.isdir():
                continue
            if not member.isfile() or member.name in result:
                raise ValueError("Unreviewable or repeated transport member: " + member.name)
            result[member.name] = tar.extractfile(member).read()
    return result


def main():
    packet = TEMP / "fuzz-harness-v14.tar.gz"
    guest = TEMP / "storage-evidence" / "mac" / "fuzz-sevenzip-v14-mac-seeds.tar.gz"
    cohort = TEMP / "storage-evidence" / "mac" / "awe-storage-v45-evidence.tar.gz"
    packet_members = members(packet)
    evidence = members(guest)
    expected = {"result.json", "instrumented-types.json", "seeds/manifest.json"} | {
        "seeds/" + name for name in SEED_NAMES}
    if set(evidence) != expected:
        raise ValueError("7z replay transport member inventory changed")
    result = json.loads(evidence["result.json"])
    manifest = json.loads(evidence["seeds/manifest.json"])
    if result["packet_sha256"] != sha(packet.read_bytes()) or result["harness_sha256"] != sha(
            packet_members["harness/FileCat.ArchiveFuzzHarness.dll"]):
        raise ValueError("7z replay does not bind its exact test-only packet")
    binaries = members(cohort)
    for name, key in (("FileCat.Core.dll", "core_sha256"), ("FileCat.Archives.dll", "archives_sha256")):
        if result[key] != sha(binaries["awe-storage-v45/binaries/" + name]):
            raise ValueError("7z replay does not bind the sealed Mac production binary: " + name)
    if manifest != result["seeds"] or manifest["target"] != "sevenzip-strict":
        raise ValueError("7z seed manifest differs from the guest result")
    seeds = {row["name"]: row for row in manifest["seeds"]}
    if set(seeds) != SEED_NAMES or len(manifest["seeds"]) != len(SEED_NAMES):
        raise ValueError("7z corpus misses a required codec or adversarial fixture")
    for name, row in seeds.items():
        data = evidence["seeds/" + name]
        if row["bytes"] != len(data) or row["sha256"] != sha(data) or len(data) > 1048576:
            raise ValueError("7z seed byte identity changed: " + name)
    replay = {row["name"]: row for row in result["replay"]}
    if set(replay) != SEED_NAMES or len(result["replay"]) != len(SEED_NAMES):
        raise ValueError("7z replay input inventory differs from its seed corpus")
    for name, row in replay.items():
        if row["sha256"] != seeds[name]["sha256"] or row["returncode"] != 0 or row["stderr"]:
            raise ValueError("7z replay failed or did not use the retained seed: " + name)
        if row["accepted"] is not name.startswith("generated-codec-"):
            raise ValueError("7z replay accepted/refused the wrong corpus family: " + name)
    types = json.loads(evidence["instrumented-types.json"])
    if result["instrumented_types"] != types or not TYPES.issubset(types):
        raise ValueError("7z catalog/framing/codec instrumentation is incomplete")
    print(json.dumps({"packet_sha256": result["packet_sha256"], "transport_sha256": sha(guest.read_bytes()),
                      "accepted": 7, "refused": 8, "instrumented_types": len(types)}))


if __name__ == "__main__":
    main()
