"""Retain exact source, initial regressions, fixture-only reruns and native staging evidence."""
import hashlib
import io
import json
from pathlib import Path
import tarfile
import xml.etree.ElementTree as ET

root = Path.cwd()
temp = root / ".tmp"
validation = root / "docs/validation/disk-space"
manifest = validation / "source-manifest-r1393-recipe-staging.json"
source_output = validation / "exact-source-deltas-r1393-recipe-staging.tgz"
receipts = validation / "exact-receipts-r1393-recipe-staging.tgz"
assert not any(path.exists() for path in (manifest, source_output, receipts))


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def member(packet, name):
    return json.loads(packet.extractfile(name).read().decode("utf-8-sig"))


broad, retry, native_revision = "r1389", "r1392", "r1390"
case = "NativeRecipeStagingChecksExplicitInclusionsAndPreservesProtectedOrChangedFiles"
test_source = "tests/FileCat.Rendering.Tests/AnalysisRenderingTests.Recipes.cs"
source_proofs = {rev: read(temp / f"recipe-staging-source-proof-{rev}.json") for rev in (broad, retry)}
packs = {rev: read(temp / f"recipe-rendering-proof-{rev}.json") for rev in (broad, retry)}
native = read(temp / f"recipe-staging-native-proof-{native_revision}.json")
sources = source_proofs[retry]["changes"] | native["sources"]
assert all(sha(root / name) == digest for name, digest in sources.items())
assert source_proofs[broad]["changes"][test_source] != sources[test_source]
assert all(sources[name] == digest for name, digest in source_proofs[broad]["changes"].items() if name != test_source)
files = {f"recipe-staging-native-build-{native_revision}.txt", f"recipe-staging-native-proof-{native_revision}.json"}
for rev in (broad, retry):
    files.update((f"recipe-staging-render-build-{rev}.txt", f"recipe-rendering-proof-{rev}.json", f"recipe-staging-source-proof-{rev}.json"))
    archive = f"source-{rev}-recipe-staging-delta.tgz"
    assert sha(temp / archive) == source_proofs[rev]["archive"]
    files.add(archive)
for name, exit_code in ((f"windows-batch-{broad}-task-outcome.json", 1), (f"windows-batch-{retry}-task-outcome.json", 0),
                        (f"windows-native-{native_revision}-task-outcome.json", 0)):
    files.add(name)
    task = read(temp / name)
    assert task["terminalExit"] == exit_code and task["runLevel"] == "Limited"

