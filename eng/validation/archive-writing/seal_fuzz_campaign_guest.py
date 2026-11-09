#!/usr/bin/env python3
"""Seal one finished guest-only fuzz run with its exact image and raw evidence."""

import argparse
import hashlib
import io
import json
from pathlib import Path
import tarfile


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--run", required=True)
    parser.add_argument("--packet", type=Path, required=True)
    parser.add_argument("--target", required=True)
    parser.add_argument("--minimum-cpu-seconds", type=int, required=True)
    args = parser.parse_args()
    if not args.image.replace("-", "").isalnum() or not args.run.replace("-", "").isalnum():
        parser.error("image and run must have simple alphanumeric names")
    task = args.task.resolve(strict=True)
    root = (task / args.image).resolve(strict=True)
    run = (root / "runs" / args.run).resolve(strict=True)
    packet = args.packet.resolve(strict=True)
    if root.parent != task or run.parent != root / "runs" or packet.parent != task:
        raise ValueError("Evidence paths must stay within the named guest task")
    prep = json.loads((root / "preparation.json").read_text())
    result = json.loads((run / "result.json").read_text())
    final = result["final_properties"]
    report = result["managed_report"]
    if not result["terminal"] or result.get("output_budget_failure") or result["artifacts"] or \
        result["phase"] != "clean" or result["target"] != args.target or \
        final["MainPID"] != "0" or final["Result"] != "success" or final["ExecMainStatus"] != "0" or \
        int(final["CPUUsageNSec"]) < args.minimum_cpu_seconds * 10**9 or \
        int(final["MemoryPeak"]) > 1073741824 or report["Target"] != args.target or \
        report["Calls"] != report["Accepted"] + report["Refused"] or report["Calls"] <= 0:
        raise ValueError("This fuzz run has not earned the requested clean CPU floor")
    if prep["packet_sha256"] != sha(packet) or result["sha256"]["core"] != sha(root / "clean" / "FileCat.Core.dll") or \
        result["sha256"]["harness"] != sha(root / "clean" / "FileCat.ArchiveFuzzHarness.dll") or \
        report["ModuleSha256"].lower() != result["sha256"]["core"]:
        raise ValueError("The fuzz run does not match its prepared image")
    if args.target == "tar-strict":
        names = {row["name"] for row in result["extra_seeds"]}
        if names != {"ustar-one-file.tar", "ustar-directory.tar", "ustar-duplicate.tar", "pax-unicode-time.tar"} or \
            report["SecondaryModuleSha256"].lower() != sha(root / "clean" / "FileCat.Archives.dll") or \
            not any("StrictTarCatalog::Read" in name for name in report["production_methods"]) or \
            not any("StrictTarCatalog::CompareDecoder" in name for name in report["production_methods"]):
            raise ValueError("The strict TAR and decoder coverage evidence is incomplete")
    if any((run / "crashes").iterdir()):
        raise ValueError("A crash artifact was found after result collection")
    if any(path.is_symlink() for path in run.rglob("*")):
        raise ValueError("A fuzz evidence entry is a symbolic link")
    members = [packet, root / "preparation.json", root / "instrumented-types-core-clean.json",
               root / "instrumented-types-tar-clean.json", root / "clean" / "FileCat.Core.dll",
               root / "clean" / "FileCat.Archives.dll", root / "clean" / "FileCat.ArchiveFuzzHarness.dll"]
    members += sorted(path for path in run.rglob("*") if path.is_file())
    if sum(path.stat().st_size for path in members) > 256 * 1048576:
        raise ValueError("Fuzz evidence exceeds its retained byte limit")
    rows = []
    for path in members:
        if path.is_symlink() or not path.resolve(strict=True).is_relative_to(task) or not path.is_file():
            raise ValueError("Unreviewable fuzz evidence member: " + str(path))
        name = "packet/" + path.name if path == packet else "image/" + path.relative_to(root).as_posix()
        rows.append({"path": name, "bytes": path.stat().st_size, "sha256": sha(path)})
    output = task / (args.image + "-" + args.run + "-evidence.tar.gz")
    index = json.dumps({"image": args.image, "run": args.run, "target": args.target,
                        "minimum_cpu_seconds": args.minimum_cpu_seconds, "files": rows}, indent=2).encode()
    with tarfile.open(output, "x:gz", compresslevel=1) as archive:
        for path, row in zip(members, rows):
            archive.add(path, arcname=row["path"], recursive=False)
        entry = tarfile.TarInfo("index.json")
        entry.size = len(index)
        archive.addfile(entry, io.BytesIO(index))
    sidecar = {"bytes": output.stat().st_size, "sha256": sha(output)}
    output.with_suffix(".json").write_text(json.dumps(sidecar, indent=2))
    print(json.dumps({"transport": str(output), **sidecar, "members": len(rows),
                      "cpu_seconds": int(final["CPUUsageNSec"]) / 10**9,
                      "executions": report["Calls"]}))


if __name__ == "__main__":
    main()
