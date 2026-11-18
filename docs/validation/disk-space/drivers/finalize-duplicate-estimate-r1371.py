"""Retain the exact duplicate read-cost preview and three-platform evidence."""
import hashlib
import io
import json
from pathlib import Path
import tarfile
import xml.etree.ElementTree as ET

root = Path.cwd()
temp = root / ".tmp"
validation = root / "docs/validation/disk-space"
manifest = validation / "source-manifest-r1371-duplicate-estimate.json"
source_output = validation / "exact-source-deltas-r1371-duplicate-estimate.tgz"
receipt_output = validation / "exact-receipts-r1371-duplicate-estimate.tgz"
assert not any(path.exists() for path in (manifest, source_output, receipt_output))


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def member(archive, name):
    return json.loads(archive.extractfile(name).read().decode("utf-8-sig"))


render = read(temp / "duplicate-estimate-source-proof-r1366.json")
original = read(temp / "duplicate-estimate-native-proof-r1368.json")
native = read(temp / "duplicate-estimate-native-proof-r1370.json")
render_package = read(temp / "recipe-rendering-proof-r1366.json")
sources = dict(render["changes"])
folder = "tests/FileCat.NativeAcceptance/DiskSpaceHarness/"
sources[folder + "DuplicateReview.cs"] = native["harnessSource"]
sources[folder + "Program.cs"] = native["programSource"]
assert all(sha(root / name) == digest for name, digest in sources.items())
assert sha(temp / "source-r1366-duplicate-estimate-delta.tgz") == render["archive"]
assert sha(temp / "duplicate-review-harness-source-r1368.cs") == original["harnessSource"]
assert native["programSource"] == original["programSource"]

files = {"duplicate-estimate-render-build-r1366.txt", "duplicate-estimate-render-build-r1366-serial.txt",
         "duplicate-estimate-native-build-r1367.txt", "duplicate-estimate-native-build-r1368.txt",
         "duplicate-estimate-source-proof-r1366.json", "recipe-rendering-proof-r1366.json",
         "duplicate-estimate-native-build-r1370.txt", "duplicate-review-harness-source-r1368.cs",
         "duplicate-estimate-native-proof-r1368.json", "duplicate-estimate-native-proof-r1370.json",
         "windows-batch-r1366-task-outcome.json", "windows-native-r1368-task-outcome.json", "windows-native-r1370-task-outcome.json"}
assert read(temp / "windows-native-r1368-task-outcome.json")["terminalExit"] == 1
for receipt in ("windows-batch-r1366-task-outcome.json", "windows-native-r1370-task-outcome.json"):
    task = read(temp / receipt)
    assert task["terminalExit"] == 0 and task["runLevel"] == "Limited"

