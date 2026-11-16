#!/usr/bin/env python3
"""Turn a collected fuzz corpus into seed folders for a corpus-seeded structured-mutation pass (test tooling, §13.6).

Usage: sample_corpus_seeds.py CORPORA.tar.gz IMAGE_SEEDS.tar.gz OUTPUT.tar.gz [--windows]

The image's own seed folders already drove the structured lanes; this pass also starts from what the native campaign
found. For every target of CORPORA (collect_fuzz_corpora_guest.py), seeds-<target>/ takes up to half its files from the
image seeds (the valid archives and requests that reach deep code; campaign corpora of the strict readers are mostly
inputs refused early) and fills the rest with corpus files that are not image seeds, each group in SHA-256 name order
(an arbitrary but reproducible sample), within the harness limits: 256 files, 1 MiB each, 15 MiB in all. The output has
the image seed archive's layout, so run_structured_lanes_unix.sh / run_structured_lanes_windows.ps1 take it unchanged;
it is extra coverage, not floor evidence (verify_structured_lanes.py binds lanes to the image's seed folders).

The image and its campaign are Linux, so session records and saved-copy intents name the harness's Unix session and
destination roots, which a Windows harness refuses before any path check. --windows rewrites those roots (separators
included) to the harness's Windows ones, as make_session_fuzz_seeds.py --platform windows writes them, after the
image-seed match; the other targets carry no host paths.
"""

import hashlib
import io
import json
from pathlib import PurePosixPath
import re
import sys
import tarfile

LIMIT_FILES = 256
LIMIT_BYTES = 15 << 20
HOST_PATH_TARGETS = ("session-record", "save-copy-intent")
UNIX_ROOT = re.compile(rb'/tmp/filecat-fuzz-(session|destination)([^"]*)')


def windows_paths(data):
    return UNIX_ROOT.sub(lambda match: b"C:\\\\filecat-fuzz-" + match.group(1) + match.group(2).replace(b"/", b"\\\\"), data)


def main():
    arguments = [argument for argument in sys.argv[1:] if argument != "--windows"]
    windows = len(arguments) != len(sys.argv) - 1
    corpora, image_seeds, output = arguments
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
            rewrite = windows and target in HOST_PATH_TARGETS
            chosen, total, image, rewritten = [], 0, 0, 0
            for from_image, limit in ((True, LIMIT_FILES // 2), (False, LIMIT_FILES)):
                for name, data in sorted(files.items()):
                    if len(chosen) == limit:
                        break
                    if (hashlib.sha256(data).hexdigest() in known) != from_image:
                        continue
                    original, data = data, windows_paths(data) if rewrite else data
                    if not data or len(data) > 1 << 20 or total + len(data) > LIMIT_BYTES:
                        continue
                    chosen.append((name, data)); total += len(data); image += from_image; rewritten += data != original
            for name, data in chosen:
                entry = tarfile.TarInfo(f"seeds-{target}/{name}")
                entry.size = len(data)
                archive.addfile(entry, io.BytesIO(data))
            summary[target] = {"corpus": len(files), "image_seeds": image, "found": len(chosen) - image, "bytes": total}
            if rewrite:
                summary[target]["windows_paths"] = rewritten
    print(json.dumps({"output": output, "windows": windows, "targets": summary}, indent=1))


if __name__ == "__main__":
    main()
