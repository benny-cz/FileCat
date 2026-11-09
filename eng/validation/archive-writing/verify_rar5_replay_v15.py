#!/usr/bin/env python3
"""Check guest RAR5 corpus and replay bytes without host-side product execution."""

import hashlib
import json
from pathlib import Path
import tarfile


ROOT = Path(__file__).resolve().parents[3]
TEMP = ROOT / ".tmp"
NAMES = {"Rar4.rar", "Rar.malformed_512byte.rar"} | {
    f"generated-method-{method}-{stage}.rar"
    for method in range(6) for stage in ("source", "rebuilt")
}
TYPES = {"FileCat.Archives.Writing.StrictRar5Catalog",
         "FileCat.Archives.Writing.StrictRar5Framing"}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def members(path):
    with tarfile.open(path) as archive:
        found = {}
        for item in archive:
            if item.isdir():
                continue
            if not item.isfile() or item.name in found:
                raise ValueError("Unreviewable or duplicate RAR evidence member: " + item.name)
            found[item.name] = archive.extractfile(item).read()
    return found


def main():
    packet = TEMP / "fuzz-harness-v15.tar.gz"
    guest = TEMP / "storage-evidence" / "mac" / "fuzz-rar5-v15-mac-seeds.tar.gz"
    cohort = TEMP / "storage-evidence" / "mac" / "awe-storage-v45-evidence.tar.gz"
    packet_data, evidence = members(packet), members(guest)
    expected = {"result.json", "instrumented-types.json", "seeds/manifest.json"} | {
        "seeds/" + name for name in NAMES}
    if set(evidence) != expected:
        raise ValueError("RAR5 replay transport member inventory changed")
    result = json.loads(evidence["result.json"])
    manifest = json.loads(evidence["seeds/manifest.json"])
    if result["packet_sha256"] != sha(packet.read_bytes()) or result["harness_sha256"] != sha(
            packet_data["harness/FileCat.ArchiveFuzzHarness.dll"]):
        raise ValueError("RAR5 replay did not use the test-only packet")
    binaries = members(cohort)
    for name, key in (("FileCat.Core.dll", "core_sha256"), ("FileCat.Archives.dll", "archives_sha256")):
        if result[key] != sha(binaries["awe-storage-v45/binaries/" + name]):
            raise ValueError("RAR5 replay did not use the sealed Mac production binary: " + name)
    if manifest != result["seeds"] or manifest["target"] != "rar5-strict":
        raise ValueError("RAR5 result and seed manifest disagree")
    seeds = {row["name"]: row for row in manifest["seeds"]}
    replay = {row["name"]: row for row in result["replay"]}
    if set(seeds) != NAMES or len(manifest["seeds"]) != len(NAMES) or \
            set(replay) != NAMES or len(result["replay"]) != len(NAMES):
        raise ValueError("RAR5 corpus/replay inventory changed")
    for name in NAMES:
        data, seed, row = evidence["seeds/" + name], seeds[name], replay[name]
        if seed["bytes"] != len(data) or seed["sha256"] != sha(data) or len(data) > 1048576 or \
                row["sha256"] != seed["sha256"] or row["returncode"] != 0 or row["stderr"] or \
                row["accepted"] is not name.startswith("generated-"):
            raise ValueError("RAR5 seed or replay outcome changed: " + name)
    types = json.loads(evidence["instrumented-types.json"])
    if result["instrumented_types"] != types or not TYPES.issubset(types):
        raise ValueError("Production RAR5 framing/catalog was not instrumented")
    print(json.dumps({"packet_sha256": result["packet_sha256"], "transport_sha256": sha(guest.read_bytes()),
                      "accepted": 12, "refused": 2, "instrumented_types": len(types)}))


if __name__ == "__main__":
    main()
