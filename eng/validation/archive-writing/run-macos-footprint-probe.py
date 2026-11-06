#!/usr/bin/env python3
"""Run only an owned API probe, with a finite allocation ceiling and full reaping."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import signal
import stat
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument("--probe", type=Path, required=True)
parser.add_argument("--evidence", type=Path, required=True)
args = parser.parse_args()
if platform.system() != "Darwin":
    raise SystemExit("Run this validation only on the authorized Mac.")
probe = args.probe.absolute()
info = probe.lstat()
if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or info.st_size > 2 * 1024 * 1024:
    raise SystemExit("A small regular executable owned by this actual user is required.")
if probe.resolve() != probe or info.st_mode & (stat.S_IWGRP | stat.S_IWOTH):
    raise SystemExit("The owned probe must not be linked or writable by another user.")
evidence = args.evidence.absolute()
evidence.mkdir(parents=True, exist_ok=False)
facts = {"system": platform.platform(), "uid": os.getuid(), "probe_sha256": hashlib.sha256(probe.read_bytes()).hexdigest(), "runs": []}
for mode in ("control", "limit"):
    process = subprocess.Popen([str(probe), mode], stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
    killed = False
    try:
        output, error = process.communicate(timeout=20)
    except subprocess.TimeoutExpired:
        try:
            os.killpg(process.pid, signal.SIGKILL)
            killed = True
        except ProcessLookupError:
            pass  # The owned child may have exited between the timeout and the signal.
        output, error = process.communicate(timeout=5)
    (evidence / (mode + ".jsonl")).write_bytes(output)
    (evidence / (mode + ".stderr.txt")).write_bytes(error)
    if len(output) > 16384 or len(error) > 16384:
        raise SystemExit("The owned probe exceeded its diagnostic bound.")
    records = [json.loads(line) for line in output.splitlines()]
    if not records or records[0].get("phase") != "setup" or records[0].get("pid") != process.pid or records[0].get("uid") != os.getuid():
        raise SystemExit("The exact owned child did not report its setup identity.")
    facts["runs"].append({"mode": mode, "pid": process.pid, "exit_code": process.returncode, "supervisor_killed": killed,
                          "setup": records[0], "last": records[-1], "reaped": True})
(evidence / "facts.json").write_text(json.dumps(facts, indent=2) + "\n")
print(json.dumps(facts))
