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


PRODUCT_CORE = "9d1897fb1ba19c0c16a19477cd2d82e996c00d42671d6e22db5e3f890953faba"
PRODUCT_ARCHIVES = "1ad84b8bc81b659ce04f1356c3874ec6877bb67b646901eb7812391b72f3e4a2"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True)
    parser.add_argument("--packet", type=Path, required=True)
    parser.add_argument("--packet-sha256", required=True)
    parser.add_argument("--image-name", required=True)
    args = parser.parse_args()
    task = args.task.resolve(strict=True)
    packet = args.packet.resolve(strict=True)
    if sha(packet) != args.packet_sha256:
        raise ValueError("Fuzz harness packet hash mismatch")
    if not args.image_name.replace("-", "").isalnum():
        parser.error("image-name must be alphanumeric with optional dashes")
    prior = task / "fuzz-canary-v2"
    product = task / "archive-workflow-source-v1" / "tests" / "FileCat.Core.Tests" / "bin" / "Release" / "net10.0"
    if sha(product / "FileCat.Core.dll") != PRODUCT_CORE or sha(product / "FileCat.Archives.dll") != PRODUCT_ARCHIVES:
        raise ValueError("Verified guest product binaries have changed")
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
            shutil.copyfile(product / name, root / phase / name)
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
        if "FileCat.Core.Edit.EditSessionStore" not in core_types or "FileCat.Core.Archives.ArchiveWritePolicy" not in core_types:
            raise ValueError("Production recovery parser/write guard was not instrumented")
        with (root / f"instrument-tar-{phase}.log").open("x") as log:
            subprocess.run([str(dotnet), str(root / phase / "FileCat.ArchiveFuzzHarness.dll"),
                            "instrument-tar", str(root / phase / "FileCat.Archives.dll"),
                            str(root / f"instrumented-types-tar-{phase}.json")],
                           env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=60)
        types = json.loads((root / f"instrumented-types-tar-{phase}.json").read_text())
        if "FileCat.Archives.Writing.StrictTarCatalog" not in types:
            raise ValueError("Strict TAR scanner was not instrumented")
    facts = {"candidate": "8355ee58cd21edf2b7e4eab185309be669c3c9ad",
             "packet_sha256": sha(packet), "product_core_sha256": PRODUCT_CORE,
             "product_archives_sha256": PRODUCT_ARCHIVES,
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
