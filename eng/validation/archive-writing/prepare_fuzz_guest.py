#!/usr/bin/env python3
"""Create fresh, source-bound guest fuzz images from a verified product build."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile


CANDIDATE = "a521bf45285110a2c836d9caec824e207fb99a5e"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True)
    parser.add_argument("--packet", type=Path, required=True)
    parser.add_argument("--packet-sha256", required=True)
    parser.add_argument("--image-name", required=True)
    parser.add_argument("--product-core-sha256", required=True)
    parser.add_argument("--product-archives-sha256", required=True)
    args = parser.parse_args()
    task = args.task.resolve(strict=True)
    packet = args.packet.resolve(strict=True)
    if sha(packet) != args.packet_sha256:
        raise ValueError("Fuzz harness packet hash mismatch")
    if not args.image_name.replace("-", "").isalnum():
        parser.error("image-name must be alphanumeric with optional dashes")
    prior = task / "fuzz-canary-v2"
    product = task / "archive-workflow-source-v1" / "tests" / "FileCat.Core.Tests" / "bin" / "Release" / "net10.0"
    verified = task / "awe-storage-v40"
    if json.loads((verified / "delta.json").read_text())["candidate_commit"] != CANDIDATE:
        raise ValueError("Guest product candidate differs from the v40 recovery cohort")
    index = {row["path"]: row for row in json.loads((verified / "files.json").read_text())["files"]}
    expected = {"FileCat.Core.dll": args.product_core_sha256,
                "FileCat.Archives.dll": args.product_archives_sha256}
    for name, digest in expected.items():
        if len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest) or \
            index["binaries/" + name]["sha256"] != digest or \
            sha(verified / "binaries" / name) != digest:
            raise ValueError("Guest product binary differs from sealed v40 evidence: " + name)
    root = task / args.image_name
    root.mkdir(exist_ok=False)
    for name in ("libfuzzer-dotnet", "driver.cc", "LICENSE", "tool-artifacts.json"):
        shutil.copyfile(prior / name, root / name)
    (root / "libfuzzer-dotnet").chmod(0o700)
    with tarfile.open(packet) as tar:
        names = tar.getnames()
        if any(not n.startswith(("harness/", "source/")) for n in names):
            raise ValueError("Unexpected packet member")
        tar.extractall(root, filter="data")
    for phase in ("clean", "canary"):
        shutil.copytree(prior / phase, root / phase)
        for source in (root / "harness").iterdir():
            shutil.copyfile(source, root / phase / source.name)
        for name in ("FileCat.Core.dll", "FileCat.Core.pdb", "FileCat.Archives.dll", "FileCat.Archives.pdb", "FileCat.Archives.Rar15.dll",
                     "FileCat.Archives.Rar15.pdb", "FileCat.SharpCompress.dll", "FileCat.SharpCompress.pdb",
                     "DiscUtils.Core.dll", "DiscUtils.Streams.dll", "DiscUtils.Iso9660.dll", "DiscUtils.Udf.dll"):
            # A later product build may already be under test. Freeze the exact v40
            # assemblies in this campaign without changing that build's output.
            sealed = verified / "binaries" / name
            if sealed.is_file():
                row = index.get("binaries/" + name)
                if row is None or sha(sealed) != row["sha256"]:
                    raise ValueError("Sealed v40 dependency differs from its manifest: " + name)
            shutil.copyfile(sealed if sealed.is_file() else product / name,
                            root / phase / name)
    (root / "runs").mkdir()
    dotnet = task / "dotnet" / "dotnet"
    env = os.environ.copy()
    env.update(SHARPFUZZ_ENABLE_ON_BRANCH_CALLBACK="1", DOTNET_EnableDiagnostics="0")
    for phase in ("clean", "canary"):
        if phase == "canary":
            patched = root / phase / "FileCat.Core.canary.dll"
            with (root / "patch-canary.log").open("x") as log:
                subprocess.run([str(dotnet), str(root / phase / "FileCat.ArchiveFuzzHarness.dll"),
                                "patch-canary", str(root / phase / "FileCat.Core.dll"), str(patched)],
                               env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=60)
            patched.replace(root / phase / "FileCat.Core.dll")
        with (root / f"instrument-core-{phase}.log").open("x") as log:
            subprocess.run([str(dotnet), str(root / phase / "FileCat.ArchiveFuzzHarness.dll"),
                            "instrument", str(root / phase / "FileCat.Core.dll"),
                            str(root / f"instrumented-types-core-{phase}.json")],
                           env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=60)
        core_types = json.loads((root / f"instrumented-types-core-{phase}.json").read_text())
        if not {"FileCat.Core.Edit.EditSessionStore", "FileCat.Core.Edit.EditSaveCopyIntent",
                "FileCat.Core.Edit.EditSaveCopyRecord", "FileCat.Core.Jobs.JobJournal",
                "FileCat.Core.Archives.ArchiveTransactionCollector",
                "FileCat.Core.Archives.ArchiveAssessmentWire", "FileCat.Core.Archives.ArchiveWriteBudget",
                "FileCat.Core.Archives.ArchiveWritePolicy"}.issubset(core_types):
            raise ValueError("Production recovery parser/write guard was not instrumented")
        with (root / f"instrument-tar-{phase}.log").open("x") as log:
            subprocess.run([str(dotnet), str(root / phase / "FileCat.ArchiveFuzzHarness.dll"),
                            "instrument-tar", str(root / phase / "FileCat.Archives.dll"),
                            str(root / f"instrumented-types-tar-{phase}.json")],
                           env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=60)
        types = json.loads((root / f"instrumented-types-tar-{phase}.json").read_text())
        if not {"FileCat.Archives.Writing.StrictTarCatalog",
                "FileCat.Archives.Writing.StrictSevenZipCatalog",
                "FileCat.Archives.Writing.StrictSevenZipFraming",
                "FileCat.Archives.Writing.SevenZipStrictMetadata",
                "FileCat.Archives.Writing.SevenZipStrictHeaderReader",
                "FileCat.Archives.Writing.StrictRar5Catalog",
                "FileCat.Archives.Writing.StrictRar5Framing",
                "FileCat.Archives.Writing.StrictGzipReadStream",
                "FileCat.Archives.Writing.StrictBzip2ReadStream",
                "FileCat.Archives.Writing.StrictZstdReadStream",
                "FileCat.Archives.Writing.ZstdFrameHeader",
                "FileCat.Archives.SevenZipReadPasswordSession",
                "FileCat.Archives.Rar.Rar5ReadPasswordSession",
                "FileCat.Archives.Writing.ArchiveCryptoWorkBudget"}.issubset(types):
            raise ValueError("Strict TAR, wrapper, 7z or RAR5 scanner was not instrumented")
    facts = {"candidate": CANDIDATE, "verified_folder": "awe-storage-v40",
             "verified_index_sha256": sha(verified / "files.json"),
             "packet_sha256": sha(packet), "product_core_sha256": expected["FileCat.Core.dll"],
             "product_archives_sha256": expected["FileCat.Archives.dll"],
             "driver_sha256": sha(root / "libfuzzer-dotnet"),
             "images": {phase: {name: {"bytes": (root / phase / name).stat().st_size,
                                         "sha256": sha(root / phase / name)}
                                for name in ("FileCat.Core.dll", "FileCat.Archives.dll",
                                             "FileCat.ArchiveFuzzHarness.dll")}
                        for phase in ("clean", "canary")},
             "instrumented_tar_types": {
                 phase: json.loads((root / f"instrumented-types-tar-{phase}.json").read_text())
                 for phase in ("clean", "canary")},
             "instrumented_core_types": {
                 phase: json.loads((root / f"instrumented-types-core-{phase}.json").read_text())
                 for phase in ("clean", "canary")}}
    (root / "preparation.json").write_text(json.dumps(facts, indent=2))
    print(json.dumps(facts))


if __name__ == "__main__":
    main()
