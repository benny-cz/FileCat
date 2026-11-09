#!/usr/bin/env python3
"""Copy small, source-pinned 7z fixtures into a bounded native fuzz corpus."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil


NAMES = (
    "EmptyEntries.7z",
    "EmptyEntries.solid.7z",
    "FileCat.Methods.Copy.7z",
    "FileCat.Methods.LZMA.7z",
    "FileCat.Methods.BZip2.7z",
    "FileCat.Methods.Deflate.7z",
    "FileCat.Methods.Deflate64.7z",
    "7Zip.LZMA2.7z",
)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixtures", type=Path, required=True)
    parser.add_argument("--generated", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    fixtures = args.fixtures.resolve(strict=True)
    generated = args.generated.resolve(strict=True)
    out = args.out.resolve()
    out.mkdir(exist_ok=False)
    rows = []
    total = 0
    for name in NAMES:
        source = fixtures / name
        if not source.is_file() or source.is_symlink():
            raise ValueError("Unreviewable 7z fixture: " + name)
        size = source.stat().st_size
        total += size
        if size > 1048576 or total > 4 * 1048576:
            raise ValueError("7z seed corpus exceeds its byte budget")
        target = out / name
        shutil.copyfile(source, target)
        if sha(target) != sha(source):
            raise ValueError("7z fixture changed during corpus copy: " + name)
        rows.append({"name": name, "bytes": size, "sha256": sha(target)})
    for codec in range(7):
        name = f"generated-codec-{codec}.7z"
        source = generated / f"codec-{codec}-filter-0-protection-0" / "members.7z"
        if not source.is_file() or source.is_symlink():
            raise ValueError("Missing generated 7z catalog seed: " + name)
        size = source.stat().st_size
        total += size
        if size > 1048576 or total > 4 * 1048576:
            raise ValueError("7z seed corpus exceeds its byte budget")
        target = out / name
        shutil.copyfile(source, target)
        if sha(target) != sha(source):
            raise ValueError("Generated 7z vector changed during corpus copy: " + name)
        rows.append({"name": name, "bytes": size, "sha256": sha(target)})
    (out / "manifest.json").write_text(json.dumps({"target": "sevenzip-strict", "seeds": rows}, indent=2))
    print(json.dumps({"seeds": len(rows), "bytes": total}))


if __name__ == "__main__":
    main()
