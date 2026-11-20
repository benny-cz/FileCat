"""Retain the exact largest-folder sources and three-platform evidence."""
import hashlib
import io
import json
from pathlib import Path
import tarfile
import xml.etree.ElementTree as ET

root = Path.cwd()
temp = root / ".tmp"
validation = root / "docs/validation/disk-space"
manifest = validation / "source-manifest-r1385-largest-folders.json"
source_output = validation / "exact-source-deltas-r1385-largest-folders.tgz"
receipt_output = validation / "exact-receipts-r1385-largest-folders.tgz"
assert not any(path.exists() for path in (manifest, source_output, receipt_output))


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def member(packet, name):
    return json.loads(packet.extractfile(name).read().decode("utf-8-sig"))


rendered = read(temp / "recorded-insights-source-proof-r1382.json")
render_package = read(temp / "recipe-rendering-proof-r1382.json")
native = read(temp / "recorded-insights-native-proof-r1384.json")
sources = rendered["changes"] | {"tests/FileCat.NativeAcceptance/DiskSpaceHarness/DuplicateReview.cs": native["harnessSource"]}
assert all(sha(root / name) == digest for name, digest in sources.items())
assert sha(temp / "source-r1382-recorded-insights-delta.tgz") == rendered["archive"]
files = {"recorded-insight-render-build-r1382.txt", "recorded-insights-native-build-r1384.txt",
         "recorded-insights-source-proof-r1382.json", "recipe-rendering-proof-r1382.json", "recorded-insights-native-proof-r1384.json",
         "windows-batch-r1382-task-outcome.json", "windows-native-r1386-task-outcome.json",
         "windows-batch-r1384-task-outcome.json", "mac-native-r1383-results/result.json"}
assert read(temp / "windows-batch-r1384-task-outcome.json")["terminalExit"] == 75
for name in ("windows-batch-r1382-task-outcome.json", "windows-native-r1386-task-outcome.json"):
    task = read(temp / name)
    assert task["terminalExit"] == 0 and task["runLevel"] == "Limited"
checks = {}
private_mac = None
for platform, name in (("windows", "windows"), ("mac", "mac"), ("linux", "ubuntu")):
    suffix = ".ps1" if platform == "windows" else ".sh"
    native_revision = "r1386" if platform == "windows" else "r1384"
    render_run = f"batch-{platform}-r1382{suffix}"
    native_run = f"batch-{platform}-{native_revision}{suffix}"
    plan_file = f"batch-r1384-{platform}/plan.json"
    files.update((render_run, native_run, plan_file))
    assert sha(temp / render_run) == render_package[platform]["runner"]
    assert sha(temp / native_run) == native["platforms"][platform]["runner"]
    assert sha(temp / f"dsa-batch-{platform}-r1382.tgz") == render_package[platform]["archive"]
    assert sha(temp / f"dsa-batch-{platform}-{native_revision}.tgz") == native["platforms"][platform]["bundle"]
    plan = read(temp / plan_file)
    with tarfile.open(temp / f"dsa-batch-{platform}-{native_revision}.tgz") as bundle:
        assert member(bundle, "plan.json") == plan
    installed = {row["path"]: row["sha256"] for row in plan["files"]}
    borrowed = {row["path"] for row in plan["files"] if row["render"]}
    assert borrowed >= {"FileCat.dll", "FileCat.Core.dll", "FileCat.Platform.Windows.dll"}
    assert len(borrowed) == native["platforms"][platform]["sharedRenderInputs"]
    assert {row["path"] for row in plan["files"] if row["patch"]} == {"DiskSpaceHarness.dll", "DiskSpaceHarness.pdb"}

    rendering = f"{name}-batch-r1382-results.tgz"
    files.add(rendering)
    for post in (f"{name}-batch-r1382-post.json", f"{name}-native-{native_revision}-post.json"):
        files.add(post)
        assert not read(temp / post)["filecatOrDotnet"]
    with tarfile.open(temp / rendering) as packet:
        results = ET.fromstring(packet.extractfile("render.xml").read()).find("assembly")
        assert tuple(int(results.get(key)) for key in ("total", "passed", "failed", "errors", "skipped")) == (20, 20, 0, 0, 0)
        tests = [test for test in results.iter("test") if "RecordedInsight" in test.get("name", "")]
        assert len(tests) == 4 and all(test.get("result") == "Pass" for test in tests)
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
        assert "recorded-insights-largest-folders.png" in packet.getnames()
        checks[name] = probe
        if platform != "mac":
            files.add(native_packet)
            continue
        capture, = result["compositorCaptures"]
        desktop = "compositor-recorded-insights-largest-folders.png"
        assert capture["frame"] == "recorded-insights-largest-folders.png"
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
    item = tarfile.TarInfo("mac-native-r1384-results-without-desktop.tgz")
    filtered = safe.getvalue()
    item.size = len(filtered)
    packet.addfile(item, io.BytesIO(filtered))
    retained[item.name] = hashlib.sha256(filtered).hexdigest()
with tarfile.open(source_output, "w:gz") as packet:
    for name in sorted(sources):
        packet.add(root / name, arcname=name, recursive=False)
manifest.write_text(json.dumps({"sourceBase": rendered["head"], "sources": sources, "retained": retained, "nativeResults": checks,
    "archives": {path.name: sha(path) for path in (source_output, receipt_output)}, "localMacDesktopCapture": private_mac,
    "streamAndForkMeasurementsQualified": False, "publicEntryEnabled": False, "physicalInputQualified": False,
    "fullGoalComplete": False, "finalIndependentReviewPerformed": False}, indent=2) + "\n", encoding="utf-8")
print(f"Retained {len(retained)} largest-folder inputs and receipts across {len(checks)} platforms.")