checks = {}
private_mac = None
for platform, host in (("windows", "windows"), ("mac", "mac"), ("linux", "ubuntu")):
    suffix = ".ps1" if platform == "windows" else ".sh"
    installed = {}
    for rev, counts, expected in ((broad, (51, 49, 2, 0, 0), "Fail"), (retry, (2, 2, 0, 0, 0), "Pass")):
        runner = f"batch-{platform}-{rev}{suffix}"
        receipt = f"{host}-batch-{rev}-results.tgz"
        post = f"{host}-batch-{rev}-post.json"
        files.update((runner, receipt, post))
        assert sha(temp / runner) == packs[rev][platform]["runner"]
        assert sha(temp / f"dsa-batch-{platform}-{rev}.tgz") == packs[rev][platform]["archive"]
        assert not read(temp / post)["filecatOrDotnet"]
        with tarfile.open(temp / receipt) as packet:
            assembly = ET.fromstring(packet.extractfile("render.xml").read()).find("assembly")
            assert tuple(int(assembly.get(key)) for key in ("total", "passed", "failed", "errors", "skipped")) == counts
            tests = [test for test in assembly.iter("test") if test.get("method") == case]
            assert len(tests) == 2 and all(test.get("result") == expected for test in tests)
            installed[rev] = {row["path"]: row["sha256"] for row in member(packet, "cache-proof.json")["files"]}
    assert {name for name, digest in installed[broad].items() if installed[retry][name] != digest} == {
        "FileCat.Rendering.Tests.dll", "FileCat.Rendering.Tests.pdb"}

    native_runner = f"batch-{platform}-{native_revision}{suffix}"
    native_plan = f"batch-{native_revision}-{platform}/plan.json"
    native_post = f"{host}-native-{native_revision}-post.json"
    files.update((native_runner, native_plan, native_post))
    assert sha(temp / native_runner) == native["platforms"][platform]["runner"]
    assert sha(temp / f"dsa-batch-{platform}-{native_revision}.tgz") == native["platforms"][platform]["bundle"]
    assert not read(temp / native_post)["filecatOrDotnet"]
    plan = read(temp / native_plan)
    shared = {row["path"]: row["sha256"] for row in plan["files"] if row["render"]}
    assert shared.keys() >= {"FileCat.dll", "FileCat.Core.dll", "FileCat.Platform.Windows.dll"}
    assert all(installed[retry][name] == digest for name, digest in shared.items())
    harness, = [row["sha256"] for row in plan["files"] if row["path"] == "DiskSpaceHarness.dll"]

    native_receipt = f"{host}-native-{native_revision}-results.tgz"
    with tarfile.open(temp / native_receipt) as packet:
        result = member(packet, "result.json")
        finished = result["finished"]
        probe = finished["duplicateReviewProbe"]
        assert result["passed"] and result["exitCode"] == 0 and result["closed"]["sourceUnchanged"]
        assert not result.get("forcedOwnedTermination") and not result["postProcesses"]
        assert finished["appSha256"] == shared["FileCat.dll"] and finished["harnessSha256"] == harness
        assert probe["recipeStagingPassed"] and probe["largestFoldersPassed"] and probe["retainedHiddenMarksAndBasket"]
        assert "recipe-staging-included.png" in packet.getnames()
        checks[host] = {"regressionsPassed": 49, "correctedStagingCasesPassed": 2, "native": probe}
        if platform != "mac":
            files.add(native_receipt)
            continue
        capture, = result["compositorCaptures"]
        desktop = "compositor-recipe-staging-included.png"
        assert capture["frame"] == "recipe-staging-included.png"
        assert hashlib.sha256(packet.extractfile(desktop).read()).hexdigest() == capture["sha256"]
        private_mac = {"capture": capture, "archiveSha256": sha(temp / native_receipt)}
        safe = io.BytesIO()
        with tarfile.open(fileobj=safe, mode="w:gz") as stripped:
            for item in packet.getmembers():
                assert item.isfile() and item.name == Path(item.name).name
                if item.name != desktop:
                    assert not item.name.startswith("compositor-")
                    stripped.addfile(item, packet.extractfile(item))

assert private_mac is not None
retained = {name: sha(temp / name) for name in sorted(files)}
with tarfile.open(receipts, "w:gz") as packet:
    for name in sorted(files):
        packet.add(temp / name, arcname=name, recursive=False)
    item = tarfile.TarInfo(f"mac-native-{native_revision}-results-without-desktop.tgz")
    payload = safe.getvalue()
    item.size = len(payload)
    packet.addfile(item, io.BytesIO(payload))
    retained[item.name] = hashlib.sha256(payload).hexdigest()
with tarfile.open(source_output, "w:gz") as packet:
    for name in sorted(sources):
        packet.add(root / name, arcname=name, recursive=False)
manifest.write_text(json.dumps({"sourceBase": source_proofs[retry]["head"], "sources": sources, "retained": retained,
    "platforms": checks, "archives": {path.name: sha(path) for path in (source_output, receipts)},
    "localMacDesktopCapture": private_mac, "qualifiedSingleLinkRecipeStaging": True,
    "publicEntryEnabled": False, "physicalInputQualified": False, "fullGoalComplete": False,
    "finalIndependentReviewPerformed": False}, indent=2) + "\n", encoding="utf-8")
print(f"Retained {len(retained)} recipe staging inputs and receipts across {len(checks)} platforms.")
