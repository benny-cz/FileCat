#!/usr/bin/env python3
"""Independently verify sealed native fuzz transports of one candidate-bound campaign (seal_fuzz_candidate_guest.py).

`verify IMAGE RUN TARGET MINIMUM_CPU_SECONDS CANDIDATE` checks one transport from its raw files: hashes, the candidate and
packet binding, the clean image, the canary and replay proof, the short run, exact synthetic seeds, required production
methods, instrumented types and the CPU floor. `campaign CANDIDATE LIST.json` verifies every listed transport and the plan's
format-release floors (13.6): at least 24 CPU-hours per format family and for the shared stateful/protocol/recovery
targets, and at least 2 CPU-hours in each target. Test tooling, not an application dependency.
"""

import hashlib
import io
import json
from pathlib import Path
import sys
import tarfile


ROOT = Path(__file__).resolve().parents[3]
EVIDENCE = ROOT / ".tmp/storage-evidence/ubuntu"
HOUR = 3600

# Each target's family, the production methods every short and long run must reach (substrings of the managed report's
# method names), and whether it binds the instrumented archive assembly as its secondary module.
TARGETS = {
    "tar-strict": ("tar", ("StrictTarCatalog::Read(", "StrictTarCatalog::CompareDecoder("), True),
    "member-keys": ("tar", ("ArchiveTarTransformPlan::Create(", "TarRewriteEngine::"), False),
    "create-inputs": ("tar", ("ArchiveTarTransformPlan::Create(", "TarRewriteEngine::", "ArchiveVerificationClient"), False),
    "update-changes": ("tar", ("ArchiveTarTransformPlan::Create(", "TarRewriteEngine::", "ArchiveVerificationClient"), False),
    "verify-output": ("tar", ("ArchiveVerificationClient", "StrictTarCatalog::Read("), False),
    "gzip-strict": ("compressed-streams", ("StrictGzipReadStream::.ctor(", "StrictGzipReadStream::Read("), True),
    "bzip2-strict": ("compressed-streams", ("StrictBzip2ReadStream::.ctor(", "StrictBzip2ReadStream::Read("), True),
    "zstd-strict": ("compressed-streams", ("StrictZstdReadStream::.ctor(", "StrictZstdReadStream::Read(", "ZstdFrameHeader::Read("), True),
    "standalone-streams": ("compressed-streams", ("ArchiveStandaloneTransformPlan::Create(", "StandaloneRewriteEngine::"), False),
    "sevenzip-strict": ("sevenzip", ("StrictSevenZipCatalog::Read(", "StrictSevenZipFraming::Read("), True),
    "sevenzip-password": ("sevenzip", ("StrictSevenZipCatalog::Read(", "StrictSevenZipFraming::Read(", "SevenZipReadPasswordSession::.ctor("), True),
    "sevenzip-writer": ("sevenzip", ("ArchiveSevenZipTransformPlan::Create(", "SevenZipRewriteEngine::", "StrictSevenZipCatalog::Read("), False),
    "rar5-strict": ("rar5", ("StrictRar5Catalog::Read(", "StrictRar5Framing::Read("), True),
    "rar5-password": ("rar5", ("StrictRar5Catalog::Read(", "StrictRar5Framing::Read(", "Rar5ReadPasswordSession::.ctor("), True),
    "rar5-writer": ("rar5", ("ArchiveRar5TransformPlan::Create(", "Rar5RewriteEngine::", "StrictRar5Catalog::Read("), False),
    "zip-archives": ("zip", ("ArchiveZipTransformPlan::Create(", "ZipRewriteEngine::", "StrictZipCatalog::"), False),
    "worker-frames": ("shared", ("ArchiveWorkerProtocol::Read(", "ArchiveWorkerSession"), False),
    "stream-faults": ("shared", ("ArchiveWorkerSession", "ArchiveTransformClient"), False),
    "repeated-cancel": ("shared", ("ArchiveWorkerSession", "ArchiveWorkerProtocol::Read("), False),
    "publication-races": ("shared", ("ArchiveNewFilePublication::", "JobJournal::"), False),
    "journal-state": ("shared", ("JobJournal::ReadRecords(", "ArchiveTransactionCollector::Read("), False),
    "journal-record": ("shared", ("JobJournal::ReadRecords(", "JobJournal::ReadBoundedLines("), False),
    "session-record": ("shared", ("EditSessionStore::ParseRecord(",), False),
    "capture-record": ("shared", ("EditSessionStore::ParseCaptureRecord(",), False),
    "save-copy-intent": ("shared", ("EditSaveCopyIntent::Decode(",), False),
    "save-copy-receipt": ("shared", ("EditSaveCopyRecord::Decode(",), False),
    "manifest-member": ("shared", ("ArchiveAssessmentWire::ReadMember(",), False),
}
REQUIRED_TYPES = {
    "core": {"FileCat.Core.Archives.ArchiveWritePolicy", "FileCat.Core.Archives.ArchiveTransformWire",
             "FileCat.Core.Archives.ArchiveTarTransformPlan", "FileCat.Core.Archives.ArchiveWorkerProtocol",
             "FileCat.Core.Archives.ArchiveTransactionCollector", "FileCat.Core.Jobs.JobJournal",
             "FileCat.Core.Edit.EditSessionStore"},
    "tar": {"FileCat.Archives.Writing.StrictTarCatalog", "FileCat.Archives.Writing.StrictSevenZipCatalog",
            "FileCat.Archives.Writing.StrictRar5Catalog", "FileCat.Archives.Writing.StrictZipCatalog",
            "FileCat.Archives.Writing.TarRewriteEngine", "FileCat.Archives.Writing.SevenZipRewriteEngine",
            "FileCat.Archives.Writing.Rar5RewriteEngine", "FileCat.Archives.Writing.ZipRewriteEngine",
            "FileCat.Archives.Writing.StandaloneRewriteEngine", "FileCat.Archives.Writing.ArchiveTransformWorker"},
    "codecs": {"SharpCompress.Compressors.LZMA.LzmaStream", "SharpCompress.Compressors.BZip2.CBZip2InputStream",
               "SharpCompress.Compressors.Deflate.DeflateStream", "SharpCompress.Compressors.PPMd.PpmdStream",
               "SharpCompress.Compressors.ZStandard.Unsafe.Methods"},
}
# The runner's seed selection: request bytes for the structured targets, archives or records for the format/record readers.
SEED_EXTENSIONS = {"tar-strict": ".tar", "sevenzip-strict": ".7z", "sevenzip-password": ".7z", "rar5-strict": ".rar",
                   "rar5-password": ".rar", "gzip-strict": ".gz", "bzip2-strict": ".bz2", "zstd-strict": ".zst",
                   **{target: ".json" for target in ("session-record", "capture-record", "save-copy-intent",
                                                     "save-copy-receipt", "journal-record", "journal-state")}}
