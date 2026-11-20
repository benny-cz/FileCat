#!/usr/bin/env python3
"""Verify one RID's replay of a fuzz image's collected regression corpus (test tooling, §13.6).

Usage: verify_corpus_replay.py REPLAY.tar.gz CORPORA.tar.gz [SKIPPED_TARGET...]

Every target folder of the collected corpus (except the named skipped ones) needs a report whose file count and corpus
digest equal that folder's (the digest `replay-corpus` computes over sorted names and content hashes), with no failure.
"""

import hashlib
import json
from pathlib import PurePosixPath
import sys
import tarfile


def main():
    replay, corpora, skipped = sys.argv[1], sys.argv[2], set(sys.argv[3:])
    folders = {}
    with tarfile.open(corpora, "r:gz") as archive:
        manifest = json.load(archive.extractfile("manifest.json"))
        for item in archive.getmembers():
            parts = PurePosixPath(item.name).parts
            if item.isfile() and len(parts) == 3 and parts[0] == "corpora":
                folders.setdefault(parts[1], []).append((parts[2], hashlib.sha256(archive.extractfile(item).read()).digest()))
    expected = {}
    for target, files in folders.items():
        digest = hashlib.sha256()
        for name, content in sorted(files, key=lambda row: row[0].encode("utf-8")):  # ordinal, as the harness sorts
            digest.update((name + "\n").encode("utf-8")); digest.update(content)
        expected[target] = (len(files), digest.hexdigest().upper())
        assert manifest["targets"][target]["files"] == len(files), target
    with tarfile.open(replay, "r:gz") as archive:
        reports = {PurePosixPath(item.name).stem: json.load(archive.extractfile(item)) for item in archive.getmembers()
                   if item.isfile() and PurePosixPath(item.name).parent.name == "reports" and item.name.endswith(".json")}
    assert set(reports) == set(expected) - skipped, sorted(set(reports) ^ (set(expected) - skipped))
    rows = []
    for target, report in sorted(reports.items()):
        files, digest = expected[target]
        assert report["target"] == target and report["files"] == files and report["corpus_sha256"].upper() == digest, target
        assert not report["failures"] and report["accepted"] + report["refused"] == files, (target, report["failures"][:3])
        rows.append({"target": target, "files": files, "accepted": report["accepted"], "refused": report["refused"],
                     "rid": report["os"], "cpu_seconds": round(report["process_cpu_seconds"], 1)})
    print(json.dumps({"replay": replay, "image": manifest["image"], "targets": len(rows), "skipped": sorted(skipped),
                      "files": sum(row["files"] for row in rows), "failures": 0, "rows": rows}, indent=2))


if __name__ == "__main__":
    main()
