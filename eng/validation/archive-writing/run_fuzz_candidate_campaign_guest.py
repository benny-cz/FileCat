#!/usr/bin/env python3
"""Plan and run the long native fuzz campaign on one candidate image (test tooling, not evidence by itself).

The defaults name the image this scheduler ran on 11 November 2026: fuzz-candidate-v4, prepared by
prepare_fuzz_candidate_guest.py from candidate 62f0a457, with its harness packet; a later image passes --image NAME and
--packet PATH (its run ids keep the -long-c4- form the sealer and verifier expect). Each finished run is sealed by
seal_fuzz_candidate_guest.py at its planned floor; verify_fuzz_candidate_transport.py re-checks every transport and the
family floors on the host. The scheduler runs this folder's run_fuzz_guest.py and sealer.

plan [--image NAME --packet PATH --targets A,B --seed-base N]:    read each target's short run, chunk its CPU
       allocation into runs of at most CHUNK CPU-hours, size each run's wall time from the short run's CPU rate with a
       margin, and write campaign-plan.json; --targets plans only the named targets (a family repeated after a change
       to its code), and a new image takes a new --seed-base so its runs do not repeat the earlier mutation sequences.
run SLOTS [--image NAME --packet PATH]:    keep SLOTS units busy, longest first; seal each finished run at its planned CPU
       floor; record campaign-status.json.
"""
import json
import math
import pathlib
import subprocess
import sys
import time

T = pathlib.Path.home() / "FileCatValidation/archive-writing-42829297"
S = pathlib.Path(__file__).resolve().parent
arguments = sys.argv[1:]


def option(name, default):
    if name not in arguments:
        return default
    at = arguments.index(name)
    value = arguments[at + 1]
    del arguments[at:at + 2]
    return value


I = T / option("--image", "fuzz-candidate-v4")
PACKET = pathlib.Path(option("--packet", str(T / "fuzz-candidate-62f0a457-harness.tar.gz")))
ONLY = [target for target in option("--targets", "").split(",") if target]
SEED_BASE = int(option("--seed-base", "20261801"))
HOUR = 3600
CHUNK = 4.3
MARGIN = 1.18
# CPU-hours per target: every family reaches 25 or more (plan floor 24), every target 2.3 or more (floor 2).
ALLOCATION = {
    "tar-strict": 5.0, "member-keys": 5.0, "create-inputs": 5.0, "update-changes": 5.0, "verify-output": 5.0,
    "gzip-strict": 6.25, "bzip2-strict": 6.25, "zstd-strict": 6.25, "standalone-streams": 6.25,
    "sevenzip-strict": 8.4, "sevenzip-password": 8.4, "sevenzip-writer": 8.4,
    "rar5-strict": 8.4, "rar5-password": 8.4, "rar5-writer": 8.4,
    "zip-archives": 25.0,
    **{target: 2.3 for target in ("worker-frames", "stream-faults", "repeated-cancel", "publication-races", "journal-state",
                                  "journal-record", "session-record", "capture-record", "save-copy-intent",
                                  "save-copy-receipt", "manifest-member")},
}


def plan():
    runs, seed = [], SEED_BASE
    unknown = sorted(set(ONLY) - set(ALLOCATION))
    if unknown:
        raise SystemExit(f"unknown targets: {unknown}")
    for target, hours in ALLOCATION.items():
        if ONLY and target not in ONLY:
            seed += math.ceil(hours / CHUNK)  # each run keeps the seed it has in the complete plan
            continue
        short = json.loads((I / "runs" / f"{target}-short-c4" / "result.json").read_text())
        if short["artifacts"] or short["final_properties"]["Result"] != "success":
            raise SystemExit(f"{target}: its short run is not clean")
        observed = [row for row in short["observations"] if row.get("SubState") == "running"]
        elapsed = observed[-1]["elapsed"] if observed else 120
        rate = int(short["final_properties"]["CPUUsageNSec"]) / 1e9 / max(elapsed, 60)
        corpus = [path for path in (I / "runs" / f"{target}-short-c4" / "corpus").iterdir() if path.is_file()]
        reuse = len(corpus) <= 1024 and sum(path.stat().st_size for path in corpus) <= 16 << 20
        count = math.ceil(hours / CHUNK)
        floor = math.ceil(hours * HOUR / count)
        for number in range(1, count + 1):
            seconds = min(86000, math.ceil(floor / min(rate, 1.6) * MARGIN))
            runs.append({"target": target, "run": f"{target}-long-c4-{number}", "seconds": seconds, "seed": seed,
                         "minimum_cpu_seconds": floor, "rate": round(rate, 3), "reuse": reuse})
            seed += 1
    runs.sort(key=lambda row: -row["seconds"])
    (I / "campaign-plan.json").write_text(json.dumps(runs, indent=2))
    total = sum(row["minimum_cpu_seconds"] for row in runs) / HOUR
    print(json.dumps({"runs": len(runs), "planned_cpu_hours": round(total, 1),
                      "rate_sum": round(sum({r["target"]: r["rate"] for r in runs}.values()), 2),
                      "longest_hours": round(runs[0]["seconds"] / HOUR, 2)}))


def run(slots):
    runs = json.loads((I / "campaign-plan.json").read_text())
    status_path = I / "campaign-status.json"
    status = json.loads(status_path.read_text()) if status_path.exists() else {}
    pending = [row for row in runs if row["run"] not in status]
    active = {}
    while pending or active:
        while pending and len(active) < slots:
            row = pending.pop(0)
            log = (I / f"log-{row['run']}.txt").open("w")
            command = ["python3", "-I", str(S / "run_fuzz_guest.py"), "--root", str(I), "--phase", "clean",
                       "--target", row["target"], "--seconds", str(row["seconds"]), "--seed", str(row["seed"]),
                       "--run-id", row["run"], "--extra-seed-directory", str(I / f"seeds-{row['target']}")]
            if row["reuse"]:
                command += ["--reuse-run-id", f"{row['target']}-short-c4"]
            active[row["run"]] = (row, subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT), log)
            status[row["run"]] = {"state": "running", "started": time.time()}
            status_path.write_text(json.dumps(status, indent=2))
        time.sleep(30)
        for name, (row, process, log) in list(active.items()):
            if process.poll() is None:
                continue
            log.close()
            del active[name]
            seal = subprocess.run(["python3", "-I", str(S / "seal_fuzz_candidate_guest.py"), "--task", str(T),
                                   "--image", I.name, "--run", name, "--packet", str(PACKET), "--target", row["target"],
                                   "--minimum-cpu-seconds", str(row["minimum_cpu_seconds"]),
                                   "--canary-run", "digest-canary-c4", "--canary-replay-run", "digest-replay-c4",
                                   "--short-run", f"{row['target']}-short-c4"], capture_output=True, text=True)
            status[name] = {"state": "sealed" if seal.returncode == 0 else "unsealed", "runner": process.returncode,
                            "finished": time.time(), "seal": (seal.stdout or seal.stderr)[-1500:]}
            status_path.write_text(json.dumps(status, indent=2))
    (I / "campaign.done").write_text(time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()))


if __name__ == "__main__":
    if arguments == ["plan"]:
        plan()
    elif len(arguments) == 2 and arguments[0] == "run":
        run(int(arguments[1]))
    else:
        raise SystemExit("usage: plan [--targets A,B --seed-base N] | run SLOTS, each with optional --image NAME --packet PATH")
