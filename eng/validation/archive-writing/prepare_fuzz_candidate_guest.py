#!/usr/bin/env python3
"""Create fresh guest fuzz images bound to one exact candidate commit.

prepare_fuzz_guest.py binds its images to the sealed v40 cohort. This variant binds an image to one clean
checkout: the product assemblies (src/FileCat.Archives, Release) and the harness (built against them through its
hint paths) come from that checkout's own build, the harness packet must hold exactly that harness build, and every
copied product file is hashed into preparation.json. Only the native libFuzzer driver and its provenance record come
from a prior reviewed image. Test tooling: nothing here is an application dependency.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile


PRODUCT = ("FileCat.Core.dll", "FileCat.Core.pdb", "FileCat.Archives.dll", "FileCat.Archives.pdb",
           "FileCat.Archives.Rar15.dll", "FileCat.Archives.Rar15.pdb", "FileCat.SharpCompress.dll",
           "FileCat.SharpCompress.pdb", "DiscUtils.Core.dll", "DiscUtils.Streams.dll", "DiscUtils.Iso9660.dll",
           "DiscUtils.Udf.dll", "BouncyCastle.Cryptography.dll")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(checkout, *arguments):
    return subprocess.check_output(["git", "-C", str(checkout), *arguments], text=True).strip()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True, help="Folder with dotnet/ and the prior driver image")
    parser.add_argument("--prior", default="fuzz-canary-v2", help="Prior image whose reviewed native driver is reused")
    parser.add_argument("--packet", type=Path, required=True)
    parser.add_argument("--packet-sha256", required=True)
    parser.add_argument("--image-name", required=True)
    parser.add_argument("--checkout", type=Path, required=True, help="Clean checkout that built product and harness")
    parser.add_argument("--candidate", required=True, help="The checkout's HEAD commit")
    args = parser.parse_args()
    task = args.task.resolve(strict=True)
    checkout = args.checkout.resolve(strict=True)
    packet = args.packet.resolve(strict=True)
    if sha(packet) != args.packet_sha256:
        raise ValueError("Fuzz harness packet hash mismatch")
    if not args.image_name.replace("-", "").isalnum() or not args.prior.replace("-", "").isalnum():
        parser.error("image and prior names must be alphanumeric with optional dashes")
    head = git(checkout, "rev-parse", "HEAD")
    if len(head) != 40 or head != args.candidate:
        raise ValueError("The checkout is not at the named candidate")
    if git(checkout, "status", "--porcelain", "--untracked-files=no"):
        raise ValueError("The candidate checkout has uncommitted changes")
    # Build src/FileCat.Archives with -p:CopyLocalLockFileAssemblies=true so its package dependencies sit beside it.
    product = checkout / "src" / "FileCat.Archives" / "bin" / "Release" / "net10.0"
    harness = checkout / "eng" / "validation" / "archive-writing" / "ArchiveFuzzHarness" / "bin" / "Release" / "net10.0"
    missing = [name for name in PRODUCT if not (product / name).is_file()]
    if missing:
        raise FileNotFoundError("The candidate product build lacks " + ", ".join(missing))
    prior = task / args.prior
    tools = json.loads((prior / "tool-artifacts.json").read_text())
    for name in ("libfuzzer-dotnet", "driver.cc", "LICENSE"):
        if sha(prior / name) != tools[name]["sha256"]:
            raise ValueError("Prior driver artifact differs from its provenance record: " + name)
    root = task / args.image_name
    root.mkdir(exist_ok=False)
    for name in ("libfuzzer-dotnet", "driver.cc", "LICENSE", "tool-artifacts.json"):
        shutil.copyfile(prior / name, root / name)
    (root / "libfuzzer-dotnet").chmod(0o700)
    with tarfile.open(packet) as tar:
        if any(not name.startswith(("harness/", "source/")) for name in tar.getnames()):
            raise ValueError("Unexpected packet member")
        tar.extractall(root, filter="data")
    # The packet holds this checkout's harness build, and that build references exactly these product assemblies.
    for source in (root / "harness").iterdir():
        if sha(source) != sha(harness / source.name):
            raise ValueError("The packet harness is not this checkout's build: " + source.name)
    for name in ("FileCat.Core.dll", "FileCat.Archives.dll", "FileCat.SharpCompress.dll"):
        if sha(harness / name) != sha(product / name):
            raise ValueError("The harness was built against other product assemblies: " + name)
    for phase in ("clean", "canary"):
        (root / phase).mkdir()
        for source in (root / "harness").iterdir():
            shutil.copyfile(source, root / phase / source.name)
        for name in PRODUCT:
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
                           env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=120)
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
                           env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=120)
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
    facts = {"candidate": head, "checkout": str(checkout), "prior_image": args.prior,
             "packet_sha256": sha(packet), "product_core_sha256": sha(product / "FileCat.Core.dll"),
             "product_archives_sha256": sha(product / "FileCat.Archives.dll"),
             "product_files": {name: sha(product / name) for name in PRODUCT},
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
    print(json.dumps({key: value for key, value in facts.items() if not key.startswith("instrumented")}))


if __name__ == "__main__":
    main()
