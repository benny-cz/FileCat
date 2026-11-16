#!/usr/bin/env python3
"""Install exact Mac-replayed public format seeds into an idle Ubuntu fuzz image."""

import argparse
import hashlib
import json
from pathlib import Path
import tarfile


def sha(data):
    return hashlib.sha256(data).hexdigest()


def expected(target):
    if target == "sevenzip-password":
        base = {"EmptyEntries.7z", "EmptyEntries.solid.7z", "FileCat.Methods.Copy.7z",
                "FileCat.Methods.LZMA.7z", "FileCat.Methods.BZip2.7z", "FileCat.Methods.Deflate.7z",
                "FileCat.Methods.Deflate64.7z", "7Zip.LZMA2.7z"}
        return base | {f"generated-codec-{codec}-protection-{protection}.7z"
                       for codec in range(7) for protection in range(3)}
    if target == "rar5-password":
        return {"Rar4.rar", "Rar.malformed_512byte.rar"} | {
            f"generated-method-{method}-mode-{mode}-{stage}.rar"
            for method in range(6) for mode in range(3) for stage in ("source", "rebuilt")}
    raise ValueError("Only password-aware format seed transports are supported")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--target", choices=("sevenzip-password", "rar5-password"), required=True)
    parser.add_argument("--transport", type=Path, required=True)
    parser.add_argument("--transport-sha256", required=True)
    args = parser.parse_args()
    if not args.image.replace("-", "").isalnum():
        parser.error("image must have a simple name")
    task = args.task.resolve(strict=True)
    image = (task / args.image).resolve(strict=True)
    transport = args.transport.resolve(strict=True)
    if image.parent != task or transport.parent != task or sha(transport.read_bytes()) != args.transport_sha256:
        raise ValueError("Seed image or transport differs from its sealed task input")
    names = expected(args.target)
    with tarfile.open(transport) as archive:
        members = {item.name: item for item in archive}
        if len(members) != len(archive.getmembers()) or set(members) != {
            "result.json", "instrumented-types.json", "seeds/manifest.json"} | {"seeds/" + name for name in names} or \
                any(not item.isfile() or item.size > 1048576 for item in members.values()):
            raise ValueError("Mac replay transport member inventory changed")
        manifest = json.load(archive.extractfile(members["seeds/manifest.json"]))
        if manifest["target"] != args.target:
            raise ValueError("Mac replay target differs from the seed request")
        rows = {row["name"]: row for row in manifest["seeds"]}
        if set(rows) != names or len(manifest["seeds"]) != len(names):
            raise ValueError("Mac replay seed manifest changed")
        payload = {}
        for name in sorted(names):
            data = archive.extractfile(members["seeds/" + name]).read()
            if rows[name] != {"name": name, "bytes": len(data), "sha256": sha(data)}:
                raise ValueError("Mac replay seed changed: " + name)
            payload[name] = data
    out = image / ("seeds-sevenzip-password-v16" if args.target.startswith("sevenzip") else "seeds-rar5-password-v16")
    out.mkdir(exist_ok=False)
    for name, data in payload.items():
        (out / name).write_bytes(data)
    (out / "manifest.json").write_text(json.dumps(manifest, indent=2))
    print(json.dumps({"target": args.target, "seeds": len(names), "bytes": sum(map(len, payload.values())),
                      "transport_sha256": args.transport_sha256, "directory": str(out)}))


if __name__ == "__main__":
    main()
