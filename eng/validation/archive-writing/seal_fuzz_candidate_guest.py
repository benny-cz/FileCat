#!/usr/bin/env python3
"""Seal one finished fuzz run of a candidate-bound image (prepare_fuzz_candidate_guest.py) with its raw evidence.

Guest-side collection: the run must be terminal, clean, crash-free and at least as long as the requested CPU floor, and its
canary, canary replay and short qualification runs must belong to the same image. The transport carries the packet, the
image's preparation record, instrumented-type reports, clean binaries, the target's synthetic seed folder and every file of
the four runs. verify_fuzz_candidate_transport.py re-derives every semantic check (methods, seeds, instrumentation, floors)
from these raw files on the host. Test tooling, not an application dependency.
"""

import argparse
import hashlib
import io
import json
from pathlib import Path
import tarfile


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def simple(name):
    return name.replace("-", "").isalnum()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--run", required=True)
    parser.add_argument("--packet", type=Path, required=True)
    parser.add_argument("--target", required=True)
    parser.add_argument("--minimum-cpu-seconds", type=int, required=True)
    parser.add_argument("--canary-run", required=True)
    parser.add_argument("--canary-replay-run", required=True)
    parser.add_argument("--short-run", required=True)
    args = parser.parse_args()
    qualifications = (args.canary_run, args.canary_replay_run, args.short_run)
    if not all(simple(name) for name in (args.image, args.run, args.target) + qualifications) or \
            len({args.run, *qualifications}) != 4:
        parser.error("image, target and four distinct runs must have simple alphanumeric names")
    task = args.task.resolve(strict=True)
    root = (task / args.image).resolve(strict=True)
    packet = args.packet.resolve(strict=True)
    runs = [(root / "runs" / name).resolve(strict=True) for name in (args.run,) + qualifications]
    if root.parent != task or packet.parent != task or any(run.parent != root / "runs" for run in runs):
        raise ValueError("Evidence paths must stay within the named guest task")
    prep = json.loads((root / "preparation.json").read_text())
    if len(prep.get("candidate", "")) != 40 or prep["packet_sha256"] != sha(packet):
        raise ValueError("The image is not a candidate-bound image of this packet")
    result, canary, replay, short = [json.loads((run / "result.json").read_text()) for run in runs]
    final = result["final_properties"]
    report = result["managed_report"]
    if not result["terminal"] or result.get("output_budget_failure") or result["artifacts"] or \
            result["phase"] != "clean" or result["target"] != args.target or \
            final["MainPID"] != "0" or final["Result"] != "success" or final["ExecMainStatus"] != "0" or \
            int(final["CPUUsageNSec"]) < args.minimum_cpu_seconds * 10**9 or \
            int(final["MemoryPeak"]) > 1073741824 or report["Target"] != args.target or \
            report["Calls"] != report["Accepted"] + report["Refused"] or report["Calls"] <= 0:
        raise ValueError("This fuzz run has not earned the requested clean CPU floor")
    clean = prep["images"]["clean"]
    if any(result["sha256"][key] != clean[name]["sha256"] for key, name in
           (("core", "FileCat.Core.dll"), ("harness", "FileCat.ArchiveFuzzHarness.dll"),
            ("archives", "FileCat.Archives.dll"), ("codecs", "FileCat.SharpCompress.dll"))) or \
            report["ModuleSha256"].lower() != result["sha256"]["core"]:
        raise ValueError("The fuzz run does not match its prepared image")
    if (canary["phase"], canary["target"], replay["phase"], replay["target"]) != ("canary", "digest", "canary", "digest") or \
            (short["phase"], short["target"]) != ("clean", args.target) or not short["terminal"] or short["artifacts"]:
        raise ValueError("A qualification run has another phase or target")
    for run in runs[:1] + runs[3:]:
        if any((run / "crashes").iterdir()) or any((run / "dumps").iterdir()):
            raise ValueError("A crash artifact or dump was found after result collection")
    seeds = root / ("seeds-" + args.target)
    members = [packet, root / "preparation.json"]
    members += [root / f"instrumented-types-{kind}-clean.json" for kind in ("core", "tar", "codecs")]
    members += [root / "clean" / name for name in ("FileCat.Core.dll", "FileCat.Archives.dll", "FileCat.SharpCompress.dll",
                                                   "FileCat.ArchiveFuzzHarness.dll")]
    if seeds.is_dir():
        members += sorted(path for path in seeds.iterdir() if path.is_file())
    for run in runs:
        members += sorted(path for path in run.rglob("*") if path.is_file())
    if any(path.is_symlink() for run in runs for path in run.rglob("*")) or \
            sum(path.stat().st_size for path in members) > 256 * 1048576:
        raise ValueError("Fuzz evidence has a symbolic link or exceeds its retained byte limit")
    rows = []
    for path in members:
        if path.is_symlink() or not path.resolve(strict=True).is_relative_to(task) or not path.is_file():
            raise ValueError("Unreviewable fuzz evidence member: " + str(path))
        name = "packet/" + path.name if path == packet else "image/" + path.relative_to(root).as_posix()
        rows.append({"path": name, "bytes": path.stat().st_size, "sha256": sha(path)})
    output = task / (args.image + "-" + args.run + "-evidence.tar.gz")
    index = json.dumps({"image": args.image, "run": args.run, "target": args.target,
                        "minimum_cpu_seconds": args.minimum_cpu_seconds, "qualification_runs": list(qualifications),
                        "candidate": prep["candidate"], "sealer_sha256": sha(Path(__file__).resolve()),
                        "files": rows}, indent=2).encode()
    with tarfile.open(output, "x:gz", compresslevel=1) as archive:
        for path, row in zip(members, rows):
            archive.add(path, arcname=row["path"], recursive=False)
        entry = tarfile.TarInfo("index.json")
        entry.size = len(index)
        archive.addfile(entry, io.BytesIO(index))
    sidecar = {"bytes": output.stat().st_size, "sha256": sha(output)}
    output.with_suffix(".json").write_text(json.dumps(sidecar, indent=2))
    print(json.dumps({"transport": str(output), **sidecar, "members": len(rows),
                      "cpu_seconds": int(final["CPUUsageNSec"]) / 10**9, "executions": report["Calls"]}))


if __name__ == "__main__":
    main()
