#!/usr/bin/env python3
"""Collect one fuzz image's regression corpus for replay on other RIDs (test tooling, not an application dependency).

For every target, the union of all its runs' final corpora and its synthetic seed folder, deduplicated by content and
named by SHA-256, goes into corpora/<target>/ of a new tar.gz with a manifest of per-target counts and a digest of the
sorted (name, size) list. `replay-corpus` then runs each target's folder once on Windows and macOS (§13.6: the entire
regression corpus on every shipping RID).
Usage: collect_fuzz_corpora_guest.py IMAGE_ROOT OUTPUT.tar.gz
"""

import hashlib
import io
import json
from pathlib import Path
import sys
import tarfile


def main():
    image, output = Path(sys.argv[1]).resolve(strict=True), Path(sys.argv[2]).resolve()
    if output.exists():
        raise FileExistsError(output)
    corpora = {}
    for run in sorted((image / "runs").iterdir()):
        result = run / "result.json"
        if not result.is_file():
            continue
        facts = json.loads(result.read_text())
        if facts.get("phase") != "clean" or facts.get("target") in (None, "digest") or not (run / "corpus").is_dir():
            continue
        target = corpora.setdefault(facts["target"], {})
        for path in (run / "corpus").iterdir():
            if path.is_file() and not path.is_symlink() and path.stat().st_size <= 1 << 20:
                data = path.read_bytes()
                target[hashlib.sha256(data).hexdigest()] = data
    for folder in sorted(image.glob("seeds-*")):
        target = corpora.get(folder.name[6:])
        if target is None:
            continue
        for path in folder.iterdir():
            if path.is_file() and path.name != "manifest.json" and path.stat().st_size <= 1 << 20:
                data = path.read_bytes()
                target[hashlib.sha256(data).hexdigest()] = data
    manifest = {"image": image.name, "targets": {}}
    with tarfile.open(output, "x:gz", compresslevel=6) as archive:
        for name, files in sorted(corpora.items()):
            listing = hashlib.sha256()
            for digest, data in sorted(files.items()):
                entry = tarfile.TarInfo(f"corpora/{name}/{digest}")
                entry.size = len(data)
                archive.addfile(entry, io.BytesIO(data))
                listing.update(f"{digest} {len(data)}\n".encode())
            manifest["targets"][name] = {"files": len(files),
                                         "bytes": sum(len(data) for data in files.values()), "listing_sha256": listing.hexdigest()}
        body = json.dumps(manifest, indent=2).encode()
        entry = tarfile.TarInfo("manifest.json")
        entry.size = len(body)
        archive.addfile(entry, io.BytesIO(body))
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    print(json.dumps({"output": str(output), "sha256": digest, "targets": len(manifest["targets"]),
                      "files": sum(row["files"] for row in manifest["targets"].values())}))


if __name__ == "__main__":
    main()
