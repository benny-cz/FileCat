#!/usr/bin/env python3
"""Turn a collected fuzz corpus into seed folders for a corpus-seeded structured-mutation pass (test tooling, §13.6).

Usage: sample_corpus_seeds.py CORPORA.tar.gz IMAGE_SEEDS.tar.gz OUTPUT.tar.gz

The image's own seed folders already drove the structured lanes; this pass also starts from what the native campaign
found. For every target of CORPORA (collect_fuzz_corpora_guest.py), seeds-<target>/ takes up to half its files from the
image seeds (the valid archives and requests that reach deep code; campaign corpora of the strict readers are mostly
inputs refused early) and fills the rest with corpus files that are not image seeds, each group in SHA-256 name order
(an arbitrary but reproducible sample), within the harness limits: 256 files, 1 MiB each, 15 MiB in all. The output has
the image seed archive's layout, so run_structured_lanes_unix.sh / run_structured_lanes_windows.ps1 take it unchanged;
it is extra coverage, not floor evidence (verify_structured_lanes.py binds lanes to the image's seed folders).
"""

import hashlib
import io
import json
from pathlib import PurePosixPath
import sys
import tarfile

LIMIT_FILES = 256
LIMIT_BYTES = 15 << 20


def main():
    corpora, image_seeds, output = sys.argv[1], sys.argv[2], sys.argv[3]
    seeds = {}
    with tarfile.open(image_seeds, "r:gz") as archive:
        for item in archive.getmembers():
            parts = PurePosixPath(item.name).parts
            if item.isfile() and len(parts) == 2 and parts[0].startswith("seeds-") and parts[1] != "manifest.json":
                seeds.setdefault(parts[0][6:], set()).add(hashlib.sha256(archive.extractfile(item).read()).hexdigest())
    folders = {}
    with tarfile.open(corpora, "r:gz") as archive:
        for item in archive.getmembers():
            parts = PurePosixPath(item.name).parts
            if item.isfile() and len(parts) == 3 and parts[0] == "corpora":
                folders.setdefault(parts[1], {})[parts[2]] = archive.extractfile(item).read()
    summary = {}
    with tarfile.open(output, "x:gz", compresslevel=6) as archive:
        for target, files in sorted(folders.items()):
            known = seeds.get(target, set())
            chosen, total, image = [], 0, 0
            for from_image, limit in ((True, LIMIT_FILES // 2), (False, LIMIT_FILES)):
                for name, data in sorted(files.items()):
                    if len(chosen) == limit:
                        break
                    if not data or len(data) > 1 << 20 or total + len(data) > LIMIT_BYTES or \
                            (hashlib.sha256(data).hexdigest() in known) != from_image:
                        continue
                    chosen.append((name, data)); total += len(data); image += from_image
            for name, data in chosen:
                entry = tarfile.TarInfo(f"seeds-{target}/{name}")
                entry.size = len(data)
                archive.addfile(entry, io.BytesIO(data))
            summary[target] = {"corpus": len(files), "image_seeds": image, "found": len(chosen) - image, "bytes": total}
    print(json.dumps({"output": output, "targets": summary}, indent=1))


if __name__ == "__main__":
    main()
