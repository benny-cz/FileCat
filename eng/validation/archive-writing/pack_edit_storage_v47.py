#!/usr/bin/env python3
"""Seal exact predecessor-to-v47 guest source deltas for focused dual-root review."""

import hashlib
import io
import json
from pathlib import Path
import subprocess
import tarfile
import sys


ROOT = Path(__file__).resolve().parents[3]
CANDIDATE = "5227975ec849725f644814af084c3860a18d7c60"
COMMON = (
    "src/FileCat.Core/Edit/EditSessions.cs",
    "src/FileCat.Core/Edit/EditSessions.Storage.cs",
    "tests/ArchiveEditDualRootCases.cs",
    "tests/FileCat.Core.Tests/UnixEditStorageTests.cs",
    "tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj",
    "eng/validation/archive-writing/run_edit_storage_batch.py",
)
WINDOWS = (
    "tests/FileCat.Platform.Windows.Tests/WindowsEditStorageTests.cs",
    "tests/FileCat.Platform.Windows.Tests/FileCat.Platform.Windows.Tests.csproj",
)
UBUNTU = (
    "src/FileCat.Core/Edit/EditSessions.CaptureRecovery.cs",
    "src/FileCat.App/ViewModels/MainViewModel.EditSessions.cs",
    "tests/FileCat.App.Tests/EditRetainedCopyUiTests.cs",
    "tests/FileCat.Core.Tests/ContentAndToolTests.cs",
)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main(platform):
    if platform not in ("mac", "windows", "ubuntu"):
        raise ValueError("Only the completed guest predecessors are supported")
    prior = "awe-storage-v40" if platform == "ubuntu" else "awe-storage-v46"
    evidence = ROOT / ".tmp" / "storage-evidence" / platform / (prior + "-evidence.tar.gz")
    with tarfile.open(evidence) as archive:
        ledger = json.load(archive.extractfile(prior + "/source-files.json"))
    previous = {row["path"]: row["sha256"] for row in ledger["files"]}
    files = COMMON + (WINDOWS if platform == "windows" else UBUNTU if platform == "ubuntu" else ())
    rows = []
    for name in files:
        path = ROOT / name
        if not path.is_file() or path.is_symlink():
            raise ValueError("Unreviewable v47 source: " + name)
        data = path.read_bytes()
        committed = subprocess.check_output(["git", "show", CANDIDATE + ":" + name], cwd=ROOT)
        if data not in (committed, committed.replace(b"\n", b"\r\n")):
            raise ValueError("v47 source differs from candidate commit: " + name)
        rows.append({"path": name, "bytes": len(data), "sha256": sha(data),
                     "previous_sha256": previous.get(name)})
    if previous.get("tests/ArchiveEditDualRootCases.cs") is not None:
        raise ValueError("The shared dual-root fixture already existed in the v46 predecessor")
    packet = ROOT / ".tmp" / ("archive-edit-storage-" + platform + "-delta-v47.tar.gz")
    if packet.exists():
        raise FileExistsError(packet)
    delta = json.dumps({"candidate_commit": CANDIDATE, "files": rows,
                        "separate_fuzz_checkpoint": "The v14/v15 test-only harness is outside this product build graph"},
                       indent=2).encode()
    with tarfile.open(packet, "x:gz") as archive:
        member = tarfile.TarInfo("delta.json")
        member.size = len(delta)
        archive.addfile(member, io.BytesIO(delta))
        for row in rows:
            archive.add(ROOT / row["path"], arcname=row["path"], recursive=False)
    sidecar = {"platform": platform, "packet": packet.name, "bytes": packet.stat().st_size,
               "sha256": sha(packet.read_bytes()), "candidate_commit": CANDIDATE}
    packet.with_suffix(".json").write_text(json.dumps(sidecar, indent=2))
    print(json.dumps(sidecar))


if __name__ == "__main__":
    main(sys.argv[1])
