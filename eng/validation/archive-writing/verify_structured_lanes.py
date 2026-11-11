#!/usr/bin/env python3
"""Verify one RID's structured-mutation lane evidence against the campaign image's seed folders (test tooling).

Usage: verify_structured_lanes.py LANES.tar.gz SEEDS.tar.gz MINIMUM_CPU_HOURS [SKIPPED_TARGET...]

Every seed folder of the image must have a complete report with no failure, whose seed inventory equals that folder
byte for byte (names, sizes, SHA-256; manifest.json excluded as the lane does), except the named skipped targets (the
Windows lanes skip the Unix publication route). The lanes' process CPU must reach the plan's per-RID floor (§13.6).
"""

import hashlib
import json
from pathlib import PurePosixPath
import sys
import tarfile


def main():
    lanes, seeds, minimum = sys.argv[1], sys.argv[2], float(sys.argv[3])
    skipped = set(sys.argv[4:])
    with tarfile.open(seeds, "r:gz") as archive:
        folders = {}
        for item in archive.getmembers():
            parts = PurePosixPath(item.name).parts
            if item.isfile() and len(parts) == 2 and parts[0].startswith("seeds-") and parts[1] != "manifest.json":
                data = archive.extractfile(item).read()
                folders.setdefault(parts[0][6:], {})[parts[1]] = (len(data), hashlib.sha256(data).hexdigest().upper())
    with tarfile.open(lanes, "r:gz") as archive:
        assert all(not item.issym() and not item.islnk() and ".." not in PurePosixPath(item.name).parts for item in archive.getmembers())
        reports = {PurePosixPath(item.name).stem: json.load(archive.extractfile(item)) for item in archive.getmembers()
                   if item.isfile() and PurePosixPath(item.name).parent.name == "reports" and item.name.endswith(".json")}
        machine = next((archive.extractfile(item).read().decode(errors="replace") for item in archive.getmembers()
                        if item.name.endswith("machine.txt")), "")
    expected = set(folders) - skipped
    assert set(reports) == expected, (sorted(set(reports) ^ expected))
    rows, cpu = [], 0.0
    for target, report in sorted(reports.items()):
        assert report["target"] == target and report["complete"] and not report["failure"], target
        inventory = {row["name"]: (row["bytes"], row["sha256"].upper()) for row in report["seed_inventory"]}
        assert inventory == folders[target], target
        assert report["calls"] == report["accepted"] + report["refused"] and report["calls"] > 0, target
        cpu += report["process_cpu_seconds"]
        rows.append({"target": target, "calls": report["calls"], "cpu_seconds": round(report["process_cpu_seconds"], 1),
                     "oracle_skipped": report.get("oracle_skipped", 0)})
    assert cpu >= minimum * 3600, cpu
    print(json.dumps({"lanes": lanes, "targets": len(rows), "skipped": sorted(skipped), "cpu_hours": round(cpu / 3600, 3),
                      "calls": sum(row["calls"] for row in rows), "failures": 0,
                      "machine": machine.splitlines()[:2], "rows": rows}, indent=2))


if __name__ == "__main__":
    main()
