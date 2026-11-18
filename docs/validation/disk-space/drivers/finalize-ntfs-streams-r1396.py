"""Retain the exact NTFS stream allocation sources and three-platform evidence."""
import hashlib
import io
import json
from pathlib import Path
import tarfile
import xml.etree.ElementTree as ET

root = Path.cwd()
temp = root / ".tmp"
validation = root / "docs/validation/disk-space"
manifest = validation / "source-manifest-r1396-ntfs-streams.json"
source_output = validation / "exact-source-deltas-r1396-ntfs-streams.tgz"
receipt_output = validation / "exact-receipts-r1396-ntfs-streams.tgz"
assert not any(path.exists() for path in (manifest, source_output, receipt_output))


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def member(packet, name):
    return json.loads(packet.extractfile(name).read().decode("utf-8-sig"))


rendered = read(temp / "ntfs-streams-source-proof-r1394.json")
render_package = read(temp / "recipe-rendering-proof-r1394.json")
native = read(temp / "ntfs-streams-native-proof-r1395.json")
mac_native = read(temp / "ntfs-streams-native-proof-r1397.json")
sources = rendered["changes"] | native["sources"] | mac_native["sources"]
assert all(sha(root / name) == digest for name, digest in sources.items())
assert sha(temp / "source-r1394-ntfs-streams-delta.tgz") == rendered["archive"]
files = {"ntfs-streams-render-build-r1394.txt", "ntfs-streams-native-build-r1395.txt",
         "ntfs-streams-source-proof-r1394.json", "recipe-rendering-proof-r1394.json", "ntfs-streams-native-proof-r1395.json",
         "windows-batch-r1394-task-outcome.json", "windows-native-r1395-task-outcome.json",
         "ntfs-streams-native-build-r1397.txt", "ntfs-streams-native-proof-r1397.json", "mac-native-r1395-results.tgz",
         "mac-native-r1395-post.json"}
with tarfile.open(temp / "mac-native-r1395-results.tgz") as initial:
    diagnostic = member(initial, "result.json")
    assert not diagnostic["passed"] and diagnostic["exitCode"] == 0 and not diagnostic["postProcesses"]
    assert not any(name.startswith("compositor-") for name in initial.getnames())
for name in ("windows-batch-r1394-task-outcome.json", "windows-native-r1395-task-outcome.json"):
    task = read(temp / name)
    assert task["terminalExit"] == 0 and task["runLevel"] == "Limited"
