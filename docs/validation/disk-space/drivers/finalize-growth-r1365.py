"""Retain the growth preset, fixture repairs, and exact three-platform evidence."""
import hashlib
import io
import json
from pathlib import Path
import tarfile
import xml.etree.ElementTree as ET

root = Path.cwd()
temp = root / ".tmp"
validation = root / "docs/validation/disk-space"
manifest = validation / "source-manifest-r1365-growth.json"
source_output = validation / "exact-source-deltas-r1365-growth.tgz"
receipt_output = validation / "exact-receipts-r1365-growth.tgz"
assert not any(path.exists() for path in (manifest, source_output, receipt_output))

def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))

def member(archive, name):
    return json.loads(archive.extractfile(name).read().decode("utf-8-sig"))

fixture = "tests/FileCat.Rendering.Tests/AnalysisRenderingTests.IndexedComparisonUi.cs"
harness = "tests/FileCat.NativeAcceptance/DiskSpaceHarness/GrowthRecipe.cs"
program = "tests/FileCat.NativeAcceptance/DiskSpaceHarness/Program.cs"
focused = read(temp / "growth-source-proof-r1357.json")
retry = read(temp / "growth-fixture-proof-r1358.json")
native = read(temp / "native-branch-proof-r1359.json")
corrected = read(temp / "growth-native-proof-r1364.json")
assert sha(root / fixture) == retry["fixture"] and sha(root / harness) == corrected["harnessSource"]
assert sha(temp / "source-r1357-growth-delta.tgz") == focused["archive"]
assert sha(temp / "source-r1359-native-branch.tgz") == native["sourceSha256"]
for item in native["sources"]:
    if item["path"] != harness:
        assert sha(root / item["path"]) == item["sha256"], item["path"]

files = {"growth-insight-render-first-build-r1357.txt", "growth-insight-render-build-r1357.txt", "growth-fixture-build-r1358.txt",
         "growth-native-build-r1359.txt", "growth-native-build-r1360.txt", "growth-native-build-r1362.txt", "growth-native-build-r1364.txt",
         "growth-source-proof-r1357.json", "growth-fixture-proof-r1358.json", "recipe-rendering-proof-r1357.json",
         "native-branch-proof-r1359.json", "growth-native-proof-r1360.json", "growth-native-proof-r1362.json", "growth-native-proof-r1364.json",
         "source-r1357-growth-delta.tgz", "windows-native-r1359-task-outcome.json", "windows-native-r1360-task-outcome.json",
         "windows-native-r1362-task-outcome.json", "windows-native-r1364-task-outcome.json"}
for failed in ("r1359", "r1360", "r1362"):
    assert read(temp / f"windows-native-{failed}-task-outcome.json")["terminalExit"] == 1