FAMILY_FLOOR = 24 * HOUR
TARGET_FLOOR = 2 * HOUR


def sha(data):
    return hashlib.sha256(data).hexdigest()


def slow_only(artifacts):
    """libFuzzer saves an input slower than -report_slow_units (10 s) and keeps fuzzing: a report, not a failure."""
    return all(artifact["name"].startswith("slow-unit-") for artifact in artifacts)


def methods_reached(report, required):
    names = report["production_methods"]
    return all(any(fragment in name for name in names) for fragment in required)


def verify(image, run, target, minimum_cpu_seconds, candidate):
    family, required, secondary = TARGETS[target]
    path = EVIDENCE / (image + "-" + run + "-evidence.tar.gz")
    sidecar = json.loads(path.with_suffix(".json").read_text())
    assert path.stat().st_size == sidecar["bytes"] and sha(path.read_bytes()) == sidecar["sha256"]
    with tarfile.open(path, "r:gz") as archive:
        members = archive.getmembers()
        assert all(item.isfile() and not item.issym() and not item.islnk() and not item.name.startswith("/") and
                   ".." not in Path(item.name).parts for item in members)
        assert len({item.name for item in members}) == len(members)
        data = {item.name: archive.extractfile(item).read() for item in members}
    index = json.loads(data.pop("index.json"))
    assert (index["image"], index["run"], index["target"], index["minimum_cpu_seconds"], index["candidate"]) == \
        (image, run, target, minimum_cpu_seconds, candidate)
    rows = {row["path"]: row for row in index["files"]}
    assert set(rows) == set(data) and sum(len(value) for value in data.values()) <= 256 * 1048576
    for name, value in data.items():
        assert rows[name] == {"path": name, "bytes": len(value), "sha256": sha(value)}
    file = lambda name: data["image/" + name]
    prep = json.loads(file("preparation.json"))
    assert prep["candidate"] == candidate
    packets = [value for name, value in data.items() if name.startswith("packet/")]
    assert len(packets) == 1 and prep["packet_sha256"] == sha(packets[0])
    clean = prep["images"]["clean"]
    for name in ("FileCat.Core.dll", "FileCat.Archives.dll", "FileCat.SharpCompress.dll", "FileCat.ArchiveFuzzHarness.dll"):
        assert clean[name]["sha256"] == sha(file("clean/" + name)) and clean[name]["bytes"] == len(file("clean/" + name))
    # The packet holds exactly the image's harness and the sealer that sealed this transport; the instrumented product
    # copies differ from the recorded product files.
    with tarfile.open(fileobj=io.BytesIO(packets[0]), mode="r:gz") as packet:
        harness = packet.extractfile("harness/FileCat.ArchiveFuzzHarness.dll").read()
        sealer = packet.extractfile("source/seal_fuzz_candidate_guest.py").read()
    assert sha(harness) == clean["FileCat.ArchiveFuzzHarness.dll"]["sha256"]
    # The sealer shipped in the packet, or this repository's committed one (which also accepts slow-unit reports).
    # Hashed as the Linux guest sealed it: a Windows checkout of this repository may hold the script with CRLF endings.
    current = (Path(__file__).resolve().parent / "seal_fuzz_candidate_guest.py").read_bytes().replace(b"\r\n", b"\n")
    assert index["sealer_sha256"] in (sha(sealer), sha(current))
    for name in ("FileCat.Core.dll", "FileCat.Archives.dll", "FileCat.SharpCompress.dll"):
        assert prep["product_files"][name] != clean[name]["sha256"]
    for kind in ("core", "tar", "codecs"):
        types = json.loads(file(f"instrumented-types-{kind}-clean.json"))
        key = {"core": "instrumented_core_types", "tar": "instrumented_tar_types", "codecs": "instrumented_codec_types"}[kind]
        assert types == prep[key]["clean"] and REQUIRED_TYPES[kind].issubset(types)
    canary_name, replay_name, short_name = index["qualification_runs"]
    result = json.loads(file("runs/" + run + "/result.json"))
    canary = json.loads(file("runs/" + canary_name + "/result.json"))
    replay = json.loads(file("runs/" + replay_name + "/result.json"))
    short = json.loads(file("runs/" + short_name + "/result.json"))
    final = result["final_properties"]; report = result["managed_report"]
    assert result["terminal"] and not result.get("output_budget_failure") and slow_only(result["artifacts"])
    assert result["target"] == report["Target"] == target and result["phase"] == "clean"
    assert final["MainPID"] == "0" and final["Result"] == "success" and final["ExecMainStatus"] == "0"
    assert int(final["CPUUsageNSec"]) >= minimum_cpu_seconds * 10**9 and int(final["MemoryPeak"]) <= 1073741824
    assert report["Calls"] == report["Accepted"] + report["Refused"] and report["Calls"] > 0
    for item, phase in ((result, "clean"), (short, "clean"), (canary, "canary"), (replay, "canary")):
        images = prep["images"][phase]
        assert item["sha256"]["core"] == images["FileCat.Core.dll"]["sha256"] == item["managed_report"]["ModuleSha256"].lower()
        assert item["sha256"]["harness"] == images["FileCat.ArchiveFuzzHarness.dll"]["sha256"]
        assert item["sha256"]["archives"] == images["FileCat.Archives.dll"]["sha256"]
        assert item["sha256"]["codecs"] == images["FileCat.SharpCompress.dll"]["sha256"]
    # The intentional two-byte canary is found by the instrumented canary image and reproduced by its replay.
    assert all(item["target"] == "digest" and item["terminal"] and item["canary_two_byte"] and
               item["final_properties"]["Result"] == "exit-code" and item["final_properties"]["ExecMainStatus"] == "77" and
               any("ArchiveWritePolicy::ValidateDigest(" in name for name in item["managed_report"]["production_methods"])
               for item in (canary, replay))
    assert len(canary["artifacts"]) == 1 and canary["artifacts"][0]["bytes"] == 2
    crash = canary["artifacts"][0]
    assert sha(file("runs/" + canary_name + "/crashes/" + crash["name"])) == crash["sha256"]
    assert sha(file("runs/" + replay_name + "/replay-input")) == crash["sha256"]
    assert not replay["artifacts"] and replay["managed_report"]["Calls"] == 1
    assert short["target"] == target and short["terminal"] and slow_only(short["artifacts"]) and short["managed_report"]["Calls"] > 0
    assert short["final_properties"]["Result"] == "success" and short["final_properties"]["ExecMainStatus"] == "0"
    # Both runs reached the target's production entry points through the instrumented images.
    assert methods_reached(short["managed_report"], required) and methods_reached(report, required)
    if secondary:
        for item in (result, short):
            assert item["managed_report"]["SecondaryModuleSha256"].lower() == clean["FileCat.Archives.dll"]["sha256"]
    # Every synthetic seed of the image's folder entered both corpora, byte for byte.
    folder = {name.split("/")[-1]: value for name, value in data.items() if name.startswith(f"image/seeds-{target}/")}
    expected = {name: (len(value), sha(value)) for name, value in folder.items()
                if name != "manifest.json" and name.endswith(SEED_EXTENSIONS.get(target, ".bin"))}
    assert expected, "A campaign target needs its structured synthetic seeds"
    for item, name in ((result, run), (short, short_name)):
        assert {row["name"]: (row["bytes"], row["sha256"]) for row in item["extra_seeds"]} == expected
        for number, row in enumerate(item["extra_seeds"]):
            content = file("runs/" + name + f"/corpus/synthetic-{number:04d}")
            assert len(content) == row["bytes"] and sha(content) == row["sha256"]
    for name in (run, short_name):
        assert not any(key.startswith(f"image/runs/{name}/crashes/") and not key.split("/")[-1].startswith("slow-unit-") or
                       key.startswith(f"image/runs/{name}/dumps/") for key in data)
    return {"image": image, "run": run, "target": target, "family": family,
            "cpu_seconds": int(final["CPUUsageNSec"]) / 10**9, "executions": report["Calls"],
            "accepted": report["Accepted"], "refused": report["Refused"], "oracle_skipped": report.get("OracleSkipped", 0),
            "methods": len(report["production_methods"]), "seeds": len(expected), "slow_units": len(result["artifacts"]),
            "transport_sha256": sidecar["sha256"]}


