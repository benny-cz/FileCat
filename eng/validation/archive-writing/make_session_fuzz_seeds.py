#!/usr/bin/env python3
"""Create small synthetic FZ09 edit-record seeds; no real user session enters the corpus."""

import argparse
import hashlib
import json
from pathlib import Path


IDENTIFIER = "0123456789abcdef0123456789abcdef"
DIRECTORY = "/tmp/filecat-fuzz-session/" + IDENTIFIER
WORKING = DIRECTORY + "/files/owned.txt"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(exist_ok=False)
    base = {"Id": IDENTIFIER, "WorkingPath": WORKING, "BaseSha256": "0" * 64}
    records = {
        "remote-v1": {**base, "Kind": "sftp", "Version": 1, "RemotePath": "/owned.txt"},
        "zip-v2": {**base, "Kind": "archive", "Version": 2, "ArchivePath": "/tmp/archive.zip",
                   "MemberPath": "owned.txt"},
        "remote-v3": {**base, "Kind": "sftp", "Version": 3, "RemotePath": "/owned.txt",
                      "StorageRootIdentity": "root", "SessionDirectoryIdentity": "session",
                      "WorkingDirectoryIdentity": "files", "WorkingIdentity": "working"},
        "unknown-v999": {**base, "Version": 999},
        "escaped-path": {**base, "Version": 2, "WorkingPath": "/tmp/outside.txt"},
        "wrong-id": {**base, "Version": 2, "Id": "ffffffffffffffffffffffffffffffff"},
    }
    samples = {name: json.dumps(value, ensure_ascii=True, separators=(",", ":")).encode()
               for name, value in records.items()}
    samples.update({"torn": b'{"Id":"' + IDENTIFIER.encode() + b'","WorkingPath":',
                    "invalid-utf8": b'{"Id":"' + IDENTIFIER.encode() + b'","Bad":"\xff"}',
                    "deep-array": b'[' * 34 + b']' * 34})
    manifest = []
    for name, content in samples.items():
        path = output / (name + ".json")
        path.write_bytes(content)
        manifest.append({"name": path.name, "bytes": len(content),
                         "sha256": hashlib.sha256(content).hexdigest()})
    (output / "manifest.json").write_text(json.dumps({"synthetic": True, "seeds": manifest}, indent=2))
    print(json.dumps({"directory": str(output), "seeds": len(samples)}))


if __name__ == "__main__":
    main()