task = read(temp / "windows-native-r1364-task-outcome.json")
assert task["terminalExit"] == 0 and task["runLevel"] == "Limited"
checks = {}
private_mac = None
for platform, tag in (("windows", "windows"), ("mac", "mac"), ("linux", "ubuntu")):
    suffix = ".ps1" if platform == "windows" else ".sh"
    retry_plan = f"batch-r1364-{platform}/plan.json"
    files.add(retry_plan)
    plan = read(temp / retry_plan)
    retry_platform = corrected["platforms"][platform]
    assert sha(temp / f"batch-{platform}-r1364{suffix}") == retry_platform["runner"]
    assert sha(temp / f"dsa-batch-{platform}-r1364.tgz") == retry_platform["bundle"]
    with tarfile.open(temp / f"dsa-batch-{platform}-r1364.tgz") as bundle:
        assert member(bundle, "plan.json") == plan
    for revision in ("r1357", "r1358", "r1359", "r1360", "r1362", "r1364"):
        files.add(f"batch-{platform}-{revision}{suffix}")
    cached = []
    for revision, expected in (("r1357", (12, 11, 1, 0, 0)), ("r1358", (1, 1, 0, 0, 0))):
        packet = f"{tag}-batch-{revision}-results.tgz"
        post = f"{tag}-batch-{revision}-post.json"
        files.update((packet, post))
        processes = read(temp / post)["filecatOrDotnet"]
        if (platform, revision) == ("linux", "r1357"):
            assert all("/dsa-batch-20261107-r1357/" not in process for process in processes)
        else:
            assert not processes
        with tarfile.open(temp / packet) as archive:
            assembly = ET.fromstring(archive.extractfile("render.xml").read()).find("assembly")
            assert tuple(int(assembly.get(key)) for key in ("total", "passed", "failed", "errors", "skipped")) == expected
            picked = [test for test in assembly.iter("test") if "NativeIndexedGrowthRecipeShowsOnlyKnownPositiveDifferences" in test.get("name", "")]
            assert len(picked) == 1 and picked[0].get("result") == ("Fail" if revision == "r1357" else "Pass")
            cached.append({row["path"]: row["sha256"] for row in member(archive, "cache-proof.json")["files"]})
    product = next(entry for entry in native["platforms"] if entry["platform"] == platform)
    exact = {entry["path"]: entry["sha256"] for entry in product["files"]}
    for name in ("FileCat.dll", "FileCat.Core.dll"):
        assert exact[name] == cached[0][name] == cached[1][name]

    for revision in ("r1359", "r1360", "r1362", "r1364"):
        packet = f"{tag}-native-{revision}-results.tgz"
        post = f"{tag}-native-{revision}-post.json"
        files.add(post)
        assert not read(temp / post)["filecatOrDotnet"]
        with tarfile.open(temp / packet) as archive:
            result = member(archive, "result.json")
            assert result["exitCode"] == 0 and result["closed"]["sourceUnchanged"] and not result["postProcesses"]
            if revision != "r1364":
                expected = "Native command unavailable: Compare captured inventories" if revision == "r1359" else "System.TimeoutException"
                error = result["closed"]["growthRecipeProbe"]["error"]
                assert not result["passed"] and expected in error
                files.add(packet)
                continue
            assert result["passed"] and not result.get("forcedOwnedTermination")
            assert result["finished"]["appSha256"] == exact["FileCat.dll"]
            installed = member(archive, "cache-proof.json")
            assert installed["baseline"] == plan["baseline"]
            assert installed["verifiedInputs"] == retry_platform["inputs"] == len(plan["files"])
            patched = {row["path"] for row in plan["files"] if row["patch"]}
            assert set(installed["patched"]) == patched == {"DiskSpaceHarness.dll", "DiskSpaceHarness.pdb"}
            for row in plan["files"]:
                if row["path"] in ("FileCat.dll", "FileCat.Core.dll"):
                    assert row["sha256"] == row["baselineSha256"] == exact[row["path"]]
            probe = result["finished"]["growthRecipeProbe"]
            assert probe["passed"] and probe["sourceAndSavedInventoryPreserved"] and probe["sessions"] == 2
            assert probe["earlierTabsClosed"] == 2 and probe["addedBytes"] == 32768
            assert "change:growing AND delta>0" in probe["applied"] and "1 definite accounting rows" in probe["applied"]
            checks[tag] = probe
            if platform == "mac":
                capture = result["compositorCaptures"][0]
                desktop = "compositor-growth-observed.png"
                assert len(result["compositorCaptures"]) == 1 and capture["frame"] == "growth-observed.png"
                assert hashlib.sha256(archive.extractfile(desktop).read()).hexdigest() == capture["sha256"]
                private_mac = {"capture": capture, "archiveSha256": sha(temp / packet)}
                safe = io.BytesIO()
                with tarfile.open(fileobj=safe, mode="w:gz") as stripped:
                    for item in archive.getmembers():
                        assert item.isfile() and item.name == Path(item.name).name
                        if item.name != desktop:
                            assert not item.name.startswith("compositor-")
                            stripped.addfile(item, archive.extractfile(item))
            else:
                files.add(packet)

assert private_mac is not None
hashes = {name: sha(temp / name) for name in sorted(files)}
with tarfile.open(receipt_output, "w:gz") as archive:
    for name in sorted(files):
        archive.add(temp / name, arcname=name, recursive=False)
    item = tarfile.TarInfo("mac-native-r1364-results-without-desktop.tgz")
    package = safe.getvalue()
    item.size = len(package)
    archive.addfile(item, io.BytesIO(package))
    hashes[item.name] = hashlib.sha256(package).hexdigest()

changes = set(focused["changes"]).union((program, harness))
with tarfile.open(source_output, "w:gz") as archive:
    for name in sorted(changes):
        archive.add(root / name, arcname=name, recursive=False)

manifest.write_text(json.dumps({"sourceBase": focused["head"], "sources": {name: sha(root / name) for name in sorted(changes)},
    "nativeResults": checks, "retained": hashes, "archives": {path.name: sha(path) for path in (source_output, receipt_output)},
    "localMacDesktopCapture": private_mac, "publicEntryEnabled": False, "physicalInputQualified": False,
    "screenReaderQualified": False, "fullGoalComplete": False, "finalIndependentReviewPerformed": False}, indent=2) + "\n", encoding="utf-8")
print(f"Retained {len(hashes)} growth inputs and receipts for {len(checks)} platforms.")
