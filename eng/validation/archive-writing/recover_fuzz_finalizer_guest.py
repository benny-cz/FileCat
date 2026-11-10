#!/usr/bin/env python3
"""Recover a finished guest fuzz run whose supervisor lost only its final result write.

This is a narrow evidence repair, not a fuzz rerun or a waiver of the CPU,
resource, crash, image or coverage checks performed by the normal sealer.
"""

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def properties(unit):
    names = ("ActiveState", "SubState", "MainPID", "ExecMainStatus", "Result",
             "CPUUsageNSec", "MemoryPeak", "ControlGroup", "NRestarts")
    command = ["systemctl", "show", unit, *(part for name in names for part in ("-p", name))]
    output = subprocess.check_output(command, text=True)
    return dict(line.split("=", 1) for line in output.splitlines() if "=" in line)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--run", required=True)
    parser.add_argument("--failure-sha256", required=True)
    parser.add_argument("--minimum-cpu-seconds", type=int, required=True)
    args = parser.parse_args()
    if any(not value.replace("-", "").isalnum() for value in (args.image, args.run)):
        parser.error("Image and run must be simple names")
    task = args.task.resolve(strict=True)
    root = (task / args.image).resolve(strict=True)
    run = (root / "runs" / args.run).resolve(strict=True)
    if root.parent != task or run.parent != root / "runs":
        raise ValueError("The run is outside its named task image")
    if (run / "result.json").exists():
        raise ValueError("A result already exists; recovery cannot overwrite it")
    failure = task / f"{args.run}-launch.log"
    if sha(failure) != args.failure_sha256.lower():
        raise ValueError("The original supervisor failure log changed")
    failure_text = failure.read_text()
    if "FileNotFoundError" not in failure_text or "reports/managed.json.tmp" not in failure_text or \
       "sum(f.stat().st_size for f in run.rglob" not in failure_text:
        raise ValueError("The supervisor did not fail at the known atomic-report race")
    command = json.loads((run / "command.json").read_text())
    unit = "filecat-archive-fuzz-" + args.run
    if (command["unit"], command["target"], command["phase"], command["seconds"]) != \
       (unit, "zstd-strict", "clean", 12000):
        raise ValueError("This recovery is limited to the completed Zstd long run")
    image = root / "clean"
    for name, path in {
        "driver": root / "libfuzzer-dotnet",
        "managed": task / "dotnet/dotnet",
        "core": image / "FileCat.Core.dll",
        "harness": image / "FileCat.ArchiveFuzzHarness.dll",
        "archives": image / "FileCat.Archives.dll",
    }.items():
        if command["sha256"][name] != sha(path):
            raise ValueError("The frozen run image changed: " + name)
    final = properties(unit)
    if (final["ActiveState"], final["SubState"], final["MainPID"], final["Result"],
        final["ExecMainStatus"], final["NRestarts"]) != ("active", "exited", "0", "success", "0", "0") or \
       int(final["CPUUsageNSec"]) < args.minimum_cpu_seconds * 10**9 or \
       int(final["MemoryPeak"]) > 1073741824 or final["ControlGroup"] != "/system.slice/" + unit + ".service":
        raise ValueError("The retained native unit did not finish cleanly within its hard budget")
    report = json.loads((run / "reports/managed.json").read_text())
    if report["Target"] != "zstd-strict" or report["Calls"] <= 0 or \
       report["Calls"] != report["Accepted"] + report["Refused"] or \
       report["ModuleSha256"].lower() != command["sha256"]["core"] or \
       report["SecondaryModuleSha256"].lower() != command["sha256"]["archives"]:
        raise ValueError("The managed production report does not match the frozen image or calls")
    driver_log = run / "driver.log"
    match = re.search(r"Done (\d+) runs in (\d+) second\(s\)", driver_log.read_text())
    if match is None or int(match[1]) != report["Calls"] or not 12000 <= int(match[2]) <= 12030:
        raise ValueError("The native driver did not record the complete wall campaign")
    if list((run / "crashes").iterdir()):
        raise ValueError("A crash artifact exists")
    if any(path.is_symlink() for path in run.rglob("*")):
        raise ValueError("A run entry is a symbolic link")
    if sum(path.stat().st_size for path in run.rglob("*") if path.is_file()) > 256 * 1048576:
        raise ValueError("The retained run exceeds its output budget")

    shutil.copyfile(failure, run / "finalizer-failure.log")
    shutil.copyfile(__file__, run / "finalizer-recovery.py")
    shutil.copyfile(task / "run_fuzz_guest_v17.py", run / "original-supervisor.py")
    command["terminal"] = True
    command["output_budget_failure"] = False
    command["final_properties"] = final
    command["artifacts"] = []
    command["managed_report"] = report
    command["post_run_recovery"] = {
        "reason": "supervisor atomic-report stat race after driver exit",
        "failure_log_sha256": args.failure_sha256.lower(),
        "driver_log_sha256": sha(driver_log),
        "per_second_observations_lost": True,
    }
    command["files"] = {str(path.relative_to(run)): path.stat().st_size
                        for path in run.rglob("*") if path.is_file()}
    result = run / "result.json"
    temporary = run / "result.json.tmp"
    temporary.write_text(json.dumps(command, indent=2))
    temporary.replace(result)
    print(json.dumps({"result": str(result), "cpu_seconds": int(final["CPUUsageNSec"]) / 10**9,
                      "executions": report["Calls"], "post_run_recovery": command["post_run_recovery"]}))


if __name__ == "__main__":
    main()
