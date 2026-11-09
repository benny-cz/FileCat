#!/usr/bin/env python3
"""Run one frozen ArchiveFuzzHarness image inside a bounded Linux guest unit.

This is a validation tool, not an application dependency. The systemd unit owns
the driver and its managed child. The supervisor retains exact command, binary
hashes, kernel resource observations, and termination status for each run.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def properties(unit):
    output = subprocess.check_output(
        ["systemctl", "show", unit, "-p", "ActiveState", "-p", "SubState",
         "-p", "MainPID", "-p", "ExecMainCode", "-p", "ExecMainStatus",
         "-p", "Result", "-p", "CPUUsageNSec", "-p", "MemoryPeak",
         "-p", "ControlGroup"], text=True)
    return dict(line.split("=", 1) for line in output.splitlines() if "=" in line)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--phase", choices=("canary", "clean"), required=True)
    parser.add_argument("--target", choices=("digest", "tar-strict", "session-record", "capture-record", "save-copy-intent"), default="digest")
    parser.add_argument("--seconds", type=int, required=True)
    parser.add_argument("--seed", type=int, required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--valid-prefix", default="00", help="Two uppercase hex digits for a valid corpus seed")
    parser.add_argument("--short-canary-seed", action="store_true", help="Add a 63-character FC seed; it must mutate to become valid")
    parser.add_argument("--replay-text", help="Replay these ASCII input bytes once through the same bounded driver")
    parser.add_argument("--replay-file", type=Path, help="Replay one bounded synthetic input file")
    parser.add_argument("--canary-two-byte", action="store_true", help="Test-only canary input adaptation for a valid digest")
    parser.add_argument("--reuse-run-id", help="Copy bounded corpus entries from a completed run in the same image")
    parser.add_argument("--extra-seed-directory", type=Path, help="Copy synthetic TAR or session-record seeds into a campaign")
    parser.add_argument("--sudo-password", default="a", help="Disposable VM password")
    args = parser.parse_args()
    if not 1 <= args.seconds <= 86400:
        parser.error("seconds must be within 1..86400")
    if not args.run_id.replace("-", "").isalnum():
        parser.error("run-id must be alphanumeric with optional dashes")
    if len(args.valid_prefix) != 2 or any(c not in "0123456789ABCDEF" for c in args.valid_prefix):
        parser.error("valid-prefix must be two uppercase hex digits")
    if args.phase == "canary" and args.valid_prefix == "FC":
        parser.error("An exact crash seed cannot prove canary discovery")
    if args.short_canary_seed and args.phase != "canary":
        parser.error("The intentional canary seed is only for instrumentation qualification")
    if args.canary_two_byte and args.target != "digest":
        parser.error("The two-byte canary mode applies only to the digest guard")
    if args.short_canary_seed and args.canary_two_byte:
        parser.error("Choose one canary seed strategy")
    if args.replay_text is not None and (not args.replay_text.isascii() or len(args.replay_text) > 1048576):
        parser.error("replay-text must be ASCII and at most 1 MiB")
    if args.replay_file is not None and args.replay_text is not None:
        parser.error("Choose one replay input")
    if args.reuse_run_id is not None and (not args.reuse_run_id.replace("-", "").isalnum() or args.replay_text is not None or args.replay_file is not None):
        parser.error("reuse-run-id must name a completed run and cannot be used with replay")
    if args.extra_seed_directory is not None and args.target == "digest":
        parser.error("Extra seeds apply only to the format and recovery-record targets")

    root = args.root.resolve(strict=True)
    parent = root.parent
    image = root / args.phase
    driver = root / "libfuzzer-dotnet"
    managed = parent / "dotnet" / "dotnet"
    core = image / "FileCat.Core.dll"
    harness = image / "FileCat.ArchiveFuzzHarness.dll"
    for path in (driver, managed, core, harness):
        if not path.is_file():
            raise FileNotFoundError(path)
    if args.phase == "clean" and b"FILECAT_VALIDATED_DIGEST_CANARY" in core.read_bytes():
        raise ValueError("Clean image contains the intentional canary")
    unit = "filecat-archive-fuzz-" + args.run_id
    run = root / "runs" / args.run_id
    run.mkdir(parents=True, exist_ok=False)
    for name in ("corpus", "crashes", "reports", "tmp"):
        (run / name).mkdir()
    (run / "corpus" / "valid").write_bytes(b"\0" * 1024 if args.target == "tar-strict" else
                                            b"0{}" if args.target == "capture-record" else
                                            b"{}" if args.target in ("session-record", "save-copy-intent") else
                                            args.valid_prefix.encode() if args.canary_two_byte else
                                            args.valid_prefix.encode() + b"0" * 62)
    (run / "corpus" / "short").write_bytes(b"0")
    if args.short_canary_seed:
        (run / "corpus" / "near-canary").write_bytes(b"FC" + b"0" * 61)
    if args.replay_text is not None:
        (run / "replay-input").write_bytes(args.replay_text.encode("ascii"))
    if args.replay_file is not None:
        source = args.replay_file.resolve(strict=True)
        if not source.is_file() or source.is_symlink() or source.stat().st_size > 1048576:
            raise ValueError("Replay input exceeds its owned file budget")
        shutil.copyfile(source, run / "replay-input")
    reused = []
    if args.reuse_run_id is not None:
        previous = root / "runs" / args.reuse_run_id
        previous_result = previous / "result.json"
        previous_facts = json.loads(previous_result.read_text()) if previous_result.is_file() else {}
        if not previous_facts.get("terminal") or previous_facts.get("phase") != "clean" or previous_facts.get("target") != args.target or previous_facts.get("artifacts"):
            raise ValueError("The seed corpus run is not completed and terminal")
        old_corpus = previous / "corpus"
        total = 0
        for index, source in enumerate(sorted(old_corpus.iterdir())):
            if index >= 1024 or not source.is_file() or source.is_symlink():
                raise ValueError("Seed corpus has an unreviewable entry")
            size = source.stat().st_size
            total += size
            if size > 1048576 or total > 16 * 1048576:
                raise ValueError("Seed corpus exceeds its copy budget")
            target = run / "corpus" / f"reused-{index:04d}"
            shutil.copyfile(source, target)
            reused.append({"name": source.name, "bytes": size, "sha256": sha(source)})
    extra_seeds = []
    if args.extra_seed_directory is not None:
        folder = args.extra_seed_directory.resolve(strict=True)
        if folder.parent != root or not folder.is_dir():
            raise ValueError("Extra corpus must be a direct synthetic seed directory within this image")
        total = 0
        extension = "*.tar" if args.target == "tar-strict" else "*.json"
        for index, source in enumerate(sorted(p for p in folder.glob(extension) if p.name != "manifest.json")):
            if index >= 64 or not source.is_file() or source.is_symlink():
                raise ValueError("Extra corpus has an unreviewable entry")
            size = source.stat().st_size
            total += size
            if size > 1048576 or total > 16 * 1048576:
                raise ValueError("Extra corpus exceeds its copy budget")
            shutil.copyfile(source, run / "corpus" / f"synthetic-{index:04d}")
            extra_seeds.append({"name": source.name, "bytes": size, "sha256": sha(source)})

    replay = args.replay_text is not None or args.replay_file is not None
    native = [str(driver), "-runs=1" if replay else f"-max_total_time={args.seconds}", "-timeout=2",
              "-rss_limit_mb=900", "-max_len=2" if args.canary_two_byte else "-max_len=1048576", f"-seed={args.seed}",
              "-print_final_stats=1", f"-artifact_prefix={run / 'crashes'}/",
              f"--target_path={managed}", f"--target_arg={harness}", str(run / ("replay-input" if replay else "corpus"))]
    command = ["sudo", "-S", "-p", "", "systemd-run", "--unit=" + unit,
               "--service-type=exec", "--uid=benny", "--gid=benny",
               "--working-directory=" + str(root),
               "-p", "RemainAfterExit=yes", "-p", "MemoryMax=1073741824",
               "-p", "MemorySwapMax=0", "-p", f"RuntimeMaxSec={args.seconds + 30}",
               "-p", "TasksMax=64", "-p", "NoNewPrivileges=yes",
               "-p", "PrivateNetwork=yes", "-p", "PrivateDevices=yes",
               "-p", "PrivateTmp=yes", "-p", "PrivateIPC=yes",
               "-p", "ProtectSystem=strict", "-p", "ProtectHome=read-only",
               "-p", "KillMode=control-group", "-p", "LimitCORE=0",
               "-p", "LimitNOFILE=512", "-p", "LimitFSIZE=16777216",
               "-p", "ReadWritePaths=" + str(run),
               "-p", "StandardOutput=append:" + str(run / "driver.log"),
               "-p", "StandardError=append:" + str(run / "driver.log"),
               "--setenv=DOTNET_ROOT=" + str(parent / "dotnet"),
               "--setenv=DOTNET_EnableDiagnostics=0",
               "--setenv=DOTNET_GCHeapHardLimit=20000000",
               "--setenv=FILECAT_FUZZ_REPORT=" + str(run / "reports" / "managed.json"),
               "--setenv=FILECAT_FUZZ_TARGET=" + args.target,
               "--setenv=FILECAT_FUZZ_CANARY_PREFIX_MODE=" + ("1" if args.canary_two_byte else "0"),
               "--setenv=TMPDIR=" + str(run / "tmp"), *native]
    facts = {"unit": unit, "phase": args.phase, "target": args.target, "seconds": args.seconds,
             "valid_seed_prefix": args.valid_prefix, "short_canary_seed": args.short_canary_seed,
             "canary_two_byte": args.canary_two_byte,
             "reuse_run_id": args.reuse_run_id, "reused_corpus": reused, "extra_seeds": extra_seeds,
             "replay_input_sha256": sha(run / "replay-input") if replay else None,
             "seed": args.seed, "command": command, "sha256": {
                 "driver": sha(driver), "managed": sha(managed),
                 "core": sha(core), "harness": sha(harness),
                 "archives": sha(image / "FileCat.Archives.dll") if (image / "FileCat.Archives.dll").exists() else None},
             "observations": []}
    (run / "command.json").write_text(json.dumps(facts, indent=2))
    launch = subprocess.run(command, input=(args.sudo_password + "\n").encode(),
                            capture_output=True, timeout=20)
    facts["launch"] = {"returncode": launch.returncode,
                       "stdout": launch.stdout.decode(errors="replace"),
                       "stderr": launch.stderr.decode(errors="replace")}
    if launch.returncode:
        (run / "result.json").write_text(json.dumps(facts, indent=2))
        raise RuntimeError(f"Unit did not start: {facts['launch']}")

    deadline = time.monotonic() + args.seconds + 40
    terminal = False
    output_budget_failure = False
    while time.monotonic() < deadline:
        p = properties(unit)
        facts["observations"].append({"elapsed": round(time.monotonic() - (deadline - args.seconds - 40), 3), **p})
        if sum(f.stat().st_size for f in run.rglob("*") if f.is_file()) > 256 * 1048576:
            output_budget_failure = True
            break
        if p.get("MainPID") == "0" and p.get("SubState") in ("exited", "failed", "dead"):
            terminal = True
            break
        time.sleep(1)
    facts["terminal"] = terminal
    facts["output_budget_failure"] = output_budget_failure
    facts["final_properties"] = properties(unit)
    facts["artifacts"] = [{"name": p.name, "bytes": p.stat().st_size,
                           "sha256": sha(p)} for p in sorted((run / "crashes").iterdir())]
    facts["managed_report"] = json.loads((run / "reports" / "managed.json").read_text()) if (run / "reports" / "managed.json").exists() else None
    facts["files"] = {str(p.relative_to(run)): p.stat().st_size for p in run.rglob("*") if p.is_file()}
    (run / "result.json").write_text(json.dumps(facts, indent=2))
    subprocess.run(["sudo", "-S", "-p", "", "systemctl", "stop", unit],
                   input=(args.sudo_password + "\n").encode(), capture_output=True, timeout=20)
    print(json.dumps({"run": str(run), "terminal": terminal,
                      "status": facts["final_properties"],
                      "artifacts": facts["artifacts"],
                      "report": facts["managed_report"]}))
    if not terminal or output_budget_failure:
        raise RuntimeError("Fuzz service exceeded its time/output budget; owned unit was stopped")


if __name__ == "__main__":
    main()