checks = {}
private_mac = None
for platform, name in (("windows", "windows"), ("mac", "mac"), ("linux", "ubuntu")):
    suffix = ".ps1" if platform == "windows" else ".sh"
    native_revision = "r1397" if platform == "mac" else "r1395"
    native_platform = (mac_native if platform == "mac" else native)["platforms"][platform]
    render_run = f"batch-{platform}-r1394{suffix}"
    native_run = f"batch-{platform}-{native_revision}{suffix}"
    plan_file = f"batch-{native_revision}-{platform}/plan.json"
    files.update((render_run, native_run, plan_file))
    assert sha(temp / render_run) == render_package[platform]["runner"]
    assert sha(temp / native_run) == native_platform["runner"]
    assert sha(temp / f"dsa-batch-{platform}-r1394.tgz") == render_package[platform]["archive"]
    assert sha(temp / f"dsa-batch-{platform}-{native_revision}.tgz") == native_platform["bundle"]
    plan = read(temp / plan_file)
    with tarfile.open(temp / f"dsa-batch-{platform}-{native_revision}.tgz") as bundle:
        assert member(bundle, "plan.json") == plan
    installed = {row["path"]: row["sha256"] for row in plan["files"]}
    borrowed = {row["path"] for row in plan["files"] if row["render"]}
    assert borrowed >= {"FileCat.dll", "FileCat.Core.dll", "FileCat.Platform.Windows.dll"}
    assert len(borrowed) == native_platform["sharedRenderInputs"]
    assert {row["path"] for row in plan["files"] if row["patch"]} == {"DiskSpaceHarness.dll", "DiskSpaceHarness.pdb"}

    rendering = f"{name}-batch-r1394-results.tgz"
    files.add(rendering)
    for post in (f"{name}-batch-r1394-post.json", f"{name}-native-{native_revision}-post.json"):
        files.add(post)
        assert not read(temp / post)["filecatOrDotnet"]
    with tarfile.open(temp / rendering) as packet:
        results = ET.fromstring(packet.extractfile("render.xml").read()).find("assembly")
        assert tuple(int(results.get(key)) for key in ("total", "passed", "failed", "errors", "skipped")) == (53, 53, 0, 0, 0)
        tests = [test for test in results.iter("test") if test.get("method") == "NativeDetailedExtraAllocationKeepsPrimaryAndNamedDataSeparate"]
        assert len(tests) == 2 and all(test.get("result") == "Pass" for test in tests)
        render_inputs = {row["path"]: row["sha256"] for row in member(packet, "cache-proof.json")["files"]}
        assert all(installed[path] == render_inputs[path] for path in borrowed)

    native_packet = f"{name}-native-{native_revision}-results.tgz"
    with tarfile.open(temp / native_packet) as packet:
        result = member(packet, "result.json")
        finished = result["finished"]
        probe = finished["duplicateReviewProbe"]
        assert result["passed"] and result["exitCode"] == 0 and result["closed"]["sourceUnchanged"]
        assert not result.get("forcedOwnedTermination") and not result["postProcesses"]
        assert finished["appSha256"] == installed["FileCat.dll"] and finished["harnessSha256"] == installed["DiskSpaceHarness.dll"]
        verified = member(packet, "cache-proof.json")
        assert verified["baseline"] == plan["baseline"] and verified["render"] == plan["render"]
        assert verified["verifiedInputs"] == len(plan["files"])
        assert set(verified["patched"]) == {"DiskSpaceHarness.dll", "DiskSpaceHarness.pdb"}
        assert probe["recordedInsightsPassed"] and probe["largestFoldersPassed"] and probe["preservingDefault"] and probe["retainedHiddenMarksAndBasket"]
        assert probe["detailedStreamProbe"] and probe["namedAllocationAvailable"] == (platform == "windows") and probe["namedStreamBytesPreserved"]
        assert "ntfs-stream-allocation.png" in packet.getnames()
        checks[name] = probe
        if platform != "mac":
            files.add(native_packet)
            continue
        capture, = result["compositorCaptures"]
        desktop = "compositor-ntfs-stream-allocation.png"
        assert capture["frame"] == "ntfs-stream-allocation.png"
        assert hashlib.sha256(packet.extractfile(desktop).read()).hexdigest() == capture["sha256"]
        private_mac = {"capture": capture, "archiveSha256": sha(temp / native_packet)}
        safe = io.BytesIO()
        with tarfile.open(fileobj=safe, mode="w:gz") as stripped:
            for item in packet.getmembers():
                assert item.isfile() and item.name == Path(item.name).name
                if item.name != desktop:
                    assert not item.name.startswith("compositor-")
                    stripped.addfile(item, packet.extractfile(item))

assert private_mac is not None
retained = {name: sha(temp / name) for name in sorted(files)}
with tarfile.open(receipt_output, "w:gz") as packet:
    for name in sorted(files):
        packet.add(temp / name, arcname=name, recursive=False)
    item = tarfile.TarInfo("mac-native-r1397-results-without-desktop.tgz")
    filtered = safe.getvalue()
    item.size = len(filtered)
    packet.addfile(item, io.BytesIO(filtered))
    retained[item.name] = hashlib.sha256(filtered).hexdigest()
with tarfile.open(source_output, "w:gz") as packet:
    for name in sorted(sources):
        packet.add(root / name, arcname=name, recursive=False)
manifest.write_text(json.dumps({"sourceBase": rendered["head"], "sources": sources, "retained": retained, "nativeResults": checks,
    "archives": {path.name: sha(path) for path in (source_output, receipt_output)}, "localMacDesktopCapture": private_mac,
    "ntfsNamedStreamAllocationQualified": True, "resourceForkMeasurementsQualified": False, "publicEntryEnabled": False, "physicalInputQualified": False,
    "fullGoalComplete": False, "finalIndependentReviewPerformed": False}, indent=2) + "\n", encoding="utf-8")
print(f"Retained {len(retained)} named-stream inputs and receipts across {len(checks)} platforms.")
