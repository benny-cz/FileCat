"""Retain the exact branch/hint input and completed three-platform evidence."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tarfile
import xml.etree.ElementTree as ET


root = Path.cwd()
temp = root / ".tmp"
docs = root / "docs/validation/disk-space"
platforms = {"windows": "windows", "mac": "mac", "linux": "ubuntu"}
manifest_path = docs / "source-manifest-r1347-branch-refresh.json"
source_output = docs / "exact-source-r1347-branch-refresh.tgz"
receipt_output = docs / "exact-receipts-r1347-branch-refresh.tgz"
assert not any(path.exists() for path in (manifest_path, source_output, receipt_output))


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def included(path):
    assert path.is_file(), path
    retained[path.name] = path


retained = {}
original = read(temp / "branch-refresh-proof-r1334.json")
branch = read(temp / "branch-refresh-proof-r1336.json")
hints = read(temp / "branch-refresh-proof-r1339.json")
scope = read(temp / "branch-refresh-proof-r1345.json")
native = read(temp / "native-branch-proof-r1346.json")
test_source = temp / "source-r1346-native-branch.tgz"
assert sha(test_source) == native["sourceSha256"]
assert all(sha(root / entry["path"]) == entry["sha256"] for entry in native["sources"])
assert branch["baselineProofSha256"] == hints["baselineProofSha256"] == scope["baselineProofSha256"] == sha(temp / "branch-refresh-proof-r1334.json")
assert sha(temp / "source-r1334-branch-refresh.tgz") == original["sourceArchiveSha256"]

checks = {}
for platform, tag in platforms.items():
    for revision, phase in (("r1334", "app-rescan"), ("r1336", "render"), ("r1339", "render"), ("r1345", "render")):
        receipt = temp / f"{tag}-batch-{revision}-results.tgz"
        post = temp / f"{tag}-batch-{revision}-post.json"
        included(receipt)
        included(post)
        if revision != "r1334" and (platform, revision) != ("windows", "r1336"):
            assert not read(post)["filecatOrDotnet"], post
        with tarfile.open(receipt) as archive:
            assembly = ET.fromstring(archive.extractfile(phase + ".xml").read()).find("assembly")
        keys = ("total", "passed", "failed", "errors", "skipped")
        counts = tuple(int(assembly.get(key)) for key in keys)
        if revision == "r1334":
            assert counts == (6, 4, 2, 0, 0)
            failures = [test for test in assembly.iter("test") if test.get("result") == "Fail"]
            assert all("indexed: True" in test.get("name") and
                       "finished observation revision" in (test.findtext("failure/message") or "")
                       for test in failures)
        else:
            expected = {"r1336": 851, "r1339": 854, "r1345": 10}[revision]
            skipped = int(platform == "windows" and revision != "r1345")
            timed_out = int((platform, revision) == ("windows", "r1339"))
            assert counts == (expected, expected - skipped - timed_out, timed_out, 0, skipped)
            if timed_out:
                failed = [test for test in assembly.iter("test") if test.get("result") == "Fail"]
                assert len(failed) == 1 and "NativeIndexedComparisonCatalogRouteUsesEntireRevisionsAndPagesBothRecordedSides(metric: Files)" in failed[0].get("name", "")
                assert "The requested view state was not published." in (failed[0].findtext("failure/message") or "")
            controls = [test for test in assembly.iter("test") if "NativeBranchRefresh" in test.get("name") or
                        "NativeAnalysisChangeHints" in test.get("name")]
            assert len(controls) == {"r1336": 6, "r1339": 9, "r1345": 10}[revision]
            assert all(test.get("result") == "Pass" for test in controls)
        checks[f"{tag}/{revision}"] = dict(zip(keys, counts))

    receipt = temp / f"{tag}-native-r1346-results.tgz"
    post = temp / f"{tag}-native-r1346-post.json"
    included(receipt)
    included(post)
    assert not read(post)["filecatOrDotnet"], post
    with tarfile.open(receipt) as archive:
        result = json.loads(archive.extractfile("result.json").read().decode("utf-8-sig"))
    assert result["passed"] and result["exitCode"] == 0 and not result["postProcesses"]
    assert not result.get("forcedOwnedTermination")
    assert result["closed"]["state"] == "closed" and result["closed"]["sourceUnchanged"]
    probe = result["finished"]["branchRefreshProbe"]
    assert probe["originalMarksAndBasketKept"] and probe["newFilesPreserved"]
    assert probe["sessions"] == 3 and probe["branchRoot"] == "Photos" and probe["observed"] == ["after-scan.txt", "photo.png"]
    assert probe["hint"].startswith("Possibly stale: fixture.")
    checks[f"{tag}/r1346"] = probe
    for revision in ("r1334", "r1336", "r1339", "r1345", "r1346"):
        suffix = ".ps1" if platform == "windows" else ".sh"
        included(temp / f"batch-{platform}-{revision}{suffix}")

task = temp / "windows-native-r1346-task-outcome.json"
included(task)
assert read(task)["terminalExit"] == 0 and read(task)["runLevel"] == "Limited"

comparison_archive = temp / "windows-batch-r1349-results.tgz"
comparison_post = temp / "windows-batch-r1349-post.json"
included(comparison_archive)
included(comparison_post)
included(temp / "batch-windows-r1349.ps1")
assert not any("filecatdsa20261104r2" in process.lower() for process in read(comparison_post)["filecatOrDotnet"]), comparison_post
with tarfile.open(comparison_archive) as archive:
    comparison = ET.fromstring(archive.extractfile("render.xml").read()).find("assembly")
    comparison_cache = json.loads(archive.extractfile("cache-proof.json").read().decode("utf-8-sig"))
    comparison_task = json.loads(archive.extractfile("task-outcome.json").read().decode("utf-8-sig"))
with tarfile.open(temp / "windows-batch-r1345-results.tgz") as archive:
    final_scope_cache = json.loads(archive.extractfile("cache-proof.json").read().decode("utf-8-sig"))
assert comparison_cache["files"] == final_scope_cache["files"]
assert comparison_task["terminalExit"] == 0 and comparison_task["runLevel"] == "Limited"
assert tuple(int(comparison.get(key)) for key in ("total", "passed", "failed", "errors", "skipped")) == (4, 4, 0, 0, 0)
checks["windows/r1349"] = [{"name": case.get("name"), "seconds": float(case.get("time"))} for case in comparison.iter("test")]

for name in (
    "branch-refresh-proof-r1334.json", "branch-refresh-proof-r1336.json", "branch-refresh-proof-r1339.json",
    "branch-refresh-proof-r1345.json", "native-branch-proof-r1341.json", "native-branch-proof-r1346.json",
    "source-r1334-branch-refresh.tgz", "source-r1336-branch-fixture.tgz", "source-r1339-branch-fixture.tgz", "source-r1345-branch-fixture.tgz",
    "prepare-branch-refresh-r1336-original.py", "branch-refresh-controls-build-r1332.txt",
    "prepare-branch-refresh-r1339-original.py", "prepare-native-branch-r1341-original.py",
    "branch-refresh-fixture-build-r1333.txt", "branch-refresh-recorded-fixture-build-r1335.txt",
    "branch-change-hints-build-r1338.txt", "branch-refresh-native-build-r1340.txt",
    "branch-refresh-scope-final-build-r1344.txt", "branch-refresh-native-final-build-r1344.txt",
    "mac-native-r1341-results.tgz", "mac-native-r1341-post.json", "batch-mac-r1341.sh",
    "windows-batch-r1330-results.tgz", "windows-batch-r1330-post.json", "branch-refresh-main-app-proof-r1330.json",
):
    included(temp / name)

manifest = {
    "buildBase": original["head"],
    "sourceCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
    "checks": checks,
    "scope": "Branch-only native metadata refresh and bounded direct-child change hints for verified live roots.",
    "publicCommandRegistered": False,
    "physicalInputQualified": False,
    "screenReaderQualified": False,
    "completeWatcherCoverageQualified": False,
    "fullGoalComplete": False,
    "finalIndependentReviewPerformed": False,
    "retained": {name: sha(path) for name, path in sorted(retained.items())},
}

shutil.copyfile(test_source, source_output)
with tarfile.open(receipt_output, "w:gz") as output:
    for name, path in sorted(retained.items()):
        output.add(path, arcname=name, recursive=False)
manifest["archives"] = {path.name: sha(path) for path in (source_output, receipt_output)}
manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print(f"Retained {len(checks)} verification records and {len(retained)} original inputs/receipts.")
