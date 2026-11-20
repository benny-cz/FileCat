#!/usr/bin/env python3
"""Copy small, owned RAR5 transform vectors into a bounded native fuzz corpus."""

import argparse
import hashlib
import json
from pathlib import Path
import shutil


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixtures", type=Path, required=True)
    parser.add_argument("--generated", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--password-target", action="store_true")
    args = parser.parse_args()
    fixtures = args.fixtures.resolve(strict=True)
    generated = args.generated.resolve(strict=True)
    out = args.out.resolve()
    out.mkdir(exist_ok=False)
    rows = []
    total = 0
    sources = [(name, fixtures / name) for name in ("Rar4.rar", "Rar.malformed_512byte.rar")]
    for method in range(6):
        for mode in (range(3) if args.password_target else (0,)):
            for stage in ("source", "rebuilt"):
                name = f"generated-method-{method}" + (f"-mode-{mode}" if args.password_target else "") + f"-{stage}.rar"
                sources.append((name, generated / f"method-{method}-mode-{mode}" / f"{stage}.rar"))
    for name, source in sources:
        if not source.is_file() or source.is_symlink():
            raise ValueError("Unreviewable RAR seed: " + name)
        size = source.stat().st_size
        total += size
        if size > 1048576 or total > 4 * 1048576:
            raise ValueError("RAR5 corpus exceeds its byte budget")
        target = out / name
        shutil.copyfile(source, target)
        if sha(target) != sha(source):
            raise ValueError("RAR seed changed during corpus copy: " + name)
        rows.append({"name": name, "bytes": size, "sha256": sha(target)})
    (out / "manifest.json").write_text(json.dumps({"target": "rar5-password" if args.password_target else "rar5-strict", "seeds": rows}, indent=2))
    print(json.dumps({"seeds": len(rows), "bytes": total}))


if __name__ == "__main__":
    main()
