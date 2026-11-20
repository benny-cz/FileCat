#!/usr/bin/env python3
"""Seal deterministic, synthetic FZ09 replay seeds for one guest platform."""

import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tarfile


def main():
    if len(sys.argv) != 2 or sys.argv[1] not in ("unix", "windows"):
        raise SystemExit("usage: pack_recovery_replay_seeds.py unix|windows")
    platform = sys.argv[1]
    root = Path(__file__).resolve().parents[3]
    generator = Path(__file__).with_name("make_session_fuzz_seeds.py")
    folder = root / ".tmp" / ("recovery-replay-v8-" + platform)
    folder.mkdir(exist_ok=False)
    for target in ("session-record", "capture-record", "save-copy-intent", "save-copy-receipt",
                   "journal-record", "manifest-member"):
        subprocess.run([sys.executable, "-B", str(generator), "--platform", platform,
                        "--target", target, "--output", str(folder / target)], check=True)
    packet = root / ".tmp" / ("recovery-record-seeds-" + platform + "-v8.tar.gz")
    with tarfile.open(packet, "x:gz") as tar:
        for file in sorted(folder.rglob("*")):
            if file.is_file():
                tar.add(file, "seeds/" + file.relative_to(folder).as_posix())
    facts = {"platform": platform, "bytes": packet.stat().st_size,
             "sha256": hashlib.sha256(packet.read_bytes()).hexdigest(),
             "generator_sha256": hashlib.sha256(generator.read_bytes()).hexdigest(),
             "seeds": sum(1 for file in folder.rglob("*") if file.suffix in (".json", ".bin") and file.name != "manifest.json")}
    packet.with_name(packet.name.removesuffix(".tar.gz") + ".json").write_text(json.dumps(facts, indent=2))
    print(json.dumps(facts))


if __name__ == "__main__":
    main()
