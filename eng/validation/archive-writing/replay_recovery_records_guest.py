#!/usr/bin/env python3
"""Guest-only bounded replay of synthetic FZ09 seeds against guest-built product DLLs."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile


TARGETS = ("session-record", "capture-record", "save-copy-intent")
EXPECTED_ACCEPTED = {"session-record": {"remote-v1.json", "zip-v2.json", "remote-v3.json"},
                     "capture-record": {"start.json", "complete.json"},
                     "save-copy-intent": {"valid.json"}}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True)
    parser.add_argument("--packet-sha256", required=True)
    parser.add_argument("--seeds-sha256", required=True)
    parser.add_argument("--platform", choices=("unix", "windows"), required=True)
    args = parser.parse_args()
    task = args.task.resolve(strict=True)
    packet = task / "fuzz-harness-v6.tar.gz"
    seeds = task / ("recovery-record-seeds-" + args.platform + "-v6.tar.gz")
    if sha(packet) != args.packet_sha256 or sha(seeds) != args.seeds_sha256:
        raise ValueError("FZ09 replay packet or seed hash changed")
    out = task / "recovery-record-replay-v6"
    out.mkdir(exist_ok=False)
    for source in (packet, seeds):
        with tarfile.open(source) as tar:
            names = tar.getnames()
            allowed = ("harness/", "source/") if source == packet else ("seeds/",)
            if any(not name.startswith(allowed) for name in names):
                raise ValueError("Unexpected replay packet member")
            tar.extractall(out, filter="data")
    harness = out / "harness"
    product = task / "archive-workflow-source-v1" / "tests" / "FileCat.Core.Tests" / "bin" / "Release" / "net10.0"
    verified = task / "awe-storage-v38" / "binaries" / "FileCat.Core.dll"
    if sha(product / "FileCat.Core.dll") != sha(verified):
        raise ValueError("Guest product Core differs from the sealed v38 binary")
    for file in product.iterdir():
        if file.suffix == ".dll" and (file.name.startswith("FileCat.") or file.name.startswith("DiscUtils.")):
            target = harness / file.name
            if not target.exists():
                shutil.copyfile(file, target)
    dotnet = task / "dotnet" / ("dotnet.exe" if os.name == "nt" else "dotnet")
    if os.name != "nt" and "darwin" in os.uname().sysname.lower():
        dotnet = Path("/Users/benny/.dotnet/dotnet")
    environment = os.environ.copy()
    environment["DOTNET_EnableDiagnostics"] = "0"
    results = []
    for target in TARGETS:
        folder = out / "seeds" / target
        manifest = json.loads((folder / "manifest.json").read_text())
        if manifest["target"] != target or manifest["platform"] != args.platform:
            raise ValueError("Seed target/platform mismatch")
        listed = {item["name"] for item in manifest["seeds"]}
        if listed != {file.name for file in folder.glob("*.json") if file.name != "manifest.json"}:
            raise ValueError("Seed list changed")
        for item in manifest["seeds"]:
            sample = folder / item["name"]
            if sample.stat().st_size != item["bytes"] or sha(sample) != item["sha256"]:
                raise ValueError("Seed bytes changed")
            command = [str(dotnet), str(harness / "FileCat.ArchiveFuzzHarness.dll"),
                       "replay-record", target, str(sample)]
            process = subprocess.run(command, cwd=out, env=environment, capture_output=True, text=True, timeout=30)
            stdout = process.stdout.strip()
            accepted = json.loads(stdout)["accepted"] if process.returncode == 0 else None
            expected = item["name"] in EXPECTED_ACCEPTED[target]
            row = {"target": target, "seed": item["name"], "seed_sha256": item["sha256"],
                   "expected_accepted": expected, "accepted": accepted,
                   "returncode": process.returncode, "stdout": stdout[:4096], "stderr": process.stderr[:4096]}
            results.append(row)
            (out / "progress.json").write_text(json.dumps(results, indent=2))
            if process.returncode != 0 or accepted != expected:
                raise RuntimeError("Recovery-record replay failed: " + target + "/" + item["name"])
    facts = {"candidate": "a59c7d2aa5515cb81f6c59a2af8a0e138a2437b8", "platform": args.platform,
             "packet_sha256": args.packet_sha256, "seeds_sha256": args.seeds_sha256,
             "product_core_sha256": sha(verified), "runner_sha256": sha(Path(__file__)),
             "replays": results, "passed": len(results), "failed": 0}
    (out / "result.json").write_text(json.dumps(facts, indent=2))
    print(json.dumps({"passed": len(results), "failed": 0, "result": str(out / "result.json")}))


if __name__ == "__main__":
    main()
