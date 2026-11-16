#!/usr/bin/env python3
"""Replay owned RAR5 seeds against sealed guest production bytes and check instrumentation."""

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
    parser.add_argument("--packet-name", default="fuzz-harness-v15.tar.gz")
    parser.add_argument("--password-target", action="store_true")
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    task = args.task.resolve(strict=True)
    packet = task / args.packet_name
    if sha(packet) != args.packet_sha256:
        raise ValueError("RAR5 fuzz packet changed")
    cohort = task / "awe-storage-v45"
    index = {row["path"]: row for row in json.loads((cohort / "files.json").read_text())["files"]}
    product = task / "archive-workflow-source-v1" / "tests" / "FileCat.Core.Tests" / "bin" / "Release" / "net10.0"
    out = task / args.out
    out.mkdir(exist_ok=False)
    image = out / "image"
    image.mkdir()
    with tarfile.open(packet) as tar:
        if any(not name.startswith(("harness/", "source/")) for name in tar.getnames()):
            raise ValueError("Unexpected RAR5 packet member")
        tar.extractall(out, filter="data")
    for source in (out / "harness").iterdir():
        shutil.copyfile(source, image / source.name)
    for name in DEPENDENCIES:
        source = product / name
        if name in ("FileCat.Core.dll", "FileCat.Archives.dll"):
            source = cohort / "binaries" / name
            if sha(source) != index["binaries/" + name]["sha256"]:
                raise ValueError("Guest binary differs from sealed v45 cohort: " + name)
        shutil.copyfile(source, image / name)
    seeds = out / "seeds"
    subprocess.run(["python3", str(out / "source" / "make_rar5_fuzz_seeds.py"),
                    "--fixtures", str(product / "TestData" / "Archives"),
                    "--generated", str(task / "fuzz-rar5-export-v15"), "--out", str(seeds),
                    *(["--password-target"] if args.password_target else [])], check=True)
    dotnet = task / "dotnet" / "dotnet"
    if not dotnet.is_file():
        dotnet = Path.home() / ".dotnet" / "dotnet"
    harness = image / "FileCat.ArchiveFuzzHarness.dll"
    results = []
    for seed in sorted(seeds.glob("*.rar")):
        completed = subprocess.run([str(dotnet), str(harness), "replay-format", "rar5-password" if args.password_target else "rar5-strict", str(seed)],
                                   capture_output=True, text=True, timeout=60 if args.password_target else 30)
        answer = json.loads(completed.stdout) if completed.returncode == 0 else None
        results.append({"name": seed.name, "sha256": sha(seed), "returncode": completed.returncode,
                        "accepted": None if answer is None else answer["accepted"],
                        "stderr": completed.stderr[-2000:]})
    if any(row["returncode"] != 0 for row in results) or sum(row["accepted"] is True for row in results) < (36 if args.password_target else 12):
        raise ValueError("RAR5 replay lacks all twelve valid transform seeds: " + json.dumps(results))
    types = out / "instrumented-types.json"
    completed = subprocess.run([str(dotnet), str(harness), "instrument-tar", str(image / "FileCat.Archives.dll"), str(types)],
                               capture_output=True, text=True, timeout=90)
    if completed.returncode:
        raise ValueError("RAR5 instrumentation failed: " + completed.stdout + completed.stderr)
    instrumented = json.loads(types.read_text())
    if not ({"FileCat.Archives.Writing.StrictRar5Catalog",
             "FileCat.Archives.Writing.StrictRar5Framing"} |
            ({"FileCat.Archives.Rar.Rar5ReadPasswordSession", "FileCat.Archives.Writing.Rar5EncryptionPrimitives",
              "FileCat.Archives.Writing.ArchiveCryptoWorkBudget"} if args.password_target else set())).issubset(instrumented):
        raise ValueError("Production RAR5 methods were absent from instrumentation")
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