checks = {}
private_mac = None
for platform, tag in (("windows", "windows"), ("mac", "mac"), ("linux", "ubuntu")):
    suffix = ".ps1" if platform == "windows" else ".sh"
    render_runner = f"batch-{platform}-r1366{suffix}"
    native_runner = f"batch-{platform}-r1370{suffix}"
    native_plan = f"batch-r1370-{platform}/plan.json"
    original_runner = f"batch-{platform}-r1368{suffix}"
    original_plan = f"batch-r1368-{platform}/plan.json"
    files.update((render_runner, native_runner, native_plan, original_runner, original_plan))
    plan = read(temp / native_plan)
    proof = native["platforms"][platform]
    assert sha(temp / native_runner) == proof["runner"] and sha(temp / render_runner) == render_package[platform]["runner"]
    assert sha(temp / f"dsa-batch-{platform}-r1366.tgz") == render_package[platform]["archive"]
    assert sha(temp / original_runner) == original["platforms"][platform]["runner"]
    assert sha(temp / f"dsa-batch-{platform}-r1368.tgz") == original["platforms"][platform]["bundle"]
    assert sha(temp / f"dsa-batch-{platform}-r1370.tgz") == proof["bundle"]
    with tarfile.open(temp / f"dsa-batch-{platform}-r1370.tgz") as bundle:
        assert member(bundle, "plan.json") == plan

    rendering = f"{tag}-batch-r1366-results.tgz"
    files.add(rendering)
    for post in (f"{tag}-batch-r1366-post.json", f"{tag}-native-r1368-post.json", f"{tag}-native-r1370-post.json"):
        files.add(post)
        assert not read(temp / post)["filecatOrDotnet"]
    with tarfile.open(temp / rendering) as archive:
        assembly = ET.fromstring(archive.extractfile("render.xml").read()).find("assembly")
        assert tuple(int(assembly.get(key)) for key in ("total", "passed", "failed", "errors", "skipped")) == (12, 12, 0, 0, 0)
        tests = [test for test in assembly.iter("test") if "DuplicateReview" in test.get("name", "")]
        assert len(tests) == 4 and all(test.get("result") == "Pass" for test in tests)
        hashes = {row["path"]: row["sha256"] for row in member(archive, "cache-proof.json")["files"]}
    product = {row["path"]: row["sha256"] for row in plan["files"]}
    assert all(product[name] == hashes[name] for name in ("FileCat.dll", "FileCat.Core.dll"))
    original_product = {row["path"]: row["sha256"] for row in read(temp / original_plan)["files"]}
    assert all(row["baselineSha256"] == original_product[row["path"]] for row in plan["files"])
    assert {row["path"] for row in plan["files"] if row["patch"]} == {"DiskSpaceHarness.dll", "DiskSpaceHarness.pdb"}

    failed = f"{tag}-native-r1368-results.tgz"
    files.add(failed)
    with tarfile.open(temp / failed) as archive:
        result = member(archive, "result.json")
        assert not result["passed"] and result["exitCode"] == 0 and result["closed"]["sourceUnchanged"] and not result["postProcesses"]
        assert "System.TimeoutException" in result["closed"]["duplicateReviewProbe"]["error"]
        assert not any(name.startswith("compositor-") for name in archive.getnames())

    packet = f"{tag}-native-r1370-results.tgz"
    with tarfile.open(temp / packet) as archive:
        result = member(archive, "result.json")
        observed = result["finished"]
        probe = observed["duplicateReviewProbe"]
        assert result["passed"] and result["exitCode"] == 0 and result["closed"]["sourceUnchanged"]
        assert not result["postProcesses"] and not result.get("forcedOwnedTermination")
        assert observed["appSha256"] == product["FileCat.dll"] and observed["harnessSha256"] == product["DiskSpaceHarness.dll"]
        assert probe["passed"] and probe["candidateObjects"] == 2 and probe["knownReadBytes"] == 8
        assert probe["preservingDefault"] and probe["retainedHiddenMarksAndBasket"] and probe["capturedMarks"] == 4
        installed = member(archive, "cache-proof.json")
        assert installed["baseline"] == plan["baseline"] and installed["verifiedInputs"] == proof["inputs"]
        assert set(installed["patched"]) == {row["path"] for row in plan["files"] if row["patch"]}
        checks[tag] = probe
        if platform != "mac":
            files.add(packet)
            continue
        capture, = result["compositorCaptures"]
        desktop = "compositor-duplicate-review-estimate.png"
        assert capture["frame"] == "duplicate-review-estimate.png"
        assert hashlib.sha256(archive.extractfile(desktop).read()).hexdigest() == capture["sha256"]
        private_mac = {"capture": capture, "archiveSha256": sha(temp / packet)}
        safe = io.BytesIO()
        with tarfile.open(fileobj=safe, mode="w:gz") as stripped:
            for item in archive.getmembers():
                assert item.isfile() and item.name == Path(item.name).name
                if item.name != desktop:
                    assert not item.name.startswith("compositor-")
                    stripped.addfile(item, archive.extractfile(item))

assert private_mac is not None
retained = {name: sha(temp / name) for name in sorted(files)}
with tarfile.open(receipt_output, "w:gz") as archive:
    for name in sorted(files):
        archive.add(temp / name, arcname=name, recursive=False)
    item = tarfile.TarInfo("mac-native-r1370-results-without-desktop.tgz")
    filtered = safe.getvalue()
    item.size = len(filtered)
    archive.addfile(item, io.BytesIO(filtered))
    retained[item.name] = hashlib.sha256(filtered).hexdigest()
with tarfile.open(source_output, "w:gz") as archive:
    for name in sorted(sources):
        archive.add(root / name, arcname=name, recursive=False)
manifest.write_text(json.dumps({"sourceBase": render["head"], "sources": sources, "nativeResults": checks, "retained": retained,
    "archives": {path.name: sha(path) for path in (source_output, receipt_output)}, "localMacDesktopCapture": private_mac,
    "contentReadsEnabled": False, "stableHashQualified": False, "publicEntryEnabled": False, "physicalInputQualified": False,
    "fullGoalComplete": False, "finalIndependentReviewPerformed": False}, indent=2) + "\n", encoding="utf-8")
print(f"Retained {len(retained)} duplicate preview inputs and receipts for {len(checks)} platforms.")
