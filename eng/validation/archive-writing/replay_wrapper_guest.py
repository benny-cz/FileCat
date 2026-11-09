#!/usr/bin/env python3
"""Replay FZ02 seeds against guest-built production wrappers and check instrumentation."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess


TARGETS = ("gzip-strict", "bzip2-strict", "zstd-strict")
TYPES = {"FileCat.Archives.Writing.StrictGzipReadStream",
         "FileCat.Archives.Writing.StrictBzip2ReadStream",
         "FileCat.Archives.Writing.StrictZstdReadStream",
         "FileCat.Archives.Writing.ZstdFrameHeader"}


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    task = args.task.resolve(strict=True)
    out = task / args.out
    if out.resolve(strict=True).parent != task / "fuzz-wrapper-v17" or out.name != "result":
        raise ValueError("The wrapper replay must use its dedicated guest result directory")
    source = out.parent / "source"
    built = source / "bin/Release/net10.0"
    image = out / "image"
    image.mkdir(exist_ok=False)
    for path in built.iterdir():
        if path.is_file() and not path.is_symlink():
            shutil.copyfile(path, image / path.name)
    harness = image / "FileCat.ArchiveFuzzHarness.dll"
    archives = image / "FileCat.Archives.dll"
    core = image / "FileCat.Core.dll"
    dotnet = Path.home() / ".dotnet/dotnet"
    if not all(path.is_file() for path in (harness, archives, core, dotnet)):
        raise FileNotFoundError("A guest-built wrapper replay input is missing")
    original_archives = sha(archives)
    results = []
    manifests = {}
    for target in TARGETS:
        seeds = out / ("fz02-" + target.removesuffix("-strict") + "-seeds")
        manifest = json.loads((seeds / "manifest.json").read_text())
        if manifest["target"] != target:
            raise ValueError("Wrapper seed target changed")
        manifests[target] = manifest
        for row in manifest["seeds"]:
            file = seeds / row["name"]
            if file.is_symlink() or file.stat().st_size != row["bytes"] or sha(file) != row["sha256"]:
                raise ValueError("Wrapper seed bytes changed")
            completed = subprocess.run([str(dotnet), str(harness), "replay-format", target, str(file)],
                                       capture_output=True, text=True, timeout=30)
            answer = json.loads(completed.stdout) if completed.returncode == 0 else None
            result = {"target": target, "name": row["name"], "sha256": row["sha256"],
                      "expected": row["accepted"], "accepted": answer["accepted"] if answer else None,
                      "returncode": completed.returncode, "stderr": completed.stderr[-2000:]}
            results.append(result)
            if result["returncode"] != 0 or result["accepted"] != result["expected"]:
                (out / "result.json").write_text(json.dumps({"replay": results, "manifests": manifests}, indent=2))
                raise ValueError("Wrapper replay failed: " + json.dumps(result))
    completed = subprocess.run([str(dotnet), str(harness), "instrument-tar", str(archives),
                                str(out / "instrumented-types.json")], capture_output=True, text=True, timeout=90)
    if completed.returncode:
        (out / "instrumentation-error.txt").write_text(completed.stdout + completed.stderr)
        raise ValueError("Production wrapper instrumentation failed")
    instrumented = json.loads((out / "instrumented-types.json").read_text())
    if not TYPES.issubset(instrumented) or sha(archives) == original_archives:
        raise ValueError("Strict wrapper methods were not instrumented")
    evidence = {"source_sha256": {name: sha(source / name) for name in
                                  ("Program.cs", "ArchiveFuzzHarness.csproj", "packages.lock.json",
                                   "make_wrapper_fuzz_seeds.py", "replay_wrapper_guest.py")},
                "core_sha256": sha(core), "archives_sha256": original_archives,
                "instrumented_archives_sha256": sha(archives), "harness_sha256": sha(harness),
                "manifests": manifests, "replay": results, "instrumented_types": instrumented}
    (out / "result.json").write_text(json.dumps(evidence, indent=2))
    print(json.dumps({"accepted": sum(row["accepted"] for row in results),
                      "refused": sum(not row["accepted"] for row in results),
                      "instrumented_types": len(instrumented)}))


if __name__ == "__main__":
    main()
