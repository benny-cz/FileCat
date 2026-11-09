#!/usr/bin/env python3
"""Replay small 7z seeds against guest-built production bytes, then check instrumentation."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tarfile


DEPENDENCIES = (
    "FileCat.Core.dll", "FileCat.Archives.dll", "FileCat.Archives.Rar15.dll",
    "FileCat.SharpCompress.dll", "DiscUtils.Core.dll", "DiscUtils.Streams.dll",
    "DiscUtils.Iso9660.dll", "DiscUtils.Udf.dll",
)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True)
    parser.add_argument("--packet-sha256", required=True)
    parser.add_argument("--packet-name", default="fuzz-harness-v14.tar.gz")
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    task = args.task.resolve(strict=True)
    packet = task / args.packet_name
    if sha(packet) != args.packet_sha256:
        raise ValueError("7z fuzz packet changed")
    cohort = task / "awe-storage-v45"
    index = {row["path"]: row for row in json.loads((cohort / "files.json").read_text())["files"]}
    product = task / "archive-workflow-source-v1" / "tests" / "FileCat.Core.Tests" / "bin" / "Release" / "net10.0"
    out = task / args.out
    out.mkdir(exist_ok=False)
    image = out / "image"
    image.mkdir()
    with tarfile.open(packet) as tar:
        names = tar.getnames()
        if any(not n.startswith(("harness/", "source/")) for n in names):
            raise ValueError("Unexpected 7z fuzz packet member")
        tar.extractall(out, filter="data")
    for path in (out / "harness").iterdir():
        shutil.copyfile(path, image / path.name)
    for name in DEPENDENCIES:
        source = product / name
        if name in ("FileCat.Core.dll", "FileCat.Archives.dll"):
            expected = index["binaries/" + name]["sha256"]
            source = cohort / "binaries" / name
            if sha(source) != expected:
                raise ValueError("Guest product binary differs from sealed v45 cohort: " + name)
        shutil.copyfile(source, image / name)
    fixtures = product / "TestData" / "Archives"
    seeds = out / "seeds"
    subprocess.run(["python3", str(out / "source" / "make_sevenzip_fuzz_seeds.py"),
                    "--fixtures", str(fixtures), "--generated", str(task / "fuzz-sevenzip-export-v13"),
                    "--out", str(seeds)], check=True)
    dotnet = task / "dotnet" / "dotnet"
    if not dotnet.is_file():
        dotnet = Path.home() / ".dotnet" / "dotnet"
    harness = image / "FileCat.ArchiveFuzzHarness.dll"
    results = []
    for seed in sorted(seeds.glob("*.7z")):
        command = [str(dotnet), str(harness), "replay-format", "sevenzip-strict", str(seed)]
        completed = subprocess.run(command, capture_output=True, text=True, timeout=30)
        answer = json.loads(completed.stdout) if completed.returncode == 0 else None
        results.append({"name": seed.name, "sha256": sha(seed), "returncode": completed.returncode,
                        "accepted": None if answer is None else answer["accepted"],
                        "stderr": completed.stderr[-2000:]})
    if any(row["returncode"] != 0 for row in results) or sum(row["accepted"] is True for row in results) < 3:
        raise ValueError("7z production replay lacks three valid seeded paths: " + json.dumps(results))
    types = out / "instrumented-types.json"
    completed = subprocess.run([str(dotnet), str(harness), "instrument-tar", str(image / "FileCat.Archives.dll"), str(types)],
                               capture_output=True, text=True, timeout=90)
    if completed.returncode:
        raise ValueError("7z instrumentation failed: " + completed.stdout + completed.stderr)
    instrumented = json.loads(types.read_text())
    if not {"FileCat.Archives.Writing.StrictSevenZipCatalog",
            "FileCat.Archives.Writing.StrictSevenZipFraming",
            "FileCat.Archives.Writing.SevenZipStrictMetadata",
            "FileCat.Archives.Writing.SevenZipStrictHeaderReader"}.issubset(instrumented):
        raise ValueError("Production 7z methods were absent from instrumentation")
    evidence = {"packet_sha256": sha(packet), "core_sha256": sha(cohort / "binaries" / "FileCat.Core.dll"),
                "archives_sha256": sha(cohort / "binaries" / "FileCat.Archives.dll"),
                "instrumented_archives_sha256": sha(image / "FileCat.Archives.dll"),
                "harness_sha256": sha(harness), "seeds": json.loads((seeds / "manifest.json").read_text()),
                "replay": results, "instrumented_types": instrumented}
    (out / "result.json").write_text(json.dumps(evidence, indent=2))
    print(json.dumps({"accepted": sum(row["accepted"] is True for row in results),
                      "refused": sum(row["accepted"] is False for row in results),
                      "instrumented_types": len(instrumented)}))


if __name__ == "__main__":
    main()
