#!/usr/bin/env python3
"""Verify Mac password replay transport against packet and sealed product binaries."""

import hashlib
import json
from pathlib import Path
import sys
import tarfile


ROOT = Path(__file__).resolve().parents[3] / ".tmp"


def sha(data):
    return hashlib.sha256(data).hexdigest()


def members(path):
    with tarfile.open(path) as archive:
        result = {}
        for item in archive:
            if item.isdir():
                continue
            if not item.isfile() or item.name in result:
                raise ValueError("Unreviewable password replay transport member: " + item.name)
            result[item.name] = archive.extractfile(item).read()
    return result


def main(kind):
    if kind not in ("sevenzip", "rar5"):
        raise ValueError("Expected sevenzip or rar5")
    target = kind + "-password"
    folder = f"fuzz-{kind}-password-v16"
    packet = ROOT / "fuzz-harness-v16.tar.gz"
    guest = ROOT / "storage-evidence" / "mac" / (folder + "-mac-seeds.tar.gz")
    cohort = ROOT / "storage-evidence" / "mac" / "awe-storage-v45-evidence.tar.gz"
    sidecar = json.loads(guest.with_suffix(".json").read_text())
    if sidecar != {"bytes": guest.stat().st_size, "sha256": sha(guest.read_bytes()), "target": target}:
        raise ValueError("Password replay transport sidecar differs from its exact bytes")
    packet_data, data, product = members(packet), members(guest), members(cohort)
    result = json.loads(data["result.json"])
    manifest = json.loads(data["seeds/manifest.json"])
    if result["packet_sha256"] != sha(packet.read_bytes()) or result["harness_sha256"] != sha(packet_data["harness/FileCat.ArchiveFuzzHarness.dll"]):
        raise ValueError("Password replay does not bind the test-only harness")
    for name, key in (("FileCat.Core.dll", "core_sha256"), ("FileCat.Archives.dll", "archives_sha256")):
        if result[key] != sha(product["awe-storage-v45/binaries/" + name]):
            raise ValueError("Password replay does not bind sealed Mac product bytes: " + name)
    if manifest != result["seeds"] or manifest["target"] != target:
        raise ValueError("Password replay and corpus disagree")
    base = {"EmptyEntries.7z", "EmptyEntries.solid.7z", "FileCat.Methods.Copy.7z",
            "FileCat.Methods.LZMA.7z", "FileCat.Methods.BZip2.7z", "FileCat.Methods.Deflate.7z",
            "FileCat.Methods.Deflate64.7z", "7Zip.LZMA2.7z"} if kind == "sevenzip" else {"Rar4.rar", "Rar.malformed_512byte.rar"}
    generated = {f"generated-codec-{codec}-protection-{protection}.7z" for codec in range(7) for protection in range(3)} if kind == "sevenzip" else {
        f"generated-method-{method}-mode-{mode}-{stage}.rar" for method in range(6) for mode in range(3) for stage in ("source", "rebuilt")}
    names = base | generated
    if set(data) != {"result.json", "instrumented-types.json", "seeds/manifest.json"} | {"seeds/" + name for name in names}:
        raise ValueError("Password replay transport inventory changed")
    rows = {row["name"]: row for row in manifest["seeds"]}
    replay = {row["name"]: row for row in result["replay"]}
    if set(rows) != names or len(manifest["seeds"]) != len(names) or set(replay) != names or len(result["replay"]) != len(names):
        raise ValueError("Password replay seed inventory changed")
    for name in names:
        seed, row, content = rows[name], replay[name], data["seeds/" + name]
        if seed["bytes"] != len(content) or seed["sha256"] != sha(content) or len(content) > 1048576 or \
                row["sha256"] != seed["sha256"] or row["returncode"] != 0 or row["stderr"] or \
                row["accepted"] is not (name in generated):
            raise ValueError("Password replay outcome or seed byte mismatch: " + name)
    types = json.loads(data["instrumented-types.json"])
    common = {"FileCat.Archives.Writing.ArchiveCryptoWorkBudget"}
    specific = {"FileCat.Archives.Writing.StrictSevenZipCatalog", "FileCat.Archives.Writing.StrictSevenZipFraming",
                "FileCat.Archives.SevenZipReadPasswordSession", "FileCat.Archives.Writing.SevenZipEncryptionPrimitives"} if kind == "sevenzip" else {
                "FileCat.Archives.Writing.StrictRar5Catalog", "FileCat.Archives.Writing.StrictRar5Framing",
                "FileCat.Archives.Rar.Rar5ReadPasswordSession", "FileCat.Archives.Writing.Rar5EncryptionPrimitives"}
    if result["instrumented_types"] != types or not (common | specific).issubset(types):
        raise ValueError("Password parser/KDF instrumentation is incomplete")
    print(json.dumps({"target": target, "accepted": len(generated), "refused": len(base),
                      "packet_sha256": sha(packet.read_bytes()), "transport_sha256": sha(guest.read_bytes()),
                      "instrumented_types": len(types)}))


if __name__ == "__main__":
    main(sys.argv[1])
