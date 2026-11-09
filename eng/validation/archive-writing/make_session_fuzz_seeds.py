#!/usr/bin/env python3
"""Create synthetic FZ09 recovery-record seeds; no real user session enters the corpus."""

import argparse
import hashlib
import json
from pathlib import Path


IDENTIFIER = "0123456789abcdef0123456789abcdef"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--target", choices=("session-record", "capture-record", "save-copy-intent"),default="session-record")
    parser.add_argument("--platform",choices=("unix","windows"),default="unix")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(exist_ok=False)
    directory=("C:\\filecat-fuzz-session\\" if args.platform=="windows" else "/tmp/filecat-fuzz-session/")+IDENTIFIER
    working=directory+("\\files\\owned.txt" if args.platform=="windows" else "/files/owned.txt")
    base = {"Id": IDENTIFIER, "WorkingPath": working, "BaseSha256": "0" * 64}
    sessions = {
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
    nonce="11111111-1111-1111-1111-111111111111"
    artifact=".filecat-archive-"+nonce.replace("-","")+"-"
    capture={"Version":1,"Session":IDENTIFIER,"SessionRecord":"A"*64,"Directory":"captured-directory",
             "Files":"captured-files","Leaf":"owned.txt"}
    complete={**capture,"Identity":"captured-file","Length":13,"Sha256":"B"*64,
              "Metadata":"C"*64,"Origin":"D"*64}
    intent={"Version":1,"Nonce":nonce,"Session":IDENTIFIER,"SessionRecord":"A"*64,
            "CaptureIdentity":"capture","SourceIdentity":"source","Length":13,"Sha256":"B"*64,
            "SourceMetadata":"C"*64,"OriginDigest":"D"*64,"ModifiedSeconds":1700000000,
            "ModifiedNanoseconds":0,"DestinationDirectory":"C:\\filecat-fuzz-destination" if args.platform=="windows" else "/tmp/filecat-fuzz-destination",
            "ParentIdentity":"parent","TargetLeaf":"saved.txt","StageLeaf":artifact+"a.stage",
            "PreparedLeaf":artifact+"prepared.manifest","OutcomeLeaf":artifact+"outcome.manifest"}
    if args.target=="session-record":
        samples={name:json.dumps(value,ensure_ascii=True,separators=(",", ":")).encode() for name,value in sessions.items()}
        samples.update({"torn": b'{"Id":"' + IDENTIFIER.encode() + b'","WorkingPath":',
                        "invalid-utf8": b'{"Id":"' + IDENTIFIER.encode() + b'","Bad":"\xff"}',
                        "deep-array": b'[' * 34 + b']' * 34})
    elif args.target=="capture-record":
        samples={"start":b"0"+json.dumps(capture,separators=(",", ":")).encode(),
                 "complete":b"1"+json.dumps(complete,separators=(",", ":")).encode(),
                 "wrong-version":b"0"+json.dumps({**capture,"Version":999},separators=(",", ":")).encode(),
                 "escaped-leaf":b"1"+json.dumps({**complete,"Leaf":"../outside"},separators=(",", ":")).encode(),
                 "torn":b'1{"Version":',"invalid-utf8":b'0{"Version":1,"Bad":"\xff"}'}
    else:
        samples={"valid":json.dumps(intent,separators=(",", ":")).encode(),
                 "wrong-version":json.dumps({**intent,"Version":999},separators=(",", ":")).encode(),
                 "escaped-target":json.dumps({**intent,"TargetLeaf":"../outside"},separators=(",", ":")).encode(),
                 "bad-digest":json.dumps({**intent,"Sha256":"not-a-digest"},separators=(",", ":")).encode(),
                 "torn":b'{"Version":1,"Nonce":',"invalid-utf8":b'{"Version":1,"Bad":"\xff"}'}
    manifest = []
    for name, content in samples.items():
        path = output / (name + ".json")
        path.write_bytes(content)
        manifest.append({"name": path.name, "bytes": len(content),
                         "sha256": hashlib.sha256(content).hexdigest()})
    (output / "manifest.json").write_text(json.dumps({"synthetic": True, "target":args.target,
                                                       "platform":args.platform,"seeds": manifest}, indent=2))
    print(json.dumps({"directory": str(output), "target":args.target,"platform":args.platform,"seeds": len(samples)}))


if __name__ == "__main__":
    main()