def campaign(candidate, listing):
    rows = [verify(item["image"], item["run"], item["target"], item["minimum_cpu_seconds"], candidate)
            for item in json.loads(Path(listing).read_text())]
    assert len({(row["image"], row["run"]) for row in rows}) == len(rows)
    by_target = {target: sum(row["cpu_seconds"] for row in rows if row["target"] == target) for target in TARGETS}
    by_family = {}
    for target, (family, _, _) in TARGETS.items():
        by_family[family] = by_family.get(family, 0) + by_target[target]
    short = sorted(target for target, seconds in by_target.items() if seconds < TARGET_FLOOR)
    low = sorted(family for family, seconds in by_family.items() if seconds < FAMILY_FLOOR)
    return {"candidate": candidate, "transports": len(rows),
            "cpu_hours_by_family": {family: round(seconds / HOUR, 3) for family, seconds in by_family.items()},
            "cpu_hours_by_target": {target: round(seconds / HOUR, 3) for target, seconds in by_target.items()},
            "executions": sum(row["executions"] for row in rows), "targets_below_floor": short,
            "families_below_floor": low, "floors_met": not short and not low, "runs": rows}


if __name__ == "__main__":
    if sys.argv[1:2] == ["verify"] and len(sys.argv) == 7:
        print(json.dumps(verify(sys.argv[2], sys.argv[3], sys.argv[4], int(sys.argv[5]), sys.argv[6]), indent=2))
    elif sys.argv[1:2] == ["campaign"] and len(sys.argv) == 4:
        print(json.dumps(campaign(sys.argv[2], sys.argv[3]), indent=2))
    else:
        raise SystemExit("usage: verify IMAGE RUN TARGET MINIMUM_CPU_SECONDS CANDIDATE | campaign CANDIDATE LIST.json")
