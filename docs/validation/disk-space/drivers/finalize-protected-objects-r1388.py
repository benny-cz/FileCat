"""Preserve the runtime protected-directory export qualification."""
import hashlib
import json
from pathlib import Path
import tarfile
import xml.etree.ElementTree as ET

root = Path.cwd()
temp = root / ".tmp"
validation = root / "docs/validation/disk-space"
source_output = validation / "exact-source-deltas-r1388-protected-objects.tgz"
receipt_output = validation / "exact-receipts-r1388-protected-objects.tgz"
manifest = validation / "source-manifest-r1388-protected-objects.json"
assert not any(path.exists() for path in (source_output, receipt_output, manifest))


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


revision = "r1387"
source_proof = f"protected-objects-source-proof-{revision}.json"
render_proof = f"recipe-rendering-proof-{revision}.json"
sources = read(temp / source_proof)
packaged = read(temp / render_proof)
assert all(sha(root / name) == digest for name, digest in sources["changes"].items())
source_bundle = temp / f"source-{revision}-protected-objects-delta.tgz"
assert sha(source_bundle) == sources["archive"]
with tarfile.open(source_bundle) as packet:
    assert set(packet.getnames()) == set(sources["changes"])
    for name, digest in sources["changes"].items():
        assert hashlib.sha256(packet.extractfile(name).read()).hexdigest() == digest

task_receipt = f"windows-batch-{revision}-task-outcome.json"
task = read(temp / task_receipt)
assert task["terminalExit"] == 0 and task["runLevel"] == "Limited"
files = {source_proof, render_proof, task_receipt, f"protected-objects-render-build-{revision}.txt"}
checks = {}
for platform, host in (("windows", "windows"), ("mac", "mac"), ("linux", "ubuntu")):
    suffix = ".ps1" if platform == "windows" else ".sh"
    runner = f"batch-{platform}-{revision}{suffix}"
    results = f"{host}-batch-{revision}-results.tgz"
    post = f"{host}-batch-{revision}-post.json"
    plan_name = f"focused-delta-{platform}-{revision}/plan.json"
    files.update((runner, results, post, plan_name))
    assert sha(temp / runner) == packaged[platform]["runner"]
    assert sha(temp / f"dsa-batch-{platform}-{revision}.tgz") == packaged[platform]["archive"]
    assert not read(temp / post)["filecatOrDotnet"]
    plan = read(temp / plan_name)
    with tarfile.open(temp / results) as packet:
        assembly = ET.fromstring(packet.extractfile("render.xml").read()).find("assembly")
        assert tuple(int(assembly.get(key)) for key in ("total", "passed", "failed", "errors", "skipped")) == (35, 35, 0, 0, 0)
        by_method = [test for test in assembly.iter("test") if test.get("method") == "NativeCapturedReportsProtectRedirectedFileCatDirectoriesByIdentity"]
        guarded, = by_method
        assert guarded.get("result") == "Pass"
        installed = json.load(packet.extractfile("cache-proof.json"))
        assert {row["path"]: row["sha256"] for row in installed["files"]} == {row["path"]: row["sha256"] for row in plan["files"]}
        checks[host] = {"passed": 35, "redirectedProtectedDirectoryAndDescendantRefused": True, "ordinarySiblingExported": True,
                        "retainedBytesPreserved": True, "postProcesses": read(temp / post)["filecatOrDotnet"]}

source_output.write_bytes(source_bundle.read_bytes())
with tarfile.open(receipt_output, "w:gz") as packet:
    for name in sorted(files):
        packet.add(temp / name, arcname=name, recursive=False)
manifest.write_text(json.dumps({"sourceBase": sources["head"], "sources": sources["changes"], "platforms": checks,
    "retained": {name: sha(temp / name) for name in sorted(files)},
    "archives": {path.name: sha(path) for path in (source_output, receipt_output)}, "publicEntryEnabled": False,
    "recipeStagingQualified": False, "fullGoalComplete": False, "finalIndependentReviewPerformed": False}, indent=2) + "\n", encoding="utf-8")
print(f"Retained {len(files)} protected-directory records across {len(checks)} platforms.")
