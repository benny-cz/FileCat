#!/usr/bin/env python3
"""Seal a bounded guest replay of public synthetic encrypted archive seeds."""

import argparse
import hashlib
import json
from pathlib import Path
import tarfile


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True)
    parser.add_argument("--format", choices=("sevenzip", "rar5"), required=True)
    parser.add_argument("--packet-sha256", required=True)
    args = parser.parse_args()
    task = args.task.resolve(strict=True)
    folder = f"fuzz-{args.format}-password-v16"
    root = task / folder
    result = json.loads((root / "result.json").read_text())
    manifest = json.loads((root / "seeds" / "manifest.json").read_text())
    target = args.format + "-password"
    expected = 21 if args.format == "sevenzip" else 36
    if result["packet_sha256"] != args.packet_sha256 or manifest != result["seeds"] or manifest["target"] != target:
        raise ValueError("Guest replay does not bind its public password corpus and packet")
    rows = {row["name"]: row for row in manifest["seeds"]}
    if len(rows) != len(manifest["seeds"]) or len(rows) != expected + (8 if args.format == "sevenzip" else 2):
        raise ValueError("Password seed inventory changed")
    if sum(row["accepted"] is True for row in result["replay"]) != expected or any(row["returncode"] for row in result["replay"]):
        raise ValueError("Password replay did not accept every generated vector")
    members = [root / "result.json", root / "instrumented-types.json", root / "seeds" / "manifest.json"]
    for name, row in sorted(rows.items()):
        path = root / "seeds" / name
        if Path(name).name != name or path.is_symlink() or not path.is_file() or path.stat().st_size > 1048576 or \
                sha(path.read_bytes()) != row["sha256"] or path.stat().st_size != row["bytes"]:
            raise ValueError("Unreviewable password seed: " + name)
        members.append(path)
    if {path.name for path in (root / "seeds").iterdir()} != set(rows) | {"manifest.json"}:
        raise ValueError("Password seed folder has unreviewed entries")
    out = task / (folder + "-mac-seeds.tar.gz")
    with tarfile.open(out, "x:gz") as archive:
        for path in members:
            archive.add(path, arcname=str(path.relative_to(root)), recursive=False)
    sidecar = {"bytes": out.stat().st_size, "sha256": sha(out.read_bytes()), "target": target}
    out.with_suffix(".json").write_text(json.dumps(sidecar, indent=2))
    print(json.dumps(sidecar))


if __name__ == "__main__":
    main()
