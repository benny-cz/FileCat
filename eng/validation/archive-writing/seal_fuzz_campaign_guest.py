#!/usr/bin/env python3
"""Seal one finished guest-only fuzz run with its exact image and raw evidence."""

import argparse
import hashlib
import io
import json
from pathlib import Path
import tarfile


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--task", type=Path, required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--run", required=True)
    parser.add_argument("--packet", type=Path, required=True)
    parser.add_argument("--target", required=True)
    parser.add_argument("--minimum-cpu-seconds", type=int, required=True)
    parser.add_argument("--canary-run")
    parser.add_argument("--canary-replay-run")
    parser.add_argument("--short-run")
    args = parser.parse_args()
    if not args.image.replace("-", "").isalnum() or not args.run.replace("-", "").isalnum():
        parser.error("image and run must have simple alphanumeric names")
    task = args.task.resolve(strict=True)
    root = (task / args.image).resolve(strict=True)
    run = (root / "runs" / args.run).resolve(strict=True)
    packet = args.packet.resolve(strict=True)
    if root.parent != task or run.parent != root / "runs" or packet.parent != task:
        raise ValueError("Evidence paths must stay within the named guest task")
    prep = json.loads((root / "preparation.json").read_text())
    result = json.loads((run / "result.json").read_text())
    final = result["final_properties"]
    report = result["managed_report"]
    if not result["terminal"] or result.get("output_budget_failure") or result["artifacts"] or \
        result["phase"] != "clean" or result["target"] != args.target or \
        final["MainPID"] != "0" or final["Result"] != "success" or final["ExecMainStatus"] != "0" or \
        int(final["CPUUsageNSec"]) < args.minimum_cpu_seconds * 10**9 or \
        int(final["MemoryPeak"]) > 1073741824 or report["Target"] != args.target or \
        report["Calls"] != report["Accepted"] + report["Refused"] or report["Calls"] <= 0:
        raise ValueError("This fuzz run has not earned the requested clean CPU floor")
    if prep["packet_sha256"] != sha(packet) or result["sha256"]["core"] != sha(root / "clean" / "FileCat.Core.dll") or \
        result["sha256"]["harness"] != sha(root / "clean" / "FileCat.ArchiveFuzzHarness.dll") or \
        report["ModuleSha256"].lower() != result["sha256"]["core"]:
        raise ValueError("The fuzz run does not match its prepared image")
    if args.target == "tar-strict":
        names = {row["name"] for row in result["extra_seeds"]}
        if names != {"ustar-one-file.tar", "ustar-directory.tar", "ustar-duplicate.tar", "pax-unicode-time.tar"} or \
            report["SecondaryModuleSha256"].lower() != sha(root / "clean" / "FileCat.Archives.dll") or \
            not any("StrictTarCatalog::Read" in name for name in report["production_methods"]) or \
            not any("StrictTarCatalog::CompareDecoder" in name for name in report["production_methods"]):
            raise ValueError("The strict TAR and decoder coverage evidence is incomplete")
    if args.target in ("journal-state", "sevenzip-strict", "rar5-strict"):
        qualifications = (args.canary_run, args.canary_replay_run, args.short_run)
        if any(name is None or not name.replace("-", "").isalnum() for name in qualifications) or len(set(qualifications)) != 3:
            raise ValueError("Stateful journal evidence needs distinct canary, replay and short-run names")
        diagnostics = [root / "runs" / name for name in qualifications]
        if any(path.resolve(strict=True).parent != root / "runs" for path in diagnostics):
            raise ValueError("A qualification run is outside this fuzz image")
        canary, replay, short = [json.loads((path / "result.json").read_text()) for path in diagnostics]
        crashed = canary["artifacts"]
        if (canary["phase"], canary["target"], canary["terminal"], canary["canary_two_byte"]) != ("canary", "digest", True, True) or \
            (replay["phase"], replay["target"], replay["terminal"], replay["canary_two_byte"]) != ("canary", "digest", True, True) or \
            len(crashed) != 1 or crashed[0]["bytes"] != 2 or replay["artifacts"] or \
            replay["managed_report"]["Calls"] != 1 or \
            any(item["final_properties"]["Result"] != "exit-code" or item["final_properties"]["ExecMainStatus"] != "77"
                for item in (canary, replay)) or \
            any(item["sha256"]["core"] != prep["images"]["canary"]["FileCat.Core.dll"]["sha256"] for item in (canary, replay)) or \
            short["phase"] != "clean" or short["target"] != args.target or not short["terminal"] or short["artifacts"] or \
            short["final_properties"]["Result"] != "success" or short["sha256"]["core"] != prep["images"]["clean"]["FileCat.Core.dll"]["sha256"] or \
            short["managed_report"]["Calls"] <= 0:
            raise ValueError("The format/recovery canary or short clean run is incomplete")
        artifact = diagnostics[0] / "crashes" / crashed[0]["name"]
        if artifact.stat().st_size != 2 or sha(artifact) != crashed[0]["sha256"] or \
            sha(diagnostics[1] / "replay-input") != crashed[0]["sha256"]:
            raise ValueError("The intentional canary artifact changed")
        methods = short["managed_report"]["production_methods"]
        if args.target == "journal-state":
            required = ("JobJournal::ReadRecords", "ArchiveTransactionCollector::Read")
            names = {"valid-begin.json", "valid-two.json", "bad-crc.json", "torn.json",
                     "invalid-utf8.json", "unknown-tag.json", "valid-end.json", "future-header.json",
                     "unsupported-archive-record.json", "duplicate-end.json",
                     "damaged-after-header.json", "record-before-begin.json"}
            instrumented = "FileCat.Core.Archives.ArchiveTransactionCollector" in prep["instrumented_core_types"]["clean"]
        elif args.target == "sevenzip-strict":
            required = ("StrictSevenZipCatalog::Read", "StrictSevenZipFraming::Read")
            names = {"EmptyEntries.7z", "EmptyEntries.solid.7z", "FileCat.Methods.Copy.7z",
                     "FileCat.Methods.LZMA.7z", "FileCat.Methods.BZip2.7z", "FileCat.Methods.Deflate.7z",
                     "FileCat.Methods.Deflate64.7z", "7Zip.LZMA2.7z"} | {
                         f"generated-codec-{number}.7z" for number in range(7)}
            instrumented = {"FileCat.Archives.Writing.StrictSevenZipCatalog",
                            "FileCat.Archives.Writing.StrictSevenZipFraming"}.issubset(
                                prep["instrumented_tar_types"]["clean"])
        else:
            required = ("StrictRar5Catalog::Read", "StrictRar5Framing::Read")
            names = {"Rar4.rar", "Rar.malformed_512byte.rar"} | {
                f"generated-method-{method}-{stage}.rar"
                for method in range(6) for stage in ("source", "rebuilt")}
            instrumented = {"FileCat.Archives.Writing.StrictRar5Catalog",
                            "FileCat.Archives.Writing.StrictRar5Framing"}.issubset(
                                prep["instrumented_tar_types"]["clean"])
        if args.target in ("sevenzip-strict", "rar5-strict"):
            if report["SecondaryModuleSha256"].lower() != sha(root / "clean" / "FileCat.Archives.dll"):
                raise ValueError("The format run did not use the instrumented production archive assembly")
        if any(not any(method in name for name in methods) for method in required):
            raise ValueError("The short run missed a required production method")
        if {row["name"] for row in result["extra_seeds"]} != names or \
            {row["name"] for row in short["extra_seeds"]} != names or not instrumented or \
            any(not any(method in name for name in report["production_methods"]) for method in required):
            raise ValueError("The format/recovery coverage or structured seed evidence is incomplete")
    if any((run / "crashes").iterdir()):
        raise ValueError("A crash artifact was found after result collection")
    review_runs = [run] + (diagnostics if args.target in ("journal-state", "sevenzip-strict", "rar5-strict") else [])
    if any(path.is_symlink() for review in review_runs for path in review.rglob("*")):
        raise ValueError("A fuzz evidence entry is a symbolic link")
    members = [packet, root / "preparation.json",
               root / "instrumented-types-tar-clean.json", root / "clean" / "FileCat.Core.dll",
               root / "clean" / "FileCat.Archives.dll", root / "clean" / "FileCat.ArchiveFuzzHarness.dll"]
    # Early TAR images instrumented only Archives. Later journal images also
    # instrument Core; retain that report whenever the preparation claims it.
    if "instrumented_core_types" in prep:
        members.append(root / "instrumented-types-core-clean.json")
    elif args.target in ("journal-state", "sevenzip-strict", "rar5-strict"):
        raise ValueError("The image lacks its required Core instrumentation report")
    members += sorted(path for path in run.rglob("*") if path.is_file())
    if args.target in ("journal-state", "sevenzip-strict", "rar5-strict"):
        members += sorted(path for diagnostic in diagnostics if diagnostic != run
                          for path in diagnostic.rglob("*") if path.is_file())
    if sum(path.stat().st_size for path in members) > 256 * 1048576:
        raise ValueError("Fuzz evidence exceeds its retained byte limit")
    rows = []
    for path in members:
        if path.is_symlink() or not path.resolve(strict=True).is_relative_to(task) or not path.is_file():
            raise ValueError("Unreviewable fuzz evidence member: " + str(path))
        name = "packet/" + path.name if path == packet else "image/" + path.relative_to(root).as_posix()
        rows.append({"path": name, "bytes": path.stat().st_size, "sha256": sha(path)})
    output = task / (args.image + "-" + args.run + "-evidence.tar.gz")
    index = json.dumps({"image": args.image, "run": args.run, "target": args.target,
                        "minimum_cpu_seconds": args.minimum_cpu_seconds,
                        "qualification_runs": list(qualifications) if args.target in ("journal-state", "sevenzip-strict", "rar5-strict") else [],
                        "files": rows}, indent=2).encode()
    with tarfile.open(output, "x:gz", compresslevel=1) as archive:
        for path, row in zip(members, rows):
            archive.add(path, arcname=row["path"], recursive=False)
        entry = tarfile.TarInfo("index.json")
        entry.size = len(index)
        archive.addfile(entry, io.BytesIO(index))
    sidecar = {"bytes": output.stat().st_size, "sha256": sha(output)}
    output.with_suffix(".json").write_text(json.dumps(sidecar, indent=2))
    print(json.dumps({"transport": str(output), **sidecar, "members": len(rows),
                      "cpu_seconds": int(final["CPUUsageNSec"]) / 10**9,
                      "executions": report["Calls"]}))


if __name__ == "__main__":
    main()
